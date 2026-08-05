using Microsoft.Extensions.Options;
using PsaToolAgent.Dashboard;

namespace PsaToolAgent.Display;

public sealed class BusyBarRenderer
{
    private const string ApplicationName = "psatool_busybar_agent";

    // #RRGGBBAA, matching TextElement.Color's format. A traffic-light scheme so urgency is
    // visible at a glance without reading the text: white/neutral when calm, amber for an
    // SLA-risk warning. Within CRITICAL, each cycled page is colored by its own severity —
    // red for the trigger (P1/VIP), stepping down through orange (P2) and yellow (P3).
    private const string NormalColor = "#FFFFFFFF";
    private const string SlaWarningColor = "#FFA500FF";
    private const string CriticalTriggerColor = "#FF0000FF";
    private const string CriticalP2Color = "#FFA500FF";
    private const string CriticalP3Color = "#FFFF00FF";

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
            CriticalDashboardState critical => RenderCriticalAsync(critical, cyclePage, cancellationToken),
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
        var thirdLine = state.UnassignedCount > 0 ? $"UNASSIGN:{state.UnassignedCount}" : "SLA: OK";
        return DrawAsync(new[] { headerText, $"P1:{state.Rank1Count} P2:{state.Rank2Count}", thirdLine }, NormalColor, cancellationToken);
    }

    private Task RenderSlaWarningAsync(SlaWarningDashboardState state, CancellationToken cancellationToken)
    {
        var thirdLine = state.BreachedCount > 0 ? $"{state.BreachedCount} BREACHED" : $"{state.WorstMinutesRemaining}m REMAIN";
        return DrawAsync(new[] { "SLA RISK", $"{state.Count} AT RISK", thirdLine }, SlaWarningColor, cancellationToken);
    }

    private Task RenderCriticalAsync(CriticalDashboardState state, int cyclePage, CancellationToken cancellationToken)
    {
        // Page 0 (whatever triggered CRITICAL) is always shown; P2/P3/SLA-risk pages are only
        // added when that tier actually has something to show, so the cycle never lands on a
        // pointless "Count: 0". The SLA-risk page exists because P1 outranks SLA risk in the
        // overall precedence order — without cycling it in here, a P1 alert would silently hide a
        // genuinely breaching ticket for as long as the P1 stayed open.
        var pages = new List<(string Line1, string Line2, string Line3, string Color)>
        {
            ("CRITICAL", state.Reason, $"Count: {state.Count}", CriticalTriggerColor)
        };
        if (state.Rank2Count > 0)
        {
            pages.Add(("CRITICAL", "P2 OPEN", $"Count: {state.Rank2Count}", CriticalP2Color));
        }
        if (state.Rank3Count > 0)
        {
            pages.Add(("CRITICAL", "P3 OPEN", $"Count: {state.Rank3Count}", CriticalP3Color));
        }
        if (state.SlaRiskCount > 0)
        {
            var slaLine3 = state.SlaRiskBreachedCount > 0 ? $"{state.SlaRiskBreachedCount} BREACHED" : $"{state.SlaRiskWorstMinutesRemaining}m REMAIN";
            pages.Add(("SLA RISK", $"{state.SlaRiskCount} AT RISK", slaLine3, SlaWarningColor));
        }

        var normalizedPage = ((cyclePage % pages.Count) + pages.Count) % pages.Count;
        var (line1, line2, line3, color) = pages[normalizedPage];
        return DrawAsync(new[] { line1, line2, line3 }, color, cancellationToken);
    }

    private Task DrawAsync(IReadOnlyList<string> lines, string color, CancellationToken cancellationToken)
    {
        var elements = new List<Busy.Bar.DisplayElement>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            elements.Add(new Busy.Bar.TextElement
            {
                Id = i.ToString(),
                Text = lines[i],
                // The BUSY Bar's canvas is only 72x16px (DisplayCanvas.Width/Height) — TextFont.Normal
                // is too tall to stack 3 lines in that height at all (confirmed on real hardware: lines
                // overlapped completely). Tiny is the smallest available font; even so, this is a very
                // tight fit for 3 lines — verify against a real device and adjust Y spacing, or drop to
                // 2 lines / use ScrollRate for long text, if anything still overlaps or clips.
                Font = Busy.Bar.TextFont.Tiny,
                Y = i * 5,
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
