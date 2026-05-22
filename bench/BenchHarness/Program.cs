using System.Diagnostics;
using System.Text;
using IngestForge.Bench;
using IngestForge.Core;
using IngestForge.Extractors;

// Synthetic-corpus throughput bench. Generates documents in-memory, drives them
// through the extractor registry with bounded parallelism, prints docs/sec.

var (format, count, parallelism) = ParseArgs(args);

var registry = new ExtractorRegistry(new IDocumentExtractor[]
{
    new PlainTextExtractor(),
    new HtmlExtractor(),
    new PdfExtractor(),
    new DocxExtractor(),
    new EmailExtractor(),
});

Console.WriteLine($"format={format} count={count} parallelism={parallelism}");

if (format == "mixed")
{
    await RunMixedAsync(registry, count, parallelism).ConfigureAwait(false);
    return 0;
}

if (format == "all")
{
    foreach (var f in new[] { "text", "html", "email", "docx", "pdf" })
    {
        await RunOneAsync(registry, f, count, parallelism).ConfigureAwait(false);
    }
    return 0;
}

await RunOneAsync(registry, format, count, parallelism).ConfigureAwait(false);
return 0;

static async Task RunOneAsync(ExtractorRegistry registry, string format, int count, int parallelism)
{
    var contentType = ContentTypeFor(format);
    var payload = Synthesize(format);

    var sw = Stopwatch.StartNew();
    var totalChars = 0L;
    using var sem = new SemaphoreSlim(parallelism);
    var tasks = new List<Task>(count);

    for (var i = 0; i < count; i++)
    {
        await sem.WaitAsync().ConfigureAwait(false);
        tasks.Add(Task.Run(async () =>
        {
            try
            {
                using var ms = new MemoryStream(payload, writable: false);
                var result = await registry.ExtractAsync(ms, contentType, CancellationToken.None).ConfigureAwait(false);
                Interlocked.Add(ref totalChars, result.TotalChars);
            }
            finally
            {
                sem.Release();
            }
        }));
    }

    await Task.WhenAll(tasks).ConfigureAwait(false);
    sw.Stop();

    var seconds = sw.Elapsed.TotalSeconds;
    var perSec = count / seconds;
    Console.WriteLine(
        $"{format,-7} docs={count,-7} parallel={parallelism,-3} wall={seconds,7:F2}s  docs/s={perSec,9:F0}  bytes/doc={payload.Length}  total_chars={totalChars}");
}

static async Task RunMixedAsync(ExtractorRegistry registry, int count, int parallelism)
{
    // Realistic enterprise mix: a third text, a quarter HTML, smaller slices for the rest.
    var picks = new (string Format, int Weight)[]
    {
        ("text", 35),
        ("html", 25),
        ("email", 15),
        ("docx", 15),
        ("pdf", 10),
    };
    var totalWeight = picks.Sum(p => p.Weight);

    var rng = new Random(42);
    var jobs = new (string Format, byte[] Payload)[count];
    for (var i = 0; i < count; i++)
    {
        var pick = rng.Next(totalWeight);
        var running = 0;
        foreach (var p in picks)
        {
            running += p.Weight;
            if (pick < running)
            {
                jobs[i] = (p.Format, Synthesize(p.Format));
                break;
            }
        }
    }

    var sw = Stopwatch.StartNew();
    var totalChars = 0L;
    using var sem = new SemaphoreSlim(parallelism);
    var tasks = new List<Task>(count);

    foreach (var (format, payload) in jobs)
    {
        await sem.WaitAsync().ConfigureAwait(false);
        var localFormat = format;
        var localPayload = payload;
        tasks.Add(Task.Run(async () =>
        {
            try
            {
                using var ms = new MemoryStream(localPayload, writable: false);
                var result = await registry.ExtractAsync(ms, ContentTypeFor(localFormat), CancellationToken.None).ConfigureAwait(false);
                Interlocked.Add(ref totalChars, result.TotalChars);
            }
            finally
            {
                sem.Release();
            }
        }));
    }

    await Task.WhenAll(tasks).ConfigureAwait(false);
    sw.Stop();

    var seconds = sw.Elapsed.TotalSeconds;
    Console.WriteLine(
        $"mixed   docs={count,-7} parallel={parallelism,-3} wall={seconds,7:F2}s  docs/s={count / seconds,9:F0}  total_chars={totalChars}");
}

static string ContentTypeFor(string format) => format switch
{
    "text" => ContentTypes.PlainText,
    "html" => ContentTypes.Html,
    "email" => ContentTypes.Email,
    "docx" => ContentTypes.Docx,
    "pdf" => ContentTypes.Pdf,
    _ => throw new ArgumentException($"unknown format: {format}", nameof(format)),
};

static byte[] Synthesize(string format) => format switch
{
    "text" => SynthesizeText(),
    "html" => SynthesizeHtml(),
    "email" => SynthesizeEmail(),
    "docx" => Corpora.DocxBytes,
    "pdf" => Corpora.PdfBytes,
    _ => throw new ArgumentException($"unknown format: {format}", nameof(format)),
};

static byte[] SynthesizeText()
{
    var sb = new StringBuilder(2048);
    for (var i = 0; i < 40; i++)
    {
        sb.AppendLine("The quick brown fox jumps over the lazy dog. The early bird catches the worm.");
    }
    return Encoding.UTF8.GetBytes(sb.ToString());
}

static byte[] SynthesizeHtml()
{
    var sb = new StringBuilder(4096);
    sb.AppendLine("<!doctype html><html><head><title>Bench Doc</title><style>body{}</style></head><body>");
    for (var i = 0; i < 20; i++)
    {
        sb.AppendLine("<p>The quick <b>brown</b> fox jumps over the <i>lazy</i> dog. <a href=\"#\">link</a></p>");
    }
    sb.AppendLine("<script>console.log('noise')</script></body></html>");
    return Encoding.UTF8.GetBytes(sb.ToString());
}

static byte[] SynthesizeEmail()
{
    var raw =
        "From: bench@example.com\r\n" +
        "To: target@example.com\r\n" +
        "Subject: Bench email\r\n" +
        "Date: Mon, 11 May 2026 12:00:00 +0000\r\n" +
        "Content-Type: text/plain; charset=utf-8\r\n" +
        "\r\n" +
        string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog.\r\n", 20));
    return Encoding.UTF8.GetBytes(raw);
}

static (string Format, int Count, int Parallelism) ParseArgs(string[] args)
{
    var format = "text";
    var count = 10_000;
    var parallelism = Environment.ProcessorCount;
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--format" when i + 1 < args.Length:
                format = args[++i];
                break;
            case "--count" when i + 1 < args.Length:
                count = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "--parallelism" when i + 1 < args.Length:
                parallelism = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                break;
        }
    }
    return (format, count, parallelism);
}
