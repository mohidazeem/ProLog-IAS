using System.ComponentModel.DataAnnotations;

namespace InventoryAggregation.Core;

public sealed class InventoryCollectionOptions
{
    public const string SectionName = "InventoryCollection";

    [Range(1, 25)]
    public int PageSize { get; set; } = 25;

    [Range(1, 10)]
    public int MaxPageAttempts { get; set; } = 3;
}
