using Microsoft.EntityFrameworkCore;
using Sellora.CatalogService.Domain.Tenancy;
using Sellora.CatalogService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sellora.CatalogService.Tests;

/// <summary>
/// Matches Organization's PostgreSQL 16 Testcontainers fixture. Each test class
/// receives an isolated container with the real migrations applied.
/// </summary>
public sealed class PostgreSqlConstraintFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database =
        new PostgreSqlBuilder("postgres:16")
            .WithDatabase("catalog_constraint_tests")
            .WithUsername("sellora_test")
            .WithPassword("sellora_test_password")
            .Build();

    public string ConnectionString => _database.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    public CatalogDbContext CreateDbContext(Guid? companyId = null) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(ConnectionString).Options, new FixedTenantContext(companyId));

    private sealed record FixedTenantContext(Guid? CompanyId) : ITenantContext;
}
