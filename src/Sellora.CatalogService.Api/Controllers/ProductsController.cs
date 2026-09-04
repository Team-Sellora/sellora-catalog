using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sellora.CatalogService.Api.Authorization;
using Sellora.CatalogService.Api.Contracts;
using Sellora.CatalogService.Application.Common;
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

    //get products
    [HttpGet]
    [Authorize(Policy = RolePolicies.RequireCatalogReader)]
    public async Task<ActionResult<PagedResponse<ProductResponse>>> GetProducts(
    [FromQuery] string? search,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20,
    CancellationToken cancellationToken = default)
    {
        var query = new ProductListQuery(
            search,
            page,
            pageSize);

        var response = await _productService.GetProductsAsync(
            query,
            cancellationToken);

        return Ok(response);
    }

    //get product by id
    [HttpGet("{productId:guid}")]
    [Authorize(Policy = RolePolicies.RequireCatalogReader)]
    public async Task<ActionResult<ProductResponse>> GetProductById(
    Guid productId,
    CancellationToken cancellationToken)
    {
        var product = await _productService.GetProductByIdAsync(
            productId,
            cancellationToken);

        if (product is null)
        {
            return NotFound(new
            {
                Message = $"Product '{productId}' was not found."
            });
        }

        return Ok(product);
    }

    //create product
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