using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sellora.CatalogService.Domain.Entities;
using Sellora.CatalogService.Domain.Tenancy;
using Sellora.CatalogService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sellora.CatalogService.Tests;

public sealed class CategoryMigrationTests
{
    private sealed record Tenant(Guid? CompanyId) : ITenantContext;

    [Fact]
    public async Task Upgrade_renames_duplicates_preserving_ids_links_and_tenant_scope()
    {
        await using var database = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("category_upgrade_tests")
            .WithUsername("sellora_test")
            .WithPassword("sellora_test_password")
            .Build();
        await database.StartAsync();
        var company = Guid.NewGuid();
        await using var db = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(database.GetConnectionString()).Options, new Tenant(company));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260909134318_AddProductCategories");
        var now = DateTimeOffset.UtcNow;
        Category Category(string name, int age, Guid? tenant = null) => new()
        {
            CategoryId = Guid.NewGuid(), CompanyId = tenant ?? company,
            Name = name, Status = "Active", CreatedAt = now.AddMinutes(age)
        };
        var original = Category("Beverages", -10);
        var duplicate = Category("beverages", -5);
        duplicate.Status = "Inactive";
        var occupied = Category("BEVERAGES (2)", -1);
        var longOriginal = Category(new string('A', 120), -10);
        var longDuplicate = Category(new string('a', 120), -5);
        var otherTenant = Category("beverages", -1, Guid.NewGuid());
        db.Categories.AddRange(original, duplicate, occupied, longOriginal, longDuplicate, otherTenant);
        await db.SaveChangesAsync();
        var productId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO product
                (product_id, company_id, sku, name, unit_of_measure,
                 current_unit_price, status, created_at, category_id)
            VALUES ({productId}, {company}, {"UPGRADE-1"}, {"Product"}, {"Each"},
                    {1m}, {"Active"}, {now}, {duplicate.CategoryId});
            """);
        db.ChangeTracker.Clear();
        await migrator.MigrateAsync();
        var categories = await db.Categories.IgnoreQueryFilters().ToDictionaryAsync(c => c.CategoryId);
        Assert.Equal(6, categories.Count);
        Assert.Equal("Beverages", categories[original.CategoryId].Name);
        Assert.Equal("beverages (3)", categories[duplicate.CategoryId].Name);
        Assert.Equal("Inactive", categories[duplicate.CategoryId].Status);
        Assert.Equal("BEVERAGES (2)", categories[occupied.CategoryId].Name);
        Assert.Equal("beverages", categories[otherTenant.CategoryId].Name);
        Assert.Equal(new string('a', 116) + " (2)", categories[longDuplicate.CategoryId].Name);
        Assert.Equal(duplicate.CategoryId, (await db.Products.SingleAsync()).CategoryId);
        Assert.Contains("20260914163118_MakeCategoryNamesCaseInsensitive",
            await db.Database.GetAppliedMigrationsAsync());
        // Re-running startup migration must not rename anything again.
        await migrator.MigrateAsync();
        Assert.Equal("beverages (3)", (await db.Categories.AsNoTracking()
            .SingleAsync(c => c.CategoryId == duplicate.CategoryId)).Name);
    }
}
