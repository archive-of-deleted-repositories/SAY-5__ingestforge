using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace IngestForge.Bench;

/// <summary>
/// One-shot byte arrays for binary-format bench inputs. We build them at
/// startup so the per-document cost of the harness is just decoding, not also
/// re-synthesising.
/// </summary>
internal static class Corpora
{
    public static readonly byte[] DocxBytes = BuildDocx();
    public static readonly byte[] PdfBytes = BuildPdf();

    private static byte[] BuildDocx()
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(
                new Body(
                    Enumerable.Range(0, 20).Select(_ =>
                        new Paragraph(new Run(new Text("The quick brown fox jumps over the lazy dog.")))).Cast<OpenXmlElement>().ToArray()));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static byte[] BuildPdf()
    {
        // Minimal 1-page PDF with a single line of text. Hand-rolled so the bench
        // does not depend on a writer library. Layout follows the spec's
        // cross-reference table format exactly so PdfPig can parse it.
        var body = new StringBuilder();
        var offsets = new List<int>();

        void Object(string content)
        {
            offsets.Add(body.Length);
            body.Append(content);
        }

        body.Append("%PDF-1.4\n%âãÏÓ\n");

        Object("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        Object("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        Object("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n");

        const string stream = "BT /F1 12 Tf 72 720 Td (The quick brown fox jumps over the lazy dog.) Tj ET";
        Object($"4 0 obj\n<< /Length {stream.Length} >>\nstream\n{stream}\nendstream\nendobj\n");
        Object("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        var xrefOffset = body.Length;
        body.Append("xref\n");
        body.Append("0 ").Append(offsets.Count + 1).Append('\n');
        body.Append("0000000000 65535 f \n");
        foreach (var off in offsets)
        {
            body.Append(off.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }
        body.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xrefOffset).Append("\n%%EOF\n");

        return Encoding.ASCII.GetBytes(body.ToString());
    }
}
