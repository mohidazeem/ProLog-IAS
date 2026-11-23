using InventoryAggregation.Core;
using InventoryAggregation.Core.Models;

namespace InventoryAggregation.Tests.TestDoubles;

/// <summary>Answers each request with <c>respond(page, attemptForThatPage)</c>.</summary>
internal sealed class FakePageSource(Func<int, int, InventoryPage> respond) : IInventoryPageSource
{
    private readonly Dictionary<int, int> _attempts = [];

    public int AttemptsFor(int page) => _attempts.GetValueOrDefault(page);

    public Task<InventoryPage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var attempt = _attempts[page] = AttemptsFor(page) + 1;
        return Task.FromResult(respond(page, attempt));
    }
}
