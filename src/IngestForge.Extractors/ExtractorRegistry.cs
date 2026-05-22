using IngestForge.Core;

namespace IngestForge.Extractors;

/// <summary>
/// Holds one extractor per content type and dispatches by sniffing if asked.
/// Built once at startup and resolved as a singleton.
/// </summary>
public sealed class ExtractorRegistry
{
    private readonly Dictionary<string, IDocumentExtractor> _byType;

    public ExtractorRegistry(IEnumerable<IDocumentExtractor> extractors)
    {
        _byType = new Dictionary<string, IDocumentExtractor>(StringComparer.OrdinalIgnoreCase);
        foreach (var extractor in extractors)
        {
            foreach (var type in extractor.SupportedContentTypes)
            {
                _byType[type] = extractor;
            }
        }
    }

    public bool TryGet(string contentType, out IDocumentExtractor extractor)
    {
        return _byType.TryGetValue(contentType, out extractor!);
    }

    /// <summary>
    /// Dispatch an extraction. If <paramref name="contentType"/> is <see cref="ContentTypes.Auto"/>
    /// we peek the first 1 KB of the stream to pick one. The stream must be seekable for auto.
    /// </summary>
    public async Task<ExtractionResult> ExtractAsync(Stream input, string contentType, CancellationToken ct)
    {
        var resolvedType = contentType;

        if (string.Equals(contentType, ContentTypes.Auto, StringComparison.OrdinalIgnoreCase))
        {
            if (!input.CanSeek)
            {
                throw new ExtractionException("auto content-type requires a seekable stream");
            }

            var buf = new byte[1024];
            var read = await input.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false);
            input.Position = 0;
            resolvedType = ContentTypes.Sniff(buf.AsSpan(0, read));
        }

        if (!_byType.TryGetValue(resolvedType, out var extractor))
        {
            throw new ExtractionException($"no extractor registered for content type '{resolvedType}'");
        }

        return await extractor.ExtractAsync(input, resolvedType, ct).ConfigureAwait(false);
    }
}
