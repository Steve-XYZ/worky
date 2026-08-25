using System.Net;
using System.Text;
using Worky.Core.Auth;

namespace Worky.Core.Tests;

public class XApiClientAuthTests
{
    sealed class RecordingHandler(Queue<HttpResponseMessage> responses) : HttpMessageHandler
    {
        public List<(string Url, string? Authorization)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString()));
            return Task.FromResult(responses.Dequeue());
        }
    }

    sealed class DelegateAuthTokenProvider(Func<string> next) : IAuthTokenProvider
    {
        int _calls;
        public int Calls => _calls;

        public Task<string> GetTokenAsync(CancellationToken ct = default)
        {
            _calls++;
            return Task.FromResult(next());
        }
    }

    static HttpResponseMessage SearchOk() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[],"meta":{}}""", Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task ResolvesTokenPerRequestNotPerClient()
    {
        var handler = new RecordingHandler(new Queue<HttpResponseMessage>([SearchOk(), SearchOk()]));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var issued = new Queue<string>(["tok-a", "tok-b"]);
        var provider = new DelegateAuthTokenProvider(() => issued.Dequeue());
        var client = new XApiClient(http, provider);

        await client.SearchRecentAsync("q1", 10);
        await client.SearchRecentAsync("q2", 10);

        Assert.Equal(2, provider.Calls);
        Assert.Equal("Bearer tok-a", handler.Requests[0].Authorization);
        Assert.Equal("Bearer tok-b", handler.Requests[1].Authorization);
    }

    [Fact]
    public async Task SearchRequestsKeepBearerHeaderAndQueryShape()
    {
        var handler = new RecordingHandler(new Queue<HttpResponseMessage>([SearchOk()]));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var client = new XApiClient(http, new StaticAuthTokenProvider("tok"));

        await client.SearchRecentAsync("lang:en hiring", 25);

        var request = Assert.Single(handler.Requests);
        var url = Uri.UnescapeDataString(request.Url);
        Assert.Equal("Bearer tok", request.Authorization);
        Assert.Contains("/tweets/search/recent?", request.Url);
        Assert.Contains("query=lang:en hiring", url);
        Assert.Contains("max_results=25", url);
        Assert.Contains("tweet.fields=created_at,author_id,entities", url);
        Assert.Contains("expansions=author_id", url);
        Assert.Contains("user.fields=username,name,description", url);
    }
}
