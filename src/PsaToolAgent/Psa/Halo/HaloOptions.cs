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
}
