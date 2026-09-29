namespace Sellora.CatalogService.Infrastructure.Persistence.Seeding;

/// <summary>
/// Stable identifiers for the staging catalogue. The company ID matches
/// Organization's SELLORA-DEMO company.
/// </summary>
public static class DevelopmentCatalogSeedIds
{
    public static readonly Guid CompanyId =
        Guid.Parse("30000000-0000-0000-0000-000000000001");
}
