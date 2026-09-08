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
    public DbSet<ProductPriceHistory> ProductPriceHistory =>
        Set<ProductPriceHistory>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public override int SaveChanges()
    {
        EnsurePriceHistoryIsAppendOnly();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsurePriceHistoryIsAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsurePriceHistoryIsAppendOnly();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsurePriceHistoryIsAppendOnly();
        return base.SaveChangesAsync(
            acceptAllChangesOnSuccess,
            cancellationToken);
    }

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

        modelBuilder.Entity<ProductPriceHistory>()
            .HasQueryFilter(history =>
                _tenantContext.CompanyId != null &&
                history.CompanyId == _tenantContext.CompanyId);

        modelBuilder.Entity<OutboxMessage>()
            .HasQueryFilter(message =>
                _tenantContext.CompanyId != null &&
                message.CompanyId == _tenantContext.CompanyId);
    }

    private void EnsurePriceHistoryIsAppendOnly()
    {
        var attemptedMutation = ChangeTracker
            .Entries<ProductPriceHistory>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);

        if (attemptedMutation)
        {
            throw new InvalidOperationException(
                "Product price history is append-only and cannot be changed or deleted.");
        }
    }
}
