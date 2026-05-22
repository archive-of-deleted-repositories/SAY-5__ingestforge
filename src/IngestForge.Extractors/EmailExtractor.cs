using System.Text;
using IngestForge.Core;
using MimeKit;

namespace IngestForge.Extractors;

/// <summary>
/// RFC822 mail extraction via MimeKit. Subject, From, Date, and the text body
/// (preferring text/plain, falling back to text/html stripped of tags).
/// Attachments are listed by filename in metadata but their content is not
/// recursed into; the producer is expected to enqueue attachments separately.
/// </summary>
public sealed class EmailExtractor : IDocumentExtractor
{
    public IReadOnlyCollection<string> SupportedContentTypes { get; } = new[] { ContentTypes.Email };

    public async Task<ExtractionResult> ExtractAsync(Stream input, string contentType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        MimeMessage message;
        try
        {
            message = await MimeMessage.LoadAsync(input, ct).ConfigureAwait(false);
        }
        catch (FormatException ex)
        {
            return ExtractionResult.Empty(ContentTypes.Email, $"email_parse_failed:{ex.GetType().Name}");
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(message.Subject))
        {
            sb.Append("Subject: ").Append(message.Subject).Append('\n');
        }
        if (message.From.Count > 0)
        {
            sb.Append("From: ").Append(message.From.ToString()).Append('\n');
        }
        if (message.To.Count > 0)
        {
            sb.Append("To: ").Append(message.To.ToString()).Append('\n');
        }
        sb.Append('\n');

        var body = message.TextBody;
        if (string.IsNullOrEmpty(body) && !string.IsNullOrEmpty(message.HtmlBody))
        {
            body = HtmlExtractor.CollapseWhitespace(StripTags(message.HtmlBody));
        }
        if (!string.IsNullOrEmpty(body))
        {
            sb.Append(body);
        }

        return new ExtractionResult
        {
            TextChunks = ExtractionResult.Chunk(sb.ToString()),
            Metadata = new DocumentMetadata
            {
                Mime = ContentTypes.Email,
                Title = string.IsNullOrWhiteSpace(message.Subject) ? null : message.Subject,
                Author = message.From.Count > 0 ? message.From.ToString() : null,
                CreatedAt = message.Date == default ? null : message.Date,
            },
            Warnings = Array.Empty<string>(),
        };
    }

    private static string StripTags(string html)
    {
        var sb = new StringBuilder(html.Length);
        var inTag = false;
        foreach (var ch in html)
        {
            if (ch == '<')
            {
                inTag = true;
            }
            else if (ch == '>')
            {
                inTag = false;
                sb.Append(' ');
            }
            else if (!inTag)
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }
}
