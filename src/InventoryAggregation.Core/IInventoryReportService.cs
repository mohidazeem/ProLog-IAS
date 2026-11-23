using InventoryAggregation.Core.Models;

namespace InventoryAggregation.Core;

public interface IInventoryReportService
{
    Task<InventoryReport> GetReportAsync(CancellationToken cancellationToken);
}
