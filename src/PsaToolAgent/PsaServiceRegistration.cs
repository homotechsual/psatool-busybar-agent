using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PsaToolAgent.Psa;
using PsaToolAgent.Psa.Gorelo;
using PsaToolAgent.Psa.Halo;

namespace PsaToolAgent;

/// <summary>
/// Registers the <see cref="IPsaDataProvider"/> implementation selected by <c>Psa:Provider</c>,
/// plus that provider's own options and HTTP client(s) — and nothing from the other provider.
/// Extracted out of <c>Program.cs</c>'s top-level statements so a test can exercise the DI wiring
/// directly against an <see cref="IServiceCollection"/> without needing a full running host.
///
/// Each provider's <c>AddOptions&lt;...&gt;().ValidateOnStart()</c> call lives inside its own
/// branch here rather than unconditionally at the top of <c>Program.cs</c>: <c>ValidateOnStart</c>
/// eagerly validates every registered options type at host startup, so registering both
/// <see cref="HaloOptions"/> and <see cref="GoreloOptions"/> unconditionally meant a deployment
/// running one provider would crash-loop on the other provider's unset <c>[Required]</c> fields.
///
/// <c>AddHttpClient&lt;HaloAuthClient&gt;</c> / <c>AddHttpClient&lt;IPsaDataProvider, ...&gt;</c>
/// register their typed clients as transient (the default) — safe here only because
/// <see cref="PollingBackgroundService"/> (a singleton hosted service) resolves and holds one
/// instance of each for the app's lifetime, rather than re-resolving per poll.
/// </summary>
internal static class PsaServiceRegistration
{
    public static void AddPsaProvider(this IServiceCollection services, IConfiguration configuration)
    {
        var psaProviderName = configuration.GetSection(PsaOptions.SectionName)["Provider"];
        if (string.Equals(psaProviderName, "Halo", StringComparison.OrdinalIgnoreCase))
        {
            services.AddOptions<HaloOptions>()
                .Bind(configuration.GetSection(HaloOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddHttpClient<HaloAuthClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<HaloOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            services.AddHttpClient<IPsaDataProvider, HaloPsaDataProvider>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<HaloOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
            });
        }
        else if (string.Equals(psaProviderName, "Gorelo", StringComparison.OrdinalIgnoreCase))
        {
            services.AddOptions<GoreloOptions>()
                .Bind(configuration.GetSection(GoreloOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddHttpClient<IPsaDataProvider, GoreloPsaDataProvider>((provider, client) =>
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
    }
}
