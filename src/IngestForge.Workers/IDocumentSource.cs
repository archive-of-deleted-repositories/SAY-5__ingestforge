namespace IngestForge.Workers;

/// <summary>
/// Resolves a <c>source_uri</c> to a byte stream. The default
/// <see cref="FileDocumentSource"/> reads from local disk; production
/// deployments would swap in an S3 or Azure Blob source.
/// </summary>
public interface IDocumentSource
{
    Task<Stream> OpenAsync(string sourceUri, CancellationToken ct);
}
