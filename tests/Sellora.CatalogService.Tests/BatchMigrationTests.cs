using Npgsql;
using Testcontainers.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sellora.CatalogService.Domain.Entities;
using Sellora.CatalogService.Domain.Tenancy;
using Sellora.CatalogService.Infrastructure.Persistence;
using Sellora.CatalogService.Infrastructure.Products;
using Sellora.CatalogService.Application.Products;
using Xunit;

namespace Sellora.CatalogService.Tests;

public sealed class BatchMigrationTests
{
    private sealed record Tenant(Guid? CompanyId) : ITenantContext;

    [Fact]
    public async Task Migration_preserves_data_allows_cross_product_codes_and_rejects_same_product_duplicates()
    {
        await using var database = new PostgreSqlBuilder("postgres:16")
            .WithDatabase("catalog_migration_tests")
            .WithUsername("sellora_test")
            .WithPassword("sellora_test_password")
            .Build();
        await database.StartAsync();
        var tenant = new Tenant(Guid.NewGuid());
        using var db = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(database.GetConnectionString()).Options, tenant);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260904051529_InitialCatalogSchema");
        var service = new ProductService(db, tenant);
        var request = new CreateProductRequest("SKU-1", "Name", null, "Each", 1m, "BATCH", new(2026, 1, 1), new(2027, 1, 1));
        var original = await service.CreateAsync(request);
        Assert.True(original.IsSuccess);
        await migrator.MigrateAsync();
        Assert.True((await service.CreateAsync(request with { Sku = "SKU-2" })).IsSuccess);
        Assert.Equal(2, await db.Products.CountAsync());
        Assert.NotNull(await service.GetProductByIdAsync(original.Product!.ProductId));
        db.ProductBatches.Add(new ProductBatch
        {
            BatchId = Guid.NewGuid(), CompanyId = tenant.CompanyId!.Value,
            ProductId = original.Product.ProductId, BatchCode = "BATCH",
            ManufacturingDate = new(2026, 1, 1), ExpiryDate = new(2027, 1, 1),
            CreatedAt = DateTimeOffset.UtcNow
        });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("uq_product_batch_company_product_code", postgres.ConstraintName);
    }

    [Fact]
    public void Postgres_snapshot_matches_model_and_upgrade_script_scopes_index_to_product()
    {
        using var db = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql("Host=unused;Database=unused").Options, new Tenant(Guid.NewGuid()));
        Assert.False(db.Database.HasPendingModelChanges());
        var script = db.GetService<IMigrator>().GenerateScript("20260904051529_InitialCatalogSchema");
        Assert.Contains("DROP INDEX uq_product_batch_company_code", script);
        Assert.Contains("CREATE UNIQUE INDEX uq_product_batch_company_product_code ON product_batch (company_id, product_id, batch_code)", script);
    }

    [Fact]
    public async Task Service_reads_without_tenant_fail_explicitly()
    {
        using var db = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql("Host=unused;Database=unused").Options, new Tenant(null));
        var service = new ProductService(db, new Tenant(null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetProductsAsync(new ProductListQuery(null)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetProductByIdAsync(Guid.NewGuid()));
    }
}
