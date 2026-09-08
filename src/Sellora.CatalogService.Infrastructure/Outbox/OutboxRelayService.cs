using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sellora.CatalogService.Application.Outbox;
using Sellora.CatalogService.Infrastructure.Persistence;

namespace Sellora.CatalogService.Infrastructure.Outbox;

public sealed class OutboxRelayOptions
{
    public const string SectionName = "OutboxRelay";
    public int BatchSize { get; init; } = 50;
    public int PollingIntervalSeconds { get; init; } = 5;
    public int LeaseSeconds { get; init; } = 30;
    public int RetryDelaySeconds { get; init; } = 15;
}

public sealed class OutboxRelayService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventPublisher _publisher;
    private readonly OutboxRelayOptions _options;
    private readonly ILogger<OutboxRelayService> _logger;

    public OutboxRelayService(
        IServiceScopeFactory scopeFactory,
        IEventPublisher publisher,
        IOptions<OutboxRelayOptions> options,
        ILogger<OutboxRelayService> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(stoppingToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Outbox relay polling failed.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.PollingIntervalSeconds),
                stoppingToken);
        }
    }

    public async Task ProcessPendingMessagesAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var leaseId = Guid.NewGuid();
        var leaseExpiresAt = now.AddSeconds(_options.LeaseSeconds);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var candidateIds = await db.OutboxMessages
            .IgnoreQueryFilters()
            .Where(message =>
                message.PublishedAt == null &&
                message.NextAttemptAt <= now &&
                (message.LeaseExpiresAt == null || message.LeaseExpiresAt < now))
            .OrderBy(message => message.OccurredAt)
            .Select(message => message.OutboxId)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var outboxId in candidateIds)
        {
            await db.OutboxMessages
                .IgnoreQueryFilters()
                .Where(message =>
                    message.OutboxId == outboxId &&
                    message.PublishedAt == null &&
                    (message.LeaseExpiresAt == null || message.LeaseExpiresAt < now))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(message => message.LeaseId, leaseId)
                        .SetProperty(message => message.LeaseExpiresAt, leaseExpiresAt),
                    cancellationToken);
        }

        var leasedMessages = await db.OutboxMessages
            .IgnoreQueryFilters()
            .Where(message => message.LeaseId == leaseId)
            .OrderBy(message => message.OccurredAt)
            .ToListAsync(cancellationToken);

        foreach (var message in leasedMessages)
        {
            try
            {
                await _publisher.PublishAsync(
                    new OutboxMessageToPublish(
                        message.OutboxId,
                        message.EventType,
                        message.SchemaVersion,
                        message.CompanyId,
                        message.AggregateId,
                        message.Payload,
                        message.OccurredAt),
                    cancellationToken);

                await db.OutboxMessages
                    .IgnoreQueryFilters()
                    .Where(item =>
                        item.OutboxId == message.OutboxId &&
                        item.LeaseId == leaseId &&
                        item.PublishedAt == null)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(item => item.PublishedAt, DateTimeOffset.UtcNow)
                            .SetProperty(item => item.LeaseId, (Guid?)null)
                            .SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null)
                            .SetProperty(item => item.LastError, (string?)null),
                        cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Outbox message {OutboxId} could not be published; it will retry.",
                    message.OutboxId);

                var lastError = exception.Message.Length > 2000
                    ? exception.Message[..2000]
                    : exception.Message;

                await db.OutboxMessages
                    .IgnoreQueryFilters()
                    .Where(item =>
                        item.OutboxId == message.OutboxId &&
                        item.LeaseId == leaseId &&
                        item.PublishedAt == null)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1)
                            .SetProperty(item => item.LastError, lastError)
                            .SetProperty(
                                item => item.NextAttemptAt,
                                DateTimeOffset.UtcNow.AddSeconds(_options.RetryDelaySeconds))
                            .SetProperty(item => item.LeaseId, (Guid?)null)
                            .SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null),
                        cancellationToken);
            }
        }
    }
}
