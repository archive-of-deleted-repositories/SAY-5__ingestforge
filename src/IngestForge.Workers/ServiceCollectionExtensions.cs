using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IngestForge.Workers;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIngestForgeWorkers(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        services.Configure<WorkerOptions>(config.GetSection("Workers"));
        services.AddSingleton<IDocumentSource, FileDocumentSource>();
        services.AddSingleton<WorkItemProcessor>();
        services.AddHostedService<KafkaConsumerService>();
        return services;
    }
}
