namespace Sellora.CatalogService.Api.Contracts;

public sealed record ChangeProductPriceRequestBody(
    decimal NewUnitPrice,
    string? Reason,
    DateTimeOffset EffectiveFrom);
