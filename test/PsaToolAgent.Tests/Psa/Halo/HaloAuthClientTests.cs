using Microsoft.Extensions.Options;
using PsaToolAgent.Psa.Halo;
using PsaToolAgent.Tests.Internal;
using Xunit;

namespace PsaToolAgent.Tests.Psa.Halo;

public class HaloAuthClientTests
{
    private static (HaloAuthClient client, FakeHttpMessageHandler handler) CreateClient()
    {
        var handler = new FakeHttpMessageHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.halopsa.com/") };
        var options = Options.Create(new HaloOptions
        {
            BaseUrl = "https://example.halopsa.com/",
            ClientId = "test-client",
            ClientSecret = "test-secret",
            Scope = "read:tickets"
        });
        return (new HaloAuthClient(http, options), handler);
    }

    [Fact]
    public async Task GetAccessTokenAsync_SendsClientCredentialsGrant()
    {
        var (client, handler) = CreateClient();
        handler.ResponseBody = "{\"access_token\":\"abc123\",\"expires_in\":3600}";

        var token = await client.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("abc123", token);
        Assert.Contains("grant_type=client_credentials", handler.LastRequestBody);
        Assert.Contains("scope=read%3Atickets", handler.LastRequestBody);
    }

    [Fact]
    public async Task GetAccessTokenAsync_CachesTokenUntilNearExpiry()
    {
        var (client, handler) = CreateClient();
        handler.ResponseBody = "{\"access_token\":\"abc123\",\"expires_in\":3600}";

        await client.GetAccessTokenAsync(CancellationToken.None);
        await client.GetAccessTokenAsync(CancellationToken.None);

        Assert.Single(handler.Requests);
    }
}
