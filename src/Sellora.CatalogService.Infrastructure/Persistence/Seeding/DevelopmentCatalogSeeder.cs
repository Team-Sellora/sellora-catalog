using Microsoft.EntityFrameworkCore;
using Sellora.CatalogService.Domain.Entities;
using Sellora.CatalogService.Domain.Products;

namespace Sellora.CatalogService.Infrastructure.Persistence.Seeding;

/// <summary>
/// Creates a small, predictable catalogue for the Organization staging demo
/// tenant. This is idempotent and must never run outside Staging.
/// </summary>
public static class DevelopmentCatalogSeeder
{
    private const string SeedSku = "DEMO-TEA-500";
    private static readonly DateTimeOffset SeededAt =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task SeedAsync(
        CatalogDbContext db,
        CancellationToken cancellationToken = default)
    {
        var alreadySeeded = await db.Products
            .IgnoreQueryFilters()
            .AnyAsync(
                product => product.CompanyId == DevelopmentCatalogSeedIds.CompanyId &&
                           product.Sku == SeedSku,
                cancellationToken);

        if (alreadySeeded)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Products.AddRange(
            CreateProduct("40000000-0000-0000-0000-000000000001", "DEMO-TEA-500", "Ceylon Black Tea 500g", "Premium loose-leaf black tea.", 1_250m, "TEA-2026-01"),
            CreateProduct("40000000-0000-0000-0000-000000000002", "DEMO-BIS-200", "Chocolate Biscuits 200g", "Crunchy chocolate cream biscuits.", 480m, "BIS-2026-02"),
            CreateProduct("40000000-0000-0000-0000-000000000003", "DEMO-JUI-1L", "Mixed Fruit Juice 1L", "Ready-to-drink mixed fruit juice.", 690m, "JUI-2026-03"),
            CreateProduct("40000000-0000-0000-0000-000000000004", "DEMO-SOA-100", "Bath Soap 100g", "Fresh citrus bath soap.", 230m, "SOA-2026-04"),
            CreateProduct("40000000-0000-0000-0000-000000000005", "DEMO-RIC-5K", "Samba Rice 5kg", "Premium local samba rice.", 1_850m, "RIC-2026-05"));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static Product CreateProduct(
        string productId,
        string sku,
        string name,
        string description,
        decimal price,
        string batchCode)
    {
        var product = new Product
        {
            ProductId = Guid.Parse(productId),
            CompanyId = DevelopmentCatalogSeedIds.CompanyId,
            Sku = sku,
            Name = name,
            Description = description,
            UnitOfMeasure = "Each",
            CurrentUnitPrice = price,
            Status = ProductStatus.Active,
            CreatedAt = SeededAt
        };

        product.Batches.Add(new ProductBatch
        {
            BatchId = Guid.Parse(productId.Replace("40000000", "50000000")),
            ProductId = product.ProductId,
            CompanyId = product.CompanyId,
            BatchCode = batchCode,
            ManufacturingDate = new DateOnly(2026, 1, 1),
            ExpiryDate = new DateOnly(2027, 12, 31),
            Status = ProductStatus.Active,
            CreatedAt = SeededAt,
            Product = product
        });

        return product;
    }
}
