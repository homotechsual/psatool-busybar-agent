using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PsaToolAgent;
using PsaToolAgent.Display;
using PsaToolAgent.Psa;
using PsaToolAgent.Psa.Halo;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<PsaOptions>(builder.Configuration.GetSection(PsaOptions.SectionName));
builder.Services.Configure<HaloOptions>(builder.Configuration.GetSection(HaloOptions.SectionName));
builder.Services.Configure<BusyBarOptions>(builder.Configuration.GetSection(BusyBarOptions.SectionName));
builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection(DashboardOptions.SectionName));

builder.Services.AddHttpClient<HaloAuthClient>((provider, client) =>
{
    var options = provider.GetRequiredService<IOptions<HaloOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});

var psaProviderName = builder.Configuration.GetSection(PsaOptions.SectionName)["Provider"];
if (string.Equals(psaProviderName, "Halo", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IPsaDataProvider, HaloPsaDataProvider>((provider, client) =>
    {
        var options = provider.GetRequiredService<IOptions<HaloOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
    });
}
else
{
    throw new InvalidOperationException($"Unknown Psa:Provider '{psaProviderName}'. Supported: Halo.");
}

builder.Services.AddSingleton(provider =>
{
    var options = provider.GetRequiredService<IOptions<BusyBarOptions>>().Value;
    return new Busy.Bar.BusyBar(new Busy.Bar.BusyBarOptions { Addr = options.Address });
});
builder.Services.AddSingleton<BusyBarRenderer>();

builder.Services.AddHostedService<PollingBackgroundService>();

var host = builder.Build();
host.Run();
