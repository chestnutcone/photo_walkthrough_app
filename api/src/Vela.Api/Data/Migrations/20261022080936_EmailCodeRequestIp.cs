using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vela.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EmailCodeRequestIp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "request_ip",
                table: "email_codes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_email_codes_request_ip_created_at",
                table: "email_codes",
                columns: new[] { "request_ip", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_email_codes_request_ip_created_at",
                table: "email_codes");

            migrationBuilder.DropColumn(
                name: "request_ip",
                table: "email_codes");
        }
    }
}
