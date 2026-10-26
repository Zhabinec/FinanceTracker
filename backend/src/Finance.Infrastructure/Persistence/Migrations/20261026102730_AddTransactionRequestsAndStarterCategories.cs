using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionRequestsAndStarterCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transaction_requests",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_requests", x => new { x.UserId, x.RequestId });
                    table.ForeignKey(
                        name: "FK_transaction_requests_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            // Seed only completely empty category lists; custom and archived categories are left untouched.
            migrationBuilder.Sql("""
                INSERT INTO categories ("Id", "UserId", "Name", "NormalizedName", "Type", "IsActive")
                SELECT gen_random_uuid(), u."Id", starter.name, starter.normalized, starter.type, true
                FROM users u
                CROSS JOIN (VALUES
                    ('Продукты', 'ПРОДУКТЫ', 2),
                    ('Транспорт', 'ТРАНСПОРТ', 2),
                    ('Дом', 'ДОМ', 2),
                    ('Здоровье', 'ЗДОРОВЬЕ', 2),
                    ('Развлечения', 'РАЗВЛЕЧЕНИЯ', 2),
                    ('Зарплата', 'ЗАРПЛАТА', 1),
                    ('Подработка', 'ПОДРАБОТКА', 1)
                ) AS starter(name, normalized, type)
                WHERE NOT EXISTS (SELECT 1 FROM categories c WHERE c."UserId" = u."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transaction_requests");
        }
    }
}
