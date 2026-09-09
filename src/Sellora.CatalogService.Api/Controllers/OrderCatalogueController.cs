using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sellora.CatalogService.Api.Authorization;
using Sellora.CatalogService.Application.Products;
using Sellora.CatalogService.Domain.Tenancy;

namespace Sellora.CatalogService.Api.Controllers;

[ApiController]
[Route("api/products/catalogue")]
public sealed class OrderCatalogueController : ControllerBase
{
    private readonly IOrderCatalogService _orderCatalogService;
    private readonly ITenantContext _tenantContext;

    public OrderCatalogueController(
        IOrderCatalogService orderCatalogService,
        ITenantContext tenantContext)
    {
        _orderCatalogService = orderCatalogService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = RolePolicies.RequireCatalogReader)]
    public async Task<ActionResult<
        IReadOnlyCollection<OrderCatalogueProductResponse>>> GetCatalogue(
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.CompanyId is not Guid companyId)
        {
            return Unauthorized(new
            {
                Message =
                    "A valid company identifier was not found in the access token."
            });
        }

        var products = await _orderCatalogService.GetCatalogueAsync(
            companyId,
            search,
            categoryId,
            cancellationToken);

        return Ok(products);
    }
}