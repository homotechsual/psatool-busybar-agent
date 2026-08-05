using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PsaToolAgent.Psa.Halo;

public sealed class HaloPsaDataProvider : IPsaDataProvider
{
    private readonly HttpClient _http;
    private readonly HaloAuthClient _auth;
    private readonly HaloOptions _haloOptions;
    private readonly ILogger<HaloPsaDataProvider> _logger;

    private string? _organizationName;
    private bool _organizationNameFetched;

    public HaloPsaDataProvider(HttpClient http, HaloAuthClient auth, IOptions<HaloOptions> haloOptions, ILogger<HaloPsaDataProvider> logger)
    {
        _http = http;
        _auth = auth;
        _haloOptions = haloOptions.Value;
        _logger = logger;
    }

    public async Task<PsaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var token = await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var response = await SendTicketsRequestAsync(token, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Halo rejected our cached token early (rotated secret, disabled client, etc.) —
            // evict it and retry once with a freshly issued token before giving up.
            response.Dispose();
            _auth.InvalidateToken();
            token = await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            response = await SendTicketsRequestAsync(token, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<HaloTicket> tickets;
        using (response)
        {
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<HaloTicketsResponse>(cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Halo tickets response was empty.");

            if (body.Tickets.Count != body.RecordCount)
            {
                _logger.LogWarning(
                    "Halo reported {RecordCount} open tickets but returned {ActualCount} — the dashboard may be undercounting. Consider raising the query's page size.",
                    body.RecordCount, body.Tickets.Count);
            }

            tickets = body.Tickets;
        }

        var organizationName = await GetOrganizationNameAsync(token, cancellationToken).ConfigureAwait(false);
        return MapSnapshot(tickets) with { OrganizationName = organizationName };
    }

    private async Task<HttpResponseMessage> SendTicketsRequestAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "api/Tickets?open_only=true&includeslatimer=true&pageinate=false&count=200");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches the Halo organisation's <c>portal_title</c> for use as the dashboard header, once,
    /// on the first call — the result (success or failure) is cached for the process lifetime, so
    /// later polls never re-fetch it. Reuses whatever access token the caller already has (does
    /// not itself retry on 401 — <see cref="GetSnapshotAsync"/> has already resolved a valid token
    /// by the time this runs).
    /// </summary>
    private async Task<string?> GetOrganizationNameAsync(string token, CancellationToken cancellationToken)
    {
        if (_organizationNameFetched)
        {
            return _organizationName;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"api/Organisation/{_haloOptions.OrganisationId}?includedetails=true");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            var organisation = await response.Content.ReadFromJsonAsync<HaloOrganisationResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            _organizationName = string.IsNullOrWhiteSpace(organisation?.PortalTitle) ? null : organisation.PortalTitle;
        }
        else
        {
            _logger.LogWarning(
                "Failed to fetch Halo organisation {OrganisationId} details ({StatusCode}); the dashboard will use the configured header text instead.",
                _haloOptions.OrganisationId, response.StatusCode);
        }

        _organizationNameFetched = true;
        return _organizationName;
    }

    /// <summary>
    /// Pure mapping from raw Halo ticket data to the provider-agnostic <see cref="PsaSnapshot"/>.
    /// Internal (not private) so tests can exercise the mapping logic directly, without HTTP.
    /// SLA-risk thresholding is deliberately NOT applied here — <see cref="PsaSnapshot.SlaRiskTickets"/>
    /// carries every ticket with a known SLA time remaining (including already-breached, negative
    /// values); <c>PriorityEngine</c> is the single place that applies the SLA-risk threshold.
    /// On-hold tickets are excluded entirely: a paused ticket (awaiting customer, parts, a
    /// scheduled window, etc.) doesn't need anyone's immediate attention, so it must not drive a
    /// P1/SLA/VIP/unassigned signal on the display.
    /// </summary>
    internal static PsaSnapshot MapSnapshot(IReadOnlyList<HaloTicket> tickets)
    {
        var activeTickets = tickets.Where(t => !t.OnHold).ToList();

        var priorityCounts = activeTickets
            .GroupBy(t => t.PriorityId)
            .ToDictionary(g => g.Key, g => g.Count());

        var slaRiskTickets = activeTickets
            .Where(t => t.SlaTimeLeftHours is not null)
            .Select(t => new SlaRiskTicket(t.Id.ToString(), (int)(t.SlaTimeLeftHours!.Value * 60)))
            .ToList();

        // This tenant represents "no agent assigned" as agent_id 0 — verify this convention
        // against your own Halo tenant if ticket routing differs.
        var unassignedCount = activeTickets.Count(t => t.AgentId == 0);

        var vipTickets = activeTickets
            .Where(t => t.IsVip)
            .Select(t => new VipTicket(t.Id.ToString(), t.ClientName))
            .ToList();

        return new PsaSnapshot
        {
            OpenTicketCount = activeTickets.Count,
            PriorityCounts = priorityCounts,
            SlaRiskTickets = slaRiskTickets,
            UnassignedTicketCount = unassignedCount,
            VipTickets = vipTickets
        };
    }
}
