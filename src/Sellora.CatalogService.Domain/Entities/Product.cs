using Sellora.CatalogService.Domain.Products;
using Sellora.CatalogService.Domain.Tenancy;

namespace Sellora.CatalogService.Domain.Entities;

public class Product : ITenantScoped
{
    public Guid ProductId { get; set; }

    public Guid CompanyId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string UnitOfMeasure { get; set; } = string.Empty;
    public decimal CurrentUnitPrice { get; set; }
    public string Status { get; set; } = ProductStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public ICollection<ProductBatch> Batches { get; set; } = new List<ProductBatch>();
}
