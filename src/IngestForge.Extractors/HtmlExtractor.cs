using System.Text;
using HtmlAgilityPack;
using IngestForge.Core;

namespace IngestForge.Extractors;

/// <summary>
/// HTML to plain text via HtmlAgilityPack. Script/style nodes are stripped.
/// Whitespace is collapsed so a single megabyte of HTML does not produce
/// hundreds of empty chunks.
/// </summary>
public sealed class HtmlExtractor : IDocumentExtractor
{
    public IReadOnlyCollection<string> SupportedContentTypes { get; } = new[] { ContentTypes.Html };

    public async Task<ExtractionResult> ExtractAsync(Stream input, string contentType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var ms = new MemoryStream();
        await input.CopyToAsync(ms, ct).ConfigureAwait(false);
        ms.Position = 0;

        var doc = new HtmlDocument();
        doc.Load(ms);

        var warnings = new List<string>();
        if (doc.ParseErrors != null && doc.ParseErrors.Any())
        {
            warnings.Add($"html_parser_recovered_{doc.ParseErrors.Count()}_errors");
        }

        // Drop noise. SelectNodes returns null when no match.
        var noise = doc.DocumentNode.SelectNodes("//script|//style|//noscript|//template|//iframe");
        if (noise != null)
        {
            foreach (var n in noise)
            {
                n.Remove();
            }
        }

        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim();
        var rawText = doc.DocumentNode.SelectSingleNode("//body")?.InnerText
                      ?? doc.DocumentNode.InnerText
                      ?? string.Empty;

        var collapsed = CollapseWhitespace(rawText);

        return new ExtractionResult
        {
            TextChunks = ExtractionResult.Chunk(collapsed),
            Metadata = new DocumentMetadata
            {
                Mime = ContentTypes.Html,
                Title = string.IsNullOrWhiteSpace(title) ? null : title,
            },
            Warnings = warnings,
        };
    }

    internal static string CollapseWhitespace(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(input.Length);
        var prevWasSpace = false;
        foreach (var ch in input)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevWasSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                }
                prevWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                prevWasSpace = false;
            }
        }

        // Trim trailing space.
        if (sb.Length > 0 && sb[^1] == ' ')
        {
            sb.Length -= 1;
        }

        return sb.ToString();
    }
}
