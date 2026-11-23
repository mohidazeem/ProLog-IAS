namespace InventoryAggregation.Core.Models;

public sealed record InventoryReport(IReadOnlyList<CategorySummary> Categories);
