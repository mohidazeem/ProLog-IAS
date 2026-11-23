using InventoryAggregation.Core.Models;

namespace InventoryAggregation.Tests.TestDoubles;

internal static class TestData
{
    private static readonly DateTimeOffset BaseTime = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    public static InventoryRecord Record(
        int id, string category = "Electronics", decimal price = 10m, int quantity = 1, int dayOffset = 0) =>
        new(id, $"Item {id}", category, price, quantity, BaseTime.AddDays(dayOffset));

    public static IReadOnlyList<InventoryRecord> Records(int count) =>
        Enumerable.Range(1, count).Select(id => Record(id)).ToList();

    public static InventoryPage Page(IReadOnlyList<InventoryRecord> dataset, int page, int pageSize) =>
        new(
            dataset.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            page,
            pageSize,
            (dataset.Count + pageSize - 1) / pageSize,
            dataset.Count);
}
