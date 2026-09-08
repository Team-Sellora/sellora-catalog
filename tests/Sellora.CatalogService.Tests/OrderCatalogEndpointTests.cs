using System.Net;
using System.Net.Http.Json;
using Sellora.CatalogService.Api.Contracts;
using Sellora.CatalogService.Application.Products;
using Xunit;

namespace Sellora.CatalogService.Tests;

public sealed class OrderCatalogEndpointTests(
    PostgreSqlConstraintFixture database)
    : IClassFixture<PostgreSqlConstraintFixture>
{
    [Fact]
    public async Task Batch_resolution_returns_prices_and_unavailable_markers()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        using var factory =
            new CatalogApiFactory(database.ConnectionString);

        using var companyAdmin =
            factory.Client(companyId.ToString());

        using var otherCompanyAdmin =
            factory.Client(otherCompanyId.ToString());

        var availableProduct = await CreateProduct(
            companyAdmin,
            "ORDER-AVAILABLE",
            25m);

        var inactiveProduct = await CreateProduct(
            companyAdmin,
            "ORDER-INACTIVE",
            30m);

        var otherCompanyProduct = await CreateProduct(
            otherCompanyAdmin,
            "ORDER-OTHER-COMPANY",
            40m);

        var deactivateResponse = await companyAdmin.PatchAsync(
            $"/api/products/{inactiveProduct.ProductId}/deactivate",
            null);

        Assert.Equal(
            HttpStatusCode.OK,
            deactivateResponse.StatusCode);

        var missingProductId = Guid.NewGuid();

        using var internalClient =
            CreateInternalClient(factory);

        var response = await internalClient.PostAsJsonAsync(
            "/internal/catalog/products/resolve",
            new ResolveProductsRequestBody(
                companyId,
                new[]
                {
                    availableProduct.ProductId,
                    inactiveProduct.ProductId,
                    otherCompanyProduct.ProductId,
                    missingProductId
                }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result =
            await response.Content.ReadFromJsonAsync<
                ProductResolutionResponse>();

        Assert.NotNull(result);
        Assert.Equal(4, result.Items.Count);

        var available = Assert.Single(
            result.Items,
            item =>
                item.ProductId ==
                availableProduct.ProductId);

        Assert.True(available.IsAvailable);
        Assert.Equal(25m, available.CurrentUnitPrice);
        Assert.Null(available.UnavailableReason);

        var inactive = Assert.Single(
            result.Items,
            item =>
                item.ProductId ==
                inactiveProduct.ProductId);

        Assert.False(inactive.IsAvailable);
        Assert.Equal(
            "ProductInactive",
            inactive.UnavailableReason);

        var crossTenant = Assert.Single(
            result.Items,
            item =>
                item.ProductId ==
                otherCompanyProduct.ProductId);

        Assert.False(crossTenant.IsAvailable);
        Assert.Equal(
            "ProductNotFound",
            crossTenant.UnavailableReason);

        var missing = Assert.Single(
            result.Items,
            item => item.ProductId == missingProductId);

        Assert.False(missing.IsAvailable);
        Assert.Equal(
            "ProductNotFound",
            missing.UnavailableReason);
    }

    [Fact]
    public async Task Internal_endpoint_requires_correct_api_key()
    {
        using var factory =
            new CatalogApiFactory(database.ConnectionString);

        var request = new ResolveProductsRequestBody(
            Guid.NewGuid(),
            new[] { Guid.NewGuid() });

        using var noKeyClient =
            CreateInternalClient(factory, null);

        var noKeyResponse = await noKeyClient.PostAsJsonAsync(
            "/internal/catalog/products/resolve",
            request);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            noKeyResponse.StatusCode);

        using var wrongKeyClient =
            CreateInternalClient(factory, "wrong-key");

        var wrongKeyResponse =
            await wrongKeyClient.PostAsJsonAsync(
                "/internal/catalog/products/resolve",
                request);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            wrongKeyResponse.StatusCode);

        using var correctKeyClient =
            CreateInternalClient(factory);

        var correctKeyResponse =
            await correctKeyClient.PostAsJsonAsync(
                "/internal/catalog/products/resolve",
                request);

        Assert.Equal(
            HttpStatusCode.OK,
            correctKeyResponse.StatusCode);
    }

    [Fact]
    public async Task Rep_catalogue_returns_only_active_unexpired_company_products()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using var factory =
            new CatalogApiFactory(database.ConnectionString);

        using var companyAdmin =
            factory.Client(companyId.ToString());

        using var otherCompanyAdmin =
            factory.Client(otherCompanyId.ToString());

        var availableProduct = await CreateProduct(
            companyAdmin,
            "CATALOGUE-AVAILABLE",
            20m);

        var inactiveProduct = await CreateProduct(
            companyAdmin,
            "CATALOGUE-INACTIVE",
            30m);

        await CreateProduct(
            companyAdmin,
            "CATALOGUE-EXPIRED",
            40m,
            today.AddYears(-2),
            today.AddDays(-1));

        await CreateProduct(
            otherCompanyAdmin,
            "CATALOGUE-OTHER-COMPANY",
            50m);

        var deactivateResponse =
            await companyAdmin.PatchAsync(
                $"/api/products/{inactiveProduct.ProductId}/deactivate",
                null);

        Assert.Equal(
            HttpStatusCode.OK,
            deactivateResponse.StatusCode);

        using var salesRep = factory.Client(
            companyId.ToString(),
            "SalesRep");

        var response = await salesRep.GetAsync(
            "/api/products/catalogue?search=CATALOGUE");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var products =
            await response.Content.ReadFromJsonAsync<
                OrderCatalogueProductResponse[]>();

        Assert.NotNull(products);

        var product = Assert.Single(products);

        Assert.Equal(
            availableProduct.ProductId,
            product.ProductId);

        Assert.Equal(
            availableProduct.CurrentUnitPrice,
            product.CurrentUnitPrice);

        Assert.True(
            product.EarliestExpiryDate >= today);
    }

    private static HttpClient CreateInternalClient(
        CatalogApiFactory factory,
        string? apiKey =
            CatalogApiFactory.TestInternalApiKey)
    {
        var client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing
                .WebApplicationFactoryClientOptions
            {
                BaseAddress =
                    new Uri("https://localhost")
            });

        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add(
                "X-Internal-Api-Key",
                apiKey);
        }

        return client;
    }

    private static async Task<ProductResponse> CreateProduct(
     HttpClient client,
     string sku,
     decimal price,
     DateOnly? manufacturingDate = null,
     DateOnly? expiryDate = null)
    {
        var today =
            DateOnly.FromDateTime(DateTime.UtcNow);

        var response = await client.PostAsJsonAsync(
            "/api/products",
            new CreateProductRequestBody(
                sku,
                $"Product {sku}",
                "Order catalogue test product",
                "Each",
                price,
                $"BATCH-{sku}",
                manufacturingDate ?? today.AddDays(-1),
                expiryDate ?? today.AddYears(1)));

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        return (await response.Content
            .ReadFromJsonAsync<ProductResponse>())!;
    }

    [Fact]
    public async Task Price_change_invalidates_cache_before_next_order_lookup()
    {
        var companyId = Guid.NewGuid();

        using var factory =
            new CatalogApiFactory(database.ConnectionString);

        using var companyAdmin =
            factory.Client(companyId.ToString());

        using var internalClient =
            CreateInternalClient(factory);

        var product = await CreateProduct(
            companyAdmin,
            "CACHE-PRICE",
            25m);

        var resolveRequest = new ResolveProductsRequestBody(
            companyId,
            new[] { product.ProductId });

        var firstResponse =
            await internalClient.PostAsJsonAsync(
                "/internal/catalog/products/resolve",
                resolveRequest);

        Assert.Equal(
            HttpStatusCode.OK,
            firstResponse.StatusCode);

        var firstResult =
            await firstResponse.Content.ReadFromJsonAsync<
                ProductResolutionResponse>();

        var firstProduct = Assert.Single(
            firstResult!.Items);

        Assert.Equal(
            25m,
            firstProduct.CurrentUnitPrice);

        var priceChangeResponse =
            await companyAdmin.PutAsJsonAsync(
                $"/api/products/{product.ProductId}/price",
                new ChangeProductPriceRequestBody(
                    35m,
                    "Cache invalidation test",
                    DateTimeOffset.UtcNow.AddMinutes(5)));

        Assert.Equal(
            HttpStatusCode.OK,
            priceChangeResponse.StatusCode);

        var secondResponse =
            await internalClient.PostAsJsonAsync(
                "/internal/catalog/products/resolve",
                resolveRequest);

        Assert.Equal(
            HttpStatusCode.OK,
            secondResponse.StatusCode);

        var secondResult =
            await secondResponse.Content.ReadFromJsonAsync<
                ProductResolutionResponse>();

        var refreshedProduct = Assert.Single(
            secondResult!.Items);

        Assert.Equal(
            35m,
            refreshedProduct.CurrentUnitPrice);

        Assert.True(refreshedProduct.IsAvailable);
    }
}