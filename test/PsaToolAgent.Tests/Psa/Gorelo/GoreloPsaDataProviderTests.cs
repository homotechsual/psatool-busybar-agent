using PsaToolAgent.Psa.Gorelo;
using Xunit;

namespace PsaToolAgent.Tests.Psa.Gorelo;

public class GoreloPsaDataProviderTests
{
    [Fact]
    public void MapSnapshot_CountsPriorityByName_UsingTheFixedRankTable()
    {
        var tickets = new[]
        {
            new GoreloTicket("1", "T-1", 3, null, new GoreloCodeModel(10, "Urgent"), null),
            new GoreloTicket("2", "T-2", 3, null, new GoreloCodeModel(10, "Urgent"), null),
            new GoreloTicket("3", "T-3", 3, null, new GoreloCodeModel(20, "Normal"), null)
        };

        var snapshot = GoreloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal(3, snapshot.OpenTicketCount);
        Assert.Equal(2, snapshot.PriorityCounts[1]);
        Assert.Equal(1, snapshot.PriorityCounts[3]);
        Assert.Equal("Urgent", snapshot.PriorityNames![1]);
        Assert.Equal("Normal", snapshot.PriorityNames![3]);
    }

    [Fact]
    public void MapSnapshot_ExcludesUnrecognizedPriorityName_FromPriorityCountsButNotOpenTicketCount()
    {
        var tickets = new[]
        {
            new GoreloTicket("1", "T-1", 3, null, new GoreloCodeModel(99, "Emergency"), null)
        };

        var snapshot = GoreloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal(1, snapshot.OpenTicketCount);
        Assert.Empty(snapshot.PriorityCounts);
    }

    [Fact]
    public void MapSnapshot_ExcludesClosedTickets_FromAllCounts()
    {
        var tickets = new[]
        {
            new GoreloTicket("1", "T-1", null, DateTimeOffset.UtcNow, new GoreloCodeModel(10, "Urgent"),
                new GoreloSlaModel(new GoreloSlaFirstResponseModel(5))),
            new GoreloTicket("2", "T-2", 3, null, new GoreloCodeModel(20, "Normal"), null)
        };

        var snapshot = GoreloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal(1, snapshot.OpenTicketCount);
        Assert.False(snapshot.PriorityCounts.ContainsKey(1));
        Assert.Equal(1, snapshot.PriorityCounts[3]);
        Assert.Empty(snapshot.SlaRiskTickets);
        Assert.Equal(0, snapshot.UnassignedTicketCount);
    }

    [Fact]
    public void MapSnapshot_IdentifiesUnassignedTickets_ByNullLeadAssigneeId()
    {
        var tickets = new[]
        {
            new GoreloTicket("1", "T-1", null, null, new GoreloCodeModel(20, "Normal"), null),
            new GoreloTicket("2", "T-2", 3, null, new GoreloCodeModel(20, "Normal"), null)
        };

        var snapshot = GoreloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal(1, snapshot.UnassignedTicketCount);
    }

    [Fact]
    public void MapSnapshot_MapsSlaFirstResponseMinutes_IncludingNegativeForBreachedTickets()
    {
        var tickets = new[]
        {
            new GoreloTicket("1", "T-1", 3, null, new GoreloCodeModel(20, "Normal"),
                new GoreloSlaModel(new GoreloSlaFirstResponseModel(30))),
            new GoreloTicket("2", "T-2", 3, null, new GoreloCodeModel(20, "Normal"),
                new GoreloSlaModel(new GoreloSlaFirstResponseModel(-15))),
            new GoreloTicket("3", "T-3", 3, null, new GoreloCodeModel(20, "Normal"), null)
        };

        var snapshot = GoreloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal(2, snapshot.SlaRiskTickets.Count);
        Assert.Contains(snapshot.SlaRiskTickets, t => t.TicketId == "T-1" && t.MinutesRemaining == 30);
        Assert.Contains(snapshot.SlaRiskTickets, t => t.TicketId == "T-2" && t.MinutesRemaining == -15);
    }

    [Fact]
    public void MapSnapshot_PrefersDisplayNumberOverId_ForTicketIdentifiers()
    {
        var tickets = new[]
        {
            new GoreloTicket("guid-1", "T-42", 3, null, new GoreloCodeModel(20, "Normal"),
                new GoreloSlaModel(new GoreloSlaFirstResponseModel(10)))
        };

        var snapshot = GoreloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal("T-42", Assert.Single(snapshot.SlaRiskTickets).TicketId);
    }

    [Fact]
    public void MapSnapshot_FallsBackToId_WhenDisplayNumberIsNull()
    {
        var tickets = new[]
        {
            new GoreloTicket("guid-1", null, 3, null, new GoreloCodeModel(20, "Normal"),
                new GoreloSlaModel(new GoreloSlaFirstResponseModel(10)))
        };

        var snapshot = GoreloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal("guid-1", Assert.Single(snapshot.SlaRiskTickets).TicketId);
    }

    [Fact]
    public void MapSnapshot_AlwaysReturnsEmptyVipTicketsAndNullOrganizationName()
    {
        var tickets = new[]
        {
            new GoreloTicket("1", "T-1", 3, null, new GoreloCodeModel(10, "Urgent"), null)
        };

        var snapshot = GoreloPsaDataProvider.MapSnapshot(tickets);

        Assert.Empty(snapshot.VipTickets);
        Assert.Null(snapshot.OrganizationName);
    }
}
