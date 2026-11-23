namespace InventoryAggregation.Core.Models;

public sealed record CategorySummary(
    string Category,
    decimal TotalInventoryValue,
    int AvailableRecordCount,
    InventoryRecord MostRecentlyUpdated,
    IReadOnlyList<InventoryRecord> Items);
