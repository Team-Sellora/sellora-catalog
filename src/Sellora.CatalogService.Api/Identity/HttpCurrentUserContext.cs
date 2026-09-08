using System.Security.Claims;
using Sellora.CatalogService.Application.Identity;

namespace Sellora.CatalogService.Api.Identity;

public sealed class HttpCurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUserContext(
        IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? Subject =>
        _httpContextAccessor.HttpContext?.User
            .FindFirst("sub")?.Value
        ?? _httpContextAccessor.HttpContext?.User
            .FindFirst(ClaimTypes.NameIdentifier)?.Value;
}