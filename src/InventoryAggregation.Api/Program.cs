using InventoryAggregation.Api.Infrastructure;
using InventoryAggregation.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<InventoryExceptionHandler>();
builder.Services.AddInventoryAggregation(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/api/inventory/categories",
        (IInventoryReportService reportService, CancellationToken cancellationToken) =>
            reportService.GetReportAsync(cancellationToken))
    .WithName("GetInventoryByCategory");

app.Run();
