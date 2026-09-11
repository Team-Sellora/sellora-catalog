using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sellora.CatalogService.Domain.Entities;

namespace Sellora.CatalogService.Infrastructure.Persistence.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable(
            "category",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_category_status",
                    "status IN ('Active', 'Inactive')");
            });

        builder.HasKey(category => category.CategoryId)
            .HasName("pk_category");

        builder.Property(category => category.CategoryId)
            .HasColumnName("category_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(category => category.CompanyId)
            .HasColumnName("company_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(category => category.Name)
            .HasColumnName("name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(category => category.Description)
            .HasColumnName("description")
            .HasColumnType("text");

        builder.Property(category => category.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(category => category.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(category => category.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(category => new
        {
            category.CompanyId,
            category.Name
        })
        .IsUnique()
        .HasDatabaseName("uq_category_company_name");
    }
}