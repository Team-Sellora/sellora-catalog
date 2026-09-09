using Sellora.CatalogService.Domain.Products;
using Sellora.CatalogService.Domain.Tenancy;

namespace Sellora.CatalogService.Domain.Entities;

public class Category : ITenantScoped
{
    public Guid CategoryId { get; set; }

    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = CategoryStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
}