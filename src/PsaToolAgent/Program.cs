using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PsaToolAgent;
using PsaToolAgent.Display;
using PsaToolAgent.Psa;
using PsaToolAgent.Psa.Gorelo;
using PsaToolAgent.Psa.Halo;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<PsaOptions>()
    .Bind(builder.Configuration.GetSection(PsaOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<HaloOptions>()
    .Bind(builder.Configuration.GetSection(HaloOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<GoreloOptions>()
    .Bind(builder.Configuration.GetSection(GoreloOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<BusyBarOptions>()
    .Bind(builder.Configuration.GetSection(BusyBarOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<DashboardOptions>()
    .Bind(builder.Configuration.GetSection(DashboardOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<HaloAuthClient>((provider, client) =>
{
    var options = provider.GetRequiredService<IOptions<HaloOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

var psaProviderName = builder.Configuration.GetSection(PsaOptions.SectionName)["Provider"];
if (string.Equals(psaProviderName, "Halo", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IPsaDataProvider, HaloPsaDataProvider>((provider, client) =>
    {
        var options = provider.GetRequiredService<IOptions<HaloOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
    });
}
else if (string.Equals(psaProviderName, "Gorelo", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IPsaDataProvider, GoreloPsaDataProvider>((provider, client) =>
    {
        var options = provider.GetRequiredService<IOptions<GoreloOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
    });
}
else
{
    throw new InvalidOperationException($"Unknown Psa:Provider '{psaProviderName}'. Supported: Halo, Gorelo.");
}

// AddHttpClient<HaloAuthClient> / AddHttpClient<IPsaDataProvider, HaloPsaDataProvider> register both as
// transient — safe here only because PollingBackgroundService (a singleton hosted service) resolves and
// holds one instance of each for the app's lifetime, rather than re-resolving per poll.
builder.Services.AddSingleton(provider =>
{
    var options = provider.GetRequiredService<IOptions<BusyBarOptions>>().Value;
    return new Busy.Bar.BusyBar(new Busy.Bar.BusyBarOptions { Addr = options.Address });
});
builder.Services.AddSingleton<BusyBarRenderer>();

builder.Services.AddHostedService<PollingBackgroundService>();

var host = builder.Build();
host.Run();
