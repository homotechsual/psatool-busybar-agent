using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace PsaToolAgent.Psa.Halo;

public sealed class HaloAuthClient
{
    private readonly HttpClient _http;
    private readonly HaloOptions _options;
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public HaloAuthClient(HttpClient http, IOptions<HaloOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _cachedToken;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["scope"] = _options.Scope
            })
        };

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Halo token response was empty.");

        _cachedToken = body.AccessToken;
        // Refresh 60s before actual expiry so a request never races a just-expired token.
        _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(body.ExpiresIn - 60);
        return _cachedToken;
    }

    /// <summary>Clears the cached token, forcing the next <see cref="GetAccessTokenAsync"/> call to
    /// re-authenticate. Call this after a downstream 401 — Halo can reject a cached token before its
    /// stated expiry (rotated secret, disabled API client), and without this the poll loop would keep
    /// retrying the same dead token until it naturally expires.</summary>
    public void InvalidateToken()
    {
        _cachedToken = null;
        _tokenExpiresAt = DateTimeOffset.MinValue;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
