namespace Sellora.CatalogService.Application.Products;

public interface IOrderCatalogService
{
    Task<ProductResolutionResponse> ResolveProductsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<OrderCatalogueProductResponse>> GetCatalogueAsync(
        Guid companyId,
        string? search,
        CancellationToken cancellationToken = default);
}