using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sellora.CatalogService.Api.Authorization;
using Sellora.CatalogService.Api.Contracts;
using Sellora.CatalogService.Application.Products;

namespace Sellora.CatalogService.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpPost]
    [Authorize(Policy = RolePolicies.RequireCompanyAdmin)]
    public async Task<ActionResult<ProductResponse>> Create(
        CreateProductRequestBody body,
        CancellationToken cancellationToken)
    {
        var request = new CreateProductRequest(
            body.Sku,
            body.Name,
            body.Description,
            body.UnitOfMeasure,
            body.CurrentUnitPrice,
            body.BatchCode,
            body.ManufacturingDate,
            body.ExpiryDate);

        var result = await _productService.CreateAsync(
            request,
            cancellationToken);

        return result.Outcome switch
        {
            CreateProductOutcome.Success =>
                Created(
                    $"/api/products/{result.Product!.ProductId}",
                    result.Product),

            CreateProductOutcome.InvalidRequest =>
                BadRequest(new { result.Message }),

            CreateProductOutcome.TenantNotAvailable =>
                Unauthorized(new { result.Message }),

            CreateProductOutcome.DuplicateSku =>
                Conflict(new { result.Message }),

            CreateProductOutcome.DuplicateBatchCode =>
                Conflict(new { result.Message }),

            _ => Problem(
                title: "Product creation failed.",
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}