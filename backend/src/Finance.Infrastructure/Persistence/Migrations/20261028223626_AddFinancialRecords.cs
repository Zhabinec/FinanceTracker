using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_workspaces",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    VaultId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceRevision = table.Column<long>(type: "bigint", nullable: false),
                    BaseCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    MigratedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_workspaces", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_financial_workspaces_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "financial_records",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: true),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    Ciphertext = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_records", x => new { x.UserId, x.Kind, x.Id });
                    table.CheckConstraint("ck_financial_record_envelope", "octet_length(\"Nonce\") = 12 AND octet_length(\"Ciphertext\") BETWEEN 17 AND 16384");
                    table.CheckConstraint("ck_financial_record_kind", "\"Kind\" BETWEEN 1 AND 8");
                    table.ForeignKey(
                        name: "FK_financial_records_financial_workspaces_UserId",
                        column: x => x.UserId,
                        principalTable: "financial_workspaces",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_records_UserId_Kind_AccountId_OccurredOn",
                table: "financial_records",
                columns: new[] { "UserId", "Kind", "AccountId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_records_UserId_Kind_CategoryId_OccurredOn",
                table: "financial_records",
                columns: new[] { "UserId", "Kind", "CategoryId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_records_UserId_Kind_OccurredOn_CreatedAtUtc_Id",
                table: "financial_records",
                columns: new[] { "UserId", "Kind", "OccurredOn", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "financial_records");

            migrationBuilder.DropTable(
                name: "financial_workspaces");
        }
    }
}
