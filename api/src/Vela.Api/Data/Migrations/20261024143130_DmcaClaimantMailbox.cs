using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vela.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DmcaClaimantMailbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "claimant_mailbox",
                table: "dmca_notices",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_dmca_notices_claimant_mailbox_created_at",
                table: "dmca_notices",
                columns: new[] { "claimant_mailbox", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_dmca_notices_claimant_mailbox_created_at",
                table: "dmca_notices");

            migrationBuilder.DropColumn(
                name: "claimant_mailbox",
                table: "dmca_notices");
        }
    }
}
