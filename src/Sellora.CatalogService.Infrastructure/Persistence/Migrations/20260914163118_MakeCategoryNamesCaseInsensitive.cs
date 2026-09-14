using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sellora.CatalogService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeCategoryNamesCaseInsensitive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_category_company_name",
                table: "category");

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX uq_category_company_name " +
                "ON category (company_id, LOWER(name));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX uq_category_company_name;");

            migrationBuilder.CreateIndex(
                name: "uq_category_company_name",
                table: "category",
                columns: new[] { "company_id", "name" },
                unique: true);
        }
    }
}
