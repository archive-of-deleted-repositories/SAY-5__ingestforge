using System.Text;
using IngestForge.Core;

namespace IngestForge.Extractors;

/// <summary>
/// UTF-8 plain-text passthrough. Reads the whole stream, splits into 4 KB chunks.
/// </summary>
public sealed class PlainTextExtractor : IDocumentExtractor
{
    public IReadOnlyCollection<string> SupportedContentTypes { get; } = new[] { ContentTypes.PlainText };

    public async Task<ExtractionResult> ExtractAsync(Stream input, string contentType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        return new ExtractionResult
        {
            TextChunks = ExtractionResult.Chunk(text),
            Metadata = new DocumentMetadata { Mime = ContentTypes.PlainText },
            Warnings = Array.Empty<string>(),
        };
    }
}
