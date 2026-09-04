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

    private static string? ValidateRequest(CreateProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            return "SKU is required.";
        }

        if (request.Sku.Trim().Length > 80)
        {
            return "SKU cannot exceed 80 characters.";
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Product name is required.";
        }

        if (request.Name.Trim().Length > 200)
        {
            return "Product name cannot exceed 200 characters.";
        }

        if (string.IsNullOrWhiteSpace(request.UnitOfMeasure))
        {
            return "Unit of measure is required.";
        }

        if (request.UnitOfMeasure.Trim().Length > 40)
        {
            return "Unit of measure cannot exceed 40 characters.";
        }

        if (request.CurrentUnitPrice <= 0)
        {
            return "Current unit price must be greater than zero.";
        }

        if (string.IsNullOrWhiteSpace(request.BatchCode))
        {
            return "Batch code is required.";
        }

        if (request.BatchCode.Trim().Length > 80)
        {
            return "Batch code cannot exceed 80 characters.";
        }

        if (request.ManufacturingDate == default)
        {
            return "Manufacturing date is required.";
        }

        if (request.ExpiryDate == default)
        {
            return "Expiry date is required.";
        }

        if (request.ExpiryDate <= request.ManufacturingDate)
        {
            return "Expiry date must be after the manufacturing date.";
        }

        return null;
    }
}