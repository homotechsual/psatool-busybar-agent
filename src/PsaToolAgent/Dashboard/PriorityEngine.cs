using PsaToolAgent.Psa;

namespace PsaToolAgent.Dashboard;

/// <summary>
/// Pure function turning a <see cref="PsaSnapshot"/> into a <see cref="DashboardState"/>. No I/O,
/// no PSA or BUSY Bar dependency — evaluates the 5-level priority order, first match wins.
/// </summary>
public static class PriorityEngine
{
    public static DashboardState Evaluate(PsaSnapshot snapshot, int slaRiskThresholdMinutes)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var rank1Count = snapshot.PriorityCounts.GetValueOrDefault(1);
        var rank2Count = snapshot.PriorityCounts.GetValueOrDefault(2);
        var rank3Count = snapshot.PriorityCounts.GetValueOrDefault(3);
        var rank2Name = snapshot.PriorityNames?.GetValueOrDefault(2);
        var rank3Name = snapshot.PriorityNames?.GetValueOrDefault(3);

        // Computed up front (not just when SLA risk is the primary trigger) so a P1-triggered
        // CRITICAL can still carry SLA-risk info instead of silently hiding it — P1 outranks SLA
        // risk in precedence, but that must not mean breaching tickets go invisible. Every ticket
        // within threshold is kept (not just the worst one) so the display can report how many are
        // actually at risk, not just a single representative ticket.
        var slaRiskTickets = snapshot.SlaRiskTickets
            .Where(t => t.MinutesRemaining <= slaRiskThresholdMinutes)
            .OrderBy(t => t.MinutesRemaining)
            .ToList();
        var worstSlaRisk = slaRiskTickets.FirstOrDefault();
        var slaRiskBreachedCount = slaRiskTickets.Count(t => t.MinutesRemaining <= 0);

        if (rank1Count > 0)
        {
            var rank1Name = snapshot.PriorityNames?.GetValueOrDefault(1);
            return new CriticalDashboardState
            {
                Reason = (rank1Name ?? "P1").ToUpperInvariant(),
                Count = rank1Count,
                IsVipTriggered = false,
                Rank1Name = rank1Name,
                Rank2Count = rank2Count,
                Rank2Name = rank2Name,
                Rank3Count = rank3Count,
                Rank3Name = rank3Name,
                SlaRiskCount = slaRiskTickets.Count,
                SlaRiskWorstMinutesRemaining = worstSlaRisk?.MinutesRemaining,
                SlaRiskBreachedCount = slaRiskBreachedCount
            };
        }

        if (worstSlaRisk is not null)
        {
            return new SlaWarningDashboardState
            {
                Count = slaRiskTickets.Count,
                WorstMinutesRemaining = worstSlaRisk.MinutesRemaining,
                BreachedCount = slaRiskBreachedCount
            };
        }

        if (snapshot.VipTickets.Count > 0)
        {
            return new CriticalDashboardState
            {
                Reason = "VIP",
                Count = snapshot.VipTickets.Count,
                IsVipTriggered = true,
                Rank2Count = rank2Count,
                Rank2Name = rank2Name,
                Rank3Count = rank3Count,
                Rank3Name = rank3Name
            };
        }

        return new NormalDashboardState
        {
            Rank1Count = rank1Count,
            Rank2Count = rank2Count,
            UnassignedCount = snapshot.UnassignedTicketCount
        };
    }
}
