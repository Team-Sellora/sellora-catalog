using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.CatalogService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalogSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    unit_of_measure = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    current_unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product", x => x.product_id);
                    table.CheckConstraint("ck_product_current_unit_price", "current_unit_price > 0");
                    table.CheckConstraint("ck_product_status", "status IN ('Active', 'Inactive')");
                });

            migrationBuilder.CreateTable(
                name: "product_batch",
                columns: table => new
                {
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    manufacturing_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_batch", x => x.batch_id);
                    table.CheckConstraint("ck_product_batch_dates", "expiry_date > manufacturing_date");
                    table.CheckConstraint("ck_product_batch_status", "status IN ('Active', 'Inactive')");
                    table.ForeignKey(
                        name: "fk_product_batch_product",
                        column: x => x.product_id,
                        principalTable: "product",
                        principalColumn: "product_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_product_company_sku",
                table: "product",
                columns: new[] { "company_id", "sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_batch_product_id",
                table: "product_batch",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "uq_product_batch_company_code",
                table: "product_batch",
                columns: new[] { "company_id", "batch_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_batch");

            migrationBuilder.DropTable(
                name: "product");
        }
    }
}
