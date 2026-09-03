using Sellora.CatalogService.Domain.Tenancy;

namespace Sellora.CatalogService.Api.Tenancy;

public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid? CompanyId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirst("companyId")?.Value;
            return Guid.TryParse(value, out var companyId) ? companyId : null;
        }
    }
}

