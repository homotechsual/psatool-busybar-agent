using Microsoft.Extensions.Options;
using PsaToolAgent.Dashboard;

namespace PsaToolAgent.Display;

public sealed class BusyBarRenderer
{
    private const string ApplicationName = "psatool_busybar_agent";

    // #RRGGBBAA, matching TextElement.Color's format. A traffic-light scheme so urgency is
    // visible at a glance without reading the text: white/neutral when calm, amber for an
    // SLA-risk warning. Within CRITICAL, each cycled page is colored by its own severity —
    // red for a rank-1 trigger, stepping down through orange (P2) and yellow (P3). VIP is its
    // own distinct category (not a priority tier), so it gets its own color rather than sharing
    // rank 1's red.
    private const string NormalColor = "#FFFFFFFF";
    private const string SlaWarningColor = "#FFA500FF";
    private const string CriticalTriggerColor = "#FF0000FF";
    private const string CriticalP2Color = "#FFA500FF";
    private const string CriticalP3Color = "#FFFF00FF";
    private const string VipColor = "#FF00FFFF";

    private readonly Busy.Bar.BusyBar _bar;
    private readonly DashboardOptions _options;

    public BusyBarRenderer(Busy.Bar.BusyBar bar, IOptions<DashboardOptions> options)
    {
        _bar = bar;
        _options = options.Value;
    }

    /// <param name="organizationName">The PSA's display-ready organization name (e.g. Halo's
    /// <c>portal_title</c>), if the provider supplied one via <see cref="PsaToolAgent.Psa.PsaSnapshot.OrganizationName"/>.
    /// Used as the NORMAL-mode header instead of the configured <see cref="DashboardOptions.HeaderText"/>
    /// when present; ignored by every other <see cref="DashboardState"/>.</param>
    /// <param name="cyclePage">Which page to show for a <see cref="CriticalDashboardState"/> — page 0
    /// is always whichever condition triggered CRITICAL; P2 and P3 pages are added only when that
    /// tier actually has open tickets, and the caller cycles through however many pages exist,
    /// wrapping via modulo. Ignored by every other <see cref="DashboardState"/>.</param>
    public Task RenderAsync(DashboardState state, string? organizationName, int cyclePage, CancellationToken cancellationToken)
        => state switch
        {
            NormalDashboardState normal => RenderNormalAsync(normal, organizationName, cancellationToken),
            SlaWarningDashboardState slaWarning => RenderSlaWarningAsync(slaWarning, cancellationToken),
            CriticalDashboardState critical => RenderCriticalAsync(critical, organizationName, cyclePage, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

    /// <summary>Removes this application's own elements from the display, without touching
    /// anything drawn by other applications. Call this on shutdown so the BUSY Bar doesn't sit
    /// showing stale dashboard state indefinitely after the service stops.</summary>
    public Task ClearAsync(CancellationToken cancellationToken)
        => _bar.DisplayClearAsync(new Busy.Bar.DisplayClearParams(ApplicationName), cancellationToken: cancellationToken);

    private Task RenderNormalAsync(NormalDashboardState state, string? organizationName, CancellationToken cancellationToken)
    {
        var headerText = string.IsNullOrWhiteSpace(organizationName) ? _options.HeaderText : organizationName;
        var thirdLine = state.UnassignedCount > 0 ? $"Unassigned: {state.UnassignedCount}" : "SLA: OK";
        return DrawAsync(new[] { headerText, $"P1:{state.Rank1Count} P2:{state.Rank2Count}", thirdLine }, NormalColor, cancellationToken);
    }

    private Task RenderSlaWarningAsync(SlaWarningDashboardState state, CancellationToken cancellationToken)
    {
        var thirdLine = state.BreachedCount > 0 ? $"{state.BreachedCount} breached" : $"{state.WorstMinutesRemaining}m remain";
        return DrawAsync(new[] { "SLA risk", $"{state.Count} at risk", thirdLine }, SlaWarningColor, cancellationToken);
    }

    private Task RenderCriticalAsync(CriticalDashboardState state, string? organizationName, int cyclePage, CancellationToken cancellationToken)
    {
        // Page 0 (whatever triggered CRITICAL) is always shown; P2/P3/SLA-risk pages are only
        // added when that tier actually has something to show, so the cycle never lands on a
        // pointless "Count: 0". The SLA-risk page exists because P1 outranks SLA risk in the
        // overall precedence order — without cycling it in here, a P1 alert would silently hide a
        // genuinely breaching ticket for as long as the P1 stayed open.
        //
        // Priority-tier pages: "{NAME} (P{rank})" / "Open count: {n}" / the org name — the org name
        // fills the line that would otherwise sit empty now the first two lines are compact, and
        // restores the org context that's otherwise only shown in NORMAL mode. The "(P{rank})"
        // suffix only appears when a real name is actually known, so the generic fallback case (no
        // real name from the provider) doesn't read as the redundant "P2 (P2)". Sentence case
        // everywhere except the priority name itself (kept upper case so it stands out as the
        // at-a-glance signal). The SLA-risk page is unaffected by the org-name change — a
        // different kind of signal (urgency, not a priority tier), already using all 3 lines for
        // its own info, with no room to also show the org name.
        var headerText = string.IsNullOrWhiteSpace(organizationName) ? _options.HeaderText : organizationName;
        var triggerColor = state.IsVipTriggered ? VipColor : CriticalTriggerColor;
        var triggerLine1 = !state.IsVipTriggered && state.Rank1Name is not null ? $"{state.Reason} (P1)" : state.Reason;
        var pages = new List<(string[] Lines, string Color)>
        {
            (new[] { triggerLine1, $"Open count: {state.Count}", headerText }, triggerColor)
        };
        if (state.Rank2Count > 0)
        {
            var rank2Label = (state.Rank2Name ?? "P2").ToUpperInvariant();
            var rank2Line1 = state.Rank2Name is not null ? $"{rank2Label} (P2)" : rank2Label;
            pages.Add((new[] { rank2Line1, $"Open count: {state.Rank2Count}", headerText }, CriticalP2Color));
        }
        if (state.Rank3Count > 0)
        {
            var rank3Label = (state.Rank3Name ?? "P3").ToUpperInvariant();
            var rank3Line1 = state.Rank3Name is not null ? $"{rank3Label} (P3)" : rank3Label;
            pages.Add((new[] { rank3Line1, $"Open count: {state.Rank3Count}", headerText }, CriticalP3Color));
        }
        if (state.SlaRiskCount > 0)
        {
            var slaLine3 = state.SlaRiskBreachedCount > 0 ? $"{state.SlaRiskBreachedCount} breached" : $"{state.SlaRiskWorstMinutesRemaining}m remain";
            pages.Add((new[] { "SLA risk", $"{state.SlaRiskCount} at risk", slaLine3 }, SlaWarningColor));
        }

        var normalizedPage = ((cyclePage % pages.Count) + pages.Count) % pages.Count;
        var (lines, color) = pages[normalizedPage];
        return DrawAsync(lines, color, cancellationToken);
    }

    private const int CanvasHeight = 16;
    private const int LineHeight = 5;

    private Task DrawAsync(IReadOnlyList<string> lines, string color, CancellationToken cancellationToken)
    {
        // The BUSY Bar's canvas is only 72x16px (DisplayCanvas.Width/Height) — TextFont.Normal is
        // too tall to stack even 3 lines in that height at all (confirmed on real hardware: lines
        // overlapped completely). Tiny is the smallest available font, at roughly LineHeight px per
        // line. Vertically centering the block (rather than always anchoring it to Y=0) means a
        // page with fewer lines than the canvas can fit doesn't end up pushed to the top with dead
        // space at the bottom — 3 lines happen to fill the canvas almost exactly, so this leaves
        // that case unchanged.
        var blockHeight = lines.Count * LineHeight;
        var yOffset = Math.Max(0, (CanvasHeight - blockHeight) / 2);

        var elements = new List<Busy.Bar.DisplayElement>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            elements.Add(new Busy.Bar.TextElement
            {
                Id = i.ToString(),
                Text = lines[i],
                Font = Busy.Bar.TextFont.Tiny,
                Y = yOffset + (i * LineHeight),
                Color = color
            });
        }

        return _bar.DisplayDrawAsync(new Busy.Bar.DisplayDrawParams
        {
            ApplicationName = ApplicationName,
            Elements = elements
        }, cancellationToken: cancellationToken);
    }
}
