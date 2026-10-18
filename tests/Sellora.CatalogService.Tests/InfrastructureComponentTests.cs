using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sellora.CatalogService.Application.Outbox;
using Sellora.CatalogService.Application.Products;
using Sellora.CatalogService.Domain.Entities;
using Sellora.CatalogService.Domain.Products;
using Sellora.CatalogService.Infrastructure.Outbox;
using Sellora.CatalogService.Infrastructure.Persistence.Seeding;
using Sellora.CatalogService.Infrastructure.Products;
using Xunit;

namespace Sellora.CatalogService.Tests;

public sealed class InfrastructureComponentTests(PostgreSqlConstraintFixture database) : IClassFixture<PostgreSqlConstraintFixture>
{
    [Fact]
    public async Task Staging_seed_creates_the_demo_catalogue_once()
    {
        await using (var db = database.CreateDbContext())
        {
            await DevelopmentCatalogSeeder.SeedAsync(db);
            await DevelopmentCatalogSeeder.SeedAsync(db);
        }

        await using var tenantDb = database.CreateDbContext(DevelopmentCatalogSeedIds.CompanyId);
        var products = await tenantDb.Products.Include(product => product.Batches).ToListAsync();
        Assert.Equal(5, products.Count);
        Assert.All(products, product =>
        {
            Assert.Equal(ProductStatus.Active, product.Status);
            Assert.Equal(product.ProductId.ToString()[8..], Assert.Single(product.Batches).BatchId.ToString()[8..]);
        });
        Assert.Contains(products, product => product.Sku == "DEMO-TEA-500" && product.CurrentUnitPrice == 1_250m);
    }

    [Fact]
    public async Task Synchronous_saves_also_keep_price_history_append_only()
    {
        var companyId = Guid.NewGuid();
        await using var db = database.CreateDbContext(companyId);
        var product = new Product
        {
            ProductId = Guid.NewGuid(),
            CompanyId = companyId,
            Sku = "SYNC-SAVE",
            Name = "Sync save",
            UnitOfMeasure = "Each",
            CurrentUnitPrice = 10m,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var history = new ProductPriceHistory
        {
            PriceHistoryId = Guid.NewGuid(),
            CompanyId = companyId,
            ProductId = product.ProductId,
            OldUnitPrice = 10m,
            NewUnitPrice = 12m,
            ChangedBy = "tester",
            Reason = "Initial",
            ChangedAt = DateTimeOffset.UtcNow,
            EffectiveFrom = DateTimeOffset.UtcNow
        };
        db.Products.Add(product);
        db.ProductPriceHistory.Add(history);
        Assert.Equal(2, db.SaveChanges());

        history.Reason = "Changed";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges(acceptAllChangesOnSuccess: true));
    }

    [Fact]
    public async Task Price_cache_entries_expire_and_can_be_removed()
    {
        var cache = new ProductPriceCache(Options.Create(new ProductPriceCacheOptions { TtlSeconds = 0 }));
        var companyId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var product = new ProductForOrderResponse(productId, "Name", "SKU", "Each", 1m, "Active", null, true, null);

        cache.Set(companyId, productId, product);
        Assert.True(cache.TryGet(companyId, productId, out var cached));
        Assert.Same(product, cached);
        Assert.False(cache.TryGet(Guid.NewGuid(), productId, out _));

        cache.Remove(companyId, productId);
        Assert.False(cache.TryGet(companyId, productId, out var removed));
        Assert.Null(removed);

        // A TTL below the minimum is clamped to one second.
        cache.Set(companyId, productId, product);
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        Assert.False(cache.TryGet(companyId, productId, out _));
    }

    [Fact]
    public async Task Kafka_publisher_reports_delivery_failures_to_the_relay()
    {
        // Nothing listens on this port, so delivery times out and the relay retries.
        using var publisher = new KafkaEventPublisher(Options.Create(new KafkaOptions
        {
            BootstrapServers = "127.0.0.1:1",
            CatalogTopic = "sellora.catalog.tests",
            MessageTimeoutMs = 500
        }));

        var message = new OutboxMessageToPublish(
            Guid.NewGuid(), "PriceChanged", "1.0", Guid.NewGuid(), Guid.NewGuid(), "{}", DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<ProduceException<string, string>>(() => publisher.PublishAsync(message));
    }
}
