using System.Net;
using System.Net.Http.Json;
using Sellora.CatalogService.Application.Common;
using Sellora.CatalogService.Application.Products;
using Xunit;

namespace Sellora.CatalogService.Tests;

public sealed class ProductEndpointTests
{
    private static CreateProductRequest Request(string sku = "SKU-1", decimal price = 12.34m) =>
        new(sku, "Test product", "Description", "Each", price, "BATCH-1", new(2026, 1, 1), new(2027, 1, 1));

    private static async Task<ProductResponse> Create(HttpClient client, string sku = "SKU-1")
    {
        var response = await client.PostAsJsonAsync("/api/products", Request(sku));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    [Fact]
    public async Task Deactivation_hides_default_list_but_preserves_history_and_filtered_paging()
    {
        using var factory = new CatalogApiFactory();
        using var client = factory.Client(Guid.NewGuid().ToString());
        var first = await Create(client);
        await Create(client, "SKU-2");
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsync($"/api/products/{first.ProductId}/deactivate", null)).StatusCode);
        var active = await client.GetFromJsonAsync<PagedResponse<ProductResponse>>("/api/products?pageSize=1");
        Assert.Equal(1, active!.TotalCount);
        Assert.Equal("SKU-2", Assert.Single(active.Items).Sku);
        var inactive = await client.GetFromJsonAsync<PagedResponse<ProductResponse>>("/api/products?status=inactive&pageSize=1");
        Assert.Equal(1, inactive!.TotalCount);
        Assert.Equal(first.ProductId, Assert.Single(inactive.Items).ProductId);
        var all = await client.GetFromJsonAsync<PagedResponse<ProductResponse>>("/api/products?status=all&pageSize=1&page=2");
        Assert.Equal(2, all!.TotalCount);
        Assert.Single(all.Items);
        var history = await client.GetFromJsonAsync<ProductResponse>($"/api/products/{first.ProductId}");
        Assert.Equal("Inactive", history!.Status);
        Assert.Equal(first.Batches, history.Batches);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsync($"/api/products/{first.ProductId}/deactivate", null)).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task Missing_or_malformed_tenant_returns_401_on_every_endpoint(string? tenant)
    {
        using var factory = new CatalogApiFactory();
        using var client = factory.Client(tenant);
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/products/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/products", Request())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync($"/api/products/{id}", new UpdateProductRequest("SKU", "Name", null, "Each"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PatchAsync($"/api/products/{id}/deactivate", null)).StatusCode);
    }

    [Theory]
    [InlineData("0.004")]
    [InlineData("0.005")]
    [InlineData("12.345")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("10000000000000000")]
    public async Task Unrepresentable_prices_return_400_without_writing(string price)
    {
        using var factory = new CatalogApiFactory();
        using var client = factory.Client(Guid.NewGuid().ToString());
        var response = await client.PostAsJsonAsync("/api/products", Request(price: decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var products = await client.GetFromJsonAsync<PagedResponse<ProductResponse>>("/api/products");
        Assert.Empty(products!.Items);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("12.340")]
    [InlineData("9999999999999999.99")]
    public async Task Representable_prices_are_accepted(string price)
    {
        using var factory = new CatalogApiFactory();
        using var client = factory.Client(Guid.NewGuid().ToString());
        var value = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);
        var response = await client.PostAsJsonAsync("/api/products", Request(price: value));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(value, (await response.Content.ReadFromJsonAsync<ProductResponse>())!.CurrentUnitPrice);
    }

    [Fact]
    public async Task Sku_is_unique_per_tenant_and_batch_codes_can_be_reused_across_products()
    {
        using var factory = new CatalogApiFactory();
        using var a = factory.Client(Guid.NewGuid().ToString());
        using var b = factory.Client(Guid.NewGuid().ToString());
        var product = await Create(a);
        await Create(a, "SKU-2");
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsJsonAsync("/api/products", Request(" sku-1 "))).StatusCode);
        await Create(b);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/products/{product.ProductId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/products/{product.ProductId}", new UpdateProductRequest("NEW", "Name", null, "Each"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PatchAsync($"/api/products/{product.ProductId}/deactivate", null)).StatusCode);
        var list = await b.GetFromJsonAsync<PagedResponse<ProductResponse>>("/api/products?status=all");
        Assert.Single(list!.Items);
    }

    [Theory]
    [InlineData("AreaManager")]
    [InlineData("AgencyOperator")]
    [InlineData("SalesRep")]
    public async Task Readers_cannot_write(string role)
    {
        using var factory = new CatalogApiFactory();
        using var client = factory.Client(Guid.NewGuid().ToString(), role);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/products", Request())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/products/{Guid.NewGuid()}", new UpdateProductRequest("SKU", "Name", null, "Each"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsync($"/api/products/{Guid.NewGuid()}/deactivate", null)).StatusCode);
    }

    [Fact]
    public async Task Invalid_status_and_dates_are_rejected_and_update_preserves_price()
    {
        using var factory = new CatalogApiFactory();
        using var client = factory.Client(Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/products?status=invalid")).StatusCode);
        var invalid = Request() with { ExpiryDate = new(2025, 1, 1) };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/products", invalid)).StatusCode);
        var product = await Create(client);
        var response = await client.PutAsJsonAsync($"/api/products/{product.ProductId}", new UpdateProductRequest(" new-sku ", "New name", null, "Box"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
        Assert.Equal("NEW-SKU", updated.Sku);
        Assert.Equal("New name", updated.Name);
        Assert.Equal(product.CurrentUnitPrice, updated.CurrentUnitPrice);
    }
}
