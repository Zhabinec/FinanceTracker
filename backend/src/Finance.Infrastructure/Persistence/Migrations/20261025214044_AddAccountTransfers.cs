using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_transfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_transfers", x => x.Id);
                    table.CheckConstraint("ck_account_transfers_amount_positive", "\"Amount\" > 0");
                    table.CheckConstraint("ck_account_transfers_different_accounts", "\"SourceAccountId\" <> \"TargetAccountId\"");
                    table.ForeignKey(
                        name: "FK_account_transfers_accounts_SourceAccountId_UserId",
                        columns: x => new { x.SourceAccountId, x.UserId },
                        principalTable: "accounts",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_transfers_accounts_TargetAccountId_UserId",
                        columns: x => new { x.TargetAccountId, x.UserId },
                        principalTable: "accounts",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_transfers_SourceAccountId_UserId",
                table: "account_transfers",
                columns: new[] { "SourceAccountId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_account_transfers_TargetAccountId_UserId",
                table: "account_transfers",
                columns: new[] { "TargetAccountId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_account_transfers_UserId_CreatedAtUtc",
                table: "account_transfers",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_account_transfers_UserId_RequestId",
                table: "account_transfers",
                columns: new[] { "UserId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_transfers");
        }
    }
}
