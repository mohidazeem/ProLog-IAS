using InventoryAggregation.Core.Models;

namespace InventoryAggregation.Core;

public sealed class InventoryAggregator
{
    public InventoryReport Aggregate(IEnumerable<InventoryRecord> records)
    {
        var categories = records
            .Where(r => r.Quantity > 0)
            .GroupBy(r => r.Category.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(Summarise)
            .OrderBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new InventoryReport(categories);
    }

    private static CategorySummary Summarise(IGrouping<string, InventoryRecord> group)
    {
        var items = group.OrderBy(r => r.Id).ToList();
        var mostRecent = items.OrderByDescending(r => r.LastUpdated).ThenBy(r => r.Id).First();

        return new CategorySummary(
            MostCommonSpelling(items),
            items.Sum(r => r.Price * r.Quantity),
            items.Count,
            mostRecent,
            items);
    }

    private static string MostCommonSpelling(IEnumerable<InventoryRecord> items) =>
        items.Select(r => r.Category.Trim())
            .GroupBy(name => name, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .First()
            .Key;
}
