namespace IngestForge.Storage;

/// <summary>
/// Persistent record of a successfully extracted document. One row per unique
/// <see cref="DedupKey"/>: if the same key arrives twice, the second attempt
/// is a no-op (the dedup table answers "yes, already processed" before any
/// extraction work runs).
/// </summary>
public sealed class ProcessedDocument
{
    public Guid Id { get; set; }
    public string Tenant { get; set; } = string.Empty;
    public string DedupKey { get; set; } = string.Empty;
    public string SourceUri { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int CharCount { get; set; }
    public int? PageCount { get; set; }
    public string? Title { get; set; }
    public string? Author { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
    public DateTimeOffset? DocumentCreatedAt { get; set; }
    public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Documents that failed extraction repeatedly. The pipeline writes one row
/// per dedup key after the retry budget is exhausted, then stops trying.
/// Inspection of this table is how operators discover broken producers.
/// </summary>
public sealed class PoisonDocument
{
    public Guid Id { get; set; }
    public string Tenant { get; set; } = string.Empty;
    public string DedupKey { get; set; } = string.Empty;
    public string SourceUri { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string LastError { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}
