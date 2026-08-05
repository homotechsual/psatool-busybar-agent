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

        if (rank1Count > 0)
        {
            return new CriticalDashboardState
            {
                Reason = "P1 OPEN",
                Count = rank1Count,
                Rank2Count = rank2Count,
                Rank3Count = rank3Count,
                SlaRiskCount = slaRiskTickets.Count,
                SlaRiskWorstMinutesRemaining = worstSlaRisk?.MinutesRemaining
            };
        }

        if (worstSlaRisk is not null)
        {
            return new SlaWarningDashboardState
            {
                Count = slaRiskTickets.Count,
                WorstMinutesRemaining = worstSlaRisk.MinutesRemaining
            };
        }

        if (snapshot.VipTickets.Count > 0)
        {
            return new CriticalDashboardState { Reason = "VIP OPEN", Count = snapshot.VipTickets.Count, Rank2Count = rank2Count, Rank3Count = rank3Count };
        }

        return new NormalDashboardState
        {
            Rank1Count = rank1Count,
            Rank2Count = rank2Count,
            UnassignedCount = snapshot.UnassignedTicketCount
        };
    }
}
