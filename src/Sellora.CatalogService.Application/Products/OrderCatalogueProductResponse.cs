namespace Sellora.CatalogService.Application.Products;

public sealed record OrderCatalogueProductResponse(
    Guid ProductId,
    string Name,
    string Sku,
    string UnitOfMeasure,
    decimal CurrentUnitPrice,
    DateOnly EarliestExpiryDate);