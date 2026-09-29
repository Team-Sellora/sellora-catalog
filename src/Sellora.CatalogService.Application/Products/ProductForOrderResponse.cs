namespace Sellora.CatalogService.Application.Products;

public sealed record ProductForOrderResponse(
    Guid ProductId,
    string? Name,
    string? Sku,
    string? UnitOfMeasure,
    decimal? CurrentUnitPrice,
    string Status,
    DateOnly? EarliestExpiryDate,
    bool IsAvailable,
    string? UnavailableReason);