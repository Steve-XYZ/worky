using System.IO;
using Worky.Core.Graph;

namespace Worky.Core.Tests;

public class GraphStateFileStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "worky-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    static GraphState Sample(string marker = "f-1") => new()
    {
        UserId = "u-1",
        UserName = "alice",
        FollowedUsers =
        [
            new FollowedUser(marker, "bob", "Bob Builder", "Builds things"),
            new FollowedUser("f-2", "carol", "Carol", null),
        ],
        IngestedAt = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void RoundTripsSnapshotIncludingNullDescriptions()
    {
        var store = new GraphStateFileStore(_dir);

        store.Save(Sample());
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal("u-1", loaded.UserId);
        Assert.Equal("alice", loaded.UserName);
        Assert.Equal(new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero), loaded.IngestedAt);
        Assert.Equal(
            [
                new FollowedUser("f-1", "bob", "Bob Builder", "Builds things"),
                new FollowedUser("f-2", "carol", "Carol", null),
            ],
            loaded.FollowedUsers);
    }

    [Fact]
    public void LoadReturnsNullWhenFileAbsentOrCorrupt()
    {
        var store = new GraphStateFileStore(_dir);
        Assert.Null(store.Load());

        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "state.json"), "{ not json");
        Assert.Null(store.Load());

        File.WriteAllText(Path.Combine(_dir, "state.json"), """{"user_id":"u-1"}""");
        Assert.Null(store.Load());
    }

    [Fact]
    public void SaveReplacesAtomicallyAndLeavesNoTempFiles()
    {
        var store = new GraphStateFileStore(_dir);

        store.Save(Sample());
        store.Save(Sample(marker: "f-9"));

        Assert.Equal(new[] { "state.json" }, Directory.GetFiles(_dir).Select(Path.GetFileName).ToArray());
        Assert.Equal("f-9", store.Load()!.FollowedUsers[0].Id);
    }
}
