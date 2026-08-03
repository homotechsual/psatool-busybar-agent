namespace PsaToolAgent.Psa.Halo;

public sealed class HaloOptions
{
    public const string SectionName = "Psa:Halo";

    public required string BaseUrl { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }

    /// <summary>OAuth2 scope requested for the client-credentials token. Set this to the minimum
    /// your Halo API client is actually granted (e.g. a read-only tickets scope) — do not leave
    /// this at a broad/admin default.</summary>
    public string Scope { get; init; } = "all";
}
