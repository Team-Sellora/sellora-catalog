using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.CatalogService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductPriceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_price_history",
                columns: table => new
                {
                    price_history_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    new_unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    changed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_price_history", x => x.price_history_id);
                    table.CheckConstraint("ck_product_price_history_new_price", "new_unit_price > 0");
                    table.CheckConstraint("ck_product_price_history_old_price", "old_unit_price > 0");
                    table.ForeignKey(
                        name: "fk_product_price_history_product",
                        column: x => x.product_id,
                        principalTable: "product",
                        principalColumn: "product_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_price_history_company_product_changed_at",
                table: "product_price_history",
                columns: new[] { "company_id", "product_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_product_price_history_product_id",
                table: "product_price_history",
                column: "product_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_price_history");
        }
    }
}
