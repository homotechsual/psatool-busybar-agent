using PsaToolAgent.Dashboard;
using PsaToolAgent.Psa;
using Xunit;

namespace PsaToolAgent.Tests.Dashboard;

public class PriorityEngineTests
{
    private static PsaSnapshot EmptySnapshot() => new()
    {
        OpenTicketCount = 0,
        PriorityCounts = new Dictionary<int, int>(),
        SlaRiskTickets = Array.Empty<SlaRiskTicket>(),
        UnassignedTicketCount = 0,
        VipTickets = Array.Empty<VipTicket>()
    };

    [Fact]
    public void Evaluate_ReturnsNormal_WhenSnapshotIsEmpty()
    {
        var state = PriorityEngine.Evaluate(EmptySnapshot(), slaRiskThresholdMinutes: 60);

        var normal = Assert.IsType<NormalDashboardState>(state);
        Assert.Equal(0, normal.Rank1Count);
        Assert.Equal(0, normal.Rank2Count);
        Assert.Equal(0, normal.UnassignedCount);
    }

    [Fact]
    public void Evaluate_ReturnsCritical_WhenRank1TicketsPresent()
    {
        var snapshot = EmptySnapshot() with { PriorityCounts = new Dictionary<int, int> { [1] = 3 } };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        var critical = Assert.IsType<CriticalDashboardState>(state);
        Assert.Equal("P1 OPEN", critical.Reason);
        Assert.Equal(3, critical.Count);
    }

    [Fact]
    public void Evaluate_Critical_CarriesRank2AndRank3Counts_ForDisplayCycling()
    {
        var snapshot = EmptySnapshot() with
        {
            PriorityCounts = new Dictionary<int, int> { [1] = 3, [2] = 7, [3] = 2 }
        };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        var critical = Assert.IsType<CriticalDashboardState>(state);
        Assert.Equal(7, critical.Rank2Count);
        Assert.Equal(2, critical.Rank3Count);
    }

    [Fact]
    public void Evaluate_ReturnsSlaWarning_WhenTicketBreachesWithinThreshold()
    {
        var snapshot = EmptySnapshot() with { SlaRiskTickets = new[] { new SlaRiskTicket("101", 45) } };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        var slaWarning = Assert.IsType<SlaWarningDashboardState>(state);
        Assert.Equal(1, slaWarning.Count);
        Assert.Equal(45, slaWarning.WorstMinutesRemaining);
    }

    [Fact]
    public void Evaluate_IgnoresSlaTicket_WhenOutsideThreshold()
    {
        var snapshot = EmptySnapshot() with { SlaRiskTickets = new[] { new SlaRiskTicket("101", 90) } };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        Assert.IsType<NormalDashboardState>(state);
    }

    [Fact]
    public void Evaluate_SlaWarning_CountsAllAtRiskTickets_AndReportsTheWorstOnesTime()
    {
        var snapshot = EmptySnapshot() with
        {
            SlaRiskTickets = new[] { new SlaRiskTicket("101", 45), new SlaRiskTicket("102", 10) }
        };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        var slaWarning = Assert.IsType<SlaWarningDashboardState>(state);
        Assert.Equal(2, slaWarning.Count);
        Assert.Equal(10, slaWarning.WorstMinutesRemaining);
    }

    [Fact]
    public void Evaluate_ReturnsCritical_WhenVipTicketsPresent()
    {
        var snapshot = EmptySnapshot() with
        {
            PriorityCounts = new Dictionary<int, int> { [2] = 4 },
            VipTickets = new[] { new VipTicket("201", "Acme Corp") }
        };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        var critical = Assert.IsType<CriticalDashboardState>(state);
        Assert.Equal("VIP OPEN", critical.Reason);
        Assert.Equal(1, critical.Count);
        Assert.Equal(4, critical.Rank2Count);
    }

    [Fact]
    public void Evaluate_ReturnsNormalWithUnassignedCount_WhenOnlyUnassignedTicketsPresent()
    {
        var snapshot = EmptySnapshot() with { UnassignedTicketCount = 5 };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        var normal = Assert.IsType<NormalDashboardState>(state);
        Assert.Equal(5, normal.UnassignedCount);
    }

    [Fact]
    public void Evaluate_Rank1TakesPrecedenceOverEverythingElse()
    {
        var snapshot = new PsaSnapshot
        {
            OpenTicketCount = 10,
            PriorityCounts = new Dictionary<int, int> { [1] = 1 },
            SlaRiskTickets = new[] { new SlaRiskTicket("101", 5) },
            UnassignedTicketCount = 3,
            VipTickets = new[] { new VipTicket("201", "Acme Corp") }
        };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        var critical = Assert.IsType<CriticalDashboardState>(state);
        Assert.Equal("P1 OPEN", critical.Reason);
    }

    [Fact]
    public void Evaluate_Critical_CarriesAllSlaRiskTickets_SoAP1AlertDoesNotHideBreaches()
    {
        var snapshot = new PsaSnapshot
        {
            OpenTicketCount = 10,
            PriorityCounts = new Dictionary<int, int> { [1] = 1 },
            SlaRiskTickets = new[] { new SlaRiskTicket("101", 45), new SlaRiskTicket("102", 5) },
            UnassignedTicketCount = 0,
            VipTickets = Array.Empty<VipTicket>()
        };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        var critical = Assert.IsType<CriticalDashboardState>(state);
        Assert.Equal("P1 OPEN", critical.Reason);
        Assert.Equal(2, critical.SlaRiskCount);
        Assert.Equal(5, critical.SlaRiskWorstMinutesRemaining);
    }

    [Fact]
    public void Evaluate_Critical_LeavesSlaRiskCountZero_WhenNoTicketIsWithinThreshold()
    {
        var snapshot = new PsaSnapshot
        {
            OpenTicketCount = 5,
            PriorityCounts = new Dictionary<int, int> { [1] = 1 },
            SlaRiskTickets = new[] { new SlaRiskTicket("101", 90) },
            UnassignedTicketCount = 0,
            VipTickets = Array.Empty<VipTicket>()
        };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        var critical = Assert.IsType<CriticalDashboardState>(state);
        Assert.Equal(0, critical.SlaRiskCount);
        Assert.Null(critical.SlaRiskWorstMinutesRemaining);
    }

    [Fact]
    public void Evaluate_SlaWarningTakesPrecedenceOverVipAndUnassigned()
    {
        var snapshot = new PsaSnapshot
        {
            OpenTicketCount = 10,
            PriorityCounts = new Dictionary<int, int>(),
            SlaRiskTickets = new[] { new SlaRiskTicket("101", 5) },
            UnassignedTicketCount = 3,
            VipTickets = new[] { new VipTicket("201", "Acme Corp") }
        };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        Assert.IsType<SlaWarningDashboardState>(state);
    }

    [Fact]
    public void Evaluate_VipTakesPrecedenceOverUnassigned()
    {
        var snapshot = new PsaSnapshot
        {
            OpenTicketCount = 10,
            PriorityCounts = new Dictionary<int, int>(),
            SlaRiskTickets = Array.Empty<SlaRiskTicket>(),
            UnassignedTicketCount = 3,
            VipTickets = new[] { new VipTicket("201", "Acme Corp") }
        };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        Assert.IsType<CriticalDashboardState>(state);
    }
}
