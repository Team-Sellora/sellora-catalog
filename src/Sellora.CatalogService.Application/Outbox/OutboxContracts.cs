namespace Sellora.CatalogService.Application.Outbox;

public sealed record OutboxMessageToPublish(
    Guid OutboxId,
    string EventType,
    string SchemaVersion,
    Guid CompanyId,
    Guid AggregateId,
    string Payload,
    DateTimeOffset OccurredAt);

public interface IEventPublisher
{
    Task PublishAsync(
        OutboxMessageToPublish message,
        CancellationToken cancellationToken = default);
}
