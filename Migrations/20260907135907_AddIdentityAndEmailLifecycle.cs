using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TaskApi.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityAndEmailLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_profiles_auth_token_hash",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "auth_token_hash",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "auth_token_expires_at",
                table: "user_profiles");

            // An old token expiry must never be interpreted as legal consent.
            migrationBuilder.AddColumn<DateTime>(
                name: "TermsAcceptedAt",
                table: "user_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcceptedTermsVersion",
                table: "user_profiles",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AccessFailedCount",
                table: "user_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AcknowledgedPrivacyVersion",
                table: "user_profiles",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyStamp",
                table: "user_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "user_profiles",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailConfirmed",
                table: "user_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastConfirmationEmailAt",
                table: "user_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastResetEmailAt",
                table: "user_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LockoutEnabled",
                table: "user_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockoutEnd",
                table: "user_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                table: "user_profiles",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedUserName",
                table: "user_profiles",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "user_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PhoneNumberConfirmed",
                table: "user_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "user_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SessionVersion",
                table: "user_profiles",
                type: "character varying(36)",
                maxLength: 36,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "TwoFactorEnabled",
                table: "user_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UserName",
                table: "user_profiles",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_user_profiles_UserId",
                        column: x => x.UserId,
                        principalTable: "user_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_user_profiles_UserId",
                        column: x => x.UserId,
                        principalTable: "user_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_user_profiles_UserId",
                        column: x => x.UserId,
                        principalTable: "user_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_outbox",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserProfileId = table.Column<int>(type: "integer", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ProtectedPayload = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_outbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_outbox_user_profiles_UserProfileId",
                        column: x => x.UserProfileId,
                        principalTable: "user_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "user_profiles",
                column: "NormalizedEmail",
                unique: true);

            // Preserve IDs, password hashes and all task/pet foreign keys. No synthetic email
            // or inferred consent is assigned; existing users complete setup after signing in.
            migrationBuilder.Sql("""
                UPDATE user_profiles
                SET "UserName" = user_key,
                    "NormalizedUserName" = upper(user_key),
                    "SecurityStamp" = gen_random_uuid()::text,
                    "ConcurrencyStamp" = gen_random_uuid()::text,
                    "SessionVersion" = gen_random_uuid()::text,
                    "LockoutEnabled" = (password_hash IS NOT NULL);
                """);

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "user_profiles",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_email_outbox_NextAttemptAt",
                table: "email_outbox",
                column: "NextAttemptAt");

            migrationBuilder.CreateIndex(
                name: "IX_email_outbox_UserProfileId_Purpose",
                table: "email_outbox",
                columns: new[] { "UserProfileId", "Purpose" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "email_outbox");

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "user_profiles");

            migrationBuilder.DropIndex(
                name: "UserNameIndex",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "AcceptedTermsVersion",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "AccessFailedCount",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "AcknowledgedPrivacyVersion",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "EmailConfirmed",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "LastConfirmationEmailAt",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "LastResetEmailAt",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "LockoutEnabled",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "LockoutEnd",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "NormalizedUserName",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "PhoneNumberConfirmed",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "SessionVersion",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "TwoFactorEnabled",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "UserName",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedAt",
                table: "user_profiles");

            migrationBuilder.AddColumn<DateTime>(
                name: "auth_token_expires_at",
                table: "user_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "auth_token_hash",
                table: "user_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_profiles_auth_token_hash",
                table: "user_profiles",
                column: "auth_token_hash",
                unique: true);
        }
    }
}
