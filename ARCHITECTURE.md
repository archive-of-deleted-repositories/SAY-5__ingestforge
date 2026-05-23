# Architecture

## Shape

```
   Kafka: ingest.work
       |
       v
   +-----------------+      pluggable
   | Worker (N)      |----> IDocumentExtractor
   |  one per CPU    |        |
   +--------+--------+        +-- PdfExtractor      (PdfPig)
            |                 +-- DocxExtractor     (DocumentFormat.OpenXml)
            v                 +-- HtmlExtractor     (HtmlAgilityPack)
   +-----------------+        +-- TextExtractor
   |  Postgres       |        +-- EmailExtractor    (MimeKit)
   |                 |
   |  ingest_in_     |  dedup row (UNIQUE on dedup_key)
   |     progress    |
   |  ingest_        |  terminal success rows
   |     completed   |
   |  ingest_dlq     |  terminal failure rows
   +-----------------+
```

## Idempotent processing

Every work item carries a `dedup_key`. On receive, each worker tries to
`INSERT INTO ingest_in_progress (dedup_key, ...) ON CONFLICT (dedup_key)
DO NOTHING`. Conflict means another worker already claimed it (or this
is a re-delivery to the same worker), so the message is acked and the
worker moves on. After successful extraction the row moves to
`ingest_completed`; on failure with the retry budget exhausted it moves
to `ingest_dlq`.

Retry count rides on `KafkaMessage.Headers["x-attempt"]` (incremented on
each redelivery), not on the body, so re-publication via a retry topic
keeps the body immutable.

## Partitioned worker pool

N workers per process (default = `Environment.ProcessorCount`). Each is
a `BackgroundService` subscribed to the same Kafka consumer group, so
Kafka assigns partitions to workers automatically. Workers are stateless
beyond the dedup table; horizontal scale is one more process.

## Pluggable extractors

The `IDocumentExtractor` interface is:

```csharp
Task<ExtractionResult> ExtractAsync(Stream input, string contentType, CancellationToken ct);
```

`ExtractionResult` carries the extracted text (chunked at 4KB), structured
metadata (page count, author, created_at when available, mime, language
guess), and warnings (encrypted PDF, partial extraction, etc.). A
registry maps `Content-Type` to extractor. The `auto` content type runs
a small magic-bytes sniffer.

## Throughput

The bench harness publishes a deterministic mix of synthetic documents
(40% PDF, 30% DOCX, 20% HTML, 10% TXT/EML) and measures per-format and
aggregate throughput. PDFs dominate the per-doc cost; the per-format
breakdown lives in `bench/results/`.

## What is deliberately not here

- Real Azure provisioning. `infra/azure/main.bicep` is a documented stub
  for Event Hubs (Kafka-compat), Postgres Flexible, Container Apps; not
  validated against a live Azure subscription.
- OCR. Image-based PDFs return only text-extractable pages. An OCR
  swap-in would be another `IDocumentExtractor` decorator.
- PII redaction.
- Virus scanning (would be a sidecar in production).
- Encryption at rest beyond Postgres defaults.
