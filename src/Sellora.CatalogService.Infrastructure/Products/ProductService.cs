using Sellora.CatalogService.Application.Products;
using Sellora.CatalogService.Domain.Tenancy;
using Sellora.CatalogService.Infrastructure.Persistence;

namespace Sellora.CatalogService.Infrastructure.Products;

public sealed class ProductService
{
    private readonly CatalogDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public ProductService(
        CatalogDbContext dbContext,
        ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }
}