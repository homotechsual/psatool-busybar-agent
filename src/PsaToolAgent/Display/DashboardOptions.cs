using System.ComponentModel.DataAnnotations;

namespace PsaToolAgent.Display;

public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>First line of the NORMAL-mode display.</summary>
    [Required, MinLength(1)]
    public string HeaderText { get; init; } = "WRC SERVICE DESK";
}
