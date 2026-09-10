using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TaskApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskTagsAndRetroTheme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_user_profiles_theme",
                table: "user_profiles");

            migrationBuilder.CreateTable(
                name: "task_tag_definitions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_profile_id = table.Column<int>(type: "integer", nullable: true),
                    team_id = table.Column<int>(type: "integer", nullable: true),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_tag_definitions", x => x.id);
                    table.CheckConstraint("CK_task_tag_definitions_name", "char_length(name) BETWEEN 1 AND 300 AND char_length(normalized_name) BETWEEN 1 AND 300");
                    table.CheckConstraint("CK_task_tag_definitions_scope", "(user_profile_id IS NOT NULL AND team_id IS NULL) OR (user_profile_id IS NULL AND team_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_task_tag_definitions_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_task_tag_definitions_user_profiles_user_profile_id",
                        column: x => x.user_profile_id,
                        principalTable: "user_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_user_profiles_theme",
                table: "user_profiles",
                sql: "theme IN ('classic', 'retro', 'light', 'dark', 'forest', 'sunset')");

            migrationBuilder.CreateIndex(
                name: "IX_task_tag_definitions_team_id_normalized_name",
                table: "task_tag_definitions",
                columns: new[] { "team_id", "normalized_name" },
                unique: true,
                filter: "team_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_task_tag_definitions_user_profile_id_normalized_name",
                table: "task_tag_definitions",
                columns: new[] { "user_profile_id", "normalized_name" },
                unique: true,
                filter: "user_profile_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$
                BEGIN
                    IF EXISTS (SELECT 1 FROM task_tag_definitions)
                        OR EXISTS (SELECT 1 FROM user_profiles WHERE theme = 'retro') THEN
                        RAISE EXCEPTION 'Classification tags or retro preferences exist. Restore a reviewed backup rather than discard saved choices.';
                    END IF;
                END;
                $guard$;
                """);

            migrationBuilder.DropTable(
                name: "task_tag_definitions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_user_profiles_theme",
                table: "user_profiles");

            migrationBuilder.AddCheckConstraint(
                name: "CK_user_profiles_theme",
                table: "user_profiles",
                sql: "theme IN ('classic', 'light', 'dark', 'forest', 'sunset')");
        }
    }
}
