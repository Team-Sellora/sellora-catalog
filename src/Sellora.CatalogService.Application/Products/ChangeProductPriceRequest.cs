namespace Sellora.CatalogService.Application.Products;

public sealed record ChangeProductPriceRequest(
    decimal NewUnitPrice,
    string Reason,
    DateTimeOffset EffectiveFrom);