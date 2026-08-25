namespace Worky.Core;

public sealed class ApiReadTracker
{
    public int Posts { get; private set; }
    public int Users { get; private set; }

    public void CountPosts(int count) => Posts += Math.Max(count, 0);
    public void CountUsers(int count) => Users += Math.Max(count, 0);

    public decimal EstimatedPostCostUsd => Posts * CostEstimator.PostReadThirdPartyUsd;
    public decimal EstimatedUserCostUsd => Users * CostEstimator.UserReadUsd;
}
