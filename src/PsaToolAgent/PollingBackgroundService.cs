using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PsaToolAgent.Dashboard;
using PsaToolAgent.Display;
using PsaToolAgent.Psa;

namespace PsaToolAgent;

public sealed class PollingBackgroundService : BackgroundService
{
    private readonly IPsaDataProvider _provider;
    private readonly BusyBarRenderer _renderer;
    private readonly IOptions<PsaOptions> _options;
    private readonly ILogger<PollingBackgroundService> _logger;

    public PollingBackgroundService(
        IPsaDataProvider provider,
        BusyBarRenderer renderer,
        IOptions<PsaOptions> options,
        ILogger<PollingBackgroundService> logger)
    {
        _provider = provider;
        _renderer = renderer;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        do
        {
            await PollOnceAsync(stoppingToken).ConfigureAwait(false);
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>
    /// One poll-evaluate-render cycle. Internal (not private) so tests can drive it directly,
    /// without the <see cref="PeriodicTimer"/> loop. A failure here is logged and swallowed — the
    /// service must keep running and try again next interval, not crash on a transient blip.
    /// </summary>
    internal async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _provider.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var state = PriorityEngine.Evaluate(snapshot, _options.Value.SlaRiskThresholdMinutes);
            await _renderer.RenderAsync(state, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Dashboard updated: {StateType}", state.GetType().Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Poll cycle failed; will retry next interval.");
        }
    }
}
