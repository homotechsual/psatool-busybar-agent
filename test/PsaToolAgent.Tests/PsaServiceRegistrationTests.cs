using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PsaToolAgent.Psa;
using PsaToolAgent.Psa.Gorelo;
using PsaToolAgent.Psa.Halo;
using Xunit;

namespace PsaToolAgent.Tests;

/// <summary>
/// Builds a real host via <see cref="Host.CreateApplicationBuilder"/> the same way
/// <c>Program.cs</c> does, to prove <see cref="PsaServiceRegistration.AddPsaProvider"/> only
/// registers (and validates) the selected provider's options — regressing to unconditionally
/// registering both <see cref="HaloOptions"/> and <see cref="GoreloOptions"/> with
/// <c>ValidateOnStart()</c> would make these tests fail with <c>OptionsValidationException</c>
/// during <see cref="IHost.StartAsync"/>, since <c>ValidateOnStart</c>'s hosted service validates
/// every registered options type at host startup, not just the ones actually used.
/// </summary>
public class PsaServiceRegistrationTests
{
    [Fact]
    public async Task AddPsaProvider_GoreloSelected_WithNoHaloConfigAtAll_HostStartsWithoutThrowing()
    {
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Psa:Provider"] = "Gorelo",
            ["Psa:Gorelo:BaseUrl"] = "https://example.gorelo.io/",
            ["Psa:Gorelo:ApiKey"] = "configured-api-key",
            ["Psa:Gorelo:PageSize"] = "200"
        });

        builder.Services.AddPsaProvider(builder.Configuration);

        using var host = builder.Build();

        // Under the bug this guards against, starting the host here throws OptionsValidationException
        // for HaloOptions.ClientId/ClientSecret even though Halo was never selected.
        await host.StartAsync();
        try
        {
            var goreloOptions = host.Services.GetRequiredService<IOptions<GoreloOptions>>().Value;
            Assert.Equal("configured-api-key", goreloOptions.ApiKey);

            // The real Program.cs wiring sets the X-API-Key header on the typed HttpClient at DI
            // registration time; assert the resolved provider's HttpClient actually carries it,
            // closing the coverage gap the mislabeled unit test left open.
            var provider = host.Services.GetRequiredService<IPsaDataProvider>();
            var httpField = typeof(GoreloPsaDataProvider).GetField("_http", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(httpField);
            var http = (HttpClient)httpField!.GetValue(provider)!;
            Assert.Equal("configured-api-key", http.DefaultRequestHeaders.GetValues("X-API-Key").Single());
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task AddPsaProvider_HaloSelected_WithNoGoreloConfigAtAll_HostStartsWithoutThrowing()
    {
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Psa:Provider"] = "Halo",
            ["Psa:Halo:BaseUrl"] = "https://example.halopsa.com/",
            ["Psa:Halo:ClientId"] = "client-id",
            ["Psa:Halo:ClientSecret"] = "client-secret"
        });

        builder.Services.AddPsaProvider(builder.Configuration);

        using var host = builder.Build();

        // Under the bug this guards against, starting the host here throws OptionsValidationException
        // for GoreloOptions.ApiKey even though Gorelo was never selected.
        await host.StartAsync();
        try
        {
            var haloOptions = host.Services.GetRequiredService<IOptions<HaloOptions>>().Value;
            Assert.Equal("client-id", haloOptions.ClientId);
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
