using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PsaToolAgent.Psa.Halo;
using PsaToolAgent.Tests.Internal;
using Xunit;

namespace PsaToolAgent.Tests.Psa.Halo;

public class HaloPsaDataProviderTests
{
    [Fact]
    public void MapSnapshot_CountsPriorityCorrectly()
    {
        var tickets = new[]
        {
            new HaloTicket(1, "Ticket A", PriorityId: 1, AgentId: 3, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: null, OnHold: false),
            new HaloTicket(2, "Ticket B", PriorityId: 1, AgentId: 3, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: null, OnHold: false),
            new HaloTicket(3, "Ticket C", PriorityId: 2, AgentId: 3, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: null, OnHold: false)
        };

        var snapshot = HaloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal(3, snapshot.OpenTicketCount);
        Assert.Equal(2, snapshot.PriorityCounts[1]);
        Assert.Equal(1, snapshot.PriorityCounts[2]);
    }

    [Fact]
    public void MapSnapshot_ExtractsRealPriorityNames_FromTheTicketsOwnPriorityObject()
    {
        var tickets = new[]
        {
            new HaloTicket(1, "Ticket A", PriorityId: 1, AgentId: 3, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: null, OnHold: false,
                Priority: new HaloTicketPriority(PriorityId: 1, Name: "Critical")),
            new HaloTicket(2, "Ticket B", PriorityId: 2, AgentId: 3, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: null, OnHold: false,
                Priority: new HaloTicketPriority(PriorityId: 2, Name: "Urgent")),
            new HaloTicket(3, "Ticket C", PriorityId: 3, AgentId: 3, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: null, OnHold: false,
                Priority: null)
        };

        var snapshot = HaloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal("Critical", snapshot.PriorityNames![1]);
        Assert.Equal("Urgent", snapshot.PriorityNames![2]);
        Assert.False(snapshot.PriorityNames!.ContainsKey(3));
    }

    [Fact]
    public void MapSnapshot_ExcludesOnHoldTickets_FromAllCounts()
    {
        var tickets = new[]
        {
            // A single ticket engineered to hit every count this method produces, to prove
            // on-hold exclusion is applied consistently rather than to just one of them.
            new HaloTicket(1, "On hold P1 VIP unassigned", PriorityId: 1, AgentId: 0, ClientName: "Acme", IsVip: true, SlaTimeLeftHours: 0.5, OnHold: true),
            new HaloTicket(2, "Active P2", PriorityId: 2, AgentId: 3, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: null, OnHold: false)
        };

        var snapshot = HaloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal(1, snapshot.OpenTicketCount);
        Assert.False(snapshot.PriorityCounts.ContainsKey(1));
        Assert.Equal(1, snapshot.PriorityCounts[2]);
        Assert.Empty(snapshot.SlaRiskTickets);
        Assert.Equal(0, snapshot.UnassignedTicketCount);
        Assert.Empty(snapshot.VipTickets);
    }

    [Fact]
    public void MapSnapshot_IdentifiesUnassignedTickets_ByZeroAgentId()
    {
        var tickets = new[]
        {
            new HaloTicket(1, "Ticket A", PriorityId: 3, AgentId: 0, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: null, OnHold: false),
            new HaloTicket(2, "Ticket B", PriorityId: 3, AgentId: 3, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: null, OnHold: false)
        };

        var snapshot = HaloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal(1, snapshot.UnassignedTicketCount);
    }

    [Fact]
    public void MapSnapshot_IdentifiesVipTickets()
    {
        var tickets = new[]
        {
            new HaloTicket(1, "Ticket A", PriorityId: 3, AgentId: 3, ClientName: "Acme", IsVip: true, SlaTimeLeftHours: null, OnHold: false)
        };

        var snapshot = HaloPsaDataProvider.MapSnapshot(tickets);

        var vip = Assert.Single(snapshot.VipTickets);
        Assert.Equal("1", vip.TicketId);
        Assert.Equal("Acme", vip.CustomerName);
    }

    [Fact]
    public void MapSnapshot_ConvertsSlaTimeLeftHoursToMinutes_IncludingNegativeForBreachedTickets()
    {
        var tickets = new[]
        {
            new HaloTicket(1, "Within threshold", PriorityId: 3, AgentId: 3, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: 0.5, OnHold: false),
            new HaloTicket(2, "Outside threshold", PriorityId: 3, AgentId: 3, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: 5.0, OnHold: false),
            new HaloTicket(3, "Already breached", PriorityId: 3, AgentId: 3, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: -0.5, OnHold: false)
        };

        var snapshot = HaloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal(3, snapshot.SlaRiskTickets.Count);
        Assert.Contains(snapshot.SlaRiskTickets, t => t.TicketId == "1" && t.MinutesRemaining == 30);
        Assert.Contains(snapshot.SlaRiskTickets, t => t.TicketId == "2" && t.MinutesRemaining == 300);
        Assert.Contains(snapshot.SlaRiskTickets, t => t.TicketId == "3" && t.MinutesRemaining == -30);
    }

    [Fact]
    public async Task GetSnapshotAsync_AuthenticatesThenFetchesAndMapsTickets()
    {
        var handler = new FakeHttpMessageHandler
        {
            Respond = request =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("auth/token"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"access_token\":\"abc123\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
                    };
                }

                if (request.RequestUri!.AbsolutePath.Contains("Organisation"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"portal_title\":null}", Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"record_count\":1,\"tickets\":[{\"id\":1,\"summary\":\"Test\",\"priority_id\":1,\"agent_id\":3,\"client_name\":\"Acme\",\"is_vip\":false,\"slatimeleft\":null,\"onhold\":false}]}",
                        Encoding.UTF8, "application/json")
                };
            }
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.halopsa.com/") };
        var haloOptions = Options.Create(new HaloOptions
        {
            BaseUrl = "https://example.halopsa.com/",
            ClientId = "test-client",
            ClientSecret = "test-secret"
        });
        var authClient = new HaloAuthClient(http, haloOptions);
        var provider = new HaloPsaDataProvider(http, authClient, haloOptions, NullLogger<HaloPsaDataProvider>.Instance);

        var snapshot = await provider.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal(1, snapshot.OpenTicketCount);
        Assert.Equal(1, snapshot.PriorityCounts[1]);
        Assert.Contains(handler.Requests, r => r.Headers.Authorization?.Parameter == "abc123");
    }

    [Fact]
    public async Task GetSnapshotAsync_FetchesOrganizationName_OnceAndCachesIt()
    {
        var organisationCallCount = 0;
        var handler = new FakeHttpMessageHandler
        {
            Respond = request =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("auth/token"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"access_token\":\"abc123\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
                    };
                }

                if (request.RequestUri!.AbsolutePath.Contains("Organisation"))
                {
                    organisationCallCount++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"portal_title\":\"Acme Service Desk\"}", Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"record_count\":0,\"tickets\":[]}", Encoding.UTF8, "application/json")
                };
            }
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.halopsa.com/") };
        var haloOptions = Options.Create(new HaloOptions
        {
            BaseUrl = "https://example.halopsa.com/",
            ClientId = "test-client",
            ClientSecret = "test-secret",
            OrganisationId = 1
        });
        var authClient = new HaloAuthClient(http, haloOptions);
        var provider = new HaloPsaDataProvider(http, authClient, haloOptions, NullLogger<HaloPsaDataProvider>.Instance);

        var first = await provider.GetSnapshotAsync(CancellationToken.None);
        var second = await provider.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal("Acme Service Desk", first.OrganizationName);
        Assert.Equal("Acme Service Desk", second.OrganizationName);
        Assert.Equal(1, organisationCallCount);
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath.Contains("Organisation/1"));
    }

    [Fact]
    public async Task GetSnapshotAsync_LeavesOrganizationNameNull_WhenOrganisationFetchFails()
    {
        var handler = new FakeHttpMessageHandler
        {
            Respond = request =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("auth/token"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"access_token\":\"abc123\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
                    };
                }

                if (request.RequestUri!.AbsolutePath.Contains("Organisation"))
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"record_count\":0,\"tickets\":[]}", Encoding.UTF8, "application/json")
                };
            }
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.halopsa.com/") };
        var haloOptions = Options.Create(new HaloOptions
        {
            BaseUrl = "https://example.halopsa.com/",
            ClientId = "test-client",
            ClientSecret = "test-secret"
        });
        var authClient = new HaloAuthClient(http, haloOptions);
        var provider = new HaloPsaDataProvider(http, authClient, haloOptions, NullLogger<HaloPsaDataProvider>.Instance);

        var snapshot = await provider.GetSnapshotAsync(CancellationToken.None);

        Assert.Null(snapshot.OrganizationName);
        Assert.Equal(0, snapshot.OpenTicketCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_RetriesOnceAfterInvalidatingToken_When401Received()
    {
        var authCallCount = 0;
        var ticketsCallCount = 0;
        var handler = new FakeHttpMessageHandler
        {
            Respond = request =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("auth/token"))
                {
                    authCallCount++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent($"{{\"access_token\":\"token{authCallCount}\",\"expires_in\":3600}}", Encoding.UTF8, "application/json")
                    };
                }

                if (request.RequestUri!.AbsolutePath.Contains("Organisation"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"portal_title\":null}", Encoding.UTF8, "application/json")
                    };
                }

                ticketsCallCount++;
                if (ticketsCallCount == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"record_count\":0,\"tickets\":[]}",
                        Encoding.UTF8, "application/json")
                };
            }
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.halopsa.com/") };
        var haloOptions = Options.Create(new HaloOptions
        {
            BaseUrl = "https://example.halopsa.com/",
            ClientId = "test-client",
            ClientSecret = "test-secret"
        });
        var authClient = new HaloAuthClient(http, haloOptions);
        var provider = new HaloPsaDataProvider(http, authClient, haloOptions, NullLogger<HaloPsaDataProvider>.Instance);

        var snapshot = await provider.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal(0, snapshot.OpenTicketCount);
        Assert.Equal(2, authCallCount);
        Assert.Equal(2, ticketsCallCount);
    }
}
