using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sellora.CatalogService.Domain.Entities;
using Xunit;

namespace Sellora.CatalogService.Tests;

public sealed class PostgreSqlConstraintTests(PostgreSqlConstraintFixture database)
    : IClassFixture<PostgreSqlConstraintFixture>
{
    private static Product Product(Guid companyId, decimal price = 1m, string status = "Active") => new()
    {
        ProductId = Guid.NewGuid(), CompanyId = companyId, Sku = Guid.NewGuid().ToString("N"),
        Name = "Constraint test", UnitOfMeasure = "Each", CurrentUnitPrice = price,
        Status = status, CreatedAt = DateTimeOffset.UtcNow
    };

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.004")]
    public async Task Database_rejects_prices_that_are_nonpositive_after_rounding(string price)
    {
        var company = Guid.NewGuid();
        await using var db = database.CreateDbContext(company);
        db.Products.Add(Product(company, decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, postgres.SqlState);
        Assert.Equal("ck_product_current_unit_price", postgres.ConstraintName);
    }

    [Fact]
    public async Task Database_rounds_numeric_price_to_two_places()
    {
        var company = Guid.NewGuid();
        await using var db = database.CreateDbContext(company);
        var product = Product(company, 12.345m);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(12.35m, (await db.Products.SingleAsync()).CurrentUnitPrice);
    }

    [Fact]
    public async Task Database_rejects_price_overflow()
    {
        var company = Guid.NewGuid();
        await using var db = database.CreateDbContext(company);
        db.Products.Add(Product(company, 10000000000000000m));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.NumericValueOutOfRange, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }

    [Fact]
    public async Task Database_rejects_invalid_product_status()
    {
        var company = Guid.NewGuid();
        await using var db = database.CreateDbContext(company);
        db.Products.Add(Product(company, status: "Unknown"));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, postgres.SqlState);
        Assert.Equal("ck_product_status", postgres.ConstraintName);
    }

    [Fact]
    public async Task Schema_was_created_by_all_real_migrations()
    {
        await using var db = database.CreateDbContext();
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
