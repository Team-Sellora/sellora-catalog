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
            // Keep the oldest category's name and preserve every category ID.
            // The lock covers cleanup and index replacement in EF's migration
            // transaction, preventing concurrent writes from introducing duplicates.
            migrationBuilder.Sql("""
                LOCK TABLE category IN ACCESS EXCLUSIVE MODE;

                DO $cleanup$
                DECLARE
                    duplicate RECORD;
                    candidate TEXT;
                    suffix TEXT;
                    suffix_number BIGINT;
                BEGIN
                    FOR duplicate IN
                        SELECT category_id, company_id, name
                        FROM (
                            SELECT category_id, company_id, name,
                                   ROW_NUMBER() OVER (
                                       PARTITION BY company_id, LOWER(name)
                                       ORDER BY created_at, category_id
                                   ) AS position
                            FROM category
                        ) ranked
                        WHERE position > 1
                        ORDER BY company_id, category_id
                    LOOP
                        suffix_number := 2;
                        LOOP
                            suffix := ' (' || suffix_number::TEXT || ')';
                            candidate := LEFT(duplicate.name, 120 - LENGTH(suffix)) || suffix;
                            EXIT WHEN NOT EXISTS (
                                SELECT 1 FROM category
                                WHERE company_id = duplicate.company_id
                                  AND LOWER(name) = LOWER(candidate)
                            );
                            suffix_number := suffix_number + 1;
                        END LOOP;

                        UPDATE category
                        SET name = candidate, updated_at = CURRENT_TIMESTAMP
                        WHERE category_id = duplicate.category_id;
                    END LOOP;
                END
                $cleanup$;
                """);

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
