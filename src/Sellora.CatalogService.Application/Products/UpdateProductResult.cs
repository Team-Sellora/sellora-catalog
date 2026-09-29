namespace Sellora.CatalogService.Application.Products;

public enum UpdateProductOutcome
{
    Success,
    InvalidRequest,
    DuplicateSku,
    NotFound,
    TenantNotAvailable
}

public sealed class UpdateProductResult
{
    public UpdateProductOutcome Outcome { get; }

    public string Message { get; }

    public ProductResponse? Product { get; }

    private UpdateProductResult(
        UpdateProductOutcome outcome,
        string message,
        ProductResponse? product = null)
    {
        Outcome = outcome;
        Message = message;
        Product = product;
    }

    public static UpdateProductResult Success(ProductResponse product) =>
        new(
            UpdateProductOutcome.Success,
            "Product updated successfully.",
            product);

    public static UpdateProductResult InvalidRequest(string message) =>
        new(UpdateProductOutcome.InvalidRequest, message);

    public static UpdateProductResult DuplicateSku(string sku) =>
        new(
            UpdateProductOutcome.DuplicateSku,
            $"A product with SKU '{sku}' already exists in your company.");

    public static UpdateProductResult NotFound(Guid productId) =>
        new(
            UpdateProductOutcome.NotFound,
            $"Product '{productId}' was not found.");

    public static UpdateProductResult TenantNotAvailable() =>
        new(
            UpdateProductOutcome.TenantNotAvailable,
            "A valid company identifier was not found in the access token.");
}