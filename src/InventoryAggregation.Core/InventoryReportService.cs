using InventoryAggregation.Core.Models;

namespace InventoryAggregation.Core;

public sealed class InventoryReportService(InventoryCollector collector, InventoryAggregator aggregator)
    : IInventoryReportService
{
    public async Task<InventoryReport> GetReportAsync(CancellationToken cancellationToken)
    {
        var records = await collector.CollectAsync(cancellationToken);
        return aggregator.Aggregate(records);
    }
}
