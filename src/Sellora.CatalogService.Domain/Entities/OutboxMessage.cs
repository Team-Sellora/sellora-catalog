using Sellora.CatalogService.Domain.Tenancy;

namespace Sellora.CatalogService.Domain.Entities;

public sealed class OutboxMessage : ITenantScoped
{
    public Guid OutboxId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AggregateId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = "1.0";
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
}
