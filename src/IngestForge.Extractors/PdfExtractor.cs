using System.Text;
using IngestForge.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace IngestForge.Extractors;

/// <summary>
/// PDF text extraction via PdfPig. Encrypted PDFs we cannot open are surfaced
/// as a warning, not an exception, so the worker treats them as "processed
/// with zero text" rather than retrying forever.
/// </summary>
public sealed class PdfExtractor : IDocumentExtractor
{
    public IReadOnlyCollection<string> SupportedContentTypes { get; } = new[] { ContentTypes.Pdf };

    public Task<ExtractionResult> ExtractAsync(Stream input, string contentType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        // PdfPig requires a seekable byte source; copy to memory if the caller handed us a network stream.
        using var ms = new MemoryStream();
        input.CopyTo(ms);
        ms.Position = 0;

        var warnings = new List<string>();
        var sb = new StringBuilder();
        string? title = null;
        string? author = null;
        DateTimeOffset? createdAt = null;
        int pageCount = 0;

        try
        {
            using var doc = PdfDocument.Open(ms);
            pageCount = doc.NumberOfPages;

            try
            {
                title = doc.Information?.Title;
                author = doc.Information?.Author;
                if (DateTimeOffset.TryParse(doc.Information?.CreationDate, out var dt))
                {
                    createdAt = dt;
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"pdf_metadata_unreadable:{ex.GetType().Name}");
            }

            for (var pageNum = 1; pageNum <= pageCount; pageNum++)
            {
                ct.ThrowIfCancellationRequested();
                Page page;
                try
                {
                    page = doc.GetPage(pageNum);
                }
                catch (Exception ex)
                {
                    warnings.Add($"pdf_page_{pageNum}_unreadable:{ex.GetType().Name}");
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }
                sb.Append(page.Text);
            }
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("Pdf", StringComparison.Ordinal))
        {
            // PdfPig throws its own family of exceptions for encrypted/corrupt input.
            return Task.FromResult(ExtractionResult.Empty(
                ContentTypes.Pdf,
                $"pdf_open_failed:{ex.GetType().Name}"));
        }

        return Task.FromResult(new ExtractionResult
        {
            TextChunks = ExtractionResult.Chunk(sb.ToString()),
            Metadata = new DocumentMetadata
            {
                Mime = ContentTypes.Pdf,
                Title = string.IsNullOrWhiteSpace(title) ? null : title,
                Author = string.IsNullOrWhiteSpace(author) ? null : author,
                CreatedAt = createdAt,
                PageCount = pageCount == 0 ? null : pageCount,
            },
            Warnings = warnings,
        });
    }
}
