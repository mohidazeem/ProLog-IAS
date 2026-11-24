using System.ComponentModel.DataAnnotations;

namespace InventoryAggregation.Api.Infrastructure;

public sealed class InventoryApiOptions
{
    public const string SectionName = "InventoryApi";

    [Required]
    public Uri? BaseAddress { get; set; }

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string ApiKeyHeaderName { get; set; } = string.Empty;
}
