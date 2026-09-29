namespace Sellora.CatalogService.Application.Categories;

public sealed record CategoryResponse(
    Guid CategoryId,
    string Name,
    string? Description,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);