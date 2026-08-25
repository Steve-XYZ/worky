using Worky.Core;
using Worky.Core.Graph;

namespace Worky.Core.Tests;

public class NetworkProfileTests
{
    static GraphState State(params FollowedUser[] users) => new()
    {
        UserId = "me",
        UserName = "me",
        FollowedUsers = users,
        IngestedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void ScoresDistinctKeywordHitsInNameAndDescription()
    {
        var state = State(
            new FollowedUser("1", "alice", "Alice", "rust and gamedev, hiring rust devs"));

        var profile = NetworkProfile.Build(state, ["rust", "gamedev", "go"]);

        Assert.Equal(2, Assert.Single(profile).Score);
    }

    [Fact]
    public void RepeatedSameKeywordCountsOnce()
    {
        var state = State(new FollowedUser("1", "alice", "Alice", "rust rust rust"));

        var profile = NetworkProfile.Build(state, ["rust"]);

        Assert.Equal(1, Assert.Single(profile).Score);
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        var state = State(new FollowedUser("1", "alice", "RUST Developer", null));

        var profile = NetworkProfile.Build(state, ["rust"]);

        Assert.Equal(1, Assert.Single(profile).Score);
    }

    [Fact]
    public void KeywordRequiresWordBoundary()
    {
        var state = State(new FollowedUser("1", "alice", "Industrious trustee", null));

        var profile = NetworkProfile.Build(state, ["rust", "trust"]);

        Assert.Equal(0, Assert.Single(profile).Score);
    }

    [Fact]
    public void MultiWordSeedMatchesPhrase()
    {
        var state = State(new FollowedUser("1", "alice", "Alice", "Machine learning engineer"));

        var profile = NetworkProfile.Build(state, ["machine learning"]);

        Assert.Equal(1, Assert.Single(profile).Score);
    }

    [Fact]
    public void OrdersByScoreDescThenUsernameAsc()
    {
        var state = State(
            new FollowedUser("1", "zoe", "Zoe", "rust"),
            new FollowedUser("2", "mia", "Mia", "rust go"),
            new FollowedUser("3", "ada", "Ada", null));

        var profile = NetworkProfile.Build(state, ["rust", "go"]);

        Assert.Equal(["mia", "zoe", "ada"], profile.Select(a => a.User.UserName).ToArray());
        Assert.Equal([2, 1, 0], profile.Select(a => a.Score).ToArray());
    }

    [Fact]
    public void EmptySeedsLeaveAllScoresZeroOrderedByUsername()
    {
        var state = State(
            new FollowedUser("1", "zoe", "hiring rust", null),
            new FollowedUser("2", "ada", "", null));

        var profile = NetworkProfile.Build(state, []);

        Assert.Equal([("ada", 0), ("zoe", 0)], profile.Select(a => (a.User.UserName, a.Score)).ToArray());
    }

    [Fact]
    public void NullSeedsBehaveLikeEmpty()
    {
        var state = State(new FollowedUser("1", "zoe", "rust", null));

        var profile = NetworkProfile.Build(state);

        Assert.Equal(0, Assert.Single(profile).Score);
    }
}
