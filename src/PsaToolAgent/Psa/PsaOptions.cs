using System.ComponentModel.DataAnnotations;

namespace PsaToolAgent.Psa;

public sealed class PsaOptions
{
    public const string SectionName = "Psa";

    /// <summary>Which <see cref="IPsaDataProvider"/> implementation to use. Only "Halo" is
    /// supported for v1.</summary>
    [Required]
    public required string Provider { get; init; }

    [Range(1, int.MaxValue)]
    public int PollIntervalSeconds { get; init; } = 60;

    /// <summary>A ticket is "SLA-risk" when its remaining time to breach is at or below this
    /// threshold.</summary>
    [Range(0, int.MaxValue)]
    public int SlaRiskThresholdMinutes { get; init; } = 60;
}
