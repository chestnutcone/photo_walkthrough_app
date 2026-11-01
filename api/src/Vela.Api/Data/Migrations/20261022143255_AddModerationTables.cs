using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vela.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    target_type = table.Column<string>(type: "text", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_actions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "banned_identities",
                columns: table => new
                {
                    provider = table.Column<string>(type: "text", nullable: false),
                    subject_hash = table.Column<string>(type: "text", nullable: false),
                    user_ref = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_banned_identities", x => new { x.provider, x.subject_hash });
                });

            migrationBuilder.CreateIndex(
                name: "ix_admin_actions_at_id",
                table: "admin_actions",
                columns: new[] { "at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_banned_identities_user_ref",
                table: "banned_identities",
                column: "user_ref");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_actions");

            migrationBuilder.DropTable(
                name: "banned_identities");
        }
    }
}
