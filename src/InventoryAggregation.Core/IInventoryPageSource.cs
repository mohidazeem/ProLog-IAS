using InventoryAggregation.Core.Models;

namespace InventoryAggregation.Core;

/// <summary>
/// Supplies one page of inventory records. Implementations throw <see cref="InventoryUnavailableException"/>
/// when the source cannot be reached and <see cref="InventoryDataException"/> when its payload is unreadable.
/// </summary>
public interface IInventoryPageSource
{
    Task<InventoryPage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken);
}
