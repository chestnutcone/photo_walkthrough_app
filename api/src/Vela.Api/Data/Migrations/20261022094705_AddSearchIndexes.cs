using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vela.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AddColumn<string>(
                name: "search_text",
                table: "works",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Story search text = lower-cased title, intro and chapter titles. Published works cannot be edited, so the text is
            // effectively fixed once a story is visible; triggers keep it right on every write path (publish, restore, seeding).
            migrationBuilder.Sql("""
                CREATE FUNCTION works_search_text(p_id uuid, p_title text, p_intro text) RETURNS text
                LANGUAGE sql STABLE AS $$
                    SELECT lower(p_title || ' ' || p_intro || ' ' || coalesce(
                        (SELECT string_agg(c.title, ' ' ORDER BY c."index") FROM chapters c WHERE c.work_id = p_id), ''))
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION works_set_search_text() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    NEW.search_text := works_search_text(NEW.id, NEW.title, NEW.intro);
                    RETURN NEW;
                END
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_works_search_text BEFORE INSERT OR UPDATE OF title, intro ON works
                FOR EACH ROW EXECUTE FUNCTION works_set_search_text();
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION chapters_refresh_work_search_text() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        UPDATE works SET search_text = works_search_text(id, title, intro) WHERE id = OLD.work_id;
                    END IF;
                    IF TG_OP <> 'DELETE' AND (TG_OP = 'INSERT' OR NEW.work_id <> OLD.work_id) THEN
                        UPDATE works SET search_text = works_search_text(id, title, intro) WHERE id = NEW.work_id;
                    END IF;
                    RETURN NULL;
                END
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_chapters_search_text AFTER INSERT OR UPDATE OF title, work_id OR DELETE ON chapters
                FOR EACH ROW EXECUTE FUNCTION chapters_refresh_work_search_text();
                """);
            migrationBuilder.Sql("UPDATE works SET search_text = works_search_text(id, title, intro);");

            migrationBuilder.AddColumn<string>(
                name: "search_text",
                table: "profiles",
                type: "text",
                nullable: false,
                computedColumnSql: "lower(handle || ' ' || display_name || ' ' || city || ' ' || discipline)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_works_search_text_trgm",
                table: "works",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_profiles_search_text_trgm",
                table: "profiles",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_chapters_search_text ON chapters;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_works_search_text ON works;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS chapters_refresh_work_search_text();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS works_set_search_text();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS works_search_text(uuid, text, text);");

            migrationBuilder.DropIndex(
                name: "ix_works_search_text_trgm",
                table: "works");

            migrationBuilder.DropIndex(
                name: "ix_profiles_search_text_trgm",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "search_text",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "search_text",
                table: "works");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
