using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlyBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monthly_budgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monthly_budgets", x => x.Id);
                    table.CheckConstraint("ck_budgets_expense", "\"Type\" = 2");
                    table.CheckConstraint("ck_budgets_month", "EXTRACT(DAY FROM \"Month\") = 1 AND \"Month\" >= DATE '1900-01-01' AND \"Month\" < DATE '9999-01-01'");
                    table.CheckConstraint("ck_budgets_positive", "\"Limit\" > 0");
                    table.ForeignKey(
                        name: "FK_monthly_budgets_categories_CategoryId_UserId_Type",
                        columns: x => new { x.CategoryId, x.UserId, x.Type },
                        principalTable: "categories",
                        principalColumns: new[] { "Id", "UserId", "Type" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_monthly_budgets_CategoryId_UserId_Type",
                table: "monthly_budgets",
                columns: new[] { "CategoryId", "UserId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_monthly_budgets_UserId_Month_CategoryId",
                table: "monthly_budgets",
                columns: new[] { "UserId", "Month", "CategoryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monthly_budgets");
        }
    }
}
