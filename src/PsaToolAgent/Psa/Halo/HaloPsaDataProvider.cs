using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace PsaToolAgent.Psa.Halo;

public sealed class HaloPsaDataProvider : IPsaDataProvider
{
    private readonly HttpClient _http;
    private readonly HaloAuthClient _auth;
    private readonly PsaOptions _psaOptions;

    public HaloPsaDataProvider(HttpClient http, HaloAuthClient auth, IOptions<PsaOptions> psaOptions)
    {
        _http = http;
        _auth = auth;
        _psaOptions = psaOptions.Value;
    }

    public async Task<PsaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var token = await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            "api/Tickets?open_only=true&includeslatimer=true&pageinate=false&count=200");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<HaloTicketsResponse>(cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Halo tickets response was empty.");

        return MapSnapshot(body.Tickets, _psaOptions.SlaRiskThresholdMinutes);
    }

    /// <summary>
    /// Pure mapping from raw Halo ticket data to the provider-agnostic <see cref="PsaSnapshot"/>.
    /// Internal (not private) so tests can exercise the mapping logic directly, without HTTP.
    /// </summary>
    internal static PsaSnapshot MapSnapshot(IReadOnlyList<HaloTicket> tickets, int slaRiskThresholdMinutes)
    {
        var priorityCounts = tickets
            .GroupBy(t => t.PriorityId)
            .ToDictionary(g => g.Key, g => g.Count());

        var slaRiskTickets = tickets
            .Where(t => t.SlaTimeLeftHours is not null)
            .Select(t => new SlaRiskTicket(t.Id.ToString(), (int)(t.SlaTimeLeftHours!.Value * 60)))
            .Where(t => t.MinutesRemaining <= slaRiskThresholdMinutes)
            .ToList();

        // This tenant represents "no agent assigned" as agent_id 0 — verify this convention
        // against your own Halo tenant if ticket routing differs.
        var unassignedCount = tickets.Count(t => t.AgentId == 0);

        var vipTickets = tickets
            .Where(t => t.IsVip)
            .Select(t => new VipTicket(t.Id.ToString(), t.ClientName))
            .ToList();

        return new PsaSnapshot
        {
            OpenTicketCount = tickets.Count,
            PriorityCounts = priorityCounts,
            SlaRiskTickets = slaRiskTickets,
            UnassignedTicketCount = unassignedCount,
            VipTickets = vipTickets
        };
    }
}
