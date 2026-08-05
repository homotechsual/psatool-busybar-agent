using System.Net;
using System.Text;

namespace PsaToolAgent.Tests.Internal;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();
    public HttpRequestMessage? LastRequest => Requests.Count > 0 ? Requests[^1] : null;
    public string? LastRequestBody { get; private set; }

    /// <summary>Every request body seen so far, in order — unlike <see cref="LastRequestBody"/>,
    /// lets a test verify content varied across several requests (e.g. a cycling display).</summary>
    public List<string?> RequestBodies { get; } = new();

    /// <summary>Status code and body returned for every request, unless <see cref="Respond"/> is set.</summary>
    public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.OK;
    public string ResponseBody { get; set; } = "{}";

    /// <summary>When set, overrides <see cref="ResponseStatusCode"/>/<see cref="ResponseBody"/> so a
    /// test can return different responses for different requests (e.g. an auth call vs. a data
    /// call).</summary>
    public Func<HttpRequestMessage, HttpResponseMessage>? Respond { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        RequestBodies.Add(LastRequestBody);

        if (Respond is not null)
        {
            return Respond(request);
        }

        return new HttpResponseMessage(ResponseStatusCode)
        {
            Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
        };
    }
}
