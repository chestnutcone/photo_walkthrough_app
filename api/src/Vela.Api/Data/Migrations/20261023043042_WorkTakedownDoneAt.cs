using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vela.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkTakedownDoneAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "takedown_done_at",
                table: "works",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "takedown_done_at",
                table: "works");
        }
    }
}
