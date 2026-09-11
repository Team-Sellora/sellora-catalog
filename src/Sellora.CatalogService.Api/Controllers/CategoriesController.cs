using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sellora.CatalogService.Api.Authorization;
using Sellora.CatalogService.Api.Contracts;
using Sellora.CatalogService.Application.Categories;
using Sellora.CatalogService.Domain.Products;
using Sellora.CatalogService.Domain.Tenancy;

namespace Sellora.CatalogService.Api.Controllers;

[ApiController]
[Route("api/categories")]
public sealed class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;
    private readonly ITenantContext _tenantContext;

    public CategoriesController(
        ICategoryService categoryService,
        ITenantContext tenantContext)
    {
        _categoryService = categoryService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = RolePolicies.RequireCatalogReader)]
    public async Task<ActionResult<IReadOnlyCollection<CategoryResponse>>> GetCategories(
        [FromQuery] string status = "Active",
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.CompanyId is null)
        {
            return Unauthorized(new
            {
                Message = "A valid company identifier was not found in the access token."
            });
        }

        var normalizedStatus = status.Trim().ToLowerInvariant() switch
        {
            "active" => CategoryStatus.Active,
            "inactive" => CategoryStatus.Inactive,
            "all" => "All",
            _ => null
        };

        if (normalizedStatus is null)
        {
            return BadRequest(new
            {
                Message = "Status must be Active, Inactive, or All."
            });
        }

        return Ok(await _categoryService.GetCategoriesAsync(
            normalizedStatus,
            cancellationToken));
    }

    [HttpGet("{categoryId:guid}")]
    [Authorize(Policy = RolePolicies.RequireCatalogReader)]
    public async Task<ActionResult<CategoryResponse>> GetById(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.CompanyId is null)
        {
            return Unauthorized(new
            {
                Message = "A valid company identifier was not found in the access token."
            });
        }

        var category = await _categoryService.GetByIdAsync(
            categoryId,
            cancellationToken);

        return category is null
            ? NotFound(new { Message = $"Category '{categoryId}' was not found." })
            : Ok(category);
    }

    [HttpPost]
    [Authorize(Policy = RolePolicies.RequireCompanyAdmin)]
    public async Task<ActionResult<CategoryResponse>> Create(
        CreateCategoryRequestBody body,
        CancellationToken cancellationToken = default)
    {
        var result = await _categoryService.CreateAsync(
            new CreateCategoryRequest(body.Name, body.Description),
            cancellationToken);

        return result.Outcome switch
        {
            CreateCategoryOutcome.Success => Created(
                $"/api/categories/{result.Category!.CategoryId}",
                result.Category),

            CreateCategoryOutcome.InvalidRequest =>
                BadRequest(new { result.Message }),

            CreateCategoryOutcome.DuplicateName =>
                Conflict(new { result.Message }),

            CreateCategoryOutcome.TenantNotAvailable =>
                Unauthorized(new { result.Message }),

            _ => Problem(
                title: "Category creation failed.",
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    [HttpPut("{categoryId:guid}")]
    [Authorize(Policy = RolePolicies.RequireCompanyAdmin)]
    public async Task<ActionResult<CategoryResponse>> Update(
        Guid categoryId,
        UpdateCategoryRequestBody body,
        CancellationToken cancellationToken = default)
    {
        var result = await _categoryService.UpdateAsync(
            categoryId,
            new UpdateCategoryRequest(body.Name, body.Description),
            cancellationToken);

        return result.Outcome switch
        {
            UpdateCategoryOutcome.Success => Ok(result.Category),

            UpdateCategoryOutcome.InvalidRequest =>
                BadRequest(new { result.Message }),

            UpdateCategoryOutcome.DuplicateName =>
                Conflict(new { result.Message }),

            UpdateCategoryOutcome.NotFound =>
                NotFound(new { result.Message }),

            UpdateCategoryOutcome.TenantNotAvailable =>
                Unauthorized(new { result.Message }),

            _ => Problem(
                title: "Category update failed.",
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    [HttpPatch("{categoryId:guid}/deactivate")]
    [Authorize(Policy = RolePolicies.RequireCompanyAdmin)]
    public async Task<ActionResult<CategoryResponse>> Deactivate(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        var result = await _categoryService.DeactivateAsync(
            categoryId,
            cancellationToken);

        return result.Outcome switch
        {
            DeactivateCategoryOutcome.Success => Ok(result.Category),

            DeactivateCategoryOutcome.NotFound =>
                NotFound(new { result.Message }),

            DeactivateCategoryOutcome.AlreadyInactive =>
                Conflict(new { result.Message }),

            DeactivateCategoryOutcome.TenantNotAvailable =>
                Unauthorized(new { result.Message }),

            _ => Problem(
                title: "Category deactivation failed.",
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}