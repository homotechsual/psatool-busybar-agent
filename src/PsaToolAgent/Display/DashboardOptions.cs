using System.ComponentModel.DataAnnotations;

namespace PsaToolAgent.Display;

public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>First line of the NORMAL-mode display.</summary>
    [Required, MinLength(1)]
    public string HeaderText { get; init; } = "WRC SERVICE DESK";

    /// <summary>How often, in seconds, the CRITICAL display cycles between its P1/P2/P3 pages.
    /// Has no effect on NORMAL or SLA WARNING, which have only one page each.</summary>
    [Range(1, int.MaxValue)]
    public int DisplayCycleSeconds { get; init; } = 5;
}
