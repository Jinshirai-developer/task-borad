using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TaskApi.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_profiles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_profiles", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "user_profiles",
                columns: new[] { "id", "user_key", "display_name", "created_at", "updated_at" },
                values: new object[] { 1, "guest", "Guest", new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.AddColumn<int>(
                name: "user_profile_id",
                table: "tasks",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "user_profile_id",
                table: "pet_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_tasks_user_profile_id",
                table: "tasks",
                column: "user_profile_id");

            migrationBuilder.CreateIndex(
                name: "IX_pet_profiles_user_profile_id",
                table: "pet_profiles",
                column: "user_profile_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_profiles_user_key",
                table: "user_profiles",
                column: "user_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_profiles");

            migrationBuilder.DropIndex(
                name: "IX_tasks_user_profile_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_pet_profiles_user_profile_id",
                table: "pet_profiles");

            migrationBuilder.DropColumn(
                name: "user_profile_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "user_profile_id",
                table: "pet_profiles");
        }
    }
}
