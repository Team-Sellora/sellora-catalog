namespace Sellora.CatalogService.Api.Contracts;

public sealed record UpdateProductRequestBody(
    string Sku,
    string Name,
    string? Description,
    string UnitOfMeasure);