using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sellora.CatalogService.Api.Contracts;
using Sellora.CatalogService.Application.Products;

namespace Sellora.CatalogService.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("internal/catalog/products")]
public sealed class InternalOrderCatalogController : ControllerBase
{
    private const int MaximumProductCount = 100;

    private readonly IOrderCatalogService _orderCatalogService;

    public InternalOrderCatalogController(
        IOrderCatalogService orderCatalogService)
    {
        _orderCatalogService = orderCatalogService;
    }

    [HttpPost("resolve")]
    public async Task<ActionResult<ProductResolutionResponse>>
        ResolveProducts(
            ResolveProductsRequestBody body,
            CancellationToken cancellationToken)
    {
        if (body.CompanyId == Guid.Empty)
        {
            return BadRequest(new
            {
                Message = "Company ID is required."
            });
        }

        if (body.ProductIds is null ||
            body.ProductIds.Count == 0)
        {
            return BadRequest(new
            {
                Message =
                    "At least one product ID is required."
            });
        }

        if (body.ProductIds.Count > MaximumProductCount)
        {
            return BadRequest(new
            {
                Message =
                    $"A maximum of {MaximumProductCount} products can be requested."
            });
        }

        if (body.ProductIds.Any(
            productId => productId == Guid.Empty))
        {
            return BadRequest(new
            {
                Message =
                    "Product IDs cannot contain an empty GUID."
            });
        }

        var response =
            await _orderCatalogService.ResolveProductsAsync(
                body.CompanyId,
                body.ProductIds,
                cancellationToken);

        return Ok(response);
    }
}