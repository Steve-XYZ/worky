using System.Net;
using System.Text;
using System.Text.Json;
using Worky.Core.Auth;

namespace Worky.Core.Tests;

public class XApiClientFollowingTests
{
    sealed class PagingHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> RequestUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestUrls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(respond(RequestUrls.Count));
        }
    }

    sealed record FakeFollowingUser(string Id, string Username, string Name, string? Description);

    static HttpResponseMessage FollowingPage(int page, string? nextToken)
    {
        var payload = new
        {
            data = new[]
            {
                new FakeFollowingUser($"u-{page}a", $"user_{page}a", $"User {page}A", null),
                new FakeFollowingUser($"u-{page}b", $"user_{page}b", $"User {page}B", $"desc {page}"),
            },
            meta = new { next_token = nextToken, result_count = 2 },
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public async Task StopsAfterExactlyMaxPagesWhenApiKeepsPaging()
    {
        var handler = new PagingHandler(page => FollowingPage(page, nextToken: $"cursor-{page}"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var client = new XApiClient(http, new StaticAuthTokenProvider("tok"));

        var results = await client.GetFollowingAsync("me-1", maxPages: 3);

        Assert.Equal(3, handler.RequestUrls.Count);
        Assert.Equal(6, results.Count);
        Assert.Equal(["u-1a", "u-1b", "u-2a", "u-2b", "u-3a", "u-3b"], results.Select(u => u.Id).ToArray());
    }

    [Fact]
    public async Task StopsWhenApiPaginationEndsBeforeMaxPages()
    {
        var handler = new PagingHandler(page => FollowingPage(page, page < 2 ? "cursor-1" : null));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var client = new XApiClient(http, new StaticAuthTokenProvider("tok"));

        var results = await client.GetFollowingAsync("me-1", maxPages: 5);

        Assert.Equal(2, handler.RequestUrls.Count);
        Assert.Equal(4, results.Count);
    }

    [Fact]
    public async Task ContinuationRequestsCarryPreviousPageToken()
    {
        var handler = new PagingHandler(page => FollowingPage(page, page < 3 ? $"cursor-{page}" : null));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var client = new XApiClient(http, new StaticAuthTokenProvider("tok"));

        await client.GetFollowingAsync("me-1", maxPages: 5);

        Assert.StartsWith(
            "https://api.x.com/2/users/me-1/following?max_results=100&user.fields=username,name,description",
            handler.RequestUrls[0]);
        Assert.Contains("pagination_token=cursor-1", handler.RequestUrls[1]);
        Assert.Contains("pagination_token=cursor-2", handler.RequestUrls[2]);
        Assert.DoesNotContain("pagination_token", handler.RequestUrls[0]);
    }

    [Fact]
    public async Task ReportsCumulativeCountsAfterEachPage()
    {
        var handler = new PagingHandler(page => FollowingPage(page, page < 3 ? $"cursor-{page}" : null));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var client = new XApiClient(http, new StaticAuthTokenProvider("tok"));
        var reported = new List<(int Page, int Total)>();

        await client.GetFollowingAsync("me-1", maxPages: 3, onPage: (page, total) => reported.Add((page, total)));

        Assert.Equal([(1, 2), (2, 4), (3, 6)], reported);
    }

    [Fact]
    public async Task RejectsMaxPagesBelowOne()
    {
        var handler = new PagingHandler(_ => throw new InvalidOperationException("must not be called"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var client = new XApiClient(http, new StaticAuthTokenProvider("tok"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.GetFollowingAsync("me-1", maxPages: 0));

        Assert.Empty(handler.RequestUrls);
    }

    [Fact]
    public async Task MapsUsersWithNullDescriptions()
    {
        var handler = new PagingHandler(page => FollowingPage(page, nextToken: null));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var client = new XApiClient(http, new StaticAuthTokenProvider("tok"));

        var results = await client.GetFollowingAsync("me-1", maxPages: 1);

        Assert.Null(results[0].Description);
        Assert.Equal("desc 1", results[1].Description);
    }
}
