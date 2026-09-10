using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskApi.Migrations
{
    /// <inheritdoc />
    public partial class DesktopBrowserSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "desktop_sign_ins",
                columns: table => new
                {
                    DeviceCodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserCodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserProfileId = table.Column<int>(type: "integer", nullable: true),
                    SessionVersion = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    Denied = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_desktop_sign_ins", x => x.DeviceCodeHash);
                    table.ForeignKey(
                        name: "FK_desktop_sign_ins_user_profiles_UserProfileId",
                        column: x => x.UserProfileId,
                        principalTable: "user_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_desktop_sign_ins_ExpiresAt",
                table: "desktop_sign_ins",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_desktop_sign_ins_UserCodeHash",
                table: "desktop_sign_ins",
                column: "UserCodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_desktop_sign_ins_UserProfileId",
                table: "desktop_sign_ins",
                column: "UserProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "desktop_sign_ins");
        }
    }
}
