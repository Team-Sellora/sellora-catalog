using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.CatalogService.Infrastructure.Persistence.Migrations;

public partial class ScopeBatchCodesToProduct : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("uq_product_batch_company_code", "product_batch");
        migrationBuilder.CreateIndex(
            name: "uq_product_batch_company_product_code",
            table: "product_batch",
            columns: new[] { "company_id", "product_id", "batch_code" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("uq_product_batch_company_product_code", "product_batch");
        migrationBuilder.CreateIndex(
            name: "uq_product_batch_company_code",
            table: "product_batch",
            columns: new[] { "company_id", "batch_code" },
            unique: true);
    }
}
