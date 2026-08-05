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
        var dashboardOptions = Options.Create(new DashboardOptions());
        var renderer = new BusyBarRenderer(bar, dashboardOptions);
        var provider = new StubPsaDataProvider();
        var options = Options.Create(new PsaOptions { Provider = "Stub", PollIntervalSeconds = 60, SlaRiskThresholdMinutes = 60 });
        var service = new PollingBackgroundService(provider, renderer, options, dashboardOptions, NullLogger<PollingBackgroundService>.Instance);
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

    [Fact]
    public async Task StopAsync_ClearsTheDisplay()
    {
        var (service, handler, _) = CreateService();
        await service.StartAsync(CancellationToken.None);

        await service.StopAsync(CancellationToken.None);

        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Delete && r.RequestUri!.AbsolutePath.Contains("display/draw"));
    }

    [Fact]
    public async Task StopAsync_DoesNotThrow_WhenClearFails()
    {
        var handler = new FakeHttpMessageHandler
        {
            Respond = _ => new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://10.0.4.20/") };
        var bar = new Busy.Bar.BusyBar(http, new Busy.Bar.BusyBarOptions());
        var dashboardOptions = Options.Create(new DashboardOptions());
        var renderer = new BusyBarRenderer(bar, dashboardOptions);
        var provider = new StubPsaDataProvider();
        var options = Options.Create(new PsaOptions { Provider = "Stub", PollIntervalSeconds = 60, SlaRiskThresholdMinutes = 60 });
        var service = new PollingBackgroundService(provider, renderer, options, dashboardOptions, NullLogger<PollingBackgroundService>.Instance);
        await service.StartAsync(CancellationToken.None);

        var exception = await Record.ExceptionAsync(() => service.StopAsync(CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task PollOnceAsync_CyclesThroughCriticalPages_UntilNextPollInterval()
    {
        var handler = new FakeHttpMessageHandler { ResponseBody = "{\"result\":\"OK\"}" };
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://10.0.4.20/") };
        var bar = new Busy.Bar.BusyBar(http, new Busy.Bar.BusyBarOptions());
        var dashboardOptions = Options.Create(new DashboardOptions { DisplayCycleSeconds = 1 });
        var renderer = new BusyBarRenderer(bar, dashboardOptions);
        var provider = new StubPsaDataProvider
        {
            OnGetSnapshot = _ => Task.FromResult(new PsaSnapshot
            {
                OpenTicketCount = 8,
                PriorityCounts = new Dictionary<int, int> { [1] = 2, [2] = 5, [3] = 1 },
                SlaRiskTickets = Array.Empty<SlaRiskTicket>(),
                UnassignedTicketCount = 0,
                VipTickets = Array.Empty<VipTicket>()
            })
        };
        var options = Options.Create(new PsaOptions { Provider = "Stub", PollIntervalSeconds = 3, SlaRiskThresholdMinutes = 60 });
        var service = new PollingBackgroundService(provider, renderer, options, dashboardOptions, NullLogger<PollingBackgroundService>.Instance);

        await service.PollOnceAsync(CancellationToken.None);

        Assert.True(handler.RequestBodies.Count >= 3, $"Expected at least 3 draw calls over the ~3s poll interval at a 1s cycle, got {handler.RequestBodies.Count}");
        Assert.Contains(handler.RequestBodies, b => b is not null && b.Contains("\"text\":\"P1 OPEN\""));
        Assert.Contains(handler.RequestBodies, b => b is not null && b.Contains("\"text\":\"P2 OPEN\""));
        Assert.Contains(handler.RequestBodies, b => b is not null && b.Contains("\"text\":\"P3 OPEN\""));
    }
}
