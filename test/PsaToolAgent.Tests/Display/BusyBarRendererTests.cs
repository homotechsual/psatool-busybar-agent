using Microsoft.Extensions.Options;
using PsaToolAgent.Dashboard;
using PsaToolAgent.Display;
using PsaToolAgent.Tests.Internal;
using Xunit;

namespace PsaToolAgent.Tests.Display;

public class BusyBarRendererTests
{
    private static (BusyBarRenderer renderer, FakeHttpMessageHandler handler) CreateRenderer()
    {
        var handler = new FakeHttpMessageHandler { ResponseBody = "{\"result\":\"OK\"}" };
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://10.0.4.20/") };
        var bar = new Busy.Bar.BusyBar(http, new Busy.Bar.BusyBarOptions());
        var options = Options.Create(new DashboardOptions());
        return (new BusyBarRenderer(bar, options), handler);
    }

    [Fact]
    public async Task RenderAsync_Normal_ShowsPriorityCountsAndSlaOk()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new NormalDashboardState { Rank1Count = 2, Rank2Count = 5, UnassignedCount = 0 };

        await renderer.RenderAsync(state, organizationName: null, CancellationToken.None);

        Assert.Contains("\"text\":\"WRC SERVICE DESK\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"P1:2 P2:5\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"SLA: OK\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Normal_UsesOrganizationName_WhenProvided()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new NormalDashboardState { Rank1Count = 2, Rank2Count = 5, UnassignedCount = 0 };

        await renderer.RenderAsync(state, organizationName: "Acme Service Desk", CancellationToken.None);

        Assert.Contains("\"text\":\"Acme Service Desk\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"text\":\"WRC SERVICE DESK\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Normal_ShowsUnassignedCallout_WhenUnassignedTicketsPresent()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new NormalDashboardState { Rank1Count = 0, Rank2Count = 0, UnassignedCount = 4 };

        await renderer.RenderAsync(state, organizationName: null, CancellationToken.None);

        Assert.Contains("\"text\":\"UNASSIGN:4\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_SlaWarning_ShowsTicketIdAndMinutesRemaining()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new SlaWarningDashboardState { TicketId = "101", MinutesRemaining = 12 };

        await renderer.RenderAsync(state, organizationName: null, CancellationToken.None);

        Assert.Contains("\"text\":\"SLA RISK\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Ticket #101\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"12m REMAIN\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_SlaWarning_ShowsBreached_WhenMinutesRemainingIsZeroOrNegative()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new SlaWarningDashboardState { TicketId = "101", MinutesRemaining = -30 };

        await renderer.RenderAsync(state, organizationName: null, CancellationToken.None);

        Assert.Contains("\"text\":\"SLA RISK\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Ticket #101\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"BREACHED\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"text\":\"-30m REMAIN\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_ShowsReasonAndCount()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "P1 OPEN", Count = 3 };

        await renderer.RenderAsync(state, organizationName: null, CancellationToken.None);

        Assert.Contains("\"text\":\"CRITICAL\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"P1 OPEN\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Count:3\"", handler.LastRequestBody);
    }
}
