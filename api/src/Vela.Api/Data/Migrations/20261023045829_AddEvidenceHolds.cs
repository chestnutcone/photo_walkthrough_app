using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vela.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEvidenceHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "evidence_holds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reply_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_type = table.Column<string>(type: "text", nullable: false),
                    blob_prefix = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    hold_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ncmec_report_id = table.Column<string>(type: "text", nullable: true),
                    ncmec_submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    created_by_admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidence_holds", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_holds_created_at_id",
                table: "evidence_holds",
                columns: new[] { "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_holds_hold_until",
                table: "evidence_holds",
                column: "hold_until");

            migrationBuilder.CreateIndex(
                name: "ux_evidence_holds_reply",
                table: "evidence_holds",
                column: "reply_id",
                unique: true,
                filter: "target_type = 'reply'");

            migrationBuilder.CreateIndex(
                name: "ux_evidence_holds_user",
                table: "evidence_holds",
                column: "user_id",
                unique: true,
                filter: "target_type = 'user'");

            migrationBuilder.CreateIndex(
                name: "ux_evidence_holds_work",
                table: "evidence_holds",
                column: "work_id",
                unique: true,
                filter: "target_type = 'work'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evidence_holds");
        }
    }
}
