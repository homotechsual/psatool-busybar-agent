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
        if (rank1Count > 0)
        {
            return new CriticalDashboardState { Reason = "P1 OPEN", Count = rank1Count, Rank2Count = rank2Count, Rank3Count = rank3Count };
        }

        var worstSlaRisk = snapshot.SlaRiskTickets
            .Where(t => t.MinutesRemaining <= slaRiskThresholdMinutes)
            .OrderBy(t => t.MinutesRemaining)
            .FirstOrDefault();
        if (worstSlaRisk is not null)
        {
            return new SlaWarningDashboardState
            {
                TicketId = worstSlaRisk.TicketId,
                MinutesRemaining = worstSlaRisk.MinutesRemaining
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
