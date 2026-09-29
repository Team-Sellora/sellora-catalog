using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sellora.CatalogService.Application.Categories;
using Sellora.CatalogService.Domain.Entities;
using Sellora.CatalogService.Domain.Products;
using Sellora.CatalogService.Domain.Tenancy;
using Sellora.CatalogService.Infrastructure.Persistence;

namespace Sellora.CatalogService.Infrastructure.Categories;

public sealed class CategoryService : ICategoryService
{
    private readonly CatalogDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public CategoryService(
        CatalogDbContext dbContext,
        ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyCollection<CategoryResponse>> GetCategoriesAsync(
        string status,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantAvailable();

        var query = _dbContext.Categories
            .AsNoTracking()
            .AsQueryable();

        if (!string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(category => category.Status == status);
        }

        return await query
            .OrderBy(category => category.Name)
            .Select(category => ToResponse(category))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<CategoryResponse?> GetByIdAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantAvailable();

        return await _dbContext.Categories
            .AsNoTracking()
            .Where(category => category.CategoryId == categoryId)
            .Select(category => ToResponse(category))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<CreateCategoryResult> CreateAsync(
        CreateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.CompanyId is not Guid companyId)
        {
            return CreateCategoryResult.TenantNotAvailable();
        }

        var validationError = Validate(request.Name, request.Description);
        if (validationError is not null)
        {
            return CreateCategoryResult.InvalidRequest(validationError);
        }

        var name = request.Name.Trim();
        var category = new Category
        {
            CategoryId = Guid.NewGuid(),
            CompanyId = companyId,
            Name = name,
            Description = NormalizeDescription(request.Description),
            Status = CategoryStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Categories.Add(category);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "uq_category_company_name"
            })
        {
            _dbContext.ChangeTracker.Clear();
            return CreateCategoryResult.DuplicateName(name);
        }

        return CreateCategoryResult.Success(ToResponse(category));
    }

    public async Task<UpdateCategoryResult> UpdateAsync(
        Guid categoryId,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.CompanyId is null)
        {
            return UpdateCategoryResult.TenantNotAvailable();
        }

        var validationError = Validate(request.Name, request.Description);
        if (validationError is not null)
        {
            return UpdateCategoryResult.InvalidRequest(validationError);
        }

        var category = await _dbContext.Categories.SingleOrDefaultAsync(
            category => category.CategoryId == categoryId,
            cancellationToken);

        if (category is null)
        {
            return UpdateCategoryResult.NotFound(categoryId);
        }

        var name = request.Name.Trim();
        category.Name = name;
        category.Description = NormalizeDescription(request.Description);
        category.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "uq_category_company_name"
            })
        {
            _dbContext.ChangeTracker.Clear();
            return UpdateCategoryResult.DuplicateName(name);
        }

        return UpdateCategoryResult.Success(ToResponse(category));
    }

    public async Task<DeactivateCategoryResult> DeactivateAsync(
    Guid categoryId,
    CancellationToken cancellationToken = default)
    {
        if (_tenantContext.CompanyId is null)
        {
            return DeactivateCategoryResult.TenantNotAvailable();
        }

        var category = await _dbContext.Categories.SingleOrDefaultAsync(
            category => category.CategoryId == categoryId,
            cancellationToken);

        if (category is null)
        {
            return DeactivateCategoryResult.NotFound(categoryId);
        }

        if (category.Status == CategoryStatus.Inactive)
        {
            return DeactivateCategoryResult.AlreadyInactive(categoryId);
        }

        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            cancellationToken);

        await _dbContext.Products
            .Where(product => product.CategoryId == categoryId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(product => product.CategoryId, (Guid?)null)
                    .SetProperty(product => product.UpdatedAt, now),
                cancellationToken);

        category.Status = CategoryStatus.Inactive;
        category.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return DeactivateCategoryResult.Success(ToResponse(category));
    }

    private static CategoryResponse ToResponse(Category category) =>
        new(
            category.CategoryId,
            category.Name,
            category.Description,
            category.Status,
            category.CreatedAt,
            category.UpdatedAt);

    private static string? Validate(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Category name is required.";
        }

        if (name.Trim().Length > 120)
        {
            return "Category name cannot exceed 120 characters.";
        }

        if (description?.Trim().Length > 2_000)
        {
            return "Category description cannot exceed 2000 characters.";
        }

        return null;
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private void EnsureTenantAvailable()
    {
        if (_tenantContext.CompanyId is null)
        {
            throw new UnauthorizedAccessException(
                "A valid company identifier was not found in the access token.");
        }
    }
}