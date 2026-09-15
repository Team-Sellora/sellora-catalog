using System.Net;
using System.Net.Http.Json;
using Sellora.CatalogService.Api.Contracts;
using Sellora.CatalogService.Application.Categories;
using Xunit;

namespace Sellora.CatalogService.Tests;

public sealed class CategoryEndpointTests(PostgreSqlConstraintFixture database) : IClassFixture<PostgreSqlConstraintFixture>
{
    [Fact]
    public async Task Category_names_are_unique_per_company_ignoring_case_on_create_and_update()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var client = factory.Client(Guid.NewGuid().ToString());

        var firstResponse = await client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequestBody("QA Unique 214153", null));
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var first = (await firstResponse.Content.ReadFromJsonAsync<CategoryResponse>())!;

        var createDuplicate = await client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequestBody("qa unique 214153", null));
        Assert.Equal(HttpStatusCode.Conflict, createDuplicate.StatusCode);

        var secondResponse = await client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequestBody("Other category", null));
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var second = (await secondResponse.Content.ReadFromJsonAsync<CategoryResponse>())!;

        var updateDuplicate = await client.PutAsJsonAsync(
            $"/api/categories/{second.CategoryId}",
            new UpdateCategoryRequestBody("qA uNIQUE 214153", null));
        Assert.Equal(HttpStatusCode.Conflict, updateDuplicate.StatusCode);

        Assert.NotEqual(first.CategoryId, second.CategoryId);
    }
}
