using Sellora.CatalogService.Application.Common;
namespace Sellora.CatalogService.Application.Products;

public interface IProductService
{
    Task<CreateProductResult> CreateAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<ProductResponse>> GetProductsAsync(
    ProductListQuery query,
    CancellationToken cancellationToken = default);

    Task<ProductResponse?> GetProductByIdAsync(
    Guid productId,
    CancellationToken cancellationToken = default);

    Task<UpdateProductResult> UpdateAsync(
    Guid productId,
    UpdateProductRequest request,
    CancellationToken cancellationToken = default);

    Task<DeactivateProductResult> DeactivateAsync(
    Guid productId,
    CancellationToken cancellationToken = default);
}