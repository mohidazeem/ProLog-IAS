using InventoryAggregation.Core;
using static InventoryAggregation.Tests.TestDoubles.TestData;

namespace InventoryAggregation.Tests;

public class InventoryAggregatorTests
{
    private readonly InventoryAggregator _aggregator = new();

    [Fact]
    public void Aggregate_ComputesFiguresPerCategory()
    {
        var report = _aggregator.Aggregate(
        [
            Record(3, "Furniture", price: 80m, quantity: 1, dayOffset: 2),
            Record(1, "Electronics", price: 149.99m, quantity: 12, dayOffset: 0),
            Record(2, "Electronics", price: 10.50m, quantity: 3, dayOffset: 5)
        ]);

        Assert.Equal(["Electronics", "Furniture"], report.Categories.Select(c => c.Category));

        var electronics = report.Categories[0];
        Assert.Equal(1831.38m, electronics.TotalInventoryValue);
        Assert.Equal(2, electronics.AvailableRecordCount);
        Assert.Equal(2, electronics.MostRecentlyUpdated.Id);
        Assert.Equal([1, 2], electronics.Items.Select(r => r.Id));
    }

    [Fact]
    public void Aggregate_ExcludesZeroQuantityRecordsFromEveryFigure()
    {
        var report = _aggregator.Aggregate(
        [
            Record(1, "Electronics", price: 100m, quantity: 2, dayOffset: 0),
            Record(2, "Electronics", price: 999m, quantity: 0, dayOffset: 10),
            Record(3, "Tools", price: 5m, quantity: 0)
        ]);

        var electronics = Assert.Single(report.Categories);
        Assert.Equal(200m, electronics.TotalInventoryValue);
        Assert.Equal(1, electronics.AvailableRecordCount);
        Assert.Equal(1, electronics.MostRecentlyUpdated.Id);
        Assert.Equal([1], electronics.Items.Select(r => r.Id));
    }

    [Fact]
    public void Aggregate_TieOnLastUpdated_PicksLowestId()
    {
        var report = _aggregator.Aggregate(
        [
            Record(5, dayOffset: 3),
            Record(2, dayOffset: 3),
            Record(1, dayOffset: 1)
        ]);

        Assert.Equal(2, Assert.Single(report.Categories).MostRecentlyUpdated.Id);
    }

    [Fact]
    public void Aggregate_GroupsCategoriesIgnoringCaseAndWhitespace_UsingMostCommonSpelling()
    {
        var report = _aggregator.Aggregate(
        [
            Record(1, " electronics "),
            Record(2, "Electronics"),
            Record(3, "ELECTRONICS"),
            Record(4, "Electronics ")
        ]);

        var category = Assert.Single(report.Categories);
        Assert.Equal("Electronics", category.Category);
        Assert.Equal(4, category.AvailableRecordCount);
    }

    [Fact]
    public void Aggregate_NoRecords_ReturnsNoCategories()
    {
        Assert.Empty(_aggregator.Aggregate([]).Categories);
    }
}
