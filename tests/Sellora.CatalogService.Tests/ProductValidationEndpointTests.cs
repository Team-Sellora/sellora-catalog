using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Sellora.CatalogService.Api.Contracts;
using Sellora.CatalogService.Application.Common;
using Sellora.CatalogService.Application.Products;
using Xunit;

namespace Sellora.CatalogService.Tests;

public sealed class ProductValidationEndpointTests(PostgreSqlConstraintFixture database) : IClassFixture<PostgreSqlConstraintFixture>
{
    private static readonly string TooLong80 = new('x', 81);
    private static readonly string TooLong200 = new('x', 201);
    private static readonly string TooLong40 = new('x', 41);

    public static TheoryData<CreateProductRequest> InvalidCreateRequests() => new()
    {
        Valid() with { Sku = " " },
        Valid() with { Sku = TooLong80 },
        Valid() with { Name = "" },
        Valid() with { Name = TooLong200 },
        Valid() with { UnitOfMeasure = " " },
        Valid() with { UnitOfMeasure = TooLong40 },
        Valid() with { BatchCode = "" },
        Valid() with { BatchCode = TooLong80 },
        Valid() with { ManufacturingDate = default },
        Valid() with { ExpiryDate = default },
        Valid() with { ExpiryDate = new(2026, 1, 1) },
        Valid() with { CategoryId = Guid.NewGuid() }
    };

    public static TheoryData<UpdateProductRequest> InvalidUpdateRequests() => new()
    {
        new("", "Name", null, "Each"),
        new(TooLong80, "Name", null, "Each"),
        new("SKU", " ", null, "Each"),
        new("SKU", TooLong200, null, "Each"),
        new("SKU", "Name", null, ""),
        new("SKU", "Name", null, TooLong40)
    };

    [Theory]
    [MemberData(nameof(InvalidCreateRequests))]
    public async Task Invalid_create_requests_return_400_without_writing(CreateProductRequest request)
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var client = factory.Client(Guid.NewGuid().ToString());

        var response = await client.PostAsJsonAsync("/api/products", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var products = await client.GetFromJsonAsync<PagedResponse<ProductResponse>>("/api/products?status=All");
        Assert.Empty(products!.Items);
    }

    [Theory]
    [MemberData(nameof(InvalidUpdateRequests))]
    public async Task Invalid_update_requests_return_400(UpdateProductRequest request)
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var client = factory.Client(Guid.NewGuid().ToString());
        var product = await Create(client, Valid());

        var response = await client.PutAsJsonAsync($"/api/products/{product.ProductId}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_returns_404_for_unknown_product_and_409_for_duplicate_sku()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var client = factory.Client(Guid.NewGuid().ToString());
        await Create(client, Valid() with { Sku = "SKU-TAKEN" });
        var product = await Create(client, Valid() with { Sku = "SKU-OTHER" });

        var missing = await client.PutAsJsonAsync($"/api/products/{Guid.NewGuid()}", new UpdateProductRequest("SKU-NEW", "Name", null, "Each"));
        var duplicate = await client.PutAsJsonAsync($"/api/products/{product.ProductId}", new UpdateProductRequest("sku-taken", "Name", "Description", "Each"));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Price_change_validation_and_missing_product_are_rejected()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var client = factory.Client(Guid.NewGuid().ToString());
        var product = await Create(client, Valid());
        var future = DateTimeOffset.UtcNow.AddMinutes(5);

        var tooLongReason = await client.PutAsJsonAsync(
            $"/api/products/{product.ProductId}/price",
            new ChangeProductPriceRequestBody(20m, new string('r', 501), future));
        var missingEffectiveDate = await client.PutAsJsonAsync(
            $"/api/products/{product.ProductId}/price",
            new ChangeProductPriceRequestBody(20m, "Reason", default));
        var missingProduct = await client.PutAsJsonAsync(
            $"/api/products/{Guid.NewGuid()}/price",
            new ChangeProductPriceRequestBody(20m, "Reason", future));

        Assert.Equal(HttpStatusCode.BadRequest, tooLongReason.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingEffectiveDate.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingProduct.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/products/{Guid.NewGuid()}/price-history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PatchAsync($"/api/products/{Guid.NewGuid()}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/products/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Products_can_be_filtered_by_category()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var client = factory.Client(Guid.NewGuid().ToString());
        var categoryResponse = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequestBody("Filter", null));
        var category = (await categoryResponse.Content.ReadFromJsonAsync<Application.Categories.CategoryResponse>())!;
        var inCategory = await Create(client, Valid() with { Sku = "SKU-IN", CategoryId = category.CategoryId });
        await Create(client, Valid() with { Sku = "SKU-OUT" });

        var filtered = await client.GetFromJsonAsync<PagedResponse<ProductResponse>>($"/api/products?categoryId={category.CategoryId}");

        Assert.Equal(inCategory.ProductId, Assert.Single(filtered!.Items).ProductId);
    }

    [Fact]
    public async Task Internal_resolve_rejects_invalid_requests()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var client = CreateInternalClient(factory, CatalogApiFactory.TestInternalApiKey);
        const string url = "/internal/catalog/products/resolve";

        var emptyCompany = await client.PostAsJsonAsync(url, new ResolveProductsRequestBody(Guid.Empty, new[] { Guid.NewGuid() }));
        var noProducts = await client.PostAsJsonAsync(url, new ResolveProductsRequestBody(Guid.NewGuid(), Array.Empty<Guid>()));
        var tooMany = await client.PostAsJsonAsync(url, new ResolveProductsRequestBody(Guid.NewGuid(), Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray()));
        var emptyProductId = await client.PostAsJsonAsync(url, new ResolveProductsRequestBody(Guid.NewGuid(), new[] { Guid.Empty }));

        Assert.Equal(HttpStatusCode.BadRequest, emptyCompany.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noProducts.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, emptyProductId.StatusCode);
    }

    [Fact]
    public async Task Internal_api_returns_503_when_no_key_is_configured()
    {
        using var baseFactory = new CatalogApiFactory(database.ConnectionString);
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseSetting("InternalApi:ApiKey", ""));
        using var client = CreateInternalClient(factory, "any-key");

        var response = await client.PostAsJsonAsync(
            "/internal/catalog/products/resolve",
            new ResolveProductsRequestBody(Guid.NewGuid(), new[] { Guid.NewGuid() }));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("not configured", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Catalogue_requires_a_company_identifier()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var client = factory.Client(null, "SalesRep");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/products/catalogue")).StatusCode);
    }

    private static CreateProductRequest Valid() =>
        new("SKU-VALID", "Valid product", "Description", "Each", 10m, "BATCH-1", new(2026, 1, 1), new(2027, 1, 1));

    private static async Task<ProductResponse> Create(HttpClient client, CreateProductRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/products", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static HttpClient CreateInternalClient<TEntryPoint>(WebApplicationFactory<TEntryPoint> factory, string apiKey)
        where TEntryPoint : class
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Internal-Api-Key", apiKey);
        return client;
    }
}
