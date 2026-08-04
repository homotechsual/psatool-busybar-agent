using System.Text.Json.Serialization;

namespace PsaToolAgent.Psa.Halo;

internal sealed record HaloTicketsResponse(
    [property: JsonPropertyName("record_count")] int RecordCount,
    [property: JsonPropertyName("tickets")] IReadOnlyList<HaloTicket> Tickets);

internal sealed record HaloTicket(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("priority_id")] int PriorityId,
    [property: JsonPropertyName("agent_id")] int AgentId,
    [property: JsonPropertyName("client_name")] string ClientName,
    [property: JsonPropertyName("is_vip")] bool IsVip,
    [property: JsonPropertyName("slatimeleft")] double? SlaTimeLeftHours,
    [property: JsonPropertyName("onhold")] bool OnHold);

internal sealed record HaloOrganisationResponse(
    [property: JsonPropertyName("portal_title")] string? PortalTitle);
