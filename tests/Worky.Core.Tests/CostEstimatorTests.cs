namespace Worky.Core.Tests;

public class CostEstimatorTests
{
    [Fact]
    public void OrdinaryScanSpansOwnedToThirdPartyPostPrice()
    {
        var estimate = CostEstimator.ForScan(200);

        Assert.Equal(0.200m, estimate.FloorUsd);
        Assert.Equal(1.000m, estimate.CeilingUsd);
    }

    [Fact]
    public void TargetedScanAddsUserReadCeilingPerAuthorCap()
    {
        var estimate = CostEstimator.ForTargetedScan(50, 100);

        Assert.Equal(0.100m, estimate.FloorUsd);
        Assert.Equal(1.000m, estimate.CeilingUsd);
    }

    [Fact]
    public void SyncGraphSpansOwnReadToFullPagesOfUserReads()
    {
        var estimate = CostEstimator.ForSyncGraph(5);

        Assert.Equal(0.001m, estimate.FloorUsd);
        Assert.Equal(5.001m, estimate.CeilingUsd);
    }

    [Fact]
    public void MorePostsRaiseTheOrdinaryScanEstimate()
    {
        Assert.True(CostEstimator.ForScan(300).CeilingUsd > CostEstimator.ForScan(100).CeilingUsd);
        Assert.True(CostEstimator.ForScan(300).FloorUsd > CostEstimator.ForScan(100).FloorUsd);
    }

    [Fact]
    public void MoreAuthorsRaiseTheTargetedScanEstimate()
    {
        var small = CostEstimator.ForTargetedScan(50, 100);
        var large = CostEstimator.ForTargetedScan(150, 100);

        Assert.True(large.CeilingUsd > small.CeilingUsd);
        Assert.Equal(small.FloorUsd, large.FloorUsd);
    }

    [Fact]
    public void MorePagesRaiseTheSyncGraphEstimate()
    {
        Assert.True(CostEstimator.ForSyncGraph(10).CeilingUsd > CostEstimator.ForSyncGraph(1).CeilingUsd);
        Assert.Equal(CostEstimator.ForSyncGraph(1).FloorUsd, CostEstimator.ForSyncGraph(10).FloorUsd);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NonPositiveLimitsClampToZeroCost(int limit)
    {
        var scan = CostEstimator.ForScan(limit);

        Assert.Equal(0.000m, scan.FloorUsd);
        Assert.Equal(0.000m, scan.CeilingUsd);
    }
}
