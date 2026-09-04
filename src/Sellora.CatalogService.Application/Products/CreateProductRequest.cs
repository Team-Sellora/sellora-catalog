namespace Sellora.CatalogService.Application.Products;

public sealed record CreateProductRequest(
    string Sku,
    string Name,
    string? Description,
    string UnitOfMeasure,
    decimal CurrentUnitPrice,
    string BatchCode,
    DateOnly ManufacturingDate,
    DateOnly ExpiryDate
);
