using Worky.Core;
using Worky.Core.Graph;

namespace Worky.Core.Tests;

public class NetworkBoostTests
{
    static JobLead Lead(string postId, string authorId, string authorUserName, double score, DateTimeOffset? at = null) =>
        new(
            new Post(postId, authorId, "hiring text", at, []),
            new XUser(authorId, authorUserName, "Name", null),
            new JobSignal(score, score >= JobSignalClassifier.MatchThreshold, ["phrase \"hiring\""]));

    static GraphState State(params string[] followedIds) => new()
    {
        UserId = "me",
        UserName = "me",
        FollowedUsers = followedIds.Select(id => new FollowedUser(id, $"user_{id}", "Name", null)).ToList(),
        IngestedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void FollowedAuthorGainsExactBonusAndReasonBeforeRanking()
    {
        var leads = new[] { Lead("p1", "f1", "alice", 0.75) };

        var boosted = NetworkBoost.Apply(leads, State("f1"));

        Assert.Equal(1.75, boosted[0].Signal.Score);
        Assert.True(boosted[0].Signal.IsMatch);
        Assert.Equal(["phrase \"hiring\"", "network match"], boosted[0].Signal.Reasons);
    }

    [Fact]
    public void UnfollowedAuthorUntouched()
    {
        var leads = new[] { Lead("p1", "other", "mallory", 0.75), Lead("p2", "f1", "alice", 2.0) };

        var boosted = NetworkBoost.Apply(leads, State("f1"));

        Assert.Equal(0.75, boosted[0].Signal.Score);
        Assert.False(boosted[0].Signal.IsMatch);
        Assert.DoesNotContain(NetworkBoost.Reason, boosted[0].Signal.Reasons);
        Assert.Equal(3.0, boosted[1].Signal.Score);
    }

    [Fact]
    public void MatchingKeysOnAuthorIdNotUserName()
    {
        var leads = new[]
        {
            Lead("p1", "id-999", "alice", 2.0),
            Lead("p2", "id-f1", "stranger", 2.0),
        };

        var state = new GraphState
        {
            UserId = "me",
            UserName = "me",
            FollowedUsers = [new FollowedUser("id-f1", "alice", "Alice", null)],
            IngestedAt = DateTimeOffset.UtcNow,
        };

        var boosted = NetworkBoost.Apply(leads, state);

        Assert.Equal(2.0, boosted[0].Signal.Score);
        Assert.Equal(3.0, boosted[1].Signal.Score);
    }

    [Fact]
    public void AbsentSnapshotLeavesAllLeadsUnchanged()
    {
        var leads = new[] { Lead("p1", "f1", "alice", 2.0), Lead("p2", "x", "bob", 0.75) };

        var result = NetworkBoost.Apply(leads, null);

        Assert.Equal(leads, result);
        Assert.All(result, l => Assert.DoesNotContain(NetworkBoost.Reason, l.Signal.Reasons));
    }
}
