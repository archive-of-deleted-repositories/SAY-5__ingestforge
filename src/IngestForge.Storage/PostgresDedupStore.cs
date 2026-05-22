using IngestForge.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IngestForge.Storage;

/// <summary>
/// Postgres-backed dedup. Reservation uses a placeholder row inserted with the
/// unique index <c>(tenant, dedup_key)</c> as the conflict gate: if the insert
/// throws on the unique constraint, another worker beat us to it.
/// </summary>
public sealed class PostgresDedupStore : IDedupStore
{
    private readonly IDbContextFactory<IngestForgeDbContext> _factory;
    private readonly ILogger<PostgresDedupStore> _logger;

    public PostgresDedupStore(IDbContextFactory<IngestForgeDbContext> factory, ILogger<PostgresDedupStore> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<bool> TryReserveAsync(string tenant, string dedupKey, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.ProcessedDocuments.Add(new ProcessedDocument
        {
            Id = Guid.NewGuid(),
            Tenant = tenant,
            DedupKey = dedupKey,
            SourceUri = string.Empty,
            ContentType = string.Empty,
            ProcessedAt = DateTimeOffset.UtcNow,
        });
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _logger.LogDebug("dedup key already reserved: tenant={Tenant} key={Key}", tenant, dedupKey);
            return false;
        }
    }

    public async Task RecordSuccessAsync(WorkItem item, ExtractionResult result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(result);

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await db.ProcessedDocuments
            .FirstOrDefaultAsync(x => x.Tenant == item.Tenant && x.DedupKey == item.DedupKey, ct)
            .ConfigureAwait(false);
        if (row == null)
        {
            return;
        }

        row.SourceUri = item.SourceUri;
        row.ContentType = result.Metadata.Mime;
        row.CharCount = result.TotalChars;
        row.PageCount = result.Metadata.PageCount;
        row.Title = result.Metadata.Title;
        row.Author = result.Metadata.Author;
        row.DocumentCreatedAt = result.Metadata.CreatedAt;
        row.Warnings = result.Warnings.ToArray();
        row.ProcessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RecordPoisonAsync(WorkItem item, string lastError, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.PoisonDocuments
            .FirstOrDefaultAsync(x => x.Tenant == item.Tenant && x.DedupKey == item.DedupKey, ct)
            .ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        if (existing == null)
        {
            db.PoisonDocuments.Add(new PoisonDocument
            {
                Id = Guid.NewGuid(),
                Tenant = item.Tenant,
                DedupKey = item.DedupKey,
                SourceUri = item.SourceUri,
                ContentType = item.ContentType,
                LastError = Truncate(lastError, 4000),
                AttemptCount = 1,
                FirstSeenAt = now,
                LastSeenAt = now,
            });
        }
        else
        {
            existing.AttemptCount += 1;
            existing.LastError = Truncate(lastError, 4000);
            existing.LastSeenAt = now;
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> IsProcessedAsync(string tenant, string dedupKey, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.ProcessedDocuments
            .AsNoTracking()
            .AnyAsync(x => x.Tenant == tenant && x.DedupKey == dedupKey && x.ContentType != string.Empty, ct)
            .ConfigureAwait(false);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // Npgsql surfaces 23505 as the inner PostgresException.SqlState.
        var inner = ex.InnerException;
        while (inner != null)
        {
            var sqlState = inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string;
            if (sqlState == "23505")
            {
                return true;
            }
            inner = inner.InnerException;
        }
        return false;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max]);
}
