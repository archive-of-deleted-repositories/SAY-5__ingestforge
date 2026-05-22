using IngestForge.Core;
using IngestForge.Extractors;
using IngestForge.Storage;
using Microsoft.Extensions.Logging;

namespace IngestForge.Workers;

/// <summary>
/// The pure unit of work. Independent of Kafka: a single <see cref="WorkItem"/>
/// in, an outcome out. Hosting tests and the bench harness call this directly.
/// </summary>
public sealed class WorkItemProcessor
{
    private readonly ExtractorRegistry _registry;
    private readonly IDocumentSource _source;
    private readonly IDedupStore _dedup;
    private readonly ILogger<WorkItemProcessor> _logger;

    public WorkItemProcessor(
        ExtractorRegistry registry,
        IDocumentSource source,
        IDedupStore dedup,
        ILogger<WorkItemProcessor> logger)
    {
        _registry = registry;
        _source = source;
        _dedup = dedup;
        _logger = logger;
    }

    public async Task<ProcessOutcome> ProcessAsync(WorkItem item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (await _dedup.IsProcessedAsync(item.Tenant, item.DedupKey, ct).ConfigureAwait(false))
        {
            return ProcessOutcome.Skipped;
        }

        var reserved = await _dedup.TryReserveAsync(item.Tenant, item.DedupKey, ct).ConfigureAwait(false);
        if (!reserved)
        {
            return ProcessOutcome.Skipped;
        }

        try
        {
            await using var stream = await _source.OpenAsync(item.SourceUri, ct).ConfigureAwait(false);
            var result = await _registry.ExtractAsync(stream, item.ContentType, ct).ConfigureAwait(false);
            await _dedup.RecordSuccessAsync(item, result, ct).ConfigureAwait(false);
            return ProcessOutcome.Processed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "extraction failed: doc_id={DocId} uri={Uri}", item.DocId, item.SourceUri);
            await _dedup.RecordPoisonAsync(item, $"{ex.GetType().Name}:{ex.Message}", ct).ConfigureAwait(false);
            return ProcessOutcome.Poisoned;
        }
    }
}

public enum ProcessOutcome
{
    Processed,
    Skipped,
    Poisoned,
}
