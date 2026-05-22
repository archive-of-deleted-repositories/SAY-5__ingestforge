namespace IngestForge.Workers;

/// <summary>
/// Bound from configuration. Defaults are tuned for a 10-core box with
/// 200k docs/hour of mixed traffic; tune per environment.
/// </summary>
public sealed class WorkerOptions
{
    /// <summary>Kafka bootstrap servers, e.g. <c>broker:9092</c>.</summary>
    public string BootstrapServers { get; set; } = string.Empty;

    public string Topic { get; set; } = "ingestforge.work-items";

    public string DeadLetterTopic { get; set; } = "ingestforge.work-items.dlq";

    public string GroupId { get; set; } = "ingestforge";

    /// <summary>How many worker tasks to run in parallel per consumer.</summary>
    public int ParallelismPerPartition { get; set; } = 4;

    /// <summary>How many times to retry an extraction before sending to DLQ.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Where blob bytes live. Files on disk for now; future: S3/Azure Blob.</summary>
    public string DocumentRoot { get; set; } = "/var/lib/ingestforge/docs";

    /// <summary>Per-document hard limit. 50 MB by default.</summary>
    public int MaxDocumentBytes { get; set; } = 50 * 1024 * 1024;
}
