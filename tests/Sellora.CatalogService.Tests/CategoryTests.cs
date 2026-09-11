using System.Net;
using System.Net.Http.Json;
using Sellora.CatalogService.Application.Categories;
using Sellora.CatalogService.Application.Common;
using Sellora.CatalogService.Application.Products;
using Xunit;

namespace Sellora.CatalogService.Tests;

/// <summary>
/// US-E2-4 QA — product categories, against a real PostgreSQL 16 database (Testcontainers).
///   CSP-215  Category filtering
///   CSP-216  Category deactivation keeps products intact
///   CSP-217  Category name uniqueness per company
/// Each test uses fresh company IDs, so tests never interfere with each other.
/// </summary>
public sealed class CategoryTests(PostgreSqlConstraintFixture database)
    : IClassFixture<PostgreSqlConstraintFixture>
{
    // ================= CSP-215 — Category filtering =================

    [Fact]
    public async Task CSP215_Filter_by_category_returns_exactly_the_assigned_products()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        var companyId = Guid.NewGuid().ToString();
        using var admin = factory.Client(companyId);
        using var rep = factory.Client(companyId, "SalesRep");

        var beverages = await CreateCategory(admin, "Beverages");
        var snacks = await CreateCategory(admin, "Snacks");
        var beverageIds = new[]
        {
            (await CreateProduct(admin, "BEV-1", beverages.CategoryId)).ProductId,
            (await CreateProduct(admin, "BEV-2", beverages.CategoryId)).ProductId,
            (await CreateProduct(admin, "BEV-3", beverages.CategoryId)).ProductId,
        };
        var snackIds = new[]
        {
            (await CreateProduct(admin, "SNK-1", snacks.CategoryId)).ProductId,
            (await CreateProduct(admin, "SNK-2", snacks.CategoryId)).ProductId,
        };
        await CreateProduct(admin, "NOCAT-1", null);

        // Admin product list
        var beveragePage = await admin.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"/api/products?status=All&pageSize=100&categoryId={beverages.CategoryId}");
        Assert.NotNull(beveragePage);
        Assert.Equal(3, beveragePage.TotalCount);
        Assert.True(beverageIds.ToHashSet().SetEquals(beveragePage.Items.Select(p => p.ProductId)));
        Assert.All(beveragePage.Items, p => Assert.Equal(beverages.CategoryId, p.CategoryId));

        var snackPage = await admin.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"/api/products?status=All&pageSize=100&categoryId={snacks.CategoryId}");
        Assert.NotNull(snackPage);
        Assert.True(snackIds.ToHashSet().SetEquals(snackPage.Items.Select(p => p.ProductId)));

        // Without a filter every product is listed
        var allPage = await admin.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            "/api/products?status=All&pageSize=100");
        Assert.NotNull(allPage);
        Assert.Equal(6, allPage.TotalCount);

        // Rep order-entry catalogue
        var repCatalogue = await rep.GetFromJsonAsync<List<OrderCatalogueProductResponse>>(
            $"/api/products/catalogue?categoryId={beverages.CategoryId}");
        Assert.NotNull(repCatalogue);
        Assert.True(beverageIds.ToHashSet().SetEquals(repCatalogue.Select(p => p.ProductId)));
    }

    [Fact]
    public async Task CSP215_Category_filter_and_categories_do_not_cross_companies()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var adminA = factory.Client(Guid.NewGuid().ToString());
        using var adminB = factory.Client(Guid.NewGuid().ToString());

        var beveragesA = await CreateCategory(adminA, "Beverages");
        await CreateProduct(adminA, "BEV-A1", beveragesA.CategoryId);

        var pageB = await adminB.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"/api/products?status=All&categoryId={beveragesA.CategoryId}");
        Assert.NotNull(pageB);
        Assert.Equal(0, pageB.TotalCount);

        Assert.Equal(HttpStatusCode.NotFound,
            (await adminB.GetAsync($"/api/categories/{beveragesA.CategoryId}")).StatusCode);

        var categoriesB = await adminB.GetFromJsonAsync<List<CategoryResponse>>("/api/categories?status=All");
        Assert.NotNull(categoriesB);
        Assert.Empty(categoriesB);

        // Company B cannot assign Company A's category
        var response = await adminB.PostAsJsonAsync("/api/products", ProductBody("BEV-B1", beveragesA.CategoryId));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CSP215_Product_category_can_be_changed_and_cleared()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var admin = factory.Client(Guid.NewGuid().ToString());
        var beverages = await CreateCategory(admin, "Beverages");
        var snacks = await CreateCategory(admin, "Snacks");
        var product = await CreateProduct(admin, "MOVE-1", beverages.CategoryId);

        var moved = await admin.PutAsJsonAsync($"/api/products/{product.ProductId}",
            new { sku = "MOVE-1", name = "Moved", description = "QA", unitOfMeasure = "Each", categoryId = snacks.CategoryId });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(snacks.CategoryId, (await moved.Content.ReadFromJsonAsync<ProductResponse>())!.CategoryId);

        var inBeverages = await admin.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"/api/products?status=All&categoryId={beverages.CategoryId}");
        Assert.Equal(0, inBeverages!.TotalCount);

        var cleared = await admin.PutAsJsonAsync($"/api/products/{product.ProductId}",
            new { sku = "MOVE-1", name = "Moved", description = "QA", unitOfMeasure = "Each", categoryId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null((await cleared.Content.ReadFromJsonAsync<ProductResponse>())!.CategoryId);
    }

    // ================= CSP-216 — Deactivation keeps products =================

    [Fact]
    public async Task CSP216_Deactivating_a_category_keeps_its_products_active_and_uncategorised()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        var companyId = Guid.NewGuid().ToString();
        using var admin = factory.Client(companyId);
        using var rep = factory.Client(companyId, "SalesRep");

        var seasonal = await CreateCategory(admin, "Seasonal");
        var snacks = await CreateCategory(admin, "Snacks");
        var seasonalProducts = new[]
        {
            await CreateProduct(admin, "SEA-1", seasonal.CategoryId, 110m),
            await CreateProduct(admin, "SEA-2", seasonal.CategoryId, 120m),
            await CreateProduct(admin, "SEA-3", seasonal.CategoryId, 130m),
        };
        var snackProduct = await CreateProduct(admin, "SNK-1", snacks.CategoryId);

        var deactivate = await admin.PatchAsync($"/api/categories/{seasonal.CategoryId}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        Assert.Equal("Inactive", (await deactivate.Content.ReadFromJsonAsync<CategoryResponse>())!.Status);

        foreach (var original in seasonalProducts)
        {
            var product = await admin.GetFromJsonAsync<ProductResponse>($"/api/products/{original.ProductId}");
            Assert.NotNull(product);
            Assert.Equal("Active", product.Status);
            Assert.Null(product.CategoryId);
            Assert.Equal(original.Name, product.Name);
            Assert.Equal(original.CurrentUnitPrice, product.CurrentUnitPrice);
        }

        // Still in the default (Active) list and in the rep catalogue
        var activeList = await admin.GetFromJsonAsync<PagedResponse<ProductResponse>>("/api/products?pageSize=100");
        Assert.All(seasonalProducts, p => Assert.Contains(activeList!.Items, i => i.ProductId == p.ProductId));
        var catalogue = await rep.GetFromJsonAsync<List<OrderCatalogueProductResponse>>("/api/products/catalogue");
        Assert.All(seasonalProducts, p => Assert.Contains(catalogue!, i => i.ProductId == p.ProductId));

        // Filtering by the deactivated category returns nothing
        var filtered = await admin.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"/api/products?status=All&categoryId={seasonal.CategoryId}");
        Assert.Equal(0, filtered!.TotalCount);

        // Other categories are untouched
        var snack = await admin.GetFromJsonAsync<ProductResponse>($"/api/products/{snackProduct.ProductId}");
        Assert.Equal(snacks.CategoryId, snack!.CategoryId);

        // Category stays retrievable as Inactive; active list hides it
        var category = await admin.GetFromJsonAsync<CategoryResponse>($"/api/categories/{seasonal.CategoryId}");
        Assert.Equal("Inactive", category!.Status);
        var activeCategories = await admin.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");
        Assert.DoesNotContain(activeCategories!, c => c.CategoryId == seasonal.CategoryId);
        var allCategories = await admin.GetFromJsonAsync<List<CategoryResponse>>("/api/categories?status=All");
        Assert.Contains(allCategories!, c => c.CategoryId == seasonal.CategoryId);

        // Second deactivation is rejected
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PatchAsync($"/api/categories/{seasonal.CategoryId}/deactivate", null)).StatusCode);
    }

    [Fact]
    public async Task CSP216_Inactive_category_cannot_be_assigned_to_products()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var admin = factory.Client(Guid.NewGuid().ToString());
        var retired = await CreateCategory(admin, "Retired");
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PatchAsync($"/api/categories/{retired.CategoryId}/deactivate", null)).StatusCode);

        var create = await admin.PostAsJsonAsync("/api/products", ProductBody("RET-1", retired.CategoryId));
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Contains("not found or is inactive", await create.Content.ReadAsStringAsync());

        var product = await CreateProduct(admin, "RET-2", null);
        var update = await admin.PutAsJsonAsync($"/api/products/{product.ProductId}",
            new { sku = "RET-2", name = "Retired test", description = "QA", unitOfMeasure = "Each", categoryId = retired.CategoryId });
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
    }

    // ================= CSP-217 — Name uniqueness per company =================

    [Fact]
    public async Task CSP217_Category_name_is_unique_within_a_company()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var admin = factory.Client(Guid.NewGuid().ToString());
        await CreateCategory(admin, "Beverages");

        var duplicate = await admin.PostAsJsonAsync("/api/categories", new { name = "Beverages", description = "dup" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("already exists", await duplicate.Content.ReadAsStringAsync());

        var padded = await admin.PostAsJsonAsync("/api/categories", new { name = "  Beverages  ", description = "dup" });
        Assert.Equal(HttpStatusCode.Conflict, padded.StatusCode);

        var snacks = await CreateCategory(admin, "Snacks");
        var rename = await admin.PutAsJsonAsync($"/api/categories/{snacks.CategoryId}",
            new { name = "Beverages", description = "rename to existing" });
        Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);

        var unchanged = await admin.GetFromJsonAsync<CategoryResponse>($"/api/categories/{snacks.CategoryId}");
        Assert.Equal("Snacks", unchanged!.Name);
    }

    [Fact]
    public async Task CSP217_Same_category_name_is_allowed_in_another_company()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var adminA = factory.Client(Guid.NewGuid().ToString());
        using var adminB = factory.Client(Guid.NewGuid().ToString());

        await CreateCategory(adminA, "Beverages");
        var inB = await adminB.PostAsJsonAsync("/api/categories", new { name = "Beverages", description = "B" });
        Assert.Equal(HttpStatusCode.Created, inB.StatusCode);
    }

    [Fact]
    public async Task CSP217_Category_validation_and_role_restrictions()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        var companyId = Guid.NewGuid().ToString();
        using var admin = factory.Client(companyId);
        using var rep = factory.Client(companyId, "SalesRep");

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/categories", new { name = "   ", description = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/categories", new { name = new string('x', 121), description = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/categories?status=Deleted")).StatusCode);

        var category = await CreateCategory(admin, "Beverages");
        Assert.Equal(HttpStatusCode.OK, (await rep.GetAsync("/api/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await rep.PostAsJsonAsync("/api/categories", new { name = "Rep category", description = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await rep.PutAsJsonAsync($"/api/categories/{category.CategoryId}", new { name = "Renamed", description = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await rep.PatchAsync($"/api/categories/{category.CategoryId}/deactivate", null)).StatusCode);
    }

    // ================= helpers =================

    private static object ProductBody(string sku, Guid? categoryId, decimal price = 100m) => new
    {
        sku,
        name = $"QA {sku}",
        description = "QA category test",
        unitOfMeasure = "Each",
        currentUnitPrice = price,
        batchCode = $"{sku}-B1",
        manufacturingDate = "2026-01-01",
        expiryDate = "2027-12-31",
        categoryId,
    };

    private static async Task<ProductResponse> CreateProduct(HttpClient client, string sku, Guid? categoryId, decimal price = 100m)
    {
        var response = await client.PostAsJsonAsync("/api/products", ProductBody(sku, categoryId, price));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static async Task<CategoryResponse> CreateCategory(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/categories", new { name, description = $"QA {name}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
    }
}
