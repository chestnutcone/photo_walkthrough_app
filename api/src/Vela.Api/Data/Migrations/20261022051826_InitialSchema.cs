using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vela.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    code_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_codes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tiers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    max_works = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tiers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    suspended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_admin = table.Column<bool>(type: "boolean", nullable: false),
                    age_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    terms_accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_tiers_tier_id",
                        column: x => x.tier_id,
                        principalTable: "tiers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "blocks",
                columns: table => new
                {
                    blocker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocked_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blocks", x => new { x.blocker_id, x.blocked_id });
                    table.ForeignKey(
                        name: "fk_blocks_users_blocked_id",
                        column: x => x.blocked_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_blocks_users_blocker_id",
                        column: x => x.blocker_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "export_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archive_blob = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_export_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_export_jobs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "follows",
                columns: table => new
                {
                    follower_id = table.Column<Guid>(type: "uuid", nullable: false),
                    followee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follows", x => new { x.follower_id, x.followee_id });
                    table.ForeignKey(
                        name: "fk_follows_users_followee_id",
                        column: x => x.followee_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_follows_users_follower_id",
                        column: x => x.follower_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identities", x => x.id);
                    table.ForeignKey(
                        name: "fk_identities_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inquiries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    artist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    emailed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inquiries", x => x.id);
                    table.ForeignKey(
                        name: "fk_inquiries_users_artist_id",
                        column: x => x.artist_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inquiries_users_sender_id",
                        column: x => x.sender_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_id = table.Column<Guid>(type: "uuid", nullable: true),
                    chapter_index = table.Column<int>(type: "integer", nullable: true),
                    reply_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "profiles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handle = table.Column<string>(type: "text", nullable: false),
                    handle_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    bio = table.Column<string>(type: "text", nullable: false),
                    city = table.Column<string>(type: "text", nullable: false),
                    discipline = table.Column<string>(type: "text", nullable: false),
                    availability = table.Column<string>(type: "text", nullable: false),
                    avatar_blob = table.Column<string>(type: "text", nullable: true),
                    website = table.Column<string>(type: "text", nullable: false),
                    social = table.Column<string>(type: "text", nullable: false),
                    studio = table.Column<string>(type: "text", nullable: false),
                    reply_time = table.Column<string>(type: "text", nullable: false),
                    public_email = table.Column<string>(type: "text", nullable: true),
                    services = table.Column<string>(type: "jsonb", nullable: false),
                    shows = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profiles", x => x.user_id);
                    table.CheckConstraint("ck_profiles_handle_lowercase", "handle = lower(handle)");
                    table.ForeignKey(
                        name: "fk_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    target_type = table.Column<string>(type: "text", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_reports_users_reporter_id",
                        column: x => x.reporter_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "works",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    artist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    intro = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    report_hidden = table.Column<bool>(type: "boolean", nullable: false),
                    moderation_override = table.Column<bool>(type: "boolean", nullable: false),
                    image_blob = table.Column<string>(type: "text", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    alt_text = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_works", x => x.id);
                    table.ForeignKey(
                        name: "fk_works_users_artist_id",
                        column: x => x.artist_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chapters",
                columns: table => new
                {
                    work_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    x = table.Column<double>(type: "double precision", nullable: false),
                    y = table.Column<double>(type: "double precision", nullable: false),
                    zoom = table.Column<double>(type: "double precision", nullable: true),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    meta = table.Column<string>(type: "text", nullable: true),
                    alt_text = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chapters", x => new { x.work_id, x.index });
                    table.ForeignKey(
                        name: "fk_chapters_works_work_id",
                        column: x => x.work_id,
                        principalTable: "works",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "replies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_index = table.Column<int>(type: "integer", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    hidden = table.Column<bool>(type: "boolean", nullable: false),
                    hidden_by = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_replies", x => x.id);
                    table.ForeignKey(
                        name: "fk_replies_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_replies_works_work_id",
                        column: x => x.work_id,
                        principalTable: "works",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "tiers",
                columns: new[] { "id", "max_works", "name" },
                values: new object[] { new Guid("5b0c1c3e-7a3d-4c55-9a52-1f0e2d6b7a01"), 20, "Freemium" });

            migrationBuilder.CreateIndex(
                name: "ix_blocks_blocked_id",
                table: "blocks",
                column: "blocked_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_codes_email_created_at",
                table: "email_codes",
                columns: new[] { "email", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_export_jobs_user_id_requested_at",
                table: "export_jobs",
                columns: new[] { "user_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_follows_followee_id",
                table: "follows",
                column: "followee_id");

            migrationBuilder.CreateIndex(
                name: "ix_identities_provider_subject",
                table: "identities",
                columns: new[] { "provider", "subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identities_user_id",
                table: "identities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_inquiries_artist_id",
                table: "inquiries",
                column: "artist_id");

            migrationBuilder.CreateIndex(
                name: "ix_inquiries_sender_id",
                table: "inquiries",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_created_at_id",
                table: "notifications",
                columns: new[] { "user_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_profiles_handle",
                table: "profiles",
                column: "handle",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_family_id",
                table: "refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_replies_author_id",
                table: "replies",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_replies_work_id_chapter_index_created_at",
                table: "replies",
                columns: new[] { "work_id", "chapter_index", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reports_reporter_id_created_at",
                table: "reports",
                columns: new[] { "reporter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reports_status_created_at",
                table: "reports",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tiers_name",
                table: "tiers",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_tier_id",
                table: "users",
                column: "tier_id");

            migrationBuilder.CreateIndex(
                name: "ix_works_artist_id_status",
                table: "works",
                columns: new[] { "artist_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_works_status_published_at_id",
                table: "works",
                columns: new[] { "status", "published_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blocks");

            migrationBuilder.DropTable(
                name: "chapters");

            migrationBuilder.DropTable(
                name: "email_codes");

            migrationBuilder.DropTable(
                name: "export_jobs");

            migrationBuilder.DropTable(
                name: "follows");

            migrationBuilder.DropTable(
                name: "identities");

            migrationBuilder.DropTable(
                name: "inquiries");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "profiles");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "replies");

            migrationBuilder.DropTable(
                name: "reports");

            migrationBuilder.DropTable(
                name: "works");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "tiers");
        }
    }
}
