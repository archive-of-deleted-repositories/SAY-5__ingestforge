namespace IngestForge.Core;

/// <summary>
/// One extractor per format. Implementations must be stateless and thread-safe:
/// they are resolved as singletons and called by every worker concurrently.
/// </summary>
public interface IDocumentExtractor
{
    /// <summary>
    /// MIME types this extractor handles. The registry maps each entry to the implementation.
    /// </summary>
    IReadOnlyCollection<string> SupportedContentTypes { get; }

    /// <summary>
    /// Extract text + metadata. Implementations should not throw on recoverable problems;
    /// they should attach a string to <see cref="ExtractionResult.Warnings"/> instead.
    /// Hard failures (corrupt bytes, unsupported sub-format) MAY throw <see cref="ExtractionException"/>.
    /// </summary>
    Task<ExtractionResult> ExtractAsync(Stream input, string contentType, CancellationToken ct);
}

public sealed class ExtractionException : Exception
{
    public ExtractionException(string message) : base(message) { }
    public ExtractionException(string message, Exception inner) : base(message, inner) { }
}
