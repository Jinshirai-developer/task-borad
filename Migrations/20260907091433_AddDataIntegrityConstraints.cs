using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskApi.Migrations
{
    /// <inheritdoc />
    public partial class AddDataIntegrityConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_user_profiles_auth_token_hash",
                table: "user_profiles",
                column: "auth_token_hash",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_tasks_priority",
                table: "tasks",
                sql: "priority IN (0, 1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tasks_status",
                table: "tasks",
                sql: "\"Status\" IN (0, 1, 2)");

            migrationBuilder.AddForeignKey(
                name: "FK_pet_profiles_user_profiles_user_profile_id",
                table: "pet_profiles",
                column: "user_profile_id",
                principalTable: "user_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_user_profiles_user_profile_id",
                table: "tasks",
                column: "user_profile_id",
                principalTable: "user_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_pet_profiles_user_profiles_user_profile_id",
                table: "pet_profiles");

            migrationBuilder.DropForeignKey(
                name: "FK_tasks_user_profiles_user_profile_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_user_profiles_auth_token_hash",
                table: "user_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tasks_priority",
                table: "tasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tasks_status",
                table: "tasks");
        }
    }
}
