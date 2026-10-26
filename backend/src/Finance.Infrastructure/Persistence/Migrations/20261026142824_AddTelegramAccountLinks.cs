using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramAccountLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TelegramLinkVersion",
                table: "refresh_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "telegram_account_links",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_account_links", x => x.UserId);
                    table.CheckConstraint("ck_telegram_user_id_positive", "\"TelegramUserId\" > 0 AND \"TelegramUserId\" <= 4503599627370495");
                    table.ForeignKey(
                        name: "FK_telegram_account_links_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_telegram_account_links_TelegramUserId",
                table: "telegram_account_links",
                column: "TelegramUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telegram_account_links");

            migrationBuilder.DropColumn(
                name: "TelegramLinkVersion",
                table: "refresh_sessions");
        }
    }
}
