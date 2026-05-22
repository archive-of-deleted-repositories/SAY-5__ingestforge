using IngestForge.Core;
using IngestForge.Extractors;

if (args.Length < 2 || args[0] != "extract")
{
    Console.Error.WriteLine("usage: ingestforge extract --file <path> [--content-type <mime|auto>]");
    return 64;
}

string? file = null;
var contentType = ContentTypes.Auto;
for (var i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--file" when i + 1 < args.Length:
            file = args[++i];
            break;
        case "--content-type" when i + 1 < args.Length:
            contentType = args[++i];
            break;
        default:
            Console.Error.WriteLine($"unknown arg: {args[i]}");
            return 64;
    }
}

if (string.IsNullOrWhiteSpace(file))
{
    Console.Error.WriteLine("missing --file");
    return 64;
}

if (!File.Exists(file))
{
    Console.Error.WriteLine($"file not found: {file}");
    return 66;
}

var registry = new ExtractorRegistry(new IDocumentExtractor[]
{
    new PlainTextExtractor(),
    new HtmlExtractor(),
    new PdfExtractor(),
    new DocxExtractor(),
    new EmailExtractor(),
});

await using var stream = File.OpenRead(file);
var result = await registry.ExtractAsync(stream, contentType, CancellationToken.None);

Console.WriteLine($"mime: {result.Metadata.Mime}");
Console.WriteLine($"title: {result.Metadata.Title ?? "(none)"}");
Console.WriteLine($"author: {result.Metadata.Author ?? "(none)"}");
Console.WriteLine($"pages: {result.Metadata.PageCount?.ToString() ?? "(n/a)"}");
Console.WriteLine($"chars: {result.TotalChars}");
Console.WriteLine($"chunks: {result.TextChunks.Count}");
if (result.Warnings.Count > 0)
{
    Console.WriteLine("warnings:");
    foreach (var w in result.Warnings)
    {
        Console.WriteLine($"  - {w}");
    }
}
return 0;
