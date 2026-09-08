namespace Sellora.CatalogService.Application.Products;

public sealed record PriceHistoryResponse(
    Guid PriceHistoryId,
    Guid ProductId,
    decimal OldUnitPrice,
    decimal NewUnitPrice,
    string ChangedBy,
    string Reason,
    DateTimeOffset ChangedAt,
    DateTimeOffset EffectiveFrom);
