using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TaskApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamsAndPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tasks_user_profiles_user_profile_id",
                table: "tasks");

            migrationBuilder.AddColumn<string>(
                name: "layout",
                table: "user_profiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "board");

            migrationBuilder.AddColumn<string>(
                name: "theme",
                table: "user_profiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "classic");

            migrationBuilder.AlterColumn<int>(
                name: "user_profile_id",
                table: "tasks",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "team_id",
                table: "tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "species",
                table: "pet_profiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "dog");

            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OwnerUserProfileId = table.Column<int>(type: "integer", nullable: false),
                    InviteCodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InviteExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_teams_user_profiles_OwnerUserProfileId",
                        column: x => x.OwnerUserProfileId,
                        principalTable: "user_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "team_members",
                columns: table => new
                {
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    UserProfileId = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_members", x => new { x.TeamId, x.UserProfileId });
                    table.ForeignKey(
                        name: "FK_team_members_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_team_members_user_profiles_UserProfileId",
                        column: x => x.UserProfileId,
                        principalTable: "user_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_user_profiles_layout",
                table: "user_profiles",
                sql: "layout IN ('board', 'list')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_user_profiles_theme",
                table: "user_profiles",
                sql: "theme IN ('classic', 'light', 'dark')");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_team_id",
                table: "tasks",
                column: "team_id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tasks_owner_or_team",
                table: "tasks",
                sql: "user_profile_id IS NOT NULL OR team_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_pet_profiles_species",
                table: "pet_profiles",
                sql: "species IN ('dog', 'cat', 'rabbit')");

            migrationBuilder.CreateIndex(
                name: "IX_team_members_UserProfileId",
                table: "team_members",
                column: "UserProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_teams_InviteCodeHash",
                table: "teams",
                column: "InviteCodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_teams_OwnerUserProfileId",
                table: "teams",
                column: "OwnerUserProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_teams_team_id",
                table: "tasks",
                column: "team_id",
                principalTable: "teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_user_profiles_user_profile_id",
                table: "tasks",
                column: "user_profile_id",
                principalTable: "user_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never turn shared tasks into private tasks or discard team membership implicitly.
            migrationBuilder.Sql("""
                DO $guard$
                BEGIN
                    IF EXISTS (SELECT 1 FROM teams) OR EXISTS (SELECT 1 FROM tasks WHERE team_id IS NOT NULL) THEN
                        RAISE EXCEPTION 'Teams exist. Restore a reviewed pre-migration backup instead of downgrading this database.';
                    END IF;
                END;
                $guard$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_tasks_teams_team_id",
                table: "tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_tasks_user_profiles_user_profile_id",
                table: "tasks");

            migrationBuilder.DropTable(
                name: "team_members");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropCheckConstraint(
                name: "CK_user_profiles_layout",
                table: "user_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_user_profiles_theme",
                table: "user_profiles");

            migrationBuilder.DropIndex(
                name: "IX_tasks_team_id",
                table: "tasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tasks_owner_or_team",
                table: "tasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_pet_profiles_species",
                table: "pet_profiles");

            migrationBuilder.DropColumn(
                name: "layout",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "theme",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "team_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "species",
                table: "pet_profiles");

            migrationBuilder.AlterColumn<int>(
                name: "user_profile_id",
                table: "tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_user_profiles_user_profile_id",
                table: "tasks",
                column: "user_profile_id",
                principalTable: "user_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
