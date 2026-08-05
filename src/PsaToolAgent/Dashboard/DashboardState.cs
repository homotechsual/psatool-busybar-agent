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
/// counts (independent of which condition actually triggered CRITICAL) so the display can cycle
/// through all three tiers rather than showing only the one that fired.</summary>
public sealed record CriticalDashboardState : DashboardState
{
    public required string Reason { get; init; }
    public required int Count { get; init; }
    public int Rank2Count { get; init; }
    public int Rank3Count { get; init; }
}
