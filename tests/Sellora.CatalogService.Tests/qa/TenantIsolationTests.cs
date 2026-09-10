using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Sellora.CatalogService.Application.Common;
using Sellora.CatalogService.Application.Products;
using Xunit;

namespace Sellora.CatalogService.Tests;

/// <summary>
/// CSP-144 (US-E2-1-T7) — tenant isolation proven with two-company seed data
/// against a real PostgreSQL 16 database (Testcontainers), not a mock.
/// Each test seeds fresh company IDs, so tests never interfere with each other.
/// </summary>
public sealed class TenantIsolationTests(PostgreSqlConstraintFixture database)
    : IClassFixture<PostgreSqlConstraintFixture>
{
    private sealed record SeedProduct(string Sku, string Name, decimal Price);

    // BEV-500 exists in BOTH companies with different prices on purpose:
    // it proves SKU uniqueness is per company and that prices never cross over.
    private static readonly SeedProduct[] CompanyACatalogue =
    {
        new("BEV-500", "Company A Cola 500ml", 250.00m),
        new("CMPA-TEA-100", "Company A Tea 100g", 480.00m),
        new("CMPA-BIS-200", "Company A Biscuits 200g", 120.00m),
    };

    private static readonly SeedProduct[] CompanyBCatalogue =
    {
        new("BEV-500", "Company B Cola 500ml", 275.00m),
        new("CMPB-SOD-330", "Company B Soda 330ml", 150.00m),
        new("CMPB-NOD-400", "Company B Noodles 400g", 320.00m),
    };

    private sealed record TwoCompanySeed(
        Guid CompanyA,
        Guid CompanyB,
        IReadOnlyList<ProductResponse> AProducts,
        IReadOnlyList<ProductResponse> BProducts);

    // ---------- IT-01 ----------
    [Fact]
    public async Task IT01_Company_A_list_contains_zero_company_B_products()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        var seed = await SeedTwoCompaniesAsync(factory);
        using var adminA = factory.Client(seed.CompanyA.ToString());

        var page = await adminA.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            "/api/products?status=All&pageSize=100");

        Assert.NotNull(page);
        Assert.Equal(CompanyACatalogue.Length, page.TotalCount);

        var expectedIds = seed.AProducts.Select(p => p.ProductId).ToHashSet();
        Assert.True(expectedIds.SetEquals(page.Items.Select(p => p.ProductId)));

        var companyBIds = seed.BProducts.Select(p => p.ProductId).ToHashSet();
        Assert.DoesNotContain(page.Items, p => companyBIds.Contains(p.ProductId));
        Assert.DoesNotContain(page.Items, p => p.Name.StartsWith("Company B"));

        // The shared SKU must resolve to Company A's row and price, never B's.
        var shared = Assert.Single(page.Items, p => p.Sku == "BEV-500");
        Assert.Equal(250.00m, shared.CurrentUnitPrice);
    }

    // ---------- IT-02 ----------
    [Theory]
    [InlineData("Company B")]
    [InlineData("CMPB")]
    [InlineData("Soda")]
    public async Task IT02_Company_A_search_never_finds_company_B_products(string search)
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        var seed = await SeedTwoCompaniesAsync(factory);
        using var adminA = factory.Client(seed.CompanyA.ToString());

        var page = await adminA.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"/api/products?status=All&search={Uri.EscapeDataString(search)}");

        Assert.NotNull(page);
        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
    }

    // ---------- IT-03 ----------
    [Fact]
    public async Task IT03_Company_A_get_by_id_of_company_B_product_returns_404_without_data()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        var seed = await SeedTwoCompaniesAsync(factory);
        using var adminA = factory.Client(seed.CompanyA.ToString());
        using var adminB = factory.Client(seed.CompanyB.ToString());

        foreach (var bProduct in seed.BProducts)
        {
            var response = await adminA.GetAsync($"/api/products/{bProduct.ProductId}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(bProduct.Name, body);
            Assert.DoesNotContain(bProduct.Sku, body);

            // Sanity check: the product really exists — its owner can read it.
            var ownerResponse = await adminB.GetAsync($"/api/products/{bProduct.ProductId}");
            Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        }
    }

    // ---------- IT-04 ----------
    [Theory]
    [InlineData("companyId")]
    [InlineData("CompanyId")]
    [InlineData("company_id")]
    [InlineData("tenantId")]
    public async Task IT04_Company_id_query_parameter_is_ignored(string parameterName)
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        var seed = await SeedTwoCompaniesAsync(factory);
        using var adminA = factory.Client(seed.CompanyA.ToString());
        var injected = $"{parameterName}={seed.CompanyB}";

        var page = await adminA.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"/api/products?status=All&pageSize=100&{injected}");

        Assert.NotNull(page);
        var expectedIds = seed.AProducts.Select(p => p.ProductId).ToHashSet();
        Assert.True(expectedIds.SetEquals(page.Items.Select(p => p.ProductId)));

        var byId = await adminA.GetAsync(
            $"/api/products/{seed.BProducts[0].ProductId}?{injected}");
        Assert.Equal(HttpStatusCode.NotFound, byId.StatusCode);
    }

    // ---------- IT-05 ----------
    [Fact]
    public async Task IT05_Spoofed_tenant_headers_are_ignored()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        var seed = await SeedTwoCompaniesAsync(factory);
        using var adminA = factory.Client(seed.CompanyA.ToString());
        adminA.DefaultRequestHeaders.Add("X-Company-Id", seed.CompanyB.ToString());
        adminA.DefaultRequestHeaders.Add("X-Tenant-Id", seed.CompanyB.ToString());
        adminA.DefaultRequestHeaders.Add("companyId", seed.CompanyB.ToString());

        var page = await adminA.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            "/api/products?status=All&pageSize=100");

        Assert.NotNull(page);
        Assert.Equal(CompanyACatalogue.Length, page.TotalCount);
        Assert.DoesNotContain(page.Items, p => p.Name.StartsWith("Company B"));
    }

    // ---------- IT-06 ----------
    [Fact]
    public async Task IT06_Company_id_in_create_body_is_ignored()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        var seed = await SeedTwoCompaniesAsync(factory);
        using var adminA = factory.Client(seed.CompanyA.ToString());
        using var adminB = factory.Client(seed.CompanyB.ToString());

        // Mass-assignment attempt: Company A tries to create a product "for" Company B.
        var response = await adminA.PostAsJsonAsync("/api/products", new
        {
            companyId = seed.CompanyB,
            sku = "CMPA-INJ-001",
            name = "Company A Injection Attempt",
            description = "companyId in body must be ignored",
            unitOfMeasure = "Each",
            currentUnitPrice = 99.00m,
            batchCode = "CMPA-INJ-B1",
            manufacturingDate = "2026-01-01",
            expiryDate = "2027-12-31",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ProductResponse>())!;

        // It belongs to A (the token's tenant), so B cannot see it.
        Assert.Equal(HttpStatusCode.OK,
            (await adminA.GetAsync($"/api/products/{created.ProductId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await adminB.GetAsync($"/api/products/{created.ProductId}")).StatusCode);
    }

    // ---------- IT-07 ----------
    [Fact]
    public async Task IT07_Company_A_cannot_update_or_deactivate_company_B_product()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        var seed = await SeedTwoCompaniesAsync(factory);
        using var adminA = factory.Client(seed.CompanyA.ToString());
        using var adminB = factory.Client(seed.CompanyB.ToString());
        var target = seed.BProducts[0];

        var update = await adminA.PutAsJsonAsync(
            $"/api/products/{target.ProductId}",
            new UpdateProductRequest("HACKED", "Hacked by A", null, "Each"));
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        var deactivate = await adminA.PatchAsync(
            $"/api/products/{target.ProductId}/deactivate", null);
        Assert.Equal(HttpStatusCode.NotFound, deactivate.StatusCode);

        // Company B's product is untouched.
        var after = await adminB.GetFromJsonAsync<ProductResponse>(
            $"/api/products/{target.ProductId}");
        Assert.NotNull(after);
        Assert.Equal(target.Sku, after.Sku);
        Assert.Equal(target.Name, after.Name);
        Assert.Equal("Active", after.Status);
        Assert.Equal(target.CurrentUnitPrice, after.CurrentUnitPrice);
    }

    // ---------- IT-08 ----------
    [Fact]
    public async Task IT08_Tenant_filter_is_enforced_in_the_database_query()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        var seed = await SeedTwoCompaniesAsync(factory);

        await using var dbA = database.CreateDbContext(seed.CompanyA);

        // 1. Both tenants' rows really are in the same table.
        var rowsForBothCompanies = await dbA.Products
            .IgnoreQueryFilters()
            .CountAsync(p => p.CompanyId == seed.CompanyA || p.CompanyId == seed.CompanyB);
        Assert.Equal(CompanyACatalogue.Length + CompanyBCatalogue.Length, rowsForBothCompanies);

        // 2. With the filter on, Company A's context sees only Company A.
        var visibleCompanies = await dbA.Products
            .Select(p => p.CompanyId)
            .Distinct()
            .ToListAsync();
        Assert.Equal(new[] { seed.CompanyA }, visibleCompanies);

        // 3. Even an explicit WHERE for Company B returns nothing.
        Assert.Equal(0, await dbA.Products.CountAsync(p => p.CompanyId == seed.CompanyB));

        // 4. The predicate is inside the generated SQL, i.e. PostgreSQL filters the rows.
        var sql = dbA.Products.ToQueryString();
        Assert.Contains("company_id", sql);

        // 5. Fail closed: no tenant in the context means no rows at all.
        await using var dbNoTenant = database.CreateDbContext(null);
        Assert.Equal(0, await dbNoTenant.Products.CountAsync(
            p => p.CompanyId == seed.CompanyA || p.CompanyId == seed.CompanyB));
    }

    // ---------- seed helpers ----------
    private static async Task<TwoCompanySeed> SeedTwoCompaniesAsync(CatalogApiFactory factory)
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        using var adminA = factory.Client(companyA.ToString());
        using var adminB = factory.Client(companyB.ToString());

        var aProducts = new List<ProductResponse>();
        foreach (var product in CompanyACatalogue)
            aProducts.Add(await CreateAsync(adminA, product));

        var bProducts = new List<ProductResponse>();
        foreach (var product in CompanyBCatalogue)
            bProducts.Add(await CreateAsync(adminB, product));

        return new TwoCompanySeed(companyA, companyB, aProducts, bProducts);
    }

    private static async Task<ProductResponse> CreateAsync(HttpClient client, SeedProduct product)
    {
        var request = new CreateProductRequest(
            product.Sku,
            product.Name,
            "QA tenant isolation seed",
            "Each",
            product.Price,
            $"{product.Sku}-B1",
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 12, 31));

        var response = await client.PostAsJsonAsync("/api/products", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }
}
