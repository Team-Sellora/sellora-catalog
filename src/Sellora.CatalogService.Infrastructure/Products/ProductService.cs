using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;
using Sellora.CatalogService.Application.Common;
using Sellora.CatalogService.Application.Events;
using Sellora.CatalogService.Application.Identity;
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
    private readonly ICurrentUserContext _currentUserContext;

    public ProductService(
        CatalogDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
    }

    //create product
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

    //get products
    public async Task<PagedResponse<ProductResponse>> GetProductsAsync(
    ProductListQuery query,
    CancellationToken cancellationToken = default)
    {
        EnsureTenantAvailable();
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var productsQuery = _dbContext.Products
            .AsNoTracking()
            .AsQueryable();

        if (!string.Equals(query.Status, "All", StringComparison.OrdinalIgnoreCase))
        {
            var status = string.Equals(query.Status, ProductStatus.Inactive, StringComparison.OrdinalIgnoreCase)
                ? ProductStatus.Inactive : ProductStatus.Active;
            productsQuery = productsQuery.Where(product => product.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchPattern = $"%{query.Search.Trim()}%";

            productsQuery = productsQuery.Where(product =>
                EF.Functions.ILike(product.Name, searchPattern) ||
                EF.Functions.ILike(product.Sku, searchPattern));
        }

        var totalCount = await productsQuery.CountAsync(cancellationToken);

        var products = await productsQuery
            .Include(product => product.Batches)
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Sku)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = products
            .Select(product => new ProductResponse(
                product.ProductId,
                product.Sku,
                product.Name,
                product.Description,
                product.UnitOfMeasure,
                product.CurrentUnitPrice,
                product.Status,
                product.CreatedAt,
                product.UpdatedAt,
                product.Batches
                    .OrderBy(batch => batch.ExpiryDate)
                    .Select(batch => new ProductBatchResponse(
                        batch.BatchId,
                        batch.BatchCode,
                        batch.ManufacturingDate,
                        batch.ExpiryDate,
                        batch.Status,
                        batch.CreatedAt,
                        batch.UpdatedAt))
                    .ToArray()))
            .ToArray();

        return new PagedResponse<ProductResponse>(
            items,
            page,
            pageSize,
            totalCount);
    }

    //get product by id
    public async Task<ProductResponse?> GetProductByIdAsync(
    Guid productId,
    CancellationToken cancellationToken = default)
    {
        EnsureTenantAvailable();
        var product = await _dbContext.Products
            .AsNoTracking()
            .Include(product => product.Batches)
            .SingleOrDefaultAsync(
                product => product.ProductId == productId,
                cancellationToken);

        if (product is null)
        {
            return null;
        }

        return new ProductResponse(
            product.ProductId,
            product.Sku,
            product.Name,
            product.Description,
            product.UnitOfMeasure,
            product.CurrentUnitPrice,
            product.Status,
            product.CreatedAt,
            product.UpdatedAt,
            product.Batches
                .OrderBy(batch => batch.ExpiryDate)
                .Select(batch => new ProductBatchResponse(
                    batch.BatchId,
                    batch.BatchCode,
                    batch.ManufacturingDate,
                    batch.ExpiryDate,
                    batch.Status,
                    batch.CreatedAt,
                    batch.UpdatedAt))
                .ToArray());
    }

    //update product
    public async Task<UpdateProductResult> UpdateAsync(
    Guid productId,
    UpdateProductRequest request,
    CancellationToken cancellationToken = default)
    {
        if (_tenantContext.CompanyId is null)
        {
            return UpdateProductResult.TenantNotAvailable();
        }

        var validationError = ValidateUpdateRequest(request);

        if (validationError is not null)
        {
            return UpdateProductResult.InvalidRequest(validationError);
        }

        var product = await _dbContext.Products
            .Include(product => product.Batches)
            .SingleOrDefaultAsync(
                product => product.ProductId == productId,
                cancellationToken);

        if (product is null)
        {
            return UpdateProductResult.NotFound(productId);
        }

        var normalizedSku = request.Sku.Trim().ToUpperInvariant();

        var skuExists = await _dbContext.Products.AnyAsync(
            otherProduct =>
                otherProduct.ProductId != productId &&
                otherProduct.Sku == normalizedSku,
            cancellationToken);

        if (skuExists)
        {
            return UpdateProductResult.DuplicateSku(normalizedSku);
        }

        product.Sku = normalizedSku;
        product.Name = request.Name.Trim();
        product.Description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();
        product.UnitOfMeasure = request.UnitOfMeasure.Trim();
        product.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "uq_product_company_sku"
            })
        {
            _dbContext.ChangeTracker.Clear();
            return UpdateProductResult.DuplicateSku(normalizedSku);
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
            product.Batches
                .OrderBy(batch => batch.ExpiryDate)
                .Select(batch => new ProductBatchResponse(
                    batch.BatchId,
                    batch.BatchCode,
                    batch.ManufacturingDate,
                    batch.ExpiryDate,
                    batch.Status,
                    batch.CreatedAt,
                    batch.UpdatedAt))
                .ToArray());

        return UpdateProductResult.Success(response);
    }

    // change product price
    public async Task<ChangeProductPriceResult> ChangePriceAsync(
        Guid productId,
        ChangeProductPriceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.CompanyId is not Guid companyId)
        {
            return ChangeProductPriceResult.TenantNotAvailable();
        }

        var changedBy = _currentUserContext.Subject;
        if (string.IsNullOrWhiteSpace(changedBy))
        {
            return ChangeProductPriceResult.UserNotAvailable();
        }

        var validationError = ValidatePriceChangeRequest(request);

        if (validationError is not null)
        {
            return ChangeProductPriceResult.InvalidRequest(validationError);
        }

        var product = await _dbContext.Products
            .Include(product => product.Batches)
            .SingleOrDefaultAsync(
                product => product.ProductId == productId,
                cancellationToken);

        if (product is null)
        {
            return ChangeProductPriceResult.NotFound(productId);
        }

        var now = DateTimeOffset.UtcNow;
        var history = new ProductPriceHistory
        {
            PriceHistoryId = Guid.NewGuid(),
            CompanyId = companyId,
            ProductId = product.ProductId,
            OldUnitPrice = product.CurrentUnitPrice,
            NewUnitPrice = request.NewUnitPrice,
            ChangedBy = changedBy,
            Reason = request.Reason!.Trim(),
            ChangedAt = now,
            EffectiveFrom = request.EffectiveFrom.ToUniversalTime(),
            Product = product
        };

        product.CurrentUnitPrice = request.NewUnitPrice;
        product.UpdatedAt = now;
        _dbContext.ProductPriceHistory.Add(history);

        var priceChanged = new PriceChangedEvent(
            companyId,
            product.ProductId,
            history.OldUnitPrice,
            history.NewUnitPrice,
            history.ChangedBy,
            history.Reason,
            history.ChangedAt,
            history.EffectiveFrom);

        _dbContext.OutboxMessages.Add(new OutboxMessage
        {
            OutboxId = Guid.NewGuid(),
            CompanyId = companyId,
            AggregateId = product.ProductId,
            EventType = "PriceChanged",
            SchemaVersion = "1.0",
            Payload = JsonSerializer.Serialize(
                priceChanged,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            OccurredAt = now,
            NextAttemptAt = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

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
            product.Batches
                .OrderBy(batch => batch.ExpiryDate)
                .Select(batch => new ProductBatchResponse(
                    batch.BatchId,
                    batch.BatchCode,
                    batch.ManufacturingDate,
                    batch.ExpiryDate,
                    batch.Status,
                    batch.CreatedAt,
                    batch.UpdatedAt))
                .ToArray());

        return ChangeProductPriceResult.Success(response);
    }

    //deactivate product
    public async Task<DeactivateProductResult> DeactivateAsync(
    Guid productId,
    CancellationToken cancellationToken = default)
    {
        if (_tenantContext.CompanyId is null)
        {
            return DeactivateProductResult.TenantNotAvailable();
        }

        var product = await _dbContext.Products
            .Include(product => product.Batches)
            .SingleOrDefaultAsync(
                product => product.ProductId == productId,
                cancellationToken);

        if (product is null)
        {
            return DeactivateProductResult.NotFound(productId);
        }

        if (product.Status == ProductStatus.Inactive)
        {
            return DeactivateProductResult.AlreadyInactive(productId);
        }

        product.Status = ProductStatus.Inactive;
        product.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

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
            product.Batches
                .OrderBy(batch => batch.ExpiryDate)
                .Select(batch => new ProductBatchResponse(
                    batch.BatchId,
                    batch.BatchCode,
                    batch.ManufacturingDate,
                    batch.ExpiryDate,
                    batch.Status,
                    batch.CreatedAt,
                    batch.UpdatedAt))
                .ToArray());

        return DeactivateProductResult.Success(response);
    }

    // Validate price change request
    private static string? ValidatePriceChangeRequest(
        ChangeProductPriceRequest request)
    {
        if (request.NewUnitPrice < 0.01m ||
            request.NewUnitPrice > 9999999999999999.99m ||
            decimal.Round(request.NewUnitPrice, 2) != request.NewUnitPrice)
        {
            return "New unit price must be between 0.01 and 9999999999999999.99 with at most two decimal places.";
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return "A reason for the price change is required.";
        }

        if (request.Reason!.Trim().Length > 500)
        {
            return "The price-change reason cannot exceed 500 characters.";
        }

        if (request.EffectiveFrom == default)
        {
            return "Effective date is required.";
        }

        if (request.EffectiveFrom.ToUniversalTime() < DateTimeOffset.UtcNow)
        {
            return "Effective date cannot be in the past.";
        }

        return null;
    }

    // validate update request
    private static string? ValidateUpdateRequest(
        UpdateProductRequest request)
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

        return null;
    }

    // validate request
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

        if (request.CurrentUnitPrice < 0.01m || request.CurrentUnitPrice > 9999999999999999.99m ||
            decimal.Round(request.CurrentUnitPrice, 2) != request.CurrentUnitPrice)
        {
            return "Current unit price must be between 0.01 and 9999999999999999.99 with at most two decimal places.";
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

    private void EnsureTenantAvailable()
    {
        if (_tenantContext.CompanyId is null)
            throw new UnauthorizedAccessException("A valid company identifier was not found in the access token.");
    }
}
