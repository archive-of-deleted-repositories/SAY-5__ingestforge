namespace IngestForge.Core;

/// <summary>
/// Canonical content-type strings the pipeline knows about.
/// </summary>
public static class ContentTypes
{
    public const string Pdf = "application/pdf";
    public const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string Html = "text/html";
    public const string PlainText = "text/plain";
    public const string Email = "message/rfc822";

    /// <summary>Sentinel: ask the pipeline to sniff magic bytes.</summary>
    public const string Auto = "auto";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Pdf, Docx, Html, PlainText, Email,
    };

    /// <summary>
    /// Magic-bytes sniffer for the <c>auto</c> case. Reads up to 1 KB from the buffer.
    /// </summary>
    public static string Sniff(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 4 && head[0] == 0x25 && head[1] == 0x50 && head[2] == 0x44 && head[3] == 0x46)
        {
            return Pdf;
        }

        // ZIP signature: DOCX is a ZIP, but so are XLSX/PPTX/JAR. We treat any ZIP starting
        // with these bytes as DOCX for now; the extractor itself will fail if it is not actually a DOCX.
        if (head.Length >= 4 && head[0] == 0x50 && head[1] == 0x4B && head[2] == 0x03 && head[3] == 0x04)
        {
            return Docx;
        }

        if (head.Length >= 5)
        {
            var firstFive = System.Text.Encoding.ASCII.GetString(head[..5]).ToLowerInvariant();
            if (firstFive.StartsWith("<html", StringComparison.Ordinal) ||
                firstFive.StartsWith("<!doc", StringComparison.Ordinal))
            {
                return Html;
            }
        }

        if (head.Length >= 5)
        {
            var prefix = System.Text.Encoding.ASCII.GetString(head[..Math.Min(64, head.Length)]);
            if (prefix.Contains("From: ", StringComparison.Ordinal) ||
                prefix.Contains("Return-Path:", StringComparison.Ordinal) ||
                prefix.Contains("Received:", StringComparison.Ordinal) ||
                prefix.Contains("Subject:", StringComparison.Ordinal))
            {
                return Email;
            }
        }

        return PlainText;
    }
}
