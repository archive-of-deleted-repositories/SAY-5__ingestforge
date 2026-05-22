using System.Text.Json;
using System.Text.Json.Serialization;

namespace IngestForge.Core;

/// <summary>
/// Kafka message payload that points at a document to ingest.
/// The body is immutable. Retry counters live in Kafka headers, not here.
/// </summary>
public sealed record WorkItem
{
    [JsonPropertyName("doc_id")]
    public required Guid DocId { get; init; }

    [JsonPropertyName("source_uri")]
    public required string SourceUri { get; init; }

    /// <summary>
    /// MIME-style content type. The literal value <c>auto</c> tells the
    /// pipeline to sniff the magic bytes after reading the stream.
    /// </summary>
    [JsonPropertyName("content_type")]
    public required string ContentType { get; init; }

    [JsonPropertyName("tenant")]
    public required string Tenant { get; init; }

    [JsonPropertyName("submitted_at")]
    public required DateTimeOffset SubmittedAt { get; init; }

    /// <summary>
    /// Stable key for idempotency. Typically a hash of (tenant, source_uri, content_hash).
    /// Two work items with the same dedup_key collapse to one processed document.
    /// </summary>
    [JsonPropertyName("dedup_key")]
    public required string DedupKey { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static WorkItem FromJson(string json)
    {
        var item = JsonSerializer.Deserialize<WorkItem>(json, JsonOptions)
            ?? throw new InvalidOperationException("work item payload was null after deserialization");
        return item;
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
