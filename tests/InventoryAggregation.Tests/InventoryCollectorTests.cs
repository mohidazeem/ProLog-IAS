using InventoryAggregation.Core;
using InventoryAggregation.Core.Models;
using InventoryAggregation.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InventoryAggregation.Tests;

public class InventoryCollectorTests
{
    private const int PageSize = 3;
    private const int MaxPageAttempts = 3;
    private static readonly IReadOnlyList<InventoryRecord> Dataset = TestData.Records(7);

    private static readonly Dictionary<string, Func<InventoryPage, InventoryPage>> Corruptions = new()
    {
        ["wrong page number"] = p => p with { Page = 1 },
        ["wrong page size"] = p => p with { PageSize = 20 },
        ["short page"] = p => p with { Records = p.Records.Take(1).ToList() },
        ["empty page"] = p => p with { Records = [] },
        ["id from earlier page with different content"] = p => p with { Records = [Dataset[0] with { Price = 999m }, .. p.Records] },
        ["id repeated within page with different content"] = p => p with { Records = [.. p.Records, p.Records[0] with { Quantity = 99 }] },
        ["exact repeat replacing a new record"] = p => p with { Records = [p.Records[0], p.Records[0], p.Records[2]] },
        ["totals changed between pages"] = p => p with { TotalRecords = 6, TotalPages = 2 },
        ["totalPages inconsistent with totalRecords"] = p => p with { TotalPages = 5 },
        ["negative quantity"] = p => p with { Records = [p.Records[0] with { Quantity = -1 }, .. p.Records.Skip(1)] },
        ["negative price"] = p => p with { Records = [p.Records[0] with { Price = -1m }, .. p.Records.Skip(1)] },
        ["blank category"] = p => p with { Records = [p.Records[0] with { Category = " " }, .. p.Records.Skip(1)] },
        ["null record"] = p => p with { Records = [null!, .. p.Records.Skip(1)] },
        ["unreadable payload"] = _ => throw new InventoryDataException("unreadable")
    };

    public static TheoryData<string> CorruptionNames => new(Corruptions.Keys);

    [Fact]
    public async Task CollectAsync_ReturnsEveryRecordAcrossAllPages()
    {
        var source = new FakePageSource((page, _) => TestData.Page(Dataset, page, PageSize));

        var records = await CreateCollector(source).CollectAsync(CancellationToken.None);

        Assert.Equal(Dataset, records);
        Assert.Equal(1, source.AttemptsFor(3));
        Assert.Equal(1, source.AttemptsFor(4));
        Assert.Equal(0, source.AttemptsFor(5));
    }

    [Fact]
    public async Task CollectAsync_PagesRepeatPreviousPagesLastRecord_DropsTheExactRepeat()
    {
        var source = new FakePageSource((page, _) =>
        {
            var valid = TestData.Page(Dataset, page, PageSize);
            if (page == 1)
                return valid;

            var previousPageLast = Dataset[Math.Min((page - 1) * PageSize, Dataset.Count) - 1];
            return valid with { Records = [previousPageLast, .. valid.Records] };
        });

        var records = await CreateCollector(source).CollectAsync(CancellationToken.None);

        Assert.Equal(Dataset, records);
        Assert.Equal(1, source.AttemptsFor(2));
    }

    [Fact]
    public async Task CollectAsync_RecordsBeyondReportedLastPage_AreRejected()
    {
        var source = new FakePageSource((page, _) =>
        {
            var valid = TestData.Page(Dataset, page, PageSize);
            return page == 4 ? valid with { Records = [TestData.Record(99)] } : valid;
        });

        await Assert.ThrowsAsync<InventoryDataException>(
            () => CreateCollector(source).CollectAsync(CancellationToken.None));
        Assert.Equal(MaxPageAttempts, source.AttemptsFor(4));
    }

    [Fact]
    public async Task CollectAsync_EmptyDataset_ReturnsNoRecords()
    {
        var source = new FakePageSource((page, _) => new InventoryPage([], page, PageSize, 0, 0));

        Assert.Empty(await CreateCollector(source).CollectAsync(CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(CorruptionNames))]
    public async Task CollectAsync_PageFailsValidationOnce_IsRequestedAgain(string corruption)
    {
        var source = new FakePageSource((page, attempt) =>
        {
            var valid = TestData.Page(Dataset, page, PageSize);
            return page == 2 && attempt == 1 ? Corruptions[corruption](valid) : valid;
        });

        var records = await CreateCollector(source).CollectAsync(CancellationToken.None);

        Assert.Equal(Dataset, records);
        Assert.Equal(2, source.AttemptsFor(2));
    }

    [Fact]
    public async Task CollectAsync_PageNeverValid_ThrowsAfterMaxAttempts()
    {
        var source = new FakePageSource((page, _) => TestData.Page(Dataset, page, PageSize) with { Page = 1 });

        await Assert.ThrowsAsync<InventoryDataException>(
            () => CreateCollector(source).CollectAsync(CancellationToken.None));
        Assert.Equal(MaxPageAttempts, source.AttemptsFor(2));
    }

    [Fact]
    public async Task CollectAsync_SourceUnavailable_PropagatesWithoutCollectorRetry()
    {
        var source = new FakePageSource((page, _) => page == 2
            ? throw new InventoryUnavailableException("down")
            : TestData.Page(Dataset, page, PageSize));

        await Assert.ThrowsAsync<InventoryUnavailableException>(
            () => CreateCollector(source).CollectAsync(CancellationToken.None));
        Assert.Equal(1, source.AttemptsFor(2));
    }

    private static InventoryCollector CreateCollector(IInventoryPageSource source) =>
        new(
            source,
            Options.Create(new InventoryCollectionOptions { PageSize = PageSize, MaxPageAttempts = MaxPageAttempts }),
            NullLogger<InventoryCollector>.Instance);
}
