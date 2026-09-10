using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskUsability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "assignee_user_profile_id",
                table: "tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "checklist_json",
                table: "tasks",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateTable(
                name: "task_undo_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserProfileId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: true),
                    TaskId = table.Column<int>(type: "integer", nullable: false),
                    WasDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Snapshot = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    RewardId = table.Column<int>(type: "integer", nullable: true),
                    ExpectedVersion = table.Column<long>(type: "bigint", nullable: false),
                    ExpectedUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_undo_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_undo_entries_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_task_undo_entries_user_profiles_UserProfileId",
                        column: x => x.UserProfileId,
                        principalTable: "user_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_assignee_user_profile_id",
                table: "tasks",
                column: "assignee_user_profile_id");

            migrationBuilder.CreateIndex(
                name: "IX_task_undo_entries_ExpiresAt",
                table: "task_undo_entries",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_task_undo_entries_TeamId",
                table: "task_undo_entries",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_task_undo_entries_UserProfileId_ExpiresAt",
                table: "task_undo_entries",
                columns: new[] { "UserProfileId", "ExpiresAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_user_profiles_assignee_user_profile_id",
                table: "tasks",
                column: "assignee_user_profile_id",
                principalTable: "user_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tasks_user_profiles_assignee_user_profile_id",
                table: "tasks");

            migrationBuilder.DropTable(
                name: "task_undo_entries");

            migrationBuilder.DropIndex(
                name: "IX_tasks_assignee_user_profile_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "assignee_user_profile_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "checklist_json",
                table: "tasks");
        }
    }
}
