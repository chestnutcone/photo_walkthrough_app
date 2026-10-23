using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vela.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDmcaNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dmca_notices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    claimant_name = table.Column<string>(type: "text", nullable: false),
                    claimant_email = table.Column<string>(type: "text", nullable: false),
                    claimant_address = table.Column<string>(type: "text", nullable: false),
                    claimant_phone = table.Column<string>(type: "text", nullable: true),
                    work_reference = table.Column<string>(type: "text", nullable: false),
                    original_work_description = table.Column<string>(type: "text", nullable: false),
                    signature_name = table.Column<string>(type: "text", nullable: false),
                    good_faith_statement = table.Column<bool>(type: "boolean", nullable: false),
                    perjury_statement = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    takedown_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    counter_name = table.Column<string>(type: "text", nullable: true),
                    counter_address = table.Column<string>(type: "text", nullable: true),
                    counter_phone = table.Column<string>(type: "text", nullable: true),
                    counter_statement = table.Column<string>(type: "text", nullable: true),
                    counter_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    forwarded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    client_ip = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dmca_notices", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dmca_notices_client_ip_created_at",
                table: "dmca_notices",
                columns: new[] { "client_ip", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_dmca_notices_created_at_id",
                table: "dmca_notices",
                columns: new[] { "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_dmca_notices_owner_user_id_status",
                table: "dmca_notices",
                columns: new[] { "owner_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_dmca_notices_work_id_status",
                table: "dmca_notices",
                columns: new[] { "work_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dmca_notices");
        }
    }
}
