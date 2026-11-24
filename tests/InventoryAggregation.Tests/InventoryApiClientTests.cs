using System.Diagnostics;
using System.Net;
using System.Text;
using InventoryAggregation.Api.Infrastructure;
using InventoryAggregation.Core;
using InventoryAggregation.Core.Models;
using InventoryAggregation.Tests.TestDoubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InventoryAggregation.Tests;

/// <summary>
/// Exercises the client through the same DI registration the application uses, so the real
/// resilience pipeline runs; only the network is replaced.
/// </summary>
public class InventoryApiClientTests
{
    private const int RetryAttempts = 3;

    private const string ValidPageJson = """
        {
          "records": [
            { "id": 1001, "name": "Thermal Receipt Printer", "category": "Electronics",
              "price": 149.99, "quantity": 12, "lastUpdated": "2026-10-15T12:30:00Z" }
          ],
          "page": 1, "pageSize": 25, "totalPages": 1, "totalRecords": 1
        }
        """;

    [Fact]
    public async Task GetPageAsync_SendsPagingQueryAndApiKey_AndParsesResponse()
    {
        var handler = new StubHttpMessageHandler((_, _) => Respond(HttpStatusCode.OK, ValidPageJson));
        using var services = CreateServices(handler);

        var page = await GetClient(services).GetPageAsync(1, 25, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://inventory.test/v1/api/records?page=1&pageSize=25", request.RequestUri!.ToString());
        Assert.Equal(["test-key"], request.Headers.GetValues("X-Api-Key"));

        var expected = new InventoryRecord(
            1001, "Thermal Receipt Printer", "Electronics", 149.99m, 12,
            new DateTimeOffset(2026, 10, 15, 12, 30, 0, TimeSpan.Zero));
        Assert.Equal(expected, Assert.Single(page.Records));
        Assert.Equal((1, 25, 1, 1), (page.Page, page.PageSize, page.TotalPages, page.TotalRecords));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task GetPageAsync_TransientStatus_IsRetriedUntilSuccess(HttpStatusCode transientStatus)
    {
        var handler = new StubHttpMessageHandler((call, _) =>
            call < 3 ? Respond(transientStatus) : Respond(HttpStatusCode.OK, ValidPageJson));
        using var services = CreateServices(handler);

        var page = await GetClient(services).GetPageAsync(1, 25, CancellationToken.None);

        Assert.Single(page.Records);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task GetPageAsync_PersistentServerError_ThrowsUnavailableAfterRetries()
    {
        var handler = new StubHttpMessageHandler((_, _) => Respond(HttpStatusCode.InternalServerError));
        using var services = CreateServices(handler);

        await Assert.ThrowsAsync<InventoryUnavailableException>(
            () => GetClient(services).GetPageAsync(1, 25, CancellationToken.None));
        Assert.Equal(1 + RetryAttempts, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task GetPageAsync_NonTransientStatus_FailsWithoutRetry(HttpStatusCode status)
    {
        var handler = new StubHttpMessageHandler((_, _) => Respond(status));
        using var services = CreateServices(handler);

        await Assert.ThrowsAsync<InventoryUnavailableException>(
            () => GetClient(services).GetPageAsync(1, 25, CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetPageAsync_TransportFailure_IsRetriedUntilSuccess()
    {
        var handler = new StubHttpMessageHandler((call, _) =>
            call == 1 ? throw new HttpRequestException("connection reset") : Respond(HttpStatusCode.OK, ValidPageJson));
        using var services = CreateServices(handler);

        await GetClient(services).GetPageAsync(1, 25, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetPageAsync_PersistentTransportFailure_ThrowsUnavailable()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("connection refused"));
        using var services = CreateServices(handler);

        await Assert.ThrowsAsync<InventoryUnavailableException>(
            () => GetClient(services).GetPageAsync(1, 25, CancellationToken.None));
        Assert.Equal(1 + RetryAttempts, handler.Requests.Count);
    }

    [Fact]
    public async Task GetPageAsync_UpstreamNeverResponds_TimesOutAndThrowsUnavailable()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        });
        using var services = CreateServices(handler, ("InventoryApi:Resilience:AttemptTimeout:Timeout", "00:00:00.050"));

        await Assert.ThrowsAsync<InventoryUnavailableException>(
            () => GetClient(services).GetPageAsync(1, 25, CancellationToken.None));
        Assert.Equal(1 + RetryAttempts, handler.Requests.Count);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{ "page": 1, "pageSize": 25, "totalPages": 1, "totalRecords": 1 }""")]
    [InlineData("""{ "records": [ { "id": 1, "name": "x", "category": null, "price": 1, "quantity": 1, "lastUpdated": "2026-10-15T12:30:00Z" } ], "page": 1, "pageSize": 25, "totalPages": 1, "totalRecords": 1 }""")]
    [InlineData("""{ "records": [ { "id": 1, "name": "x", "category": "A", "price": 1, "quantity": 1.5, "lastUpdated": "2026-10-15T12:30:00Z" } ], "page": 1, "pageSize": 25, "totalPages": 1, "totalRecords": 1 }""")]
    public async Task GetPageAsync_UnreadablePayload_ThrowsDataException(string body)
    {
        var handler = new StubHttpMessageHandler((_, _) => Respond(HttpStatusCode.OK, body));
        using var services = CreateServices(handler);

        await Assert.ThrowsAsync<InventoryDataException>(
            () => GetClient(services).GetPageAsync(1, 25, CancellationToken.None));
    }

    private static ServiceProvider CreateServices(StubHttpMessageHandler handler, params (string Key, string Value)[] overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["InventoryApi:BaseAddress"] = "https://inventory.test/v1",
            ["InventoryApi:ApiKey"] = "test-key",
            ["InventoryApi:ApiKeyHeaderName"] = "X-Api-Key",
            ["InventoryApi:Resilience:Retry:MaxRetryAttempts"] = RetryAttempts.ToString(),
            ["InventoryApi:Resilience:Retry:Delay"] = "00:00:00"
        };
        foreach (var (key, value) in overrides)
            settings[key] = value;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddInventoryApiClient(configuration).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static IInventoryPageSource GetClient(IServiceProvider services) =>
        services.GetRequiredService<IInventoryPageSource>();

    private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string body = "") =>
        Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
}
