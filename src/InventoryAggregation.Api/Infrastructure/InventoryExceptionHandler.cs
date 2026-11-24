using InventoryAggregation.Core;
using Microsoft.AspNetCore.Diagnostics;

namespace InventoryAggregation.Api.Infrastructure;

internal sealed class InventoryExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var title = exception switch
        {
            InventoryUnavailableException => "Inventory source unavailable",
            InventoryDataException => "Inventory source returned inconsistent data",
            _ => null
        };

        if (title is null)
            return ValueTask.FromResult(false);

        httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;

        return problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = StatusCodes.Status502BadGateway,
                Title = title,
                Detail = "The inventory report could not be produced from the upstream API. Please try again later."
            }
        });
    }
}
