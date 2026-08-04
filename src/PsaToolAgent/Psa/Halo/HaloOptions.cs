using System.ComponentModel.DataAnnotations;

namespace PsaToolAgent.Psa.Halo;

public sealed class HaloOptions
{
    public const string SectionName = "Psa:Halo";

    [Required, Url]
    public required string BaseUrl { get; init; }

    [Required]
    public required string ClientId { get; init; }

    [Required]
    public required string ClientSecret { get; init; }

    /// <summary>OAuth2 scope requested for the client-credentials token. Set this to the minimum
    /// your Halo API client is actually granted (e.g. a read-only tickets scope) — do not leave
    /// this at a broad/admin default.</summary>
    [Required]
    public string Scope { get; init; } = "read:tickets";

    /// <summary>The Halo Organisation ID whose <c>portal_title</c> is used as the dashboard header
    /// (falls back to <c>Dashboard:HeaderText</c> if the fetch fails). Fetched once at startup and
    /// cached for the process lifetime.</summary>
    [Range(1, int.MaxValue)]
    public int OrganisationId { get; init; } = 1;
}
