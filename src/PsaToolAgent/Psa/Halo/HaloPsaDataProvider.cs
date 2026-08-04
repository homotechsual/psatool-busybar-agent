using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace PsaToolAgent.Psa.Halo;

public sealed class HaloPsaDataProvider : IPsaDataProvider
{
    private readonly HttpClient _http;
    private readonly HaloAuthClient _auth;
    private readonly ILogger<HaloPsaDataProvider> _logger;

    public HaloPsaDataProvider(HttpClient http, HaloAuthClient auth, ILogger<HaloPsaDataProvider> logger)
    {
        _http = http;
        _auth = auth;
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

            return MapSnapshot(body.Tickets);
        }
    }

    private async Task<HttpResponseMessage> SendTicketsRequestAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "api/Tickets?open_only=true&includeslatimer=true&pageinate=false&count=200");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pure mapping from raw Halo ticket data to the provider-agnostic <see cref="PsaSnapshot"/>.
    /// Internal (not private) so tests can exercise the mapping logic directly, without HTTP.
    /// SLA-risk thresholding is deliberately NOT applied here — <see cref="PsaSnapshot.SlaRiskTickets"/>
    /// carries every ticket with a known SLA time remaining (including already-breached, negative
    /// values); <c>PriorityEngine</c> is the single place that applies the SLA-risk threshold.
    /// </summary>
    internal static PsaSnapshot MapSnapshot(IReadOnlyList<HaloTicket> tickets)
    {
        var priorityCounts = tickets
            .GroupBy(t => t.PriorityId)
            .ToDictionary(g => g.Key, g => g.Count());

        var slaRiskTickets = tickets
            .Where(t => t.SlaTimeLeftHours is not null)
            .Select(t => new SlaRiskTicket(t.Id.ToString(), (int)(t.SlaTimeLeftHours!.Value * 60)))
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
