using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Worky.Core.Auth;
using Worky.Core.Graph;

namespace Worky.Core.Tests;

public class GraphSyncServiceTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    readonly string _dir = Path.Combine(Path.GetTempPath(), "worky-tests", Guid.NewGuid().ToString("N"));

    sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
    }

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

    static HttpResponseMessage MeOk() => Json(new
    {
        data = new { id = "u-1", username = "alice", name = "Alice", description = (string?)"me" },
    });

    static HttpResponseMessage FollowingPage(int page, string? nextToken) => Json(new
    {
        data = new[]
        {
            new { id = $"f-{page}a", username = $"followed_{page}a", name = $"Followed {page}A", description = (string?)null },
            new { id = $"f-{page}b", username = $"followed_{page}b", name = $"Followed {page}B", description = $"desc {page}" },
        },
        meta = new { next_token = nextToken, result_count = 2 },
    });

    static HttpResponseMessage ServerError() => new(HttpStatusCode.InternalServerError)
    {
        Content = new StringContent("""{"title":"boom"}""", Encoding.UTF8, "application/json"),
    };

    static ScriptedHandler HappyPathHandler(int pages)
    {
        var responses = new Queue<HttpResponseMessage>([MeOk()]);
        for (var page = 1; page <= pages; page++)
            responses.Enqueue(FollowingPage(page, page < pages ? $"cursor-{page}" : null));
        return new ScriptedHandler(responses);
    }

    GraphStateFileStore Store() => new(_dir);

    GraphSyncService Build(XApiClient api, GraphStateFileStore store) =>
        new(api, store, new FakeClock());

    GraphState PriorSnapshot(DateTimeOffset ingestedAt) => new()
    {
        UserId = "u-1",
        UserName = "alice",
        FollowedUsers = [new FollowedUser("old-1", "old_user", "Old User", null)],
        IngestedAt = ingestedAt,
    };

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task SuccessFetchesAllPagesThenWritesSnapshotOnce()
    {
        var store = Store();
        var handler = HappyPathHandler(pages: 3);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var service = Build(new XApiClient(http, new StaticAuthTokenProvider("tok")), store);

        var result = await service.RunAsync(new GraphSyncOptions { MaxPages = 5 });

        Assert.Equal(new GraphSyncResult.Synced(3, 6), result);

        var state = store.Load()!;
        Assert.Equal("u-1", state.UserId);
        Assert.Equal("alice", state.UserName);
        Assert.Equal(Now, state.IngestedAt);
        Assert.Equal(
            [
                new FollowedUser("f-1a", "followed_1a", "Followed 1A", null),
                new FollowedUser("f-1b", "followed_1b", "Followed 1B", "desc 1"),
                new FollowedUser("f-2a", "followed_2a", "Followed 2A", null),
                new FollowedUser("f-2b", "followed_2b", "Followed 2B", "desc 2"),
                new FollowedUser("f-3a", "followed_3a", "Followed 3A", null),
                new FollowedUser("f-3b", "followed_3b", "Followed 3B", "desc 3"),
            ],
            state.FollowedUsers);
    }

    [Fact]
    public async Task FailureMidPagesLeavesPriorSnapshotUntouched()
    {
        var store = Store();
        store.Save(PriorSnapshot(Now - TimeSpan.FromDays(8)));
        var responses = new Queue<HttpResponseMessage>(
            [MeOk(), FollowingPage(1, "cursor-1"), ServerError()]);
        var handler = new ScriptedHandler(responses);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var service = Build(new XApiClient(http, new StaticAuthTokenProvider("tok")), store);

        await Assert.ThrowsAsync<XApiException>(() => service.RunAsync(new GraphSyncOptions()));

        var state = store.Load()!;
        Assert.Equal("u-1", state.UserId);
        Assert.Equal("alice", state.UserName);
        Assert.Equal(Now - TimeSpan.FromDays(8), state.IngestedAt);
        Assert.Equal(["old-1"], state.FollowedUsers.Select(u => u.Id).ToArray());
    }

    [Fact]
    public async Task FreshSnapshotSkipsWithoutAnyHttpCalls()
    {
        var store = Store();
        var prior = PriorSnapshot(Now - TimeSpan.FromDays(1));
        store.Save(prior);
        var handler = new ScriptedHandler(new Queue<HttpResponseMessage>());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var service = Build(new XApiClient(http, new StaticAuthTokenProvider("tok")), store);
        var lines = new List<string>();

        var result = await service.RunAsync(new GraphSyncOptions(), report: lines.Add);

        Assert.Equal(new GraphSyncResult.SkippedFresh(TimeSpan.FromDays(1)), result);
        Assert.Equal(0, handler.Requests);
        var output = string.Join('\n', lines);
        Assert.Contains("--refresh-graph", output);
        Assert.Contains("@alice is 1d 0h old", output);
        Assert.Equal(prior.UserName, store.Load()!.UserName);
    }

    [Fact]
    public async Task RefreshFlagBypassesFreshSnapshotSkip()
    {
        var store = Store();
        store.Save(PriorSnapshot(Now - TimeSpan.FromHours(1)));
        var handler = HappyPathHandler(pages: 1);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var service = Build(new XApiClient(http, new StaticAuthTokenProvider("tok")), store);

        var result = await service.RunAsync(new GraphSyncOptions { Refresh = true });

        Assert.Equal(new GraphSyncResult.Synced(1, 2), result);
        Assert.True(handler.Requests > 0);
        var state = store.Load()!;
        Assert.Equal(Now, state.IngestedAt);
        Assert.Equal("f-1a", state.FollowedUsers[0].Id);
    }

    [Fact]
    public async Task MissingSnapshotPerformsFullSync()
    {
        var store = Store();
        var handler = HappyPathHandler(pages: 2);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var service = Build(new XApiClient(http, new StaticAuthTokenProvider("tok")), store);

        var result = await service.RunAsync(new GraphSyncOptions());

        Assert.Equal(new GraphSyncResult.Synced(2, 4), result);
        Assert.NotNull(store.Load());
    }
}
