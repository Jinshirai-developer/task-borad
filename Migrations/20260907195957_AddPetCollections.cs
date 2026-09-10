using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPetCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pet_collections",
                columns: table => new
                {
                    PetProfileId = table.Column<int>(type: "integer", nullable: false),
                    Stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HatLevel = table.Column<int>(type: "integer", nullable: true),
                    BowLevel = table.Column<int>(type: "integer", nullable: true),
                    MatLevel = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pet_collections", x => x.PetProfileId);
                    table.CheckConstraint("CK_pet_collections_levels", "(\"HatLevel\" IS NULL OR \"HatLevel\" BETWEEN 1 AND 20) AND (\"BowLevel\" IS NULL OR \"BowLevel\" BETWEEN 1 AND 20) AND (\"MatLevel\" IS NULL OR \"MatLevel\" BETWEEN 1 AND 20)");
                    table.CheckConstraint("CK_pet_collections_stage", "\"Stage\" IN ('base','explorer','grown','festival')");
                    table.ForeignKey(
                        name: "FK_pet_collections_pet_profiles_PetProfileId",
                        column: x => x.PetProfileId,
                        principalTable: "pet_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pet_memories",
                columns: table => new
                {
                    PetProfileId = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UnlockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pet_memories", x => new { x.PetProfileId, x.Key });
                    table.ForeignKey(
                        name: "FK_pet_memories_pet_profiles_PetProfileId",
                        column: x => x.PetProfileId,
                        principalTable: "pet_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pet_reward_choices",
                columns: table => new
                {
                    PetProfileId = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Choice = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pet_reward_choices", x => new { x.PetProfileId, x.Level });
                    table.CheckConstraint("CK_pet_reward_choices_choice", "\"Choice\" IN ('hat','bow','mat')");
                    table.CheckConstraint("CK_pet_reward_choices_level", "\"Level\" BETWEEN 1 AND 20");
                    table.ForeignKey(
                        name: "FK_pet_reward_choices_pet_profiles_PetProfileId",
                        column: x => x.PetProfileId,
                        principalTable: "pet_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pet_collections");

            migrationBuilder.DropTable(
                name: "pet_memories");

            migrationBuilder.DropTable(
                name: "pet_reward_choices");
        }
    }
}
