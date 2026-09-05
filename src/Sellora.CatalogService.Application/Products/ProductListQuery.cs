namespace Sellora.CatalogService.Application.Products;

public sealed record ProductListQuery(
    string? Search,
    int Page = 1,
    int PageSize = 20,
    string Status = "Active");
