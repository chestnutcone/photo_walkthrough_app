using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vela.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SchemaConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_reports_users_reporter_id",
                table: "reports");

            migrationBuilder.CreateIndex(
                name: "ix_works_artist_id_published_at_id",
                table: "works",
                columns: new[] { "artist_id", "published_at", "id" },
                filter: "status = 'published'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_chapters_index_range",
                table: "chapters",
                sql: "\"index\" BETWEEN 0 AND 39");

            migrationBuilder.AddCheckConstraint(
                name: "ck_chapters_x_range",
                table: "chapters",
                sql: "x BETWEEN 0 AND 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_chapters_y_range",
                table: "chapters",
                sql: "y BETWEEN 0 AND 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_chapters_zoom_positive",
                table: "chapters",
                sql: "zoom IS NULL OR zoom > 0");

            migrationBuilder.AddForeignKey(
                name: "fk_reports_users_reporter_id",
                table: "reports",
                column: "reporter_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_reports_users_reporter_id",
                table: "reports");

            migrationBuilder.DropIndex(
                name: "ix_works_artist_id_published_at_id",
                table: "works");

            migrationBuilder.DropCheckConstraint(
                name: "ck_chapters_index_range",
                table: "chapters");

            migrationBuilder.DropCheckConstraint(
                name: "ck_chapters_x_range",
                table: "chapters");

            migrationBuilder.DropCheckConstraint(
                name: "ck_chapters_y_range",
                table: "chapters");

            migrationBuilder.DropCheckConstraint(
                name: "ck_chapters_zoom_positive",
                table: "chapters");

            migrationBuilder.AddForeignKey(
                name: "fk_reports_users_reporter_id",
                table: "reports",
                column: "reporter_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
