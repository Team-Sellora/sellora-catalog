using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sellora.CatalogService.Application.Products;
using Sellora.CatalogService.Domain.Entities;
using Sellora.CatalogService.Domain.Products;
using Sellora.CatalogService.Domain.Tenancy;
using Sellora.CatalogService.Infrastructure.Persistence;

namespace Sellora.CatalogService.Infrastructure.Products;

public sealed class ProductService : IProductService
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

    public async Task<CreateProductResult> CreateAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.CompanyId is not Guid companyId)
        {
            return CreateProductResult.TenantNotAvailable();
        }

        var validationError = ValidateRequest(request);

        if (validationError is not null)
        {
            return CreateProductResult.InvalidRequest(validationError);
        }

        var normalizedSku = request.Sku.Trim().ToUpperInvariant();
        var normalizedBatchCode =
            request.BatchCode.Trim().ToUpperInvariant();

        var skuExists = await _dbContext.Products.AnyAsync(
            product => product.Sku == normalizedSku,
            cancellationToken);

        if (skuExists)
        {
            return CreateProductResult.DuplicateSku(normalizedSku);
        }

        var batchCodeExists = await _dbContext.ProductBatches.AnyAsync(
            batch => batch.BatchCode == normalizedBatchCode,
            cancellationToken);

        if (batchCodeExists)
        {
            return CreateProductResult.DuplicateBatchCode(
                normalizedBatchCode);
        }

        var now = DateTimeOffset.UtcNow;
        var productId = Guid.NewGuid();

        var product = new Product
        {
            ProductId = productId,
            CompanyId = companyId,
            Sku = normalizedSku,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim(),
            UnitOfMeasure = request.UnitOfMeasure.Trim(),
            CurrentUnitPrice = request.CurrentUnitPrice,
            Status = ProductStatus.Active,
            CreatedAt = now
        };

        var batch = new ProductBatch
        {
            BatchId = Guid.NewGuid(),
            ProductId = productId,
            CompanyId = companyId,
            BatchCode = normalizedBatchCode,
            ManufacturingDate = request.ManufacturingDate,
            ExpiryDate = request.ExpiryDate,
            Status = ProductStatus.Active,
            CreatedAt = now,
            Product = product
        };

        product.Batches.Add(batch);

        _dbContext.Products.Add(product);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            } postgresException)
        {
            _dbContext.ChangeTracker.Clear();

            if (postgresException.ConstraintName ==
                "uq_product_company_sku")
            {
                return CreateProductResult.DuplicateSku(normalizedSku);
            }

            if (postgresException.ConstraintName ==
                "uq_product_batch_company_code")
            {
                return CreateProductResult.DuplicateBatchCode(
                    normalizedBatchCode);
            }

            throw;
        }

        var response = new ProductResponse(
            product.ProductId,
            product.Sku,
            product.Name,
            product.Description,
            product.UnitOfMeasure,
            product.CurrentUnitPrice,
            product.Status,
            product.CreatedAt,
            product.UpdatedAt,
            new[]
            {
            new ProductBatchResponse(
                batch.BatchId,
                batch.BatchCode,
                batch.ManufacturingDate,
                batch.ExpiryDate,
                batch.Status,
                batch.CreatedAt,
                batch.UpdatedAt)
            });

        return CreateProductResult.Success(response);
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