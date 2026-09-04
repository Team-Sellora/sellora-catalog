using Sellora.CatalogService.Domain.Products;
using Sellora.CatalogService.Domain.Tenancy;

namespace Sellora.CatalogService.Domain.Entities;

public class ProductBatch : ITenantScoped
{
    public Guid BatchId { get; set; }
    public Guid ProductId { get; set; }
    public Guid CompanyId { get; set; }
    public string BatchCode { get; set; } = string.Empty;
    public DateOnly ManufacturingDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public string Status { get; set; } = ProductStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Product Product { get; set; } = null!;
}