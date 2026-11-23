using InventoryAggregation.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InventoryAggregation.Core;

/// <summary>
/// Reads every page from the source and accepts a page only if it is consistent with the first page
/// and with everything collected so far. A page that fails validation is requested again.
/// </summary>
public sealed class InventoryCollector(
    IInventoryPageSource source,
    IOptions<InventoryCollectionOptions> options,
    ILogger<InventoryCollector> logger)
{
    private readonly InventoryCollectionOptions _options = options.Value;

    public async Task<IReadOnlyList<InventoryRecord>> CollectAsync(CancellationToken cancellationToken)
    {
        var seenIds = new HashSet<int>();

        var first = await FetchValidPageAsync(1, reference: null, seenIds, cancellationToken);
        var records = new List<InventoryRecord>(first.TotalRecords);
        records.AddRange(first.Records);

        for (var pageNumber = 2; pageNumber <= first.TotalPages; pageNumber++)
        {
            var page = await FetchValidPageAsync(pageNumber, first, seenIds, cancellationToken);
            records.AddRange(page.Records);
        }

        return records;
    }

    private async Task<InventoryPage> FetchValidPageAsync(
        int pageNumber,
        InventoryPage? reference,
        HashSet<int> seenIds,
        CancellationToken cancellationToken)
    {
        string? problem = null;

        for (var attempt = 1; attempt <= _options.MaxPageAttempts; attempt++)
        {
            try
            {
                var page = await source.GetPageAsync(pageNumber, _options.PageSize, cancellationToken);
                problem = Validate(page, pageNumber, reference ?? page, seenIds);
                if (problem is null)
                {
                    seenIds.UnionWith(page.Records.Select(r => r.Id));
                    return page;
                }
            }
            catch (InventoryDataException ex)
            {
                problem = ex.Message;
            }

            logger.LogWarning(
                "Inventory page {Page} rejected on attempt {Attempt}/{MaxAttempts}: {Problem}",
                pageNumber, attempt, _options.MaxPageAttempts, problem);
        }

        throw new InventoryDataException(
            $"Inventory page {pageNumber} was not consistent after {_options.MaxPageAttempts} attempts: {problem}");
    }

    private string? Validate(InventoryPage page, int pageNumber, InventoryPage reference, HashSet<int> seenIds)
    {
        if (page.Page != pageNumber)
            return $"requested page {pageNumber} but received page {page.Page}";

        if (page.PageSize != _options.PageSize)
            return $"requested page size {_options.PageSize} but received {page.PageSize}";

        if (!HasConsistentTotals(page))
            return $"totalPages {page.TotalPages} does not match totalRecords {page.TotalRecords}";

        if (page.TotalRecords != reference.TotalRecords || page.TotalPages != reference.TotalPages)
            return "totals differ from the first page";

        var expectedCount = Math.Clamp(page.TotalRecords - (pageNumber - 1) * page.PageSize, 0, page.PageSize);
        if (page.Records.Count != expectedCount)
            return $"expected {expectedCount} records but received {page.Records.Count}";

        var pageIds = new HashSet<int>();
        foreach (var record in page.Records)
        {
            var recordProblem = ValidateRecord(record);
            if (recordProblem is not null)
                return recordProblem;

            if (!pageIds.Add(record.Id) || seenIds.Contains(record.Id))
                return $"duplicate record id {record.Id}";
        }

        return null;
    }

    private static bool HasConsistentTotals(InventoryPage page) => page.TotalRecords switch
    {
        < 0 => false,
        0 => page.TotalPages is 0 or 1,
        _ => page.TotalPages == (page.TotalRecords + page.PageSize - 1) / page.PageSize
    };

    private static string? ValidateRecord(InventoryRecord? record) => record switch
    {
        null => "page contains a null record",
        _ when string.IsNullOrWhiteSpace(record.Category) => $"record {record.Id} has no category",
        { Price: < 0 } => $"record {record.Id} has a negative price",
        { Quantity: < 0 } => $"record {record.Id} has a negative quantity",
        _ => null
    };
}
