using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vela.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeletionDeferral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deletion_deferred_until",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deletion_deferred_until",
                table: "users");
        }
    }
}
