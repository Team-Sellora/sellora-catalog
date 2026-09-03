using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Sellora.CatalogService.Api.Authorization;

public static class RolePolicies
{
    public const string RequireCompanyAdmin = "RequireCompanyAdmin";
    public const string RequireCatalogReader = "RequireCatalogReader";

    public static void AddSelloraCatalogPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(RequireCompanyAdmin, policy =>
            policy.RequireAssertion(context => HasRole(context, "CompanyAdmin")));

        options.AddPolicy(RequireCatalogReader, policy =>
            policy.RequireAssertion(context =>
                HasRole(context, "CompanyAdmin", "AreaManager", "AgencyOperator", "SalesRep")));
    }

    private static bool HasRole(AuthorizationHandlerContext context, params string[] roles) =>
        roles.Any(role => context.User.HasClaim(ClaimTypes.Role, role));
}

