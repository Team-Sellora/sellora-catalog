namespace Sellora.CatalogService.Application.Identity;

/// <summary>
/// Provides the authenticated user's identifier from the access token.
/// </summary>
public interface ICurrentUserContext
{
    string? Subject { get; }
}