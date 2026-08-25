using System.Net;
using System.Text;
using System.Text.Json;
using Worky.Core.Auth;
using Worky.Core.Graph;

namespace Worky.Core.Tests;

public class PartialResultTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    readonly string _dir = Path.Combine(Path.GetTempPath(), "worky-tests", Guid.NewGuid().ToString("N"));

    sealed class ScriptedHandler(Queue<HttpResponseMessage> responses) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            return Task.FromResult(responses.Dequeue());
        }
    }

    sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
    }

    static HttpResponseMessage Json(object payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

    static HttpResponseMessage RateLimited(string? resetHeader = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"title":"Too Many Requests"}""", Encoding.UTF8, "application/json"),
        };
        if (resetHeader is not null)
            response.Headers.TryAddWithoutValidation("x-rate-limit-reset", resetHeader);
        return response;
    }

    static HttpResponseMessage SearchPage(int page, string? nextToken) => Json(new
    {
        data = new[]
        {
            new
            {
                id = $"p-{page}a",
                author_id = $"a-{page}",
                text = $"post {page}a hiring",
                created_at = (DateTimeOffset?)new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
                entities = (object?)null,
            },
            new
            {
                id = $"p-{page}b",
                author_id = $"b-{page}",
                text = $"post {page}b",
                created_at = (DateTimeOffset?)new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero),
                entities = (object?)null,
            },
        },
        includes = new
        {
            users = new[]
            {
                new { id = $"a-{page}", username = $"author_{page}a", name = $"A{page}", description = (string?)"bio" },
                new { id = $"b-{page}", username = $"author_{page}b", name = $"B{page}", description = (string?)null },
            },
        },
        meta = new { next_token = nextToken, result_count = 2 },
    });

    static HttpResponseMessage MeOk() => Json(new
    {
        data = new { id = "u-1", username = "alice", name = "Alice", description = (string?)"me" },
    });

    sealed record FakeFollowingUser(string Id, string Username, string Name, string? Description);

    static HttpResponseMessage FollowingPage(int page, string? nextToken) => Json(new
    {
        data = new[]
        {
            new FakeFollowingUser($"f-{page}a", $"followed_{page}a", $"A{page}", null),
            new FakeFollowingUser($"f-{page}b", $"followed_{page}b", $"B{page}", "d"),
        },
        meta = new { next_token = nextToken, result_count = 2 },
    });

    static readonly string[] SingleAuthorBatchTerms =
    [
        "t01", "t02", "t03", "t04", "t05", "t06", "t07", "t08", "t09",
        "t10", "t11", "t12", "t13", "t14", "t15", "t16", "t17",
    ];

    GraphStateFileStore Store() => new(_dir);

    GraphState Snapshot(DateTimeOffset ingestedAt, params FollowedUser[] users) => new()
    {
        UserId = "me",
        UserName = "me",
        FollowedUsers = users,
        IngestedAt = ingestedAt,
    };

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task ScanRecentAsyncDeliversCumulativePagesThroughCallback()
    {
        var handler = new ScriptedHandler(new Queue<HttpResponseMessage>(
        [
            SearchPage(1, "cursor-1"),
            SearchPage(2, null),
        ]));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var client = new XApiClient(http, new StaticAuthTokenProvider("tok"));
        var snapshots = new List<int>();

        var posts = await client.ScanRecentAsync("hiring", maxPosts: 10,
            onPage: page => snapshots.Add(page.Count));

        Assert.Equal(4, posts.Count);
        Assert.Equal([2, 4], snapshots);
    }

    [Fact]
    public async Task ScanRecentAsyncKeepsPartialPagesVisibleWhenRateLimitedMidPaging()
    {
        var handler = new ScriptedHandler(new Queue<HttpResponseMessage>(
        [
            SearchPage(1, "cursor-1"),
            RateLimited("1787600000"),
        ]));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var client = new XApiClient(http, new StaticAuthTokenProvider("tok"));
        var lastSnapshotSize = 0;

        var ex = await Assert.ThrowsAsync<XRateLimitException>(() =>
            client.ScanRecentAsync("hiring", maxPosts: 10, onPage: page => lastSnapshotSize = page.Count));

        Assert.Equal("tweets/search/recent", ex.Endpoint);
        Assert.Equal(2, lastSnapshotSize);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task TargetedScanSurfacesCollectedBatchWhenSecondBatchIsRateLimited()
    {
        Store().Save(Snapshot(Now - TimeSpan.FromDays(1),
            new FollowedUser("f1", "author_aaaa", "A", null),
            new FollowedUser("f2", "author_bbbb", "B", null)));
        var handler = new ScriptedHandler(new Queue<HttpResponseMessage>(
        [
            SearchPage(1, null),
            RateLimited("1787600000"),
        ]));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var service = new TargetedScanService(
            new XApiClient(http, new StaticAuthTokenProvider("tok")), Store(), new FakeClock());
        var partialSizes = new List<int>();

        var ex = await Assert.ThrowsAsync<XRateLimitException>(() => service.RunAsync(
            new TargetedScanRequest { Terms = SingleAuthorBatchTerms, Limit = 100 },
            onPartial: page => partialSizes.Add(page.Count)));

        Assert.Equal("tweets/search/recent", ex.Endpoint);
        Assert.True(partialSizes.Count > 0);
        Assert.All(partialSizes, size => Assert.Equal(2, size));
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task SyncGraphRateLimitLeavesPriorSnapshotUntouched()
    {
        var prior = Snapshot(Now - TimeSpan.FromDays(8),
            new FollowedUser("old-1", "old_user", "Old User", null));
        Store().Save(prior);
        var handler = new ScriptedHandler(new Queue<HttpResponseMessage>(
        [
            MeOk(),
            FollowingPage(1, "cursor-1"),
            RateLimited("1787600000"),
        ]));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var service = new GraphSyncService(
            new XApiClient(http, new StaticAuthTokenProvider("tok")), Store(), new FakeClock());

        await Assert.ThrowsAsync<XRateLimitException>(
            () => service.RunAsync(new GraphSyncOptions()));

        var state = Store().Load()!;
        Assert.Equal("old-1", state.FollowedUsers.Select(u => u.Id).Single());
        Assert.Equal(Now - TimeSpan.FromDays(8), state.IngestedAt);
        Assert.Equal(3, handler.Requests);
    }
}
