using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialWorkspaceRevisionAndKeyOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_managed_financial_keys_private_financial_vaults_UserId",
                table: "managed_financial_keys");

            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "financial_workspaces",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddForeignKey(
                name: "FK_managed_financial_keys_users_UserId",
                table: "managed_financial_keys",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_managed_financial_keys_users_UserId",
                table: "managed_financial_keys");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "financial_workspaces");

            migrationBuilder.AddForeignKey(
                name: "FK_managed_financial_keys_private_financial_vaults_UserId",
                table: "managed_financial_keys",
                column: "UserId",
                principalTable: "private_financial_vaults",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
