using IngestForge.Core;
using Microsoft.Extensions.DependencyInjection;

namespace IngestForge.Extractors;

/// <summary>
/// Wire every extractor into DI as a singleton, then resolve them into one registry.
/// Adding a new format means writing one class and adding one line below.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIngestForgeExtractors(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDocumentExtractor, PlainTextExtractor>();
        services.AddSingleton<IDocumentExtractor, HtmlExtractor>();
        services.AddSingleton<IDocumentExtractor, PdfExtractor>();
        services.AddSingleton<IDocumentExtractor, DocxExtractor>();
        services.AddSingleton<IDocumentExtractor, EmailExtractor>();
        services.AddSingleton<ExtractorRegistry>();
        return services;
    }
}
