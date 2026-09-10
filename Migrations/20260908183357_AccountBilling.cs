using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskApi.Migrations
{
    /// <inheritdoc />
    public partial class AccountBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Never silently choose/cancel one of several subscriptions. Stop the
            // transaction for explicit review, preserving every original row.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (
                    SELECT 1 FROM team_billing b JOIN teams t ON t."Id" = b."TeamId"
                    GROUP BY t."OwnerUserProfileId" HAVING count(*) > 1
                  ) THEN RAISE EXCEPTION 'Multiple team billing records for one account; review before migrating';
                  END IF;
                  IF EXISTS (SELECT 1 FROM team_billing WHERE "OperationUntil" > now())
                  THEN RAISE EXCEPTION 'Billing operation in progress; retry after it completes';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_billing_event_receipts_teams_TeamId",
                table: "billing_event_receipts");

            migrationBuilder.DropForeignKey(
                name: "FK_team_billing_teams_TeamId",
                table: "team_billing");

            migrationBuilder.DropIndex(
                name: "IX_billing_event_receipts_TeamId",
                table: "billing_event_receipts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_team_billing",
                table: "team_billing");

            migrationBuilder.RenameTable(
                name: "team_billing",
                newName: "account_billing");

            migrationBuilder.RenameIndex(
                name: "IX_team_billing_SubscriptionId",
                table: "account_billing",
                newName: "IX_account_billing_SubscriptionId");

            migrationBuilder.RenameIndex(
                name: "IX_team_billing_AttemptId",
                table: "account_billing",
                newName: "IX_account_billing_AttemptId");

            migrationBuilder.AddColumn<int>(
                name: "UserProfileId",
                table: "billing_event_receipts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserProfileId",
                table: "account_billing",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "MetadataScope",
                table: "account_billing",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "account");

            migrationBuilder.Sql("""
                UPDATE account_billing b SET "UserProfileId" = t."OwnerUserProfileId", "MetadataScope" = 'team'
                  FROM teams t WHERE t."Id" = b."TeamId";
                UPDATE billing_event_receipts r SET "UserProfileId" = t."OwnerUserProfileId"
                  FROM teams t WHERE t."Id" = r."TeamId";
                """);

            migrationBuilder.AddPrimaryKey(
                name: "PK_account_billing",
                table: "account_billing",
                column: "UserProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_event_receipts_UserProfileId",
                table: "billing_event_receipts",
                column: "UserProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_account_billing_user_profiles_UserProfileId",
                table: "account_billing",
                column: "UserProfileId",
                principalTable: "user_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_billing_event_receipts_user_profiles_UserProfileId",
                table: "billing_event_receipts",
                column: "UserProfileId",
                principalTable: "user_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Automatic rollback is safe only before account-scoped activity.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM account_billing b LEFT JOIN teams t ON t."Id" = b."TeamId"
                    WHERE b."MetadataScope" <> 'team' OR t."Id" IS NULL OR t."OwnerUserProfileId" <> b."UserProfileId")
                    OR EXISTS (SELECT 1 FROM billing_event_receipts r LEFT JOIN teams t ON t."Id" = r."TeamId"
                      WHERE t."Id" IS NULL OR t."OwnerUserProfileId" <> r."UserProfileId")
                  THEN RAISE EXCEPTION 'Account activity prevents automatic downgrade; use a reviewed backup recovery';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_account_billing_user_profiles_UserProfileId",
                table: "account_billing");

            migrationBuilder.DropForeignKey(
                name: "FK_billing_event_receipts_user_profiles_UserProfileId",
                table: "billing_event_receipts");

            migrationBuilder.DropIndex(
                name: "IX_billing_event_receipts_UserProfileId",
                table: "billing_event_receipts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_account_billing",
                table: "account_billing");

            migrationBuilder.DropColumn(
                name: "UserProfileId",
                table: "billing_event_receipts");

            migrationBuilder.DropColumn(
                name: "UserProfileId",
                table: "account_billing");

            migrationBuilder.DropColumn(
                name: "MetadataScope",
                table: "account_billing");

            migrationBuilder.RenameTable(
                name: "account_billing",
                newName: "team_billing");

            migrationBuilder.RenameIndex(
                name: "IX_account_billing_SubscriptionId",
                table: "team_billing",
                newName: "IX_team_billing_SubscriptionId");

            migrationBuilder.RenameIndex(
                name: "IX_account_billing_AttemptId",
                table: "team_billing",
                newName: "IX_team_billing_AttemptId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_team_billing",
                table: "team_billing",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_event_receipts_TeamId",
                table: "billing_event_receipts",
                column: "TeamId");

            migrationBuilder.AddForeignKey(
                name: "FK_billing_event_receipts_teams_TeamId",
                table: "billing_event_receipts",
                column: "TeamId",
                principalTable: "teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_team_billing_teams_TeamId",
                table: "team_billing",
                column: "TeamId",
                principalTable: "teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
