namespace Sellora.CatalogService.Application.Products;

public sealed record ProductResponse(
    Guid ProductId,
    string Sku,
    string Name,
    string? Description,
    string UnitOfMeasure,
    decimal CurrentUnitPrice,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyCollection<ProductBatchResponse> Batches);