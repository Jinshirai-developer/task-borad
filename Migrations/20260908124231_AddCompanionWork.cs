using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanionWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "companion_json",
                table: "tasks",
                type: "character varying(120000)",
                maxLength: 120000,
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AlterColumn<string>(
                name: "Snapshot",
                table: "task_undo_entries",
                type: "character varying(1000000)",
                maxLength: 1000000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(65536)",
                oldMaxLength: 65536);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "companion_json",
                table: "tasks");

            migrationBuilder.AlterColumn<string>(
                name: "Snapshot",
                table: "task_undo_entries",
                type: "character varying(65536)",
                maxLength: 65536,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000000)",
                oldMaxLength: 1000000);
        }
    }
}
