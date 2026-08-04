using Microsoft.Extensions.Options;
using PsaToolAgent.Dashboard;

namespace PsaToolAgent.Display;

public sealed class BusyBarRenderer
{
    private const string ApplicationName = "psatool_busybar_agent";

    private readonly Busy.Bar.BusyBar _bar;
    private readonly DashboardOptions _options;

    public BusyBarRenderer(Busy.Bar.BusyBar bar, IOptions<DashboardOptions> options)
    {
        _bar = bar;
        _options = options.Value;
    }

    public Task RenderAsync(DashboardState state, CancellationToken cancellationToken)
        => state switch
        {
            NormalDashboardState normal => RenderNormalAsync(normal, cancellationToken),
            SlaWarningDashboardState slaWarning => RenderSlaWarningAsync(slaWarning, cancellationToken),
            CriticalDashboardState critical => RenderCriticalAsync(critical, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

    private Task RenderNormalAsync(NormalDashboardState state, CancellationToken cancellationToken)
    {
        var thirdLine = state.UnassignedCount > 0 ? $"UNASSIGN:{state.UnassignedCount}" : "SLA: OK";
        return DrawAsync(new[] { _options.HeaderText, $"P1:{state.Rank1Count} P2:{state.Rank2Count}", thirdLine }, cancellationToken);
    }

    private Task RenderSlaWarningAsync(SlaWarningDashboardState state, CancellationToken cancellationToken)
    {
        var thirdLine = state.MinutesRemaining <= 0 ? "BREACHED" : $"{state.MinutesRemaining}m REMAIN";
        return DrawAsync(new[] { "SLA RISK", $"Ticket #{state.TicketId}", thirdLine }, cancellationToken);
    }

    private Task RenderCriticalAsync(CriticalDashboardState state, CancellationToken cancellationToken)
        => DrawAsync(new[] { "CRITICAL", state.Reason, $"Count:{state.Count}" }, cancellationToken);

    private Task DrawAsync(IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        var elements = new List<Busy.Bar.DisplayElement>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            elements.Add(new Busy.Bar.TextElement
            {
                Id = i.ToString(),
                Text = lines[i],
                Font = Busy.Bar.TextFont.Normal,
                // Stacked lines at 5px intervals — a starting point only. The BUSY Bar's 16px-tall
                // canvas is tight for 3 lines of normal-size text; confirm actual spacing against a
                // real device (see busybar-dotnet's samples/LiveDeviceTest for the pattern used to
                // do this) and adjust here, or switch TextFont, if lines overlap or clip.
                Y = i * 5
            });
        }

        return _bar.DisplayDrawAsync(new Busy.Bar.DisplayDrawParams
        {
            ApplicationName = ApplicationName,
            Elements = elements
        }, cancellationToken: cancellationToken);
    }
}
