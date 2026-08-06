using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PsaToolAgent;
using PsaToolAgent.Display;
using PsaToolAgent.Psa;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<PsaOptions>()
    .Bind(builder.Configuration.GetSection(PsaOptions.SectionName))
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

// Registers only the selected provider's options + HTTP client(s) — see PsaServiceRegistration
// for why each provider's ValidateOnStart() must stay inside its own branch.
builder.Services.AddPsaProvider(builder.Configuration);

builder.Services.AddSingleton(provider =>
{
    var options = provider.GetRequiredService<IOptions<BusyBarOptions>>().Value;
    return new Busy.Bar.BusyBar(new Busy.Bar.BusyBarOptions { Addr = options.Address });
});
builder.Services.AddSingleton<BusyBarRenderer>();

builder.Services.AddHostedService<PollingBackgroundService>();

var host = builder.Build();
host.Run();
