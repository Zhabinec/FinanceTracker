using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowTelegramOnlyUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "NormalizedEmail",
                table: "users",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "users",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320);

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_email_pair",
                table: "users",
                sql: "(\"Email\" IS NULL AND \"NormalizedEmail\" IS NULL) OR (\"Email\" IS NOT NULL AND \"NormalizedEmail\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM users WHERE "Email" IS NULL OR "NormalizedEmail" IS NULL) THEN
                    RAISE EXCEPTION 'Cannot downgrade while Telegram-only accounts exist. No emails were changed.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_email_pair",
                table: "users");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedEmail",
                table: "users",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "users",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320,
                oldNullable: true);
        }
    }
}
