using Microsoft.EntityFrameworkCore;
using Sellora.CatalogService.Domain.Entities;
using Sellora.CatalogService.Domain.Tenancy;

namespace Sellora.CatalogService.Infrastructure.Persistence;

public class CatalogDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public CatalogDbContext(
        DbContextOptions<CatalogDbContext> options,
        ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductBatch> ProductBatches => Set<ProductBatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(CatalogDbContext).Assembly);

        modelBuilder.Entity<Product>()
            .HasQueryFilter(product =>
                _tenantContext.CompanyId != null &&
                product.CompanyId == _tenantContext.CompanyId);

        modelBuilder.Entity<ProductBatch>()
            .HasQueryFilter(batch =>
                _tenantContext.CompanyId != null &&
                batch.CompanyId == _tenantContext.CompanyId);
    }


}
