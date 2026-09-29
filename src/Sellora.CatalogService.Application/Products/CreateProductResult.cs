namespace Sellora.CatalogService.Application.Products;

public enum CreateProductOutcome
{
    Success,
    InvalidRequest,
    DuplicateSku,
    TenantNotAvailable
}

public sealed class CreateProductResult
{
    public CreateProductOutcome Outcome { get; }

    public string Message { get; }

    public ProductResponse? Product { get; }

    private CreateProductResult(
        CreateProductOutcome outcome,
        string message,
        ProductResponse? product = null)
    {
        Outcome = outcome;
        Message = message;
        Product = product;
    }

    public bool IsSuccess =>
        Outcome == CreateProductOutcome.Success;

    public static CreateProductResult Success(ProductResponse product) =>
    new(
        CreateProductOutcome.Success,
        "Product created successfully.",
        product);

    public static CreateProductResult InvalidRequest(string message) =>
        new(CreateProductOutcome.InvalidRequest, message);

    public static CreateProductResult DuplicateSku(string sku) =>
        new(
            CreateProductOutcome.DuplicateSku,
            $"A product with SKU '{sku}' already exists in your company.");

    public static CreateProductResult TenantNotAvailable() =>
        new(
            CreateProductOutcome.TenantNotAvailable,
            "A valid company identifier was not found in the access token.");
}
