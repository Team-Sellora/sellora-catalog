using System.Text.Json.Serialization;

namespace Sellora.CatalogService.Api.Contracts;

/// <summary>
/// A complete product update. CategoryId is presence-aware: omitting it preserves
/// the assigned category, while sending it as null removes the assignment.
/// </summary>
public sealed class UpdateProductRequestBody
{
    public UpdateProductRequestBody()
    {
    }

    public UpdateProductRequestBody(
        string sku,
        string name,
        string? description,
        string unitOfMeasure)
    {
        Sku = sku;
        Name = name;
        Description = description;
        UnitOfMeasure = unitOfMeasure;
    }

    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string UnitOfMeasure { get; set; } = string.Empty;

    private Guid? _categoryId;

    public Guid? CategoryId
    {
        get => _categoryId;
        set
        {
            _categoryId = value;
            IsCategoryIdSpecified = true;
        }
    }

    [JsonIgnore]
    public bool IsCategoryIdSpecified { get; private set; }
}
