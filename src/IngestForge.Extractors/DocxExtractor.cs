using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using IngestForge.Core;

namespace IngestForge.Extractors;

/// <summary>
/// DOCX text extraction via the Office Open XML SDK. We walk the main document
/// part, paragraph by paragraph, joining runs with a single space. Tables and
/// headers/footers are pulled too.
/// </summary>
public sealed class DocxExtractor : IDocumentExtractor
{
    public IReadOnlyCollection<string> SupportedContentTypes { get; } = new[] { ContentTypes.Docx };

    public Task<ExtractionResult> ExtractAsync(Stream input, string contentType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        // OpenXml needs a seekable source. Copy if necessary.
        Stream working;
        MemoryStream? owned = null;
        if (input.CanSeek)
        {
            working = input;
        }
        else
        {
            owned = new MemoryStream();
            input.CopyTo(owned);
            owned.Position = 0;
            working = owned;
        }

        try
        {
            using var doc = WordprocessingDocument.Open(working, isEditable: false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null)
            {
                return Task.FromResult(ExtractionResult.Empty(ContentTypes.Docx, "docx_no_body"));
            }

            var sb = new StringBuilder();
            foreach (var para in body.Descendants<Paragraph>())
            {
                ct.ThrowIfCancellationRequested();
                AppendParagraph(sb, para);
            }

            // Headers/footers.
            if (doc.MainDocumentPart != null)
            {
                foreach (var header in doc.MainDocumentPart.HeaderParts)
                {
                    foreach (var para in header.Header.Descendants<Paragraph>())
                    {
                        AppendParagraph(sb, para);
                    }
                }
                foreach (var footer in doc.MainDocumentPart.FooterParts)
                {
                    foreach (var para in footer.Footer.Descendants<Paragraph>())
                    {
                        AppendParagraph(sb, para);
                    }
                }
            }

            var props = doc.PackageProperties;
            return Task.FromResult(new ExtractionResult
            {
                TextChunks = ExtractionResult.Chunk(sb.ToString()),
                Metadata = new DocumentMetadata
                {
                    Mime = ContentTypes.Docx,
                    Title = string.IsNullOrWhiteSpace(props.Title) ? null : props.Title,
                    Author = string.IsNullOrWhiteSpace(props.Creator) ? null : props.Creator,
                    CreatedAt = props.Created.HasValue
                        ? new DateTimeOffset(props.Created.Value, TimeSpan.Zero)
                        : null,
                },
                Warnings = Array.Empty<string>(),
            });
        }
        catch (FileFormatException ex)
        {
            return Task.FromResult(ExtractionResult.Empty(
                ContentTypes.Docx,
                $"docx_open_failed:{ex.GetType().Name}"));
        }
        catch (OpenXmlPackageException ex)
        {
            return Task.FromResult(ExtractionResult.Empty(
                ContentTypes.Docx,
                $"docx_open_failed:{ex.GetType().Name}"));
        }
        finally
        {
            owned?.Dispose();
        }
    }

    private static void AppendParagraph(StringBuilder sb, Paragraph para)
    {
        var any = false;
        foreach (var t in para.Descendants<Text>())
        {
            if (any)
            {
                sb.Append(' ');
            }
            sb.Append(t.Text);
            any = true;
        }
        if (any)
        {
            sb.Append('\n');
        }
    }
}
