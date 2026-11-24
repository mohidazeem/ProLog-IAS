using InventoryAggregation.Core;
using Microsoft.Extensions.Options;

namespace InventoryAggregation.Api.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInventoryAggregation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InventoryCollectionOptions>()
            .Bind(configuration.GetSection(InventoryCollectionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddInventoryApiClient(configuration);
        services.AddSingleton<InventoryAggregator>();
        services.AddTransient<InventoryCollector>();
        services.AddScoped<IInventoryReportService, InventoryReportService>();

        return services;
    }

    public static IHttpClientBuilder AddInventoryApiClient(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(InventoryApiOptions.SectionName);

        services.AddOptions<InventoryApiOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .Validate(
                o => o.BaseAddress is { IsAbsoluteUri: true } uri && uri.Scheme == Uri.UriSchemeHttps,
                $"{InventoryApiOptions.SectionName}:BaseAddress must be an absolute HTTPS URL.")
            .ValidateOnStart();

        var builder = services.AddHttpClient<IInventoryPageSource, InventoryApiClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<InventoryApiOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseAddress!.AbsoluteUri.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add(options.ApiKeyHeaderName, options.ApiKey);
        });

        builder.AddStandardResilienceHandler(section.GetSection("Resilience"));

        return builder;
    }
}
