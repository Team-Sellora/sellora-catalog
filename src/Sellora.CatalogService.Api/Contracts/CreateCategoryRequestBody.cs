namespace Sellora.CatalogService.Api.Contracts;

public sealed record CreateCategoryRequestBody(
    string Name,
    string? Description);