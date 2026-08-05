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

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 0, CancellationToken.None);

        Assert.Contains("\"text\":\"WRC SERVICE DESK\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"P1:2 P2:5\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"SLA: OK\"", handler.LastRequestBody);
        Assert.Contains("\"color\":\"#FFFFFFFF\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Normal_UsesOrganizationName_WhenProvided()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new NormalDashboardState { Rank1Count = 2, Rank2Count = 5, UnassignedCount = 0 };

        await renderer.RenderAsync(state, organizationName: "Acme Service Desk", cyclePage: 0, CancellationToken.None);

        Assert.Contains("\"text\":\"Acme Service Desk\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"text\":\"WRC SERVICE DESK\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Normal_ShowsUnassignedCallout_WhenUnassignedTicketsPresent()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new NormalDashboardState { Rank1Count = 0, Rank2Count = 0, UnassignedCount = 4 };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 0, CancellationToken.None);

        Assert.Contains("\"text\":\"UNASSIGN:4\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_SlaWarning_ShowsTicketIdAndMinutesRemaining()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new SlaWarningDashboardState { TicketId = "101", MinutesRemaining = 12 };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 0, CancellationToken.None);

        Assert.Contains("\"text\":\"SLA RISK\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Ticket #101\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"12m REMAIN\"", handler.LastRequestBody);
        Assert.Contains("\"color\":\"#FFA500FF\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_SlaWarning_ShowsBreached_WhenMinutesRemainingIsZeroOrNegative()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new SlaWarningDashboardState { TicketId = "101", MinutesRemaining = -30 };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 0, CancellationToken.None);

        Assert.Contains("\"text\":\"SLA RISK\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Ticket #101\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"BREACHED\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"text\":\"-30m REMAIN\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_Page0_ShowsReasonAndCount()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "P1 OPEN", Count = 3, Rank2Count = 7, Rank3Count = 9 };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 0, CancellationToken.None);

        Assert.Contains("\"text\":\"CRITICAL\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"P1 OPEN\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Count: 3\"", handler.LastRequestBody);
        Assert.Contains("\"color\":\"#FF0000FF\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_Page1_ShowsP2Count()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "P1 OPEN", Count = 3, Rank2Count = 7, Rank3Count = 9 };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 1, CancellationToken.None);

        Assert.Contains("\"text\":\"CRITICAL\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"P2 OPEN\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Count: 7\"", handler.LastRequestBody);
        Assert.Contains("\"color\":\"#FFA500FF\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_Page2_ShowsP3Count()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "P1 OPEN", Count = 3, Rank2Count = 7, Rank3Count = 9 };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 2, CancellationToken.None);

        Assert.Contains("\"text\":\"CRITICAL\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"P3 OPEN\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Count: 9\"", handler.LastRequestBody);
        Assert.Contains("\"color\":\"#FFFF00FF\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_SkipsEmptyTiers_StayingOnPage0Regardless()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "P1 OPEN", Count = 3, Rank2Count = 0, Rank3Count = 0 };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 5, CancellationToken.None);

        Assert.Contains("\"text\":\"P1 OPEN\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Count: 3\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"text\":\"P2 OPEN\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"text\":\"P3 OPEN\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_SkipsP3_WhenOnlyP2HasTickets()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "P1 OPEN", Count = 3, Rank2Count = 4, Rank3Count = 0 };

        // 2 pages exist (trigger, P2) — page 2 should wrap back to page 0, never show P3.
        await renderer.RenderAsync(state, organizationName: null, cyclePage: 2, CancellationToken.None);

        Assert.Contains("\"text\":\"P1 OPEN\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Count: 3\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"text\":\"P3 OPEN\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_Page3_WrapsBackToPage0()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "VIP OPEN", Count = 1, Rank2Count = 7, Rank3Count = 9 };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 3, CancellationToken.None);

        Assert.Contains("\"text\":\"VIP OPEN\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Count: 1\"", handler.LastRequestBody);
    }
}
