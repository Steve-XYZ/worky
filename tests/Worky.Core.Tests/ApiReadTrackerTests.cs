using System.Net;
using System.Text;
using System.Text.Json;
using Worky.Core.Auth;

namespace Worky.Core.Tests;

public class ApiReadTrackerTests
{
    sealed class ScriptedHandler(Queue<HttpResponseMessage> responses) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            return Task.FromResult(responses.Dequeue());
        }
    }

    static HttpResponseMessage Json(object payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

    static HttpResponseMessage SearchPage(int page, int posts, string? nextToken) => Json(new
    {
        data = Enumerable.Range(0, posts).Select(i => new
        {
            id = $"p-{page}-{i}",
            author_id = $"a-{page}-{i}",
            text = $"post {page}/{i}",
            created_at = (DateTimeOffset?)new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
            entities = (object?)null,
        }).ToArray(),
        includes = new
        {
            users = Enumerable.Range(0, posts).Select(i => new
            {
                id = $"a-{page}-{i}",
                username = $"user_{page}_{i}",
                name = $"Name {page}-{i}",
                description = (string?)"bio",
            }).ToArray(),
        },
        meta = new { next_token = nextToken, result_count = posts },
    });

    static HttpResponseMessage FollowingPage(int page, string? nextToken) => Json(new
    {
        data = new[]
        {
            new { id = $"f-{page}a", username = $"followed_{page}a", name = $"A{page}", description = (string?)null },
            new { id = $"f-{page}b", username = $"followed_{page}b", name = $"B{page}", description = (string?)"d" },
        },
        meta = new { next_token = nextToken, result_count = 2 },
    });

    static HttpResponseMessage MeOk() => Json(new
    {
        data = new { id = "u-1", username = "alice", name = "Alice", description = (string?)"me" },
    });

    [Fact]
    public void AccumulatesCountsExactlyOncePerItem()
    {
        var tracker = new ApiReadTracker();

        tracker.CountPosts(2);
        tracker.CountPosts(3);
        tracker.CountUsers(4);
        tracker.CountUsers(1);

        Assert.Equal(5, tracker.Posts);
        Assert.Equal(5, tracker.Users);
    }

    [Fact]
    public void NegativeCountsNeverMoveTheTotals()
    {
        var tracker = new ApiReadTracker();

        tracker.CountPosts(-3);
        tracker.CountUsers(-1);

        Assert.Equal(0, tracker.Posts);
        Assert.Equal(0, tracker.Users);
    }

    [Fact]
    public async Task CountsPostsWithoutDuplicatesAcrossSearchPages()
    {
        var handler = new ScriptedHandler(new Queue<HttpResponseMessage>(
        [
            SearchPage(1, 2, "cursor-1"),
            SearchPage(2, 3, "cursor-2"),
            SearchPage(3, 4, null),
        ]));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var tracker = new ApiReadTracker();
        var client = new XApiClient(http, new StaticAuthTokenProvider("tok"), tracker);

        var posts = await client.ScanRecentAsync("hiring", maxPosts: 10);

        Assert.Equal(9, posts.Count);
        Assert.Equal(9, tracker.Posts);
        Assert.Equal(0, tracker.Users);
        Assert.Equal(3, handler.Requests);
        Assert.Equal(0.045m, tracker.EstimatedPostCostUsd);
    }

    [Fact]
    public async Task CountsUsersExactlyOncePerPageOnFollowing()
    {
        var handler = new ScriptedHandler(new Queue<HttpResponseMessage>(
        [
            FollowingPage(1, "cursor-1"),
            FollowingPage(2, null),
        ]));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var tracker = new ApiReadTracker();
        var client = new XApiClient(http, new StaticAuthTokenProvider("tok"), tracker);

        var following = await client.GetFollowingAsync("me-1", maxPages: 5);

        Assert.Equal(4, following.Count);
        Assert.Equal(4, tracker.Users);
        Assert.Equal(0, tracker.Posts);
        Assert.Equal(2, handler.Requests);
        Assert.Equal(0.040m, tracker.EstimatedUserCostUsd);
    }

    [Fact]
    public async Task CountsOwnAccountAsOneUserRead()
    {
        var handler = new ScriptedHandler(new Queue<HttpResponseMessage>([MeOk()]));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var tracker = new ApiReadTracker();
        var client = new XApiClient(http, new StaticAuthTokenProvider("tok"), tracker);

        var me = await client.GetMeAsync();

        Assert.Equal("alice", me.UserName);
        Assert.Equal(1, tracker.Users);
    }

    [Fact]
    public async Task ClientWithoutTrackerKeepsWorking()
    {
        var handler = new ScriptedHandler(new Queue<HttpResponseMessage>([MeOk()]));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var client = new XApiClient(http, new StaticAuthTokenProvider("tok"));

        await client.GetMeAsync();

        Assert.Equal(1, handler.Requests);
    }
}
