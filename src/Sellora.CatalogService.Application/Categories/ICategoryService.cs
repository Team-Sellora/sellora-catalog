namespace Sellora.CatalogService.Application.Categories;

public interface ICategoryService
{
    Task<IReadOnlyCollection<CategoryResponse>> GetCategoriesAsync(
        string status,
        CancellationToken cancellationToken = default);

    Task<CategoryResponse?> GetByIdAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default);

    Task<CreateCategoryResult> CreateAsync(
        CreateCategoryRequest request,
        CancellationToken cancellationToken = default);

    Task<UpdateCategoryResult> UpdateAsync(
        Guid categoryId,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken = default);

    Task<DeactivateCategoryResult> DeactivateAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default);
}