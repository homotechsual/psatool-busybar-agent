using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PsaToolAgent.Display;
using PsaToolAgent.Psa;
using PsaToolAgent.Tests.Internal;
using Xunit;

namespace PsaToolAgent.Tests;

public class PollingBackgroundServiceTests
{
    private sealed class StubPsaDataProvider : IPsaDataProvider
    {
        public Func<CancellationToken, Task<PsaSnapshot>>? OnGetSnapshot { get; set; }

        public Task<PsaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
            => OnGetSnapshot is not null
                ? OnGetSnapshot(cancellationToken)
                : Task.FromResult(new PsaSnapshot
                {
                    OpenTicketCount = 0,
                    PriorityCounts = new Dictionary<int, int>(),
                    SlaRiskTickets = Array.Empty<SlaRiskTicket>(),
                    UnassignedTicketCount = 0,
                    VipTickets = Array.Empty<VipTicket>()
                });
    }

    private static (PollingBackgroundService service, FakeHttpMessageHandler handler, StubPsaDataProvider provider) CreateService()
    {
        var handler = new FakeHttpMessageHandler { ResponseBody = "{\"result\":\"OK\"}" };
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://10.0.4.20/") };
        var bar = new Busy.Bar.BusyBar(http, new Busy.Bar.BusyBarOptions());
        var renderer = new BusyBarRenderer(bar, Options.Create(new DashboardOptions()));
        var provider = new StubPsaDataProvider();
        var options = Options.Create(new PsaOptions { Provider = "Stub", PollIntervalSeconds = 60, SlaRiskThresholdMinutes = 60 });
        var service = new PollingBackgroundService(provider, renderer, options, NullLogger<PollingBackgroundService>.Instance);
        return (service, handler, provider);
    }

    [Fact]
    public async Task PollOnceAsync_RendersDashboard_OnSuccessfulPoll()
    {
        var (service, handler, _) = CreateService();

        await service.PollOnceAsync(CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
    }

    [Fact]
    public async Task PollOnceAsync_DoesNotThrow_WhenProviderFails()
    {
        var (service, _, provider) = CreateService();
        provider.OnGetSnapshot = _ => throw new HttpRequestException("Halo unreachable");

        var exception = await Record.ExceptionAsync(() => service.PollOnceAsync(CancellationToken.None));

        Assert.Null(exception);
    }
}
