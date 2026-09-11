using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.CatalogService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                table: "product",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "category",
                columns: table => new
                {
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category", x => x.category_id);
                    table.CheckConstraint("ck_category_status", "status IN ('Active', 'Inactive')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_category_id",
                table: "product",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "uq_category_company_name",
                table: "category",
                columns: new[] { "company_id", "name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_product_category",
                table: "product",
                column: "category_id",
                principalTable: "category",
                principalColumn: "category_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_product_category",
                table: "product");

            migrationBuilder.DropTable(
                name: "category");

            migrationBuilder.DropIndex(
                name: "IX_product_category_id",
                table: "product");

            migrationBuilder.DropColumn(
                name: "category_id",
                table: "product");
        }
    }
}
