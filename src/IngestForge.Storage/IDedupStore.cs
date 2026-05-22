using IngestForge.Core;

namespace IngestForge.Storage;

/// <summary>
/// The dedup contract, in one interface, so workers do not depend on EF Core
/// directly. Implementations may use Postgres (default), Redis (future),
/// or in-memory (tests).
/// </summary>
public interface IDedupStore
{
    /// <summary>
    /// Atomically reserve a dedup key. Returns true if this caller now owns
    /// processing for the key; false if another worker has already recorded it.
    /// Implementations must not allow two workers to both receive true for the
    /// same (tenant, dedup_key).
    /// </summary>
    Task<bool> TryReserveAsync(string tenant, string dedupKey, CancellationToken ct);

    /// <summary>
    /// Record a successful extraction. Must be called after <c>TryReserveAsync</c>
    /// returned true for the same key; otherwise the call is a no-op.
    /// </summary>
    Task RecordSuccessAsync(WorkItem item, ExtractionResult result, CancellationToken ct);

    /// <summary>
    /// Move a key to the poison table after the retry budget is exhausted.
    /// Idempotent: repeated calls just bump <c>attempt_count</c> / <c>last_seen_at</c>.
    /// </summary>
    Task RecordPoisonAsync(WorkItem item, string lastError, CancellationToken ct);

    /// <summary>True if this key has already been processed successfully.</summary>
    Task<bool> IsProcessedAsync(string tenant, string dedupKey, CancellationToken ct);
}
