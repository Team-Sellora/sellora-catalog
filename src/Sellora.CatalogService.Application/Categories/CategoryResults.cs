namespace Sellora.CatalogService.Application.Categories;

public enum CreateCategoryOutcome
{
    Success,
    InvalidRequest,
    DuplicateName,
    TenantNotAvailable
}

public sealed record CreateCategoryResult(
    CreateCategoryOutcome Outcome,
    CategoryResponse? Category,
    string? Message)
{
    public static CreateCategoryResult Success(CategoryResponse category) =>
        new(CreateCategoryOutcome.Success, category, null);

    public static CreateCategoryResult InvalidRequest(string message) =>
        new(CreateCategoryOutcome.InvalidRequest, null, message);

    public static CreateCategoryResult DuplicateName(string name) =>
        new(CreateCategoryOutcome.DuplicateName, null,
            $"A category named '{name}' already exists.");

    public static CreateCategoryResult TenantNotAvailable() =>
        new(CreateCategoryOutcome.TenantNotAvailable, null,
            "A valid company identifier was not found in the access token.");
}

public enum UpdateCategoryOutcome
{
    Success,
    InvalidRequest,
    DuplicateName,
    NotFound,
    TenantNotAvailable
}

public sealed record UpdateCategoryResult(
    UpdateCategoryOutcome Outcome,
    CategoryResponse? Category,
    string? Message)
{
    public static UpdateCategoryResult Success(CategoryResponse category) =>
        new(UpdateCategoryOutcome.Success, category, null);

    public static UpdateCategoryResult InvalidRequest(string message) =>
        new(UpdateCategoryOutcome.InvalidRequest, null, message);

    public static UpdateCategoryResult DuplicateName(string name) =>
        new(UpdateCategoryOutcome.DuplicateName, null,
            $"A category named '{name}' already exists.");

    public static UpdateCategoryResult NotFound(Guid categoryId) =>
        new(UpdateCategoryOutcome.NotFound, null,
            $"Category '{categoryId}' was not found.");

    public static UpdateCategoryResult TenantNotAvailable() =>
        new(UpdateCategoryOutcome.TenantNotAvailable, null,
            "A valid company identifier was not found in the access token.");
}

public enum DeactivateCategoryOutcome
{
    Success,
    NotFound,
    AlreadyInactive,
    TenantNotAvailable
}

public sealed record DeactivateCategoryResult(
    DeactivateCategoryOutcome Outcome,
    CategoryResponse? Category,
    string? Message)
{
    public static DeactivateCategoryResult Success(CategoryResponse category) =>
        new(DeactivateCategoryOutcome.Success, category, null);

    public static DeactivateCategoryResult NotFound(Guid categoryId) =>
        new(DeactivateCategoryOutcome.NotFound, null,
            $"Category '{categoryId}' was not found.");

    public static DeactivateCategoryResult AlreadyInactive(Guid categoryId) =>
        new(DeactivateCategoryOutcome.AlreadyInactive, null,
            $"Category '{categoryId}' is already inactive.");

    public static DeactivateCategoryResult TenantNotAvailable() =>
        new(DeactivateCategoryOutcome.TenantNotAvailable, null,
            "A valid company identifier was not found in the access token.");
}