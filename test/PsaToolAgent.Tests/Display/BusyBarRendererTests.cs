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

        Assert.Contains("\"text\":\"Unassigned: 4\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_SlaWarning_ShowsCountAndWorstMinutesRemaining()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new SlaWarningDashboardState { Count = 3, WorstMinutesRemaining = 12 };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 0, CancellationToken.None);

        Assert.Contains("\"text\":\"SLA risk\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"3 at risk\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"12m remain\"", handler.LastRequestBody);
        Assert.Contains("\"color\":\"#FFA500FF\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_SlaWarning_ShowsBreachedCount_WhenAnyTicketHasBreached()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new SlaWarningDashboardState { Count = 3, WorstMinutesRemaining = -30, BreachedCount = 2 };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 0, CancellationToken.None);

        Assert.Contains("\"text\":\"SLA risk\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"3 at risk\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"2 breached\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"text\":\"-30m remain\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_Page0_ShowsRealNameWithRankSuffix()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "CRITICAL", Count = 3, Rank1Name = "Critical", Rank2Count = 7, Rank3Count = 9 };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 0, CancellationToken.None);

        Assert.Contains("\"text\":\"CRITICAL (P1)\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Open count: 3\"", handler.LastRequestBody);
        Assert.Contains("\"color\":\"#FF0000FF\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_Page0_OmitsRankSuffix_WhenNoRealNameSupplied()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "P1", Count = 3, Rank1Name = null, Rank2Count = 0, Rank3Count = 0 };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 0, CancellationToken.None);

        Assert.Contains("\"text\":\"P1\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"text\":\"P1 (P1)\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_Page1_ShowsRealP2NameWithRankSuffix()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "CRITICAL", Count = 3, Rank1Name = "Critical", Rank2Count = 7, Rank2Name = "Urgent", Rank3Count = 9 };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 1, CancellationToken.None);

        Assert.Contains("\"text\":\"URGENT (P2)\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Open count: 7\"", handler.LastRequestBody);
        Assert.Contains("\"color\":\"#FFA500FF\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_Page1_FallsBackToGenericP2Label_WhenNoRealNameSupplied()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "CRITICAL", Count = 3, Rank1Name = "Critical", Rank2Count = 7, Rank2Name = null, Rank3Count = 9 };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 1, CancellationToken.None);

        Assert.Contains("\"text\":\"P2\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"text\":\"P2 (P2)\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_Page2_ShowsRealP3NameWithRankSuffix()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "CRITICAL", Count = 3, Rank1Name = "Critical", Rank2Count = 7, Rank3Count = 9, Rank3Name = "High" };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 2, CancellationToken.None);

        Assert.Contains("\"text\":\"HIGH (P3)\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Open count: 9\"", handler.LastRequestBody);
        Assert.Contains("\"color\":\"#FFFF00FF\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_SkipsEmptyTiers_StayingOnPage0Regardless()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "P1", Count = 3, Rank2Count = 0, Rank3Count = 0 };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 5, CancellationToken.None);

        Assert.Contains("\"text\":\"P1\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Open count: 3\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"text\":\"P2\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"text\":\"P3\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_SkipsP3_WhenOnlyP2HasTickets()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "P1", Count = 3, Rank2Count = 4, Rank3Count = 0 };

        // 2 pages exist (trigger, P2) — page 2 should wrap back to page 0, never show P3.
        await renderer.RenderAsync(state, organizationName: null, cyclePage: 2, CancellationToken.None);

        Assert.Contains("\"text\":\"P1\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Open count: 3\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"text\":\"P3\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_Page3_WrapsBackToPage0()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "VIP", Count = 1, IsVipTriggered = true, Rank2Count = 7, Rank3Count = 9 };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 3, CancellationToken.None);

        Assert.Contains("\"text\":\"VIP\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Open count: 1\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"text\":\"VIP (P1)\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_VipTrigger_UsesDistinctColor_NotSharedWithRank1()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "VIP", Count = 1, IsVipTriggered = true };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 0, CancellationToken.None);

        Assert.Contains("\"color\":\"#FF00FFFF\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"color\":\"#FF0000FF\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_CyclesInSlaRiskPage_WhenPresent()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState
        {
            Reason = "CRITICAL",
            Count = 1,
            Rank2Count = 0,
            Rank3Count = 0,
            SlaRiskCount = 2,
            SlaRiskWorstMinutesRemaining = 12
        };

        // Only 2 pages exist here (trigger, SLA risk) since Rank2/Rank3 are both 0.
        await renderer.RenderAsync(state, organizationName: null, cyclePage: 1, CancellationToken.None);

        Assert.Contains("\"text\":\"SLA risk\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"2 at risk\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"12m remain\"", handler.LastRequestBody);
        Assert.Contains("\"color\":\"#FFA500FF\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"text\":\"CRITICAL\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_SlaRiskPage_ShowsBreachedCount_WhenAnyTicketHasBreached()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState
        {
            Reason = "CRITICAL",
            Count = 1,
            SlaRiskCount = 2,
            SlaRiskWorstMinutesRemaining = -15,
            SlaRiskBreachedCount = 1
        };

        await renderer.RenderAsync(state, organizationName: null, cyclePage: 1, CancellationToken.None);

        Assert.Contains("\"text\":\"1 breached\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"text\":\"-15m remain\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_SkipsSlaRiskPage_WhenNoBreachingTicket()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "P1", Count = 1, SlaRiskCount = 0 };

        // With no other tiers and no SLA-risk ticket, only 1 page exists — any cyclePage stays on it.
        await renderer.RenderAsync(state, organizationName: null, cyclePage: 4, CancellationToken.None);

        Assert.Contains("\"text\":\"P1\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"text\":\"SLA risk\"", handler.LastRequestBody);
    }
}
