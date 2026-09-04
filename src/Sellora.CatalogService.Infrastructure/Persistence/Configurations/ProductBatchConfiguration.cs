using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sellora.CatalogService.Domain.Entities;

namespace Sellora.CatalogService.Infrastructure.Persistence.Configurations;

public class ProductBatchConfiguration
    : IEntityTypeConfiguration<ProductBatch>
{
    public void Configure(EntityTypeBuilder<ProductBatch> builder)
    {
        builder.ToTable(
            "product_batch",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_product_batch_status",
                    "status IN ('Active', 'Inactive')");

                table.HasCheckConstraint(
                    "ck_product_batch_dates",
                    "expiry_date > manufacturing_date");
            });

        builder.HasKey(batch => batch.BatchId)
            .HasName("pk_product_batch");

        builder.Property(batch => batch.BatchId)
            .HasColumnName("batch_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(batch => batch.ProductId)
            .HasColumnName("product_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(batch => batch.CompanyId)
            .HasColumnName("company_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(batch => batch.BatchCode)
    .HasColumnName("batch_code")
    .HasMaxLength(80)
    .IsRequired();

        builder.Property(batch => batch.ManufacturingDate)
            .HasColumnName("manufacturing_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(batch => batch.ExpiryDate)
            .HasColumnName("expiry_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(batch => batch.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(batch => batch.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(batch => batch.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");
        builder.HasIndex(batch => new
        {
            batch.CompanyId,
            batch.BatchCode
        })
        .IsUnique()
        .HasDatabaseName("uq_product_batch_company_code");

        builder.HasOne(batch => batch.Product)
            .WithMany(product => product.Batches)
            .HasForeignKey(batch => batch.ProductId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_product_batch_product");
    }
}