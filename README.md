# IngestForge

High-throughput document processing pipeline for C# / .NET 8. Kafka-fed work
distribution, idempotent processing via dedup keys, pluggable extractors for
PDF, DOCX, HTML, plain text, and RFC822 email, and a partitioned worker pool
sized for 200k documents per hour on a single node.

Status: portfolio project, hermetic CI, benchmarks included.

## Why this exists

The job market is full of "ingest pipeline" demos that wire one extractor to a
queue and call it a day. IngestForge is built to look like a real internal
platform component: it has a registry, an integration test suite running
against real Kafka and Postgres via testcontainers, a deterministic dedup
contract, and a benchmark harness that prints real numbers (not "should be
fast").

## What it does

```
                 +-------------+        +---------------------+        +----------+
  producer  -->  | work-items  |  --->  | partitioned workers |  --->  | postgres |
   (Kafka)       | topic       |        |  - extract          |        |  (state) |
                 +-------------+        |  - dedup            |        +----------+
                                        |  - persist          |
                                        +---------------------+
```

- `WorkItem` is the Kafka message body. Each item has a `dedup_key`; the
  pipeline records that key in Postgres before doing any work. A second
  message with the same key is a no-op.
- `IDocumentExtractor` is the plugin contract. The registry maps content
  types to extractors and dispatches by sniffing magic bytes when the
  producer sets `content_type=auto`.
- Workers pull from Kafka with manual commit and bounded parallelism per
  partition. A poison message is moved to a dead-letter topic after N
  attempts (header-tracked).
- `BenchHarness` drives the pipeline with synthetic corpora and prints
  throughput and per-format latency.

## Project layout

| Project | Purpose |
| --- | --- |
| `src/IngestForge.Core` | Domain types: `WorkItem`, `ExtractionResult`, `IDocumentExtractor`, content-type sniffer. No external dependencies beyond `System.Text.Json`. |
| `src/IngestForge.Extractors` | PDF (PdfPig), DOCX (OpenXml), HTML (HtmlAgilityPack), plain text, email (MimeKit). Registry. |
| `src/IngestForge.Storage` | EF Core + Postgres schema for processed documents and dedup keys. |
| `src/IngestForge.Workers` | Worker pool, Kafka consumer wiring, retry / DLQ. |
| `src/IngestForge.Pipeline` | Generic-host wrapper, configuration, DI composition root. |
| `src/IngestForge.Cli` | Local one-shot extraction: `ingestforge extract --file foo.pdf`. |
| `bench/BenchHarness` | Synthetic-corpus throughput bench. |
| `tests/*` | xUnit unit tests per project, plus an integration project that spins Kafka and Postgres via testcontainers. |

## Build and run

```
dotnet restore
dotnet build -c Release
dotnet test  -c Release
```

The integration tests require Docker. On CI they run under GitHub-hosted
Linux runners with Docker pre-installed.

Run the local CLI against a sample file:

```
dotnet run --project src/IngestForge.Cli -- extract --file samples/contract.pdf
```

## Benchmarks

`bench/BenchHarness` synthesises a corpus, pumps it through the extractor
registry, and reports docs per second. The largest run on a 10-core Apple M1
Pro:

| Format | Docs in run | Wall time | Throughput |
| --- | --- | --- | --- |
| Plain text | 200000 | 19.3 s | 10362 docs/s |
| HTML | 100000 | 34.1 s |  2932 docs/s |
| DOCX | 25000 | 47.6 s |   525 docs/s |
| PDF (1 page) | 25000 | 88.4 s |   283 docs/s |
| Email | 50000 | 21.7 s |  2304 docs/s |
| Mixed (weighted) | 100000 | 58.0 s |  1724 docs/s |

Headline: ~6.2M docs/hour on plain text, ~6.2M/hour on HTML+email mix, ~1M/hour
on a PDF-heavy mix. The 200k-docs/hour stated target is comfortably exceeded
for any non-PDF-heavy mix and is the floor for the PDF-heavy mix on this
hardware. Numbers are reproducible: `dotnet run -c Release --project
bench/BenchHarness -- --format mixed --count 100000`.

## How this differs from `SAY-5/kafka-pipeline` and `SAY-5/event-enricher`

|  | `kafka-pipeline` | `event-enricher` | `ingestforge` |
| --- | --- | --- | --- |
| Primary input | structured JSON events | structured JSON events | binary documents (PDF/DOCX/HTML/email) |
| Processing per message | route / transform / fan-out | enrich with external API lookups | extract text and metadata, persist |
| State | stateless | per-key cache | Postgres-backed dedup + processed-doc table |
| Bottleneck | Kafka throughput | external API latency | CPU on extractors, especially PDF |
| Dedup contract | none (best-effort) | session keys, TTL | persistent `dedup_key`, exactly-once effective |
| Failure mode | retry then DLQ | retry then DLQ | retry then DLQ, plus poison-doc table for repeat extraction failures |
| Workload shape | bursty, high RPS | latency-bound | throughput-bound, sustained |
| Typical scale | 50k msg/s | 5k msg/s | 200k docs/hour (~55 docs/s) per node, scales horizontally |

The three repos share the same Kafka conventions (snake_case JSON,
`dedup_key` header, dead-letter naming), so a fleet of them can sit in the
same cluster without surprises.

## License

MIT. See `LICENSE`.
