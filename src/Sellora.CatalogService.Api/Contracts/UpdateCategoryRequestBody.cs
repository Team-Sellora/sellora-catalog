namespace Sellora.CatalogService.Api.Contracts;

public sealed record UpdateCategoryRequestBody(
    string Name,
    string? Description);