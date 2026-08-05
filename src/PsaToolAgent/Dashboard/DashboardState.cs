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

/// <summary>Every ticket within the SLA-risk threshold, summarized as a count plus the worst
/// (soonest-to-breach, or already-breached) one's remaining time — not just a single representative
/// ticket, so the display doesn't silently drop how many others are also at risk. Also separately
/// counts how many of those are already past due (<see cref="BreachedCount"/>), so a flat "BREACHED"
/// label can say how many rather than just that at least one has.</summary>
public sealed record SlaWarningDashboardState : DashboardState
{
    public required int Count { get; init; }
    public required int WorstMinutesRemaining { get; init; }
    public int BreachedCount { get; init; }
}

/// <summary>Covers both the "rank-1 tickets open" and "VIP tickets open" cases — <see cref="Reason"/>
/// distinguishes which triggered it (e.g. "P1 OPEN" or "VIP OPEN"). Also carries the rank-2/rank-3
/// counts, and — since P1 always outranks SLA risk in the precedence order, which would otherwise
/// hide breaching tickets entirely — a summary of any SLA-risk tickets too (count plus the worst
/// one's remaining time), if any exist alongside whatever triggered CRITICAL. All independent of
/// the trigger condition, so the display can cycle through everything relevant rather than showing
/// only the one thing that fired.</summary>
public sealed record CriticalDashboardState : DashboardState
{
    public required string Reason { get; init; }
    public required int Count { get; init; }
    public int Rank2Count { get; init; }
    public int Rank3Count { get; init; }
    public int SlaRiskCount { get; init; }
    public int? SlaRiskWorstMinutesRemaining { get; init; }
    public int SlaRiskBreachedCount { get; init; }
}
