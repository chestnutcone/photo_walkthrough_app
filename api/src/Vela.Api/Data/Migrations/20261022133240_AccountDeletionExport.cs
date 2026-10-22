using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vela.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccountDeletionExport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deletion_failed_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "deletion_failures",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                table: "export_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "started_at",
                table: "export_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_deleted_at_pending",
                table: "users",
                column: "deleted_at",
                filter: "deleted_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_export_jobs_status_requested_at",
                table: "export_jobs",
                columns: new[] { "status", "requested_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_deleted_at_pending",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_export_jobs_status_requested_at",
                table: "export_jobs");

            migrationBuilder.DropColumn(
                name: "deletion_failed_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "deletion_failures",
                table: "users");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "export_jobs");

            migrationBuilder.DropColumn(
                name: "started_at",
                table: "export_jobs");
        }
    }
}
