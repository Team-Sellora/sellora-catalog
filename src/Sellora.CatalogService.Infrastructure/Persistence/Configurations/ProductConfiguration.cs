using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sellora.CatalogService.Domain.Entities;

namespace Sellora.CatalogService.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable(
            "product",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_product_status",
                    "status IN ('Active', 'Inactive')");

                table.HasCheckConstraint(
                    "ck_product_current_unit_price",
                    "current_unit_price > 0");
            }
        );

        builder.HasKey(product => product.ProductId)
            .HasName("pk_product");

        builder.Property(product => product.ProductId)
            .HasColumnName("product_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(product => product.CompanyId)
            .HasColumnName("company_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(product => product.Sku)
            .HasColumnName("sku")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(product => product.Name)
    .HasColumnName("name")
    .HasMaxLength(200)
    .IsRequired();

        builder.Property(product => product.Description)
            .HasColumnName("description")
            .HasColumnType("text");

        builder.Property(product => product.UnitOfMeasure)
            .HasColumnName("unit_of_measure")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(product => product.CurrentUnitPrice)
            .HasColumnName("current_unit_price")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(product => product.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(product => product.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(product => product.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(product => new
        {
            product.CompanyId,
            product.Sku
        })
        .IsUnique()
        .HasDatabaseName("uq_product_company_sku");
    }
}