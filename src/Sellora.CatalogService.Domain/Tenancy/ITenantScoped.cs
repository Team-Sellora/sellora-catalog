namespace Sellora.CatalogService.Domain.Tenancy;

public interface ITenantScoped
{
    Guid CompanyId { get; }
}

