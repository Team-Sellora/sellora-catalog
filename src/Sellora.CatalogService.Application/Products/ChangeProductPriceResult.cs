namespace Sellora.CatalogService.Application.Products;

public enum ChangeProductPriceOutcome
{
    Success,
    InvalidRequest,
    NotFound,
    TenantNotAvailable
}

public sealed class ChangeProductPriceResult
{
    public ChangeProductPriceOutcome Outcome { get; }

    public string Message { get; }

    public ProductResponse? Product { get; }

    private ChangeProductPriceResult(
        ChangeProductPriceOutcome outcome,
        string message,
        ProductResponse? product = null)
    {
        Outcome = outcome;
        Message = message;
        Product = product;
    }

    public static ChangeProductPriceResult Success(ProductResponse product) =>
        new(
            ChangeProductPriceOutcome.Success,
            "Product price changed successfully.",
            product);

    public static ChangeProductPriceResult InvalidRequest(string message) =>
        new(ChangeProductPriceOutcome.InvalidRequest, message);

    public static ChangeProductPriceResult NotFound(Guid productId) =>
        new(
            ChangeProductPriceOutcome.NotFound,
            $"Product '{productId}' was not found.");

    public static ChangeProductPriceResult TenantNotAvailable() =>
        new(
            ChangeProductPriceOutcome.TenantNotAvailable,
            "A valid company identifier was not found in the access token.");
}