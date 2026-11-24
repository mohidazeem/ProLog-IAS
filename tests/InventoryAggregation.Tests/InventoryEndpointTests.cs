using System.Net;
using System.Text.Json;
using InventoryAggregation.Core;
using InventoryAggregation.Tests.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InventoryAggregation.Tests;

public class InventoryEndpointTests
{
    private const string Route = "/api/inventory/categories";
    private const int ConfiguredPageSize = 25;

    [Fact]
    public async Task Get_ReturnsAggregatedCategories()
    {
        var dataset = new[]
        {
            TestData.Record(1, "Electronics", price: 149.99m, quantity: 12),
            TestData.Record(2, "Tools", price: 5m, quantity: 0)
        };
        await using var factory = CreateFactory(new FakePageSource((page, _) => TestData.Page(dataset, page, ConfiguredPageSize)));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var category = Assert.Single(body.RootElement.GetProperty("categories").EnumerateArray());
        Assert.Equal("Electronics", category.GetProperty("category").GetString());
        Assert.Equal(1799.88m, category.GetProperty("totalInventoryValue").GetDecimal());
        Assert.Equal(1, category.GetProperty("availableRecordCount").GetInt32());
        Assert.Equal(1, category.GetProperty("mostRecentlyUpdated").GetProperty("id").GetInt32());
        Assert.Single(category.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Get_UpstreamUnavailable_ReturnsBadGatewayProblem()
    {
        await using var factory = CreateFactory(new FakePageSource((_, _) => throw new InventoryUnavailableException("down")));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_UnexpectedFailure_ReturnsProblemWithoutInternals()
    {
        await using var factory = CreateFactory(new FakePageSource((_, _) => throw new InvalidOperationException("secret internals")));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("secret internals", await response.Content.ReadAsStringAsync());
    }

    private static WebApplicationFactory<Program> CreateFactory(IInventoryPageSource source) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("InventoryApi:BaseAddress", "https://inventory.test/");
            builder.UseSetting("InventoryApi:ApiKey", "test-key");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IInventoryPageSource>();
                services.AddSingleton(source);
            });
        });
}
