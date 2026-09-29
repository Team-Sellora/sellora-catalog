namespace Sellora.CatalogService.Application.Categories;

public sealed record UpdateCategoryRequest(
    string Name,
    string? Description);