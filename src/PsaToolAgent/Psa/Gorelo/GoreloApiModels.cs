using System.Text.Json.Serialization;

namespace PsaToolAgent.Psa.Gorelo;

internal sealed record GoreloTicketsResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<GoreloTicket> Data,
    [property: JsonPropertyName("nextCursor")] string? NextCursor,
    [property: JsonPropertyName("hasMore")] bool HasMore);

internal sealed record GoreloTicket(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("displayNumber")] string? DisplayNumber,
    [property: JsonPropertyName("leadAssigneeId")] long? LeadAssigneeId,
    [property: JsonPropertyName("closedOn")] DateTimeOffset? ClosedOn,
    [property: JsonPropertyName("priority")] GoreloCodeModel? Priority,
    [property: JsonPropertyName("sla")] GoreloSlaModel? Sla);

/// <summary>Gorelo's shared "id + name" shape, used here for <see cref="GoreloTicket.Priority"/>.
/// The id is tenant-assigned and carries no guaranteed urgency ordering — see
/// <see cref="GoreloPsaDataProvider.MapSnapshot"/>, which resolves rank from the name instead.</summary>
internal sealed record GoreloCodeModel(
    [property: JsonPropertyName("id")] long? Id,
    [property: JsonPropertyName("name")] string? Name);

internal sealed record GoreloSlaModel(
    [property: JsonPropertyName("firstResponse")] GoreloSlaFirstResponseModel? FirstResponse);

internal sealed record GoreloSlaFirstResponseModel(
    [property: JsonPropertyName("minutes")] double? Minutes);
