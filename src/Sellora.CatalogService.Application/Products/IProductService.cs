namespace Sellora.CatalogService.Application.Products;

public interface IProductService
{
    Task<CreateProductResult> CreateAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken = default);
}