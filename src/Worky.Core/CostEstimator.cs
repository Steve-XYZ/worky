namespace Worky.Core;

public sealed record CostEstimate(decimal FloorUsd, decimal CeilingUsd);

public static class CostEstimator
{
    public const decimal PostReadThirdPartyUsd = 0.005m;
    public const decimal UserReadUsd = 0.010m;
    public const decimal OwnedReadUsd = 0.001m;
    public const int MaxFollowingAccountsPerPage = 100;

    public static CostEstimate ForScan(int postLimit)
    {
        var posts = Math.Max(postLimit, 0);
        return new CostEstimate(posts * OwnedReadUsd, posts * PostReadThirdPartyUsd);
    }

    public static CostEstimate ForTargetedScan(int maxAuthors, int postLimit)
    {
        var authors = Math.Max(maxAuthors, 0);
        var posts = Math.Max(postLimit, 0);
        return new CostEstimate(
            posts * OwnedReadUsd,
            posts * PostReadThirdPartyUsd + authors * UserReadUsd);
    }

    public static CostEstimate ForSyncGraph(int maxPages)
    {
        var pages = Math.Max(maxPages, 1);
        return new CostEstimate(
            OwnedReadUsd,
            pages * MaxFollowingAccountsPerPage * UserReadUsd + OwnedReadUsd);
    }
}
