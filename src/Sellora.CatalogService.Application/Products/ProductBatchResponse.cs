namespace Sellora.CatalogService.Application.Products;

public sealed record ProductBatchResponse(
    Guid BatchId,
    string BatchCode,
    DateOnly ManufacturingDate,
    DateOnly ExpiryDate,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);