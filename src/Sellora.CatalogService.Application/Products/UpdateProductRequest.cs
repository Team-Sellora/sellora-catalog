namespace Sellora.CatalogService.Application.Products;

public sealed record UpdateProductRequest(
    string Sku,
    string Name,
    string? Description,
    string UnitOfMeasure);