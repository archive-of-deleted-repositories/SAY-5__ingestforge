namespace IngestForge.Core;

/// <summary>
/// What an extractor returns. Text content is split into 4 KB chunks so a single
/// huge document does not blow up downstream readers. Metadata is best-effort:
/// extractors fill what they have and leave the rest null.
/// </summary>
public sealed record ExtractionResult
{
    public required IReadOnlyList<string> TextChunks { get; init; }

    public required DocumentMetadata Metadata { get; init; }

    /// <summary>
    /// Non-fatal issues encountered while extracting. Examples:
    /// "encrypted_pdf_text_not_recoverable", "html_parser_recovered_3_malformed_tags".
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    public int TotalChars => TextChunks.Sum(c => c.Length);

    public static ExtractionResult Empty(string mime, params string[] warnings) => new()
    {
        TextChunks = Array.Empty<string>(),
        Metadata = new DocumentMetadata { Mime = mime },
        Warnings = warnings,
    };

    public const int ChunkSize = 4096;

    /// <summary>
    /// Split a single string into <see cref="ChunkSize"/> chunks. An empty input returns an empty list.
    /// </summary>
    public static IReadOnlyList<string> Chunk(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        var chunks = new List<string>((text.Length / ChunkSize) + 1);
        for (var i = 0; i < text.Length; i += ChunkSize)
        {
            var len = Math.Min(ChunkSize, text.Length - i);
            chunks.Add(text.Substring(i, len));
        }

        return chunks;
    }
}

public sealed record DocumentMetadata
{
    public string? Author { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? Title { get; init; }
    public int? PageCount { get; init; }
    public string? LanguageGuess { get; init; }
    public required string Mime { get; init; }
}
