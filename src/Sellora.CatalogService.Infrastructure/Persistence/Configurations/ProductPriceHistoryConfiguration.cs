using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sellora.CatalogService.Domain.Entities;

namespace Sellora.CatalogService.Infrastructure.Persistence.Configurations;

public sealed class ProductPriceHistoryConfiguration
    : IEntityTypeConfiguration<ProductPriceHistory>
{
    public void Configure(
        EntityTypeBuilder<ProductPriceHistory> builder)
    {
        builder.ToTable(
            "product_price_history",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_product_price_history_old_price",
                    "old_unit_price > 0");

                table.HasCheckConstraint(
                    "ck_product_price_history_new_price",
                    "new_unit_price > 0");
            });

        builder.HasKey(history => history.PriceHistoryId)
            .HasName("pk_product_price_history");

        builder.Property(history => history.PriceHistoryId)
            .HasColumnName("price_history_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(history => history.CompanyId)
            .HasColumnName("company_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(history => history.ProductId)
            .HasColumnName("product_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(history => history.OldUnitPrice)
            .HasColumnName("old_unit_price")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(history => history.NewUnitPrice)
            .HasColumnName("new_unit_price")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(history => history.ChangedBy)
            .HasColumnName("changed_by")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(history => history.Reason)
            .HasColumnName("reason")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(history => history.ChangedAt)
            .HasColumnName("changed_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(history => history.EffectiveFrom)
            .HasColumnName("effective_from")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(history => new
        {
            history.CompanyId,
            history.ProductId,
            history.ChangedAt
        })
        .HasDatabaseName(
            "ix_product_price_history_company_product_changed_at");

        builder.HasOne(history => history.Product)
            .WithMany(product => product.PriceHistory)
            .HasForeignKey(history => history.ProductId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_product_price_history_product");
    }
}