using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Sellora.CatalogService.Application.Products;

namespace Sellora.CatalogService.Infrastructure.Products;

public sealed class ProductPriceCacheOptions
{
    public const string SectionName = "ProductPriceCache";

    public int TtlSeconds { get; set; } = 30;
}

public sealed class ProductPriceCache : IProductPriceCache
{
    private readonly ConcurrentDictionary<CacheKey, CacheEntry> _entries = new();
    private readonly TimeSpan _timeToLive;

    public ProductPriceCache(IOptions<ProductPriceCacheOptions> options)
    {
        var ttlSeconds = Math.Clamp(
            options.Value.TtlSeconds,
            1,
            300);

        _timeToLive = TimeSpan.FromSeconds(ttlSeconds);
    }

    public bool TryGet(
        Guid companyId,
        Guid productId,
        out ProductForOrderResponse? product)
    {
        var key = new CacheKey(companyId, productId);

        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > DateTimeOffset.UtcNow)
            {
                product = entry.Product;
                return true;
            }

            _entries.TryRemove(key, out _);
        }

        product = null;
        return false;
    }

    public void Set(
        Guid companyId,
        Guid productId,
        ProductForOrderResponse product)
    {
        var key = new CacheKey(companyId, productId);
        var entry = new CacheEntry(
            product,
            DateTimeOffset.UtcNow.Add(_timeToLive));

        _entries[key] = entry;
    }

    public void Remove(Guid companyId, Guid productId)
    {
        _entries.TryRemove(
            new CacheKey(companyId, productId),
            out _);
    }

    private readonly record struct CacheKey(
        Guid CompanyId,
        Guid ProductId);

    private sealed record CacheEntry(
        ProductForOrderResponse Product,
        DateTimeOffset ExpiresAt);
}