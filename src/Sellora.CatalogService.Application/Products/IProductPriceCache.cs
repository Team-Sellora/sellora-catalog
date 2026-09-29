namespace Sellora.CatalogService.Application.Products;

public interface IProductPriceCache
{
    bool TryGet(
        Guid companyId,
        Guid productId,
        out ProductForOrderResponse? product);

    void Set(
        Guid companyId,
        Guid productId,
        ProductForOrderResponse product);

    void Remove(Guid companyId, Guid productId);
}
