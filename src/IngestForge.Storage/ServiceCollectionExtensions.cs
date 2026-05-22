using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IngestForge.Storage;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register the Postgres-backed storage layer. Caller passes a connection
    /// string; we use a context factory so each worker thread can grab its own
    /// short-lived DbContext.
    /// </summary>
    public static IServiceCollection AddIngestForgeStorage(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextFactory<IngestForgeDbContext>(opts => opts.UseNpgsql(connectionString));
        services.AddSingleton<IDedupStore, PostgresDedupStore>();
        return services;
    }
}
