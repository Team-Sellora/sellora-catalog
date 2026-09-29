namespace Sellora.CatalogService.Api.Contracts;

public sealed record ResolveProductsRequestBody(
    Guid CompanyId,
    IReadOnlyCollection<Guid> ProductIds);