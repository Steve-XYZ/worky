namespace Worky.Core.Graph;

public sealed record GraphSyncOptions
{
    public const int DefaultMaxPages = 5;

    public int MaxPages { get; init; } = DefaultMaxPages;

    public bool Refresh { get; init; }
}

public abstract record GraphSyncResult
{
    public sealed record Synced(int Pages, int Authors) : GraphSyncResult;

    public sealed record SkippedFresh(TimeSpan Age) : GraphSyncResult;
}

public sealed class GraphSyncService(
    XApiClient api,
    GraphStateFileStore store,
    IClock clock)
{
    public async Task<GraphSyncResult> RunAsync(
        GraphSyncOptions options,
        Action<string>? report = null,
        CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var existing = store.Load();
        if (existing is not null && !options.Refresh && !existing.IsStale(now))
        {
            var age = now - existing.IngestedAt;
            report?.Invoke(
                $"Snapshot of @{existing.UserName} is {DescribeAge(age)} old (fresh for "
                + $"{GraphState.FreshnessTtl.TotalDays:0} days); pass --refresh-graph to re-sync.");
            return new GraphSyncResult.SkippedFresh(age);
        }

        report?.Invoke("Resolving your X user...");
        var me = await api.GetMeAsync(ct);
        report?.Invoke($"Fetching who @{me.UserName} follows (up to {options.MaxPages} pages)...");

        var pages = 0;
        var following = (await api.GetFollowingAsync(me.Id, options.MaxPages, (page, total) =>
        {
            pages = page;
            report?.Invoke($"Page {page}/{options.MaxPages}: {total} followed authors.");
        }, ct)).ToList();

        store.Save(new GraphState
        {
            UserId = me.Id,
            UserName = me.UserName,
            FollowedUsers = following.Select(u => new FollowedUser(u.Id, u.UserName, u.Name, u.Description)).ToList(),
            IngestedAt = now,
        });

        report?.Invoke($"Saved {following.Count} followed authors to {GraphStateFileStore.DefaultPath}.");
        return new GraphSyncResult.Synced(pages, following.Count);
    }

    static string DescribeAge(TimeSpan age) =>
        age >= TimeSpan.FromDays(1)
            ? $"{age.Days}d {age.Hours}h"
            : $"{(int)age.TotalHours}h {age.Minutes}m";
}
