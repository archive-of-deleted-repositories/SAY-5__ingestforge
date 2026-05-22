using System.Threading.Channels;
using Confluent.Kafka;
using IngestForge.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IngestForge.Workers;

/// <summary>
/// One hosted service per process. Consumes the work-item topic with manual
/// offset commit and a bounded in-process channel fanning out to N workers.
/// Each partition gets <see cref="WorkerOptions.ParallelismPerPartition"/>
/// concurrent extractors.
/// </summary>
public sealed class KafkaConsumerService : BackgroundService
{
    private readonly WorkerOptions _options;
    private readonly WorkItemProcessor _processor;
    private readonly ILogger<KafkaConsumerService> _logger;
    private readonly IProducer<string, byte[]> _dlqProducer;

    public KafkaConsumerService(
        IOptions<WorkerOptions> options,
        WorkItemProcessor processor,
        ILogger<KafkaConsumerService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _processor = processor;
        _logger = logger;

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
        };
        _dlqProducer = new ProducerBuilder<string, byte[]>(producerConfig).Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BootstrapServers))
        {
            _logger.LogWarning("Kafka BootstrapServers is empty; consumer will not start");
            return;
        }

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.GroupId,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnablePartitionEof = false,
            SessionTimeoutMs = 30000,
            MaxPollIntervalMs = 600000,
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(_options.Topic);
        _logger.LogInformation("subscribed: topic={Topic} group={Group}", _options.Topic, _options.GroupId);

        var channel = Channel.CreateBounded<ConsumeResult<string, byte[]>>(new BoundedChannelOptions(_options.ParallelismPerPartition * 4)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true,
        });

        var workers = Enumerable.Range(0, _options.ParallelismPerPartition)
            .Select(_ => Task.Run(() => WorkerLoopAsync(channel.Reader, consumer, stoppingToken), stoppingToken))
            .ToArray();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, byte[]>? msg;
                try
                {
                    msg = consumer.Consume(TimeSpan.FromSeconds(1));
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "kafka consume error");
                    continue;
                }

                if (msg == null)
                {
                    continue;
                }

                await channel.Writer.WriteAsync(msg, stoppingToken).ConfigureAwait(false);
            }
        }
        finally
        {
            channel.Writer.Complete();
            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
            consumer.Close();
        }
    }

    private async Task WorkerLoopAsync(
        ChannelReader<ConsumeResult<string, byte[]>> reader,
        IConsumer<string, byte[]> consumer,
        CancellationToken ct)
    {
        await foreach (var msg in reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            await HandleAsync(msg, consumer, ct).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(
        ConsumeResult<string, byte[]> msg,
        IConsumer<string, byte[]> consumer,
        CancellationToken ct)
    {
        var attempt = GetAttempt(msg.Message.Headers) + 1;

        WorkItem item;
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(msg.Message.Value);
            item = WorkItem.FromJson(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "malformed work item; sending straight to DLQ: partition={P} offset={O}",
                msg.Partition.Value, msg.Offset.Value);
            await SendToDlqAsync(msg, $"malformed:{ex.Message}", attempt, ct).ConfigureAwait(false);
            SafeCommit(consumer, msg);
            return;
        }

        try
        {
            var outcome = await _processor.ProcessAsync(item, ct).ConfigureAwait(false);
            _logger.LogDebug("processed: doc_id={DocId} outcome={Outcome}", item.DocId, outcome);
            SafeCommit(consumer, msg);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (attempt >= _options.MaxAttempts)
            {
                _logger.LogError(ex, "max attempts reached, sending to DLQ: doc_id={DocId}", item.DocId);
                await SendToDlqAsync(msg, ex.Message, attempt, ct).ConfigureAwait(false);
                SafeCommit(consumer, msg);
            }
            else
            {
                _logger.LogWarning(ex, "transient failure: doc_id={DocId} attempt={Attempt}", item.DocId, attempt);
                // Republish with incremented attempt header. Simpler than seek-back.
                await RepublishAsync(msg, attempt, ct).ConfigureAwait(false);
                SafeCommit(consumer, msg);
            }
        }
    }

    private static int GetAttempt(Headers headers)
    {
        if (headers != null && headers.TryGetLastBytes("attempt", out var bytes) && bytes.Length > 0)
        {
            if (int.TryParse(System.Text.Encoding.UTF8.GetString(bytes), out var n))
            {
                return n;
            }
        }
        return 0;
    }

    private async Task SendToDlqAsync(ConsumeResult<string, byte[]> msg, string reason, int attempt, CancellationToken ct)
    {
        var headers = new Headers
        {
            { "attempt", System.Text.Encoding.UTF8.GetBytes(attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)) },
            { "dlq_reason", System.Text.Encoding.UTF8.GetBytes(reason) },
            { "source_topic", System.Text.Encoding.UTF8.GetBytes(_options.Topic) },
        };
        await _dlqProducer.ProduceAsync(_options.DeadLetterTopic, new Message<string, byte[]>
        {
            Key = msg.Message.Key,
            Value = msg.Message.Value,
            Headers = headers,
        }, ct).ConfigureAwait(false);
    }

    private async Task RepublishAsync(ConsumeResult<string, byte[]> msg, int attempt, CancellationToken ct)
    {
        var headers = new Headers
        {
            { "attempt", System.Text.Encoding.UTF8.GetBytes(attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)) },
        };
        await _dlqProducer.ProduceAsync(_options.Topic, new Message<string, byte[]>
        {
            Key = msg.Message.Key,
            Value = msg.Message.Value,
            Headers = headers,
        }, ct).ConfigureAwait(false);
    }

    private static void SafeCommit(IConsumer<string, byte[]> consumer, ConsumeResult<string, byte[]> msg)
    {
        try
        {
            consumer.Commit(msg);
        }
        catch (KafkaException)
        {
            // surfaced by next poll; nothing to do here
        }
    }

    public override void Dispose()
    {
        _dlqProducer.Flush(TimeSpan.FromSeconds(5));
        _dlqProducer.Dispose();
        base.Dispose();
    }
}
