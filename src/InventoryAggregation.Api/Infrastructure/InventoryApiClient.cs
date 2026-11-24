using System.Net.Http.Json;
using System.Text.Json;
using InventoryAggregation.Core;
using InventoryAggregation.Core.Models;
using Polly;

namespace InventoryAggregation.Api.Infrastructure;

/// <summary>
/// Typed HttpClient for the upstream Inventory API. Retries, timeouts and circuit breaking are applied
/// by the resilience handler registered in <see cref="ServiceCollectionExtensions"/>; this class only
/// translates the final outcome into domain exceptions.
/// </summary>
internal sealed class InventoryApiClient(HttpClient httpClient) : IInventoryPageSource
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    public async Task<InventoryPage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var requestUri = $"api/records?page={page}&pageSize={pageSize}";

        try
        {
            using var response = await httpClient.GetAsync(
                requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InventoryUnavailableException(
                    $"Inventory API returned {(int)response.StatusCode} for page {page}.");
            }

            return await response.Content.ReadFromJsonAsync<InventoryPage>(SerializerOptions, cancellationToken)
                   ?? throw new InventoryDataException($"Inventory API returned an empty body for page {page}.");
        }
        catch (JsonException ex)
        {
            throw new InventoryDataException($"Inventory API returned an unreadable payload for page {page}.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InventoryUnavailableException($"Inventory API could not be reached for page {page}.", ex);
        }
        catch (ExecutionRejectedException ex)
        {
            throw new InventoryUnavailableException($"Inventory API request for page {page} was rejected: {ex.Message}", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InventoryUnavailableException($"Inventory API request for page {page} timed out.", ex);
        }
    }
}
