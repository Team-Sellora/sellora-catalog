namespace Sellora.CatalogService.Application.Categories;

public sealed record CreateCategoryRequest(
    string Name,
    string? Description);