using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PsaToolAgent.Psa.Gorelo;

public sealed class GoreloPsaDataProvider : IPsaDataProvider
{
    /// <summary>Hard ceiling on pages fetched per poll (at the default 200-per-page size, 100
    /// pages is 20,000 tickets — far beyond the sub-2,000-ticket tenant size this provider targets).
    /// Guards against an unbounded fetch loop (e.g. a server bug where <c>hasMore</c> never turns
    /// false) hanging the poll cycle forever; <see cref="PollingBackgroundService"/> catches and
    /// logs the resulting exception, then retries next interval, same as any other poll failure.</summary>
    private const int MaxPages = 100;

    private readonly HttpClient _http;
    private readonly GoreloOptions _options;
    private readonly ILogger<GoreloPsaDataProvider> _logger;
    private readonly HashSet<string> _loggedUnknownPriorityNames = new(StringComparer.OrdinalIgnoreCase);

    public GoreloPsaDataProvider(HttpClient http, IOptions<GoreloOptions> options, ILogger<GoreloPsaDataProvider> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PsaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var tickets = new List<GoreloTicket>();
        string? cursor = null;
        var pageCount = 0;
        bool hasMore;

        do
        {
            pageCount++;
            if (pageCount > MaxPages)
            {
                throw new InvalidOperationException(
                    $"Gorelo tickets pagination exceeded {MaxPages} pages without hasMore becoming false — aborting to avoid an unbounded fetch loop.");
            }

            var uri = cursor is null
                ? $"v1/tickets?pageSize={_options.PageSize}"
                : $"v1/tickets?pageSize={_options.PageSize}&cursor={Uri.EscapeDataString(cursor)}";

            using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<GoreloTicketsResponse>(cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Gorelo tickets response was empty.");

            tickets.AddRange(body.Data);
            cursor = body.NextCursor;
            hasMore = body.HasMore;

            if (hasMore && string.IsNullOrEmpty(cursor))
            {
                throw new InvalidOperationException(
                    "Gorelo reported hasMore=true but returned no nextCursor — aborting to avoid refetching the same page.");
            }
        } while (hasMore);

        LogUnrecognizedPriorityNames(tickets);

        return MapSnapshot(tickets);
    }

    /// <summary>Logs a warning once per distinct unrecognized priority name seen (not once per
    /// ticket), so a renamed or custom priority tier doesn't spam logs every poll.</summary>
    private void LogUnrecognizedPriorityNames(IReadOnlyList<GoreloTicket> tickets)
    {
        var unknownNames = tickets
            .Where(t => t.ClosedOn is null)
            .Select(t => t.Priority?.Name)
            .Where(name => name is not null && !PriorityRankByName.ContainsKey(name));

        foreach (var name in unknownNames)
        {
            if (_loggedUnknownPriorityNames.Add(name!))
            {
                _logger.LogWarning(
                    "Gorelo ticket priority {PriorityName} is not one of the known Urgent/High/Normal/Low/None labels — tickets with this priority are counted in OpenTicketCount but excluded from PriorityCounts.",
                    name);
            }
        }
    }

    /// <summary>
    /// Pure mapping from raw Gorelo ticket data (the full paginated set, open and closed) to the
    /// provider-agnostic <see cref="PsaSnapshot"/>. Internal (not private) so tests can exercise
    /// the mapping logic directly, without HTTP.
    ///
    /// A ticket counts as active/open when <see cref="GoreloTicket.ClosedOn"/> is null — Gorelo's
    /// API has no server-side open filter and no documented status taxonomy, so this is the one
    /// tenant-customization-proof signal.
    ///
    /// Priority rank is resolved by name against <see cref="PriorityRankByName"/>, not by
    /// <see cref="GoreloCodeModel.Id"/>: <see cref="PsaSnapshot.PriorityCounts"/>'s "1 = most
    /// urgent" contract has no guaranteed relationship to a tenant-assigned numeric id, and
    /// PriorityEngine hardcodes rank 1 as its CRITICAL trigger, so getting this wrong would
    /// misfire the display. A ticket whose priority name isn't in the table is excluded from
    /// PriorityCounts/PriorityNames (still counted in OpenTicketCount).
    ///
    /// VipTickets is always empty and OrganizationName is always null: neither concept exists in
    /// Gorelo's public API as of this implementation.
    ///
    /// Unlike <see cref="Halo.HaloPsaDataProvider.MapSnapshot"/>, on-hold/paused tickets are NOT
    /// excluded here: Gorelo's ticket schema has no reliable on-hold signal (its
    /// <c>baseStatusId</c> taxonomy is undocumented and tenant-customizable), so a paused Gorelo
    /// ticket still counts toward every metric below, including P1/SLA/unassigned.
    /// </summary>
    internal static PsaSnapshot MapSnapshot(IReadOnlyList<GoreloTicket> tickets)
    {
        var activeTickets = tickets.Where(t => t.ClosedOn is null).ToList();

        var priorityCounts = new Dictionary<int, int>();
        var priorityNames = new Dictionary<int, string>();

        foreach (var ticket in activeTickets)
        {
            var name = ticket.Priority?.Name;
            if (name is null || !PriorityRankByName.TryGetValue(name, out var rank))
            {
                continue;
            }

            priorityCounts[rank] = priorityCounts.GetValueOrDefault(rank) + 1;
            priorityNames[rank] = name;
        }

        var slaRiskTickets = activeTickets
            .Where(t => t.Sla?.FirstResponse?.Minutes is not null)
            .Select(t => new SlaRiskTicket(t.DisplayNumber ?? t.Id, (int)t.Sla!.FirstResponse!.Minutes!.Value))
            .ToList();

        var unassignedCount = activeTickets.Count(t => t.LeadAssigneeId is null);

        return new PsaSnapshot
        {
            OpenTicketCount = activeTickets.Count,
            PriorityCounts = priorityCounts,
            PriorityNames = priorityNames,
            SlaRiskTickets = slaRiskTickets,
            UnassignedTicketCount = unassignedCount,
            VipTickets = Array.Empty<VipTicket>(),
            OrganizationName = null
        };
    }

    /// <summary>Gorelo tenants run one of two fixed priority schemes (2-level Urgent/Normal, or
    /// 5-level Urgent/High/Normal/Low/None) — unlike Halo, priority names aren't arbitrary, so a
    /// fixed table (rather than per-tenant discovery) is sufficient.</summary>
    private static readonly IReadOnlyDictionary<string, int> PriorityRankByName =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Urgent"] = 1,
            ["High"] = 2,
            ["Normal"] = 3,
            ["Low"] = 4,
            ["None"] = 5
        };
}
