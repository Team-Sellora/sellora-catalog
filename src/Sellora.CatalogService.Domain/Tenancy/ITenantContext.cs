namespace Sellora.CatalogService.Domain.Tenancy;

public interface ITenantContext
{
    Guid? CompanyId { get; }
}

