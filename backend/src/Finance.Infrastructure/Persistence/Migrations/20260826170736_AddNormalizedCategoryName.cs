using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedCategoryName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_categories_UserId_Type_Name",
                table: "categories");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "categories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE categories
                SET "NormalizedName" = UPPER(BTRIM("Name"));
                """);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedName",
                table: "categories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_categories_UserId_Type_NormalizedName",
                table: "categories",
                columns: new[] { "UserId", "Type", "NormalizedName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_categories_UserId_Type_NormalizedName",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "categories");

            migrationBuilder.CreateIndex(
                name: "IX_categories_UserId_Type_Name",
                table: "categories",
                columns: new[] { "UserId", "Type", "Name" },
                unique: true);
        }
    }
}
