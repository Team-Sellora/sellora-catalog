namespace Sellora.CatalogService.Api.Contracts;

public sealed record CreateProductRequestBody(
    string Sku,
    string Name,
    string? Description,
    string UnitOfMeasure,
    decimal CurrentUnitPrice,
    string BatchCode,
    DateOnly ManufacturingDate,
    DateOnly ExpiryDate);