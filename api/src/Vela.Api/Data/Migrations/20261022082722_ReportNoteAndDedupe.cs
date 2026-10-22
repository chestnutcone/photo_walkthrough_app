using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vela.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportNoteAndDedupe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "reports",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_reports_pending_auto_flag",
                table: "reports",
                columns: new[] { "target_type", "target_id" },
                unique: true,
                filter: "source = 'auto_flag' AND status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ux_reports_reporter_target",
                table: "reports",
                columns: new[] { "reporter_id", "target_type", "target_id" },
                unique: true,
                filter: "reporter_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_reports_pending_auto_flag",
                table: "reports");

            migrationBuilder.DropIndex(
                name: "ux_reports_reporter_target",
                table: "reports");

            migrationBuilder.DropColumn(
                name: "note",
                table: "reports");
        }
    }
}
