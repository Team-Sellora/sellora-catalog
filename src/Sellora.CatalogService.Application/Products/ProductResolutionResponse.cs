namespace Sellora.CatalogService.Application.Products;

public sealed record ProductResolutionResponse(
    IReadOnlyCollection<ProductForOrderResponse> Items);