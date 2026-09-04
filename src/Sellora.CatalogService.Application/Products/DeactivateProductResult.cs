namespace Sellora.CatalogService.Application.Products;

public enum DeactivateProductOutcome
{
    Success,
    NotFound,
    AlreadyInactive,
    TenantNotAvailable
}

public sealed class DeactivateProductResult
{
    public DeactivateProductOutcome Outcome { get; }

    public string Message { get; }

    public ProductResponse? Product { get; }

    private DeactivateProductResult(
        DeactivateProductOutcome outcome,
        string message,
        ProductResponse? product = null)
    {
        Outcome = outcome;
        Message = message;
        Product = product;
    }

    public static DeactivateProductResult Success(ProductResponse product) =>
        new(
            DeactivateProductOutcome.Success,
            "Product deactivated successfully.",
            product);

    public static DeactivateProductResult NotFound(Guid productId) =>
        new(
            DeactivateProductOutcome.NotFound,
            $"Product '{productId}' was not found.");

    public static DeactivateProductResult AlreadyInactive(Guid productId) =>
        new(
            DeactivateProductOutcome.AlreadyInactive,
            $"Product '{productId}' is already inactive.");

    public static DeactivateProductResult TenantNotAvailable() =>
        new(
            DeactivateProductOutcome.TenantNotAvailable,
            "A valid company identifier was not found in the access token.");
}