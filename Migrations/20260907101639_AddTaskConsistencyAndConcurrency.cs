using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskConsistencyAndConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE tasks SET is_completed = (\"Status\" = 2) "
                + "WHERE is_completed IS DISTINCT FROM (\"Status\" = 2);");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "tasks",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "pet_profiles",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddCheckConstraint(
                name: "CK_tasks_completion_status",
                table: "tasks",
                sql: "is_completed = (\"Status\" = 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_tasks_completion_status",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "pet_profiles");
        }
    }
}
