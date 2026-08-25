using System.IO;
using Worky.Core.Auth;

namespace Worky.Core.Tests;

public class AuthFileStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "worky-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    static AuthSession Sample() => new(
        "access-1",
        "refresh-1",
        new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero),
        "tweet.read users.read",
        "u-1",
        "alice");

    [Fact]
    public void RoundTripsAllSessionFields()
    {
        var store = new AuthFileStore(_dir);

        store.Save(Sample());

        Assert.Equal(Sample(), store.Load());
    }

    [Fact]
    public void LoadReturnsNullWhenFileAbsentOrCorrupt()
    {
        var store = new AuthFileStore(_dir);
        Assert.Null(store.Load());

        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "auth.json"), "{ not json");
        Assert.Null(store.Load());
    }

    [Fact]
    public void SaveReplacesAtomicallyAndLeavesNoTempFiles()
    {
        var store = new AuthFileStore(_dir);

        store.Save(Sample());
        store.Save(Sample() with { AccessToken = "access-2" });

        Assert.Equal(new[] { "auth.json" }, Directory.GetFiles(_dir).Select(Path.GetFileName).ToArray());
        Assert.Equal("access-2", store.Load()!.AccessToken);
    }

    [Fact]
    public void SavedPathsAreOwnerOnlyWhereSupported()
    {
        var store = new AuthFileStore(_dir);

        store.Save(Sample());

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsFreeBSD()) return;

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(Path.Combine(_dir, "auth.json")));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(_dir));
    }
}
