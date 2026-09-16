using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Sellora.CatalogService.Application.Outbox;

namespace Sellora.CatalogService.Infrastructure.Outbox;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";
    public string BootstrapServers { get; init; } = "localhost:9092";
    public string CatalogTopic { get; init; } = "sellora.catalog.v1";
    public int MessageTimeoutMs { get; init; } = 10_000;

    // Confluent Cloud requires SASL. Leave empty for a local broker.
    public string? SaslUsername { get; init; }
    public string? SaslPassword { get; init; }
}


public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private readonly KafkaOptions _options;
    private readonly IProducer<string, string> _producer;

    public KafkaEventPublisher(IOptions<KafkaOptions> options)
    {
        _options = options.Value;

        var config = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            MessageTimeoutMs = _options.MessageTimeoutMs
        };

        ApplySasl(config, _options);

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    private static void ApplySasl(ClientConfig config, KafkaOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SaslUsername))
        {
            return;
        }

        config.SecurityProtocol = SecurityProtocol.SaslSsl;
        config.SaslMechanism = SaslMechanism.Plain;
        config.SaslUsername = options.SaslUsername;
        config.SaslPassword = options.SaslPassword;
    }


    public async Task PublishAsync(
        OutboxMessageToPublish message,
        CancellationToken cancellationToken = default)
    {
        var headers = new Headers
        {
            { "event-id", message.OutboxId.ToByteArray() },
            { "event-type", System.Text.Encoding.UTF8.GetBytes(message.EventType) },
            { "schema-version", System.Text.Encoding.UTF8.GetBytes(message.SchemaVersion) },
            { "company-id", System.Text.Encoding.UTF8.GetBytes(message.CompanyId.ToString()) }
        };

        await _producer.ProduceAsync(
            _options.CatalogTopic,
            new Message<string, string>
            {
                Key = message.AggregateId.ToString(),
                Value = message.Payload,
                Headers = headers
            },
            cancellationToken);
    }

    public void Dispose() => _producer.Dispose();
}
