using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Worky.Core;
using Worky.Core.Auth;
using Worky.Core.Graph;

namespace Worky.Core.Tests;

public class TargetedScanServiceTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    readonly string _dir = Path.Combine(Path.GetTempPath(), "worky-tests", Guid.NewGuid().ToString("N"));

    sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
    }

    sealed class RecordingHandler : HttpMessageHandler
    {
        readonly Queue<HttpResponseMessage> _responses;

        public RecordingHandler(Queue<HttpResponseMessage> responses) => _responses = responses;

        public List<string> Queries { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            Queries.Add(Regex.Match(query, @"query=([^&]+)").Groups[1].Value);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    static HttpResponseMessage SearchOk(params (string PostId, string AuthorId, string Text)[] posts) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            data = posts.Select(p => new
            {
                id = p.PostId,
                author_id = p.AuthorId,
                text = p.Text,
                created_at = "2026-08-24T10:00:00.000Z",
                entities = (object?)null,
            }),
            includes = new
            {
                users = posts.Select(p => new
                {
                    id = p.AuthorId,
                    username = $"user_{p.AuthorId}",
                    name = $"Name {p.AuthorId}",
                    description = (string?)"bio",
                }).DistinctBy(u => u.id),
            },
            meta = new { result_count = posts.Length },
        }), Encoding.UTF8, "application/json"),
    };

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
    public async Task MissingSnapshotFailsWithoutHttpCalls()
    {
        var handler = new RecordingHandler(new Queue<HttpResponseMessage>());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var service = new TargetedScanService(new XApiClient(http, new StaticAuthTokenProvider("tok")), Store(), new FakeClock());

        var result = await service.RunAsync(new TargetedScanRequest());

        Assert.IsType<TargetedScanResult.MissingSnapshot>(result);
        Assert.Empty(handler.Queries);
    }

    [Fact]
    public async Task StaleSnapshotRejectedWithoutHttpCalls()
    {
        Store().Save(Snapshot(Now - GraphState.FreshnessTtl - TimeSpan.FromHours(1),
            new FollowedUser("f1", "alice", "Alice", null)));
        var handler = new RecordingHandler(new Queue<HttpResponseMessage>());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var service = new TargetedScanService(new XApiClient(http, new StaticAuthTokenProvider("tok")), Store(), new FakeClock());

        var result = await service.RunAsync(new TargetedScanRequest());

        var stale = Assert.IsType<TargetedScanResult.StaleSnapshot>(result);
        Assert.Equal(GraphState.FreshnessTtl + TimeSpan.FromHours(1), stale.Age);
        Assert.Empty(handler.Queries);
    }

    [Fact]
    public async Task FreshSnapshotRunsOneSearchPerBatchWithExpectedQueries()
    {
        Store().Save(Snapshot(Now - TimeSpan.FromDays(1),
            new FollowedUser("f1", "author_aaaa", "A", null),
            new FollowedUser("f2", "author_bbbb", "B", null)));
        var handler = new RecordingHandler(new Queue<HttpResponseMessage>([SearchOk(), SearchOk()]));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var service = new TargetedScanService(new XApiClient(http, new StaticAuthTokenProvider("tok")), Store(), new FakeClock());

        var result = await service.RunAsync(new TargetedScanRequest
        {
            Terms = SingleAuthorBatchTerms,
            Limit = 10,
        });

        var completed = Assert.IsType<TargetedScanResult.Completed>(result);
        var expected = TargetedScanQueryBuilder.BuildQueries(
            ["author_aaaa", "author_bbbb"], SingleAuthorBatchTerms);
        Assert.True(expected.Count > 1);
        Assert.Equal(2, expected.Count);
        Assert.Equal(expected, handler.Queries);
        Assert.Equal(expected.Count, completed.Batches);
        Assert.All(handler.Queries, q => Assert.Contains("from:author_", q));
    }

    [Fact]
    public async Task PrioritizesProfileScoredAuthorsUnderMaxAuthorsCap()
    {
        Store().Save(Snapshot(Now - TimeSpan.FromDays(1),
            new FollowedUser("low", "low_priority_user", "Plain", null),
            new FollowedUser("high", "high_priority_user", "rustacean", null),
            new FollowedUser("mid", "mid_priority_user", "gamer", null)));
        var handler = new RecordingHandler(new Queue<HttpResponseMessage>([SearchOk()]));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var service = new TargetedScanService(new XApiClient(http, new StaticAuthTokenProvider("tok")), Store(), new FakeClock());

        await service.RunAsync(new TargetedScanRequest { MaxAuthors = 1, Terms = ["rust"], Limit = 10 });

        var query = Assert.Single(handler.Queries);
        Assert.Contains("from:high_priority_user", query);
        Assert.DoesNotContain("from:low_priority_user", query);
        Assert.DoesNotContain("from:mid_priority_user", query);
    }

    [Fact]
    public async Task MergesBatchesClassifiesAndAppliesNetworkBoostBeforeRanking()
    {
        Store().Save(Snapshot(Now - TimeSpan.FromDays(1),
            new FollowedUser("f1", "author_one", "One", null),
            new FollowedUser("f2", "author_two", "Two", null),
            new FollowedUser("s9", "stranger_nine", "Nine", null)));

        var handler = new RecordingHandler(new Queue<HttpResponseMessage>(
        [
            SearchOk(("p-followed", "f1", "we're hiring!")),
            SearchOk(("p-stranger", "ghost-id", "we're hiring!")),
            SearchOk(("p-weak", "f2", "mentioning hiring in passing")),
        ]));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var service = new TargetedScanService(new XApiClient(http, new StaticAuthTokenProvider("tok")), Store(), new FakeClock());

        var result = await service.RunAsync(new TargetedScanRequest
        {
            Terms = SingleAuthorBatchTerms,
            Limit = 100,
        });

        var completed = Assert.IsType<TargetedScanResult.Completed>(result);
        Assert.Equal(3, completed.Posts.Count);

        var followedLead = completed.Leads.Single(l => l.Post.Id == "p-followed");
        Assert.Equal(2.0 + 0.75 + NetworkBoost.ScoreBonus, followedLead.Signal.Score);
        Assert.EndsWith(NetworkBoost.Reason, followedLead.Signal.Reasons.Last());

        var boosted = completed.Leads.Single(l => l.Post.Id == "p-weak");
        Assert.Equal(0.75 + NetworkBoost.ScoreBonus, boosted.Signal.Score);
        Assert.EndsWith(NetworkBoost.Reason, boosted.Signal.Reasons.Last());

        var stranger = completed.Leads.Single(l => l.Post.Id == "p-stranger");
        Assert.Equal(2.0 + 0.75, stranger.Signal.Score);
        Assert.DoesNotContain(NetworkBoost.Reason, stranger.Signal.Reasons);

        Assert.Equal(
            ["p-followed", "p-stranger", "p-weak"],
            completed.Leads.Select(l => l.Post.Id).ToArray());
    }

    [Fact]
    public async Task EmptyFollowGraphCompletesWithoutHttpCalls()
    {
        Store().Save(Snapshot(Now - TimeSpan.FromDays(1)));
        var handler = new RecordingHandler(new Queue<HttpResponseMessage>());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        var service = new TargetedScanService(new XApiClient(http, new StaticAuthTokenProvider("tok")), Store(), new FakeClock());

        var result = await service.RunAsync(new TargetedScanRequest());

        var completed = Assert.IsType<TargetedScanResult.Completed>(result);
        Assert.Equal(0, completed.Batches);
        Assert.Empty(completed.Posts);
        Assert.Empty(handler.Queries);
    }
}
