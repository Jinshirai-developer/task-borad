using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamTestBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_event_receipts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_event_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_billing_event_receipts_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "team_billing",
                columns: table => new
                {
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    PriceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    MonthlyYen = table.Column<int>(type: "integer", nullable: false),
                    ReturnOrigin = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    AttemptStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SessionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SubscriptionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PaidThrough = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelAtPeriodEnd = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OperationToken = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    OperationUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_billing", x => x.TeamId);
                    table.ForeignKey(
                        name: "FK_team_billing_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_event_receipts_ProcessedAt",
                table: "billing_event_receipts",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_billing_event_receipts_TeamId",
                table: "billing_event_receipts",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_team_billing_AttemptId",
                table: "team_billing",
                column: "AttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_billing_SubscriptionId",
                table: "team_billing",
                column: "SubscriptionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_event_receipts");

            migrationBuilder.DropTable(
                name: "team_billing");
        }
    }
}
