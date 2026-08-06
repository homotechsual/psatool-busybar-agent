using System.ComponentModel.DataAnnotations;

namespace PsaToolAgent.Psa.Gorelo;

public sealed class GoreloOptions
{
    public const string SectionName = "Psa:Gorelo";

    [Required, Url]
    public required string BaseUrl { get; init; }

    /// <summary>Sent as the <c>X-API-Key</c> header on every request — a secret, set via
    /// env/user-secrets, never committed.</summary>
    [Required]
    public required string ApiKey { get; init; }

    /// <summary>Tickets requested per page from <c>GET /v1/tickets</c>. Gorelo's API documents no
    /// upper bound, but an unbounded default would be reckless — 200 keeps each poll to a handful
    /// of requests for a typical (sub-2,000-ticket) tenant.</summary>
    [Range(1, int.MaxValue)]
    public int PageSize { get; init; } = 200;
}
