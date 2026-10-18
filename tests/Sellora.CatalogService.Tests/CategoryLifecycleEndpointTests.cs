using System.Net;
using System.Net.Http.Json;
using Sellora.CatalogService.Api.Contracts;
using Sellora.CatalogService.Application.Categories;
using Sellora.CatalogService.Application.Products;
using Xunit;

namespace Sellora.CatalogService.Tests;

public sealed class CategoryLifecycleEndpointTests(PostgreSqlConstraintFixture database) : IClassFixture<PostgreSqlConstraintFixture>
{
    [Fact]
    public async Task Categories_are_listed_by_status_and_retrieved_by_id()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var client = factory.Client(Guid.NewGuid().ToString());
        var beverages = await CreateCategory(client, "  Beverages  ", "  Drinks  ");
        var snacks = await CreateCategory(client, "Snacks", "   ");
        Assert.Equal("Beverages", beverages.Name);
        Assert.Equal("Drinks", beverages.Description);
        Assert.Null(snacks.Description);
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsync($"/api/categories/{snacks.CategoryId}/deactivate", null)).StatusCode);

        var active = await client.GetFromJsonAsync<CategoryResponse[]>("/api/categories");
        Assert.Equal(beverages.CategoryId, Assert.Single(active!).CategoryId);
        var inactive = await client.GetFromJsonAsync<CategoryResponse[]>("/api/categories?status=INACTIVE");
        Assert.Equal(snacks.CategoryId, Assert.Single(inactive!).CategoryId);
        var all = await client.GetFromJsonAsync<CategoryResponse[]>("/api/categories?status=%20all%20");
        Assert.Equal(new[] { "Beverages", "Snacks" }, all!.Select(category => category.Name));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/categories?status=unknown")).StatusCode);

        var found = await client.GetFromJsonAsync<CategoryResponse>($"/api/categories/{beverages.CategoryId}");
        Assert.Equal(beverages.CategoryId, found!.CategoryId);
        Assert.Equal("Beverages", found.Name);
        Assert.Equal("Active", found.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/categories/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Categories_are_tenant_scoped()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var client = factory.Client(Guid.NewGuid().ToString());
        using var otherTenant = factory.Client(Guid.NewGuid().ToString());
        var category = await CreateCategory(client, "Tenant scoped", null);

        Assert.Equal(HttpStatusCode.NotFound, (await otherTenant.GetAsync($"/api/categories/{category.CategoryId}")).StatusCode);
        Assert.Empty((await otherTenant.GetFromJsonAsync<CategoryResponse[]>("/api/categories?status=All"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await otherTenant.PutAsJsonAsync(
            $"/api/categories/{category.CategoryId}",
            new UpdateCategoryRequestBody("Renamed", null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherTenant.PatchAsync($"/api/categories/{category.CategoryId}/deactivate", null)).StatusCode);
    }

    [Fact]
    public async Task Update_changes_name_and_description()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var client = factory.Client(Guid.NewGuid().ToString());
        var category = await CreateCategory(client, "Original", "Old description");

        var response = await client.PutAsJsonAsync(
            $"/api/categories/{category.CategoryId}",
            new UpdateCategoryRequestBody(" Renamed ", " New description "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal("New description", updated.Description);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, 121)]
    [InlineData("Valid", 2001)]
    public async Task Invalid_category_requests_return_400(string? name, int? oversizedLength)
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var client = factory.Client(Guid.NewGuid().ToString());
        var category = await CreateCategory(client, "Existing", null);
        var (requestName, description) = oversizedLength switch
        {
            121 => (new string('n', 121), (string?)null),
            2001 => (name!, new string('d', 2001)),
            _ => (name!, null)
        };

        var create = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequestBody(requestName, description));
        var update = await client.PutAsJsonAsync($"/api/categories/{category.CategoryId}", new UpdateCategoryRequestBody(requestName, description));

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
    }

    [Fact]
    public async Task Deactivation_clears_product_assignments_and_rejects_repeat()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var client = factory.Client(Guid.NewGuid().ToString());
        var category = await CreateCategory(client, "Seasonal", null);
        var productResponse = await client.PostAsJsonAsync(
            "/api/products",
            new CreateProductRequest("SKU-SEASONAL", "Seasonal product", null, "Each", 10m, "BATCH-1", new(2026, 1, 1), new(2027, 1, 1), category.CategoryId));
        Assert.Equal(HttpStatusCode.Created, productResponse.StatusCode);
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductResponse>())!;
        Assert.Equal(category.CategoryId, product.CategoryId);

        var deactivate = await client.PatchAsync($"/api/categories/{category.CategoryId}/deactivate", null);

        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        Assert.Equal("Inactive", (await deactivate.Content.ReadFromJsonAsync<CategoryResponse>())!.Status);
        var reloaded = await client.GetFromJsonAsync<ProductResponse>($"/api/products/{product.ProductId}");
        Assert.Null(reloaded!.CategoryId);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsync($"/api/categories/{category.CategoryId}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PatchAsync($"/api/categories/{Guid.NewGuid()}/deactivate", null)).StatusCode);

        // An inactive category cannot be assigned to products.
        var assignInactive = await client.PostAsJsonAsync(
            "/api/products",
            new CreateProductRequest("SKU-SEASONAL-2", "Seasonal product", null, "Each", 10m, "BATCH-1", new(2026, 1, 1), new(2027, 1, 1), category.CategoryId));
        Assert.Equal(HttpStatusCode.BadRequest, assignInactive.StatusCode);
        var updateInactive = await client.PutAsJsonAsync(
            $"/api/products/{product.ProductId}",
            new { sku = "SKU-SEASONAL", name = "Seasonal product", unitOfMeasure = "Each", categoryId = category.CategoryId });
        Assert.Equal(HttpStatusCode.BadRequest, updateInactive.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task Missing_or_malformed_tenant_returns_401(string? tenant)
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var client = factory.Client(tenant);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/categories/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequestBody("Name", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync($"/api/categories/{id}", new UpdateCategoryRequestBody("Name", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PatchAsync($"/api/categories/{id}/deactivate", null)).StatusCode);
    }

    [Theory]
    [InlineData("AreaManager")]
    [InlineData("SalesRep")]
    public async Task Readers_can_list_but_cannot_write_categories(string role)
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var client = factory.Client(Guid.NewGuid().ToString(), role);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequestBody("Name", null))).StatusCode);
    }

    private static async Task<CategoryResponse> CreateCategory(HttpClient client, string name, string? description)
    {
        var response = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequestBody(name, description));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
    }
}
