namespace PsaToolAgent.Dashboard;

public abstract record DashboardState;

public sealed record NormalDashboardState : DashboardState
{
    public required int Rank1Count { get; init; }
    public required int Rank2Count { get; init; }

    /// <summary>Non-zero when unassigned tickets exist but nothing more urgent does — the
    /// display shows an unassigned-count callout instead of "SLA: OK" in this case.</summary>
    public int UnassignedCount { get; init; }
}

public sealed record SlaWarningDashboardState : DashboardState
{
    public required string TicketId { get; init; }
    public required int MinutesRemaining { get; init; }
}

/// <summary>Covers both the "rank-1 tickets open" and "VIP tickets open" cases — <see cref="Reason"/>
/// distinguishes which triggered it (e.g. "P1 OPEN" or "VIP OPEN"). Also carries the rank-2/rank-3
/// counts, and — since P1 always outranks SLA risk in the precedence order, which would otherwise
/// hide a genuinely breaching ticket entirely — the worst SLA-risk ticket too, if one exists
/// alongside whatever triggered CRITICAL. All independent of the trigger condition, so the display
/// can cycle through everything relevant rather than showing only the one thing that fired.</summary>
public sealed record CriticalDashboardState : DashboardState
{
    public required string Reason { get; init; }
    public required int Count { get; init; }
    public int Rank2Count { get; init; }
    public int Rank3Count { get; init; }
    public string? SlaRiskTicketId { get; init; }
    public int? SlaRiskMinutesRemaining { get; init; }
}
