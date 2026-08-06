using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PsaToolAgent.Psa.Gorelo;
using PsaToolAgent.Tests.Internal;
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

    [Fact]
    public async Task GetSnapshotAsync_MapsASinglePageOfTickets()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseBody = "{\"data\":[{\"id\":\"t1\",\"displayNumber\":\"T-1\",\"leadAssigneeId\":3,\"closedOn\":null,\"priority\":{\"id\":10,\"name\":\"Urgent\"},\"sla\":null}],\"nextCursor\":null,\"hasMore\":false}"
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.gorelo.io/") };
        var options = Options.Create(new GoreloOptions { BaseUrl = "https://example.gorelo.io/", ApiKey = "test-key" });
        var provider = new GoreloPsaDataProvider(http, options, NullLogger<GoreloPsaDataProvider>.Instance);

        var snapshot = await provider.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal(1, snapshot.OpenTicketCount);
        Assert.Equal(1, snapshot.PriorityCounts[1]);
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath.Contains("v1/tickets"));
    }

    [Fact]
    public async Task GetSnapshotAsync_FollowsCursorPagination_UntilHasMoreIsFalse()
    {
        var requestCount = 0;
        var handler = new FakeHttpMessageHandler
        {
            Respond = request =>
            {
                requestCount++;
                if (requestCount == 1)
                {
                    Assert.DoesNotContain("cursor=", request.RequestUri!.Query);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "{\"data\":[{\"id\":\"t1\",\"displayNumber\":\"T-1\",\"leadAssigneeId\":3,\"closedOn\":null,\"priority\":{\"id\":10,\"name\":\"Urgent\"},\"sla\":null}],\"nextCursor\":\"abc\",\"hasMore\":true}",
                            Encoding.UTF8, "application/json")
                    };
                }

                Assert.Contains("cursor=abc", request.RequestUri!.Query);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"data\":[{\"id\":\"t2\",\"displayNumber\":\"T-2\",\"leadAssigneeId\":3,\"closedOn\":null,\"priority\":{\"id\":20,\"name\":\"Normal\"},\"sla\":null}],\"nextCursor\":null,\"hasMore\":false}",
                        Encoding.UTF8, "application/json")
                };
            }
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.gorelo.io/") };
        var options = Options.Create(new GoreloOptions { BaseUrl = "https://example.gorelo.io/", ApiKey = "test-key" });
        var provider = new GoreloPsaDataProvider(http, options, NullLogger<GoreloPsaDataProvider>.Instance);

        var snapshot = await provider.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal(2, snapshot.OpenTicketCount);
        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_ThrowsRatherThanLoopingForever_WhenHasMoreNeverBecomesFalse()
    {
        var handler = new FakeHttpMessageHandler
        {
            Respond = request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[],\"nextCursor\":\"loop\",\"hasMore\":true}", Encoding.UTF8, "application/json")
            }
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.gorelo.io/") };
        var options = Options.Create(new GoreloOptions { BaseUrl = "https://example.gorelo.io/", ApiKey = "test-key" });
        var provider = new GoreloPsaDataProvider(http, options, NullLogger<GoreloPsaDataProvider>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetSnapshotAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetSnapshotAsync_ThrowsImmediately_WhenHasMoreIsTrueButNextCursorIsMissing()
    {
        var requestCount = 0;
        var handler = new FakeHttpMessageHandler
        {
            Respond = request =>
            {
                requestCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"data\":[],\"nextCursor\":null,\"hasMore\":true}",
                        Encoding.UTF8, "application/json")
                };
            }
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.gorelo.io/") };
        var options = Options.Create(new GoreloOptions { BaseUrl = "https://example.gorelo.io/", ApiKey = "test-key" });
        var provider = new GoreloPsaDataProvider(http, options, NullLogger<GoreloPsaDataProvider>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetSnapshotAsync(CancellationToken.None));

        Assert.Equal(1, requestCount);
    }
}
