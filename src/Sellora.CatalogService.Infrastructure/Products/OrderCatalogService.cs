using Microsoft.EntityFrameworkCore;
using Sellora.CatalogService.Application.Products;
using Sellora.CatalogService.Domain.Products;
using Sellora.CatalogService.Infrastructure.Persistence;

namespace Sellora.CatalogService.Infrastructure.Products;

public sealed class OrderCatalogService : IOrderCatalogService
{
    private const string ProductNotFound = "ProductNotFound";
    private const string ProductInactive = "ProductInactive";
    private const string NoActiveUnexpiredBatch = "NoActiveUnexpiredBatch";

    private readonly CatalogDbContext _dbContext;
    private readonly IProductPriceCache _cache;

    public OrderCatalogService(
        CatalogDbContext dbContext,
        IProductPriceCache cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<ProductResolutionResponse> ResolveProductsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException(
                "Company ID is required.",
                nameof(companyId));
        }

        var requestedIds = productIds
            .Where(productId => productId != Guid.Empty)
            .Distinct()
            .ToArray();

        if (requestedIds.Length == 0)
        {
            return new ProductResolutionResponse(
                Array.Empty<ProductForOrderResponse>());
        }

        var resolvedProducts =
            new Dictionary<Guid, ProductForOrderResponse>();

        var uncachedIds = new List<Guid>();

        foreach (var productId in requestedIds)
        {
            if (_cache.TryGet(
                companyId,
                productId,
                out var cachedProduct))
            {
                resolvedProducts[productId] = cachedProduct!;
            }
            else
            {
                uncachedIds.Add(productId);
            }
        }

        if (uncachedIds.Count > 0)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // All uncached products are fetched in one database query.
            var databaseProducts = await _dbContext.Products
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(product =>
                    product.CompanyId == companyId &&
                    uncachedIds.Contains(product.ProductId))
                .Select(product => new
                {
                    product.ProductId,
                    product.Name,
                    product.Sku,
                    product.UnitOfMeasure,
                    product.CurrentUnitPrice,
                    product.Status,

                    EarliestExpiryDate = product.Batches
                        .Where(batch =>
                            batch.CompanyId == companyId &&
                            batch.Status == ProductStatus.Active &&
                            batch.ExpiryDate >= today)
                        .OrderBy(batch => batch.ExpiryDate)
                        .Select(batch => (DateOnly?)batch.ExpiryDate)
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            foreach (var product in databaseProducts)
            {
                var isActive =
                    product.Status == ProductStatus.Active;

                var hasValidBatch =
                    product.EarliestExpiryDate.HasValue;

                var isAvailable =
                    isActive && hasValidBatch;

                string? unavailableReason = null;

                if (!isActive)
                {
                    unavailableReason = ProductInactive;
                }
                else if (!hasValidBatch)
                {
                    unavailableReason = NoActiveUnexpiredBatch;
                }

                var response = new ProductForOrderResponse(
                    product.ProductId,
                    product.Name,
                    product.Sku,
                    product.UnitOfMeasure,
                    product.CurrentUnitPrice,
                    product.Status,
                    product.EarliestExpiryDate,
                    isAvailable,
                    unavailableReason);

                resolvedProducts[product.ProductId] = response;

                _cache.Set(
                    companyId,
                    product.ProductId,
                    response);
            }

            var databaseProductIds = databaseProducts
                .Select(product => product.ProductId)
                .ToHashSet();

            foreach (var missingId in uncachedIds
                .Where(id => !databaseProductIds.Contains(id)))
            {
                var notFoundResponse =
                    new ProductForOrderResponse(
                        missingId,
                        null,
                        null,
                        null,
                        null,
                        "NotFound",
                        null,
                        false,
                        ProductNotFound);

                resolvedProducts[missingId] =
                    notFoundResponse;

                _cache.Set(
                    companyId,
                    missingId,
                    notFoundResponse);
            }
        }

        var orderedItems = requestedIds
            .Select(productId => resolvedProducts[productId])
            .ToArray();

        return new ProductResolutionResponse(orderedItems);
    }

    public async Task<IReadOnlyCollection<OrderCatalogueProductResponse>>
        GetCatalogueAsync(
            Guid companyId,
            string? search,
            CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException(
                "Company ID is required.",
                nameof(companyId));
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var productsQuery = _dbContext.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(product =>
                product.CompanyId == companyId &&
                product.Status == ProductStatus.Active);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchPattern = $"%{search.Trim()}%";

            productsQuery = productsQuery.Where(product =>
                EF.Functions.ILike(
                    product.Name,
                    searchPattern) ||
                EF.Functions.ILike(
                    product.Sku,
                    searchPattern));
        }

        var products = await productsQuery
            .Select(product => new
            {
                product.ProductId,
                product.Name,
                product.Sku,
                product.UnitOfMeasure,
                product.CurrentUnitPrice,

                EarliestExpiryDate = product.Batches
                    .Where(batch =>
                        batch.CompanyId == companyId &&
                        batch.Status == ProductStatus.Active &&
                        batch.ExpiryDate >= today)
                    .OrderBy(batch => batch.ExpiryDate)
                    .Select(batch => (DateOnly?)batch.ExpiryDate)
                    .FirstOrDefault()
            })
            .Where(product =>
                product.EarliestExpiryDate != null)
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Sku)
            .ToListAsync(cancellationToken);

        return products
            .Select(product =>
                new OrderCatalogueProductResponse(
                    product.ProductId,
                    product.Name,
                    product.Sku,
                    product.UnitOfMeasure,
                    product.CurrentUnitPrice,
                    product.EarliestExpiryDate!.Value))
            .ToArray();
    }
}