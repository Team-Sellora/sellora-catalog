using Sellora.CatalogService.Domain.Tenancy;

namespace Sellora.CatalogService.Domain.Entities;

public class ProductPriceHistory : ITenantScoped
{
    public Guid PriceHistoryId { get; set; }

    public Guid CompanyId { get; set; }

    public Guid ProductId { get; set; }

    public decimal OldUnitPrice { get; set; }

    public decimal NewUnitPrice { get; set; }

    public string ChangedBy { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset ChangedAt { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; }

    public Product Product { get; set; } = null!;
}