namespace Sellora.CatalogService.Application.Events;

public sealed record PriceChangedEvent(
    Guid CompanyId,
    Guid ProductId,
    decimal OldUnitPrice,
    decimal NewUnitPrice,
    string ChangedBy,
    string Reason,
    DateTimeOffset ChangedAt,
    DateTimeOffset EffectiveFrom);
