using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sellora.CatalogService.Application.Outbox;
using Sellora.CatalogService.Domain.Entities;
using Sellora.CatalogService.Domain.Tenancy;
using Sellora.CatalogService.Infrastructure.Outbox;
using Sellora.CatalogService.Infrastructure.Persistence;
using Xunit;

namespace Sellora.CatalogService.Tests;

public sealed class OutboxRelayServiceTests(PostgreSqlConstraintFixture database) : IClassFixture<PostgreSqlConstraintFixture>
{
    [Fact]
    public async Task Published_messages_are_marked_published_and_released()
    {
        var message = await AddPendingMessage();
        var publisher = new RecordingPublisher();
        var relay = CreateRelay(publisher, new ListLogger<OutboxRelayService>());

        await relay.ProcessPendingMessagesAsync();

        var published = Assert.Single(publisher.Published, item => item.OutboxId == message.OutboxId);
        Assert.Equal(message.AggregateId, published.AggregateId);
        Assert.Equal(message.CompanyId, published.CompanyId);
        Assert.Equal("PriceChanged", published.EventType);
        Assert.Equal("1.0", published.SchemaVersion);
        Assert.Equal(message.Payload, published.Payload);

        var stored = await Reload(message.OutboxId);
        Assert.NotNull(stored.PublishedAt);
        Assert.Null(stored.LeaseId);
        Assert.Null(stored.LeaseExpiresAt);
        Assert.Null(stored.LastError);

        // Already-published messages are not sent again.
        await relay.ProcessPendingMessagesAsync();
        Assert.Single(publisher.Published, item => item.OutboxId == message.OutboxId);
    }

    [Fact]
    public async Task Failed_publishes_are_rescheduled_with_a_truncated_error()
    {
        var message = await AddPendingMessage();
        var logger = new ListLogger<OutboxRelayService>();
        var publisher = new RecordingPublisher { Failure = new InvalidOperationException(new string('e', 3000)) };
        var relay = CreateRelay(publisher, logger);
        var before = DateTimeOffset.UtcNow;

        await relay.ProcessPendingMessagesAsync();

        var stored = await Reload(message.OutboxId);
        Assert.Null(stored.PublishedAt);
        Assert.Equal(1, stored.AttemptCount);
        Assert.Equal(2000, stored.LastError!.Length);
        Assert.True(stored.NextAttemptAt > before);
        Assert.Null(stored.LeaseId);
        Assert.Null(stored.LeaseExpiresAt);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);

        // The retry delay keeps the message out of the next poll.
        publisher.Failure = null;
        await relay.ProcessPendingMessagesAsync();
        Assert.DoesNotContain(publisher.Published, item => item.OutboxId == message.OutboxId);
    }

    [Fact]
    public async Task Short_errors_are_stored_unchanged()
    {
        var message = await AddPendingMessage();
        var publisher = new RecordingPublisher { Failure = new InvalidOperationException("broker down") };

        await CreateRelay(publisher, new ListLogger<OutboxRelayService>()).ProcessPendingMessagesAsync();

        Assert.Equal("broker down", (await Reload(message.OutboxId)).LastError);
    }

    [Fact]
    public async Task Background_loop_publishes_until_stopped()
    {
        var message = await AddPendingMessage();
        var publisher = new RecordingPublisher();
        var relay = CreateRelay(publisher, new ListLogger<OutboxRelayService>());

        await relay.StartAsync(CancellationToken.None);
        await publisher.WaitForAsync(message.OutboxId, TimeSpan.FromSeconds(30));
        await relay.StopAsync(CancellationToken.None);

        Assert.Contains(publisher.Published, item => item.OutboxId == message.OutboxId);
    }

    [Fact]
    public async Task Background_loop_logs_polling_failures_and_keeps_running()
    {
        // No DbContext is registered, so every poll fails.
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = new ListLogger<OutboxRelayService>();
        var relay = new OutboxRelayService(
            services.GetRequiredService<IServiceScopeFactory>(),
            new RecordingPublisher(),
            Options.Create(new OutboxRelayOptions { PollingIntervalSeconds = 1 }),
            logger);

        await relay.StartAsync(CancellationToken.None);
        await logger.WaitForAsync(LogLevel.Error, TimeSpan.FromSeconds(30));
        await relay.StopAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message == "Outbox relay polling failed.");
    }

    private OutboxRelayService CreateRelay(IEventPublisher publisher, ILogger<OutboxRelayService> logger)
    {
        var services = new ServiceCollection()
            .AddScoped<ITenantContext>(_ => new NoTenant())
            .AddDbContext<CatalogDbContext>(options => options.UseNpgsql(database.ConnectionString))
            .BuildServiceProvider();

        return new OutboxRelayService(
            services.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            Options.Create(new OutboxRelayOptions { BatchSize = 50, PollingIntervalSeconds = 1, LeaseSeconds = 30, RetryDelaySeconds = 60 }),
            logger);
    }

    private async Task<OutboxMessage> AddPendingMessage()
    {
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.AddSeconds(-1);
        var message = new OutboxMessage
        {
            OutboxId = Guid.NewGuid(),
            CompanyId = companyId,
            AggregateId = Guid.NewGuid(),
            EventType = "PriceChanged",
            SchemaVersion = "1.0",
            Payload = "{\"value\": 1}",
            OccurredAt = now,
            NextAttemptAt = now
        };

        await using var db = database.CreateDbContext(companyId);
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        return message;
    }

    private async Task<OutboxMessage> Reload(Guid outboxId)
    {
        await using var db = database.CreateDbContext();
        return await db.OutboxMessages.IgnoreQueryFilters().AsNoTracking().SingleAsync(item => item.OutboxId == outboxId);
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid? CompanyId => null;
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        private readonly ConcurrentQueue<OutboxMessageToPublish> _published = new();

        public Exception? Failure { get; set; }

        public IReadOnlyCollection<OutboxMessageToPublish> Published => _published.ToArray();

        public Task PublishAsync(OutboxMessageToPublish message, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            _published.Enqueue(message);
            return Task.CompletedTask;
        }

        public async Task WaitForAsync(Guid outboxId, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!_published.Any(item => item.OutboxId == outboxId))
            {
                Assert.True(DateTime.UtcNow < deadline, "The relay did not publish the message in time.");
                await Task.Delay(50);
            }
        }
    }
}

internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

    public IReadOnlyCollection<(LogLevel Level, string Message)> Entries => _entries.ToArray();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _entries.Enqueue((logLevel, formatter(state, exception)));

    public async Task WaitForAsync(LogLevel level, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!_entries.Any(entry => entry.Level == level))
        {
            Assert.True(DateTime.UtcNow < deadline, $"No {level} log entry was written in time.");
            await Task.Delay(50);
        }
    }
}
