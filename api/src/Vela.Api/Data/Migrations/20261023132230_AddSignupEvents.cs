using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vela.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSignupEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "signup_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ip_key = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signup_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_signup_events_created_at",
                table: "signup_events",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_signup_events_ip_key_created_at",
                table: "signup_events",
                columns: new[] { "ip_key", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "signup_events");
        }
    }
}
