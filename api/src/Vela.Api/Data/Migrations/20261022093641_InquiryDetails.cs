using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vela.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InquiryDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inquiries_sender_id",
                table: "inquiries");

            migrationBuilder.AddColumn<string>(
                name: "client_ip",
                table: "inquiries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sender_email",
                table: "inquiries",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "type",
                table: "inquiries",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "work_id",
                table: "inquiries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_inquiries_client_ip_created_at",
                table: "inquiries",
                columns: new[] { "client_ip", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inquiries_sender_id_created_at",
                table: "inquiries",
                columns: new[] { "sender_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inquiries_client_ip_created_at",
                table: "inquiries");

            migrationBuilder.DropIndex(
                name: "ix_inquiries_sender_id_created_at",
                table: "inquiries");

            migrationBuilder.DropColumn(
                name: "client_ip",
                table: "inquiries");

            migrationBuilder.DropColumn(
                name: "sender_email",
                table: "inquiries");

            migrationBuilder.DropColumn(
                name: "type",
                table: "inquiries");

            migrationBuilder.DropColumn(
                name: "work_id",
                table: "inquiries");

            migrationBuilder.CreateIndex(
                name: "ix_inquiries_sender_id",
                table: "inquiries",
                column: "sender_id");
        }
    }
}
