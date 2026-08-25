using Worky.Core.Graph;

namespace Worky.Core.Tests;

public class GraphStateTests
{
    static readonly DateTimeOffset IngestedAt = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    static GraphState Sample() => new()
    {
        UserId = "u-1",
        UserName = "alice",
        FollowedUsers =
        [
            new FollowedUser("f-1", "bob", "Bob Builder", "Builds things"),
            new FollowedUser("f-2", "carol", "Carol", null),
        ],
        IngestedAt = IngestedAt,
    };

    [Fact]
    public void SnapshotIsFreshJustInsideTtl()
    {
        var state = Sample();

        Assert.False(state.IsStale(IngestedAt + TimeSpan.FromDays(6) + TimeSpan.FromHours(23)));
    }

    [Fact]
    public void SnapshotIsStaleAtExactTtlBoundary()
    {
        var state = Sample();

        Assert.True(state.IsStale(IngestedAt + TimeSpan.FromDays(7)));
    }

    [Fact]
    public void SnapshotIsStaleBeyondTtl()
    {
        var state = Sample();

        Assert.True(state.IsStale(IngestedAt + TimeSpan.FromDays(30)));
    }
}
