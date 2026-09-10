using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TaskApi.Migrations
{
    /// <inheritdoc />
    public partial class AddReversibleRewardsAndUnlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_user_profiles_layout",
                table: "user_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_user_profiles_theme",
                table: "user_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_pet_profiles_species",
                table: "pet_profiles");

            migrationBuilder.AddColumn<DateTime>(
                name: "legacy_last_completed_at",
                table: "pet_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "legacy_streak_days",
                table: "pet_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "completion_rewards",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    task_id = table.Column<int>(type: "integer", nullable: true),
                    user_profile_id = table.Column<int>(type: "integer", nullable: true),
                    awarded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    experience = table.Column<int>(type: "integer", nullable: false),
                    energy_granted = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_completion_rewards", x => x.id);
                    table.CheckConstraint("CK_completion_rewards_dates", "revoked_at IS NULL OR revoked_at >= awarded_at");
                    table.CheckConstraint("CK_completion_rewards_energy", "energy_granted BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_completion_rewards_experience", "experience = 25");
                    table.ForeignKey(
                        name: "FK_completion_rewards_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_completion_rewards_user_profiles_user_profile_id",
                        column: x => x.user_profile_id,
                        principalTable: "user_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_user_profiles_layout",
                table: "user_profiles",
                sql: "layout IN ('board', 'list', 'compact', 'gallery', 'focus')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_user_profiles_theme",
                table: "user_profiles",
                sql: "theme IN ('classic', 'light', 'dark', 'forest', 'sunset')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_pet_profiles_species",
                table: "pet_profiles",
                sql: "species IN ('dog', 'cat', 'rabbit', 'fox', 'panda', 'dragon')");

            migrationBuilder.CreateIndex(
                name: "IX_completion_rewards_task_id",
                table: "completion_rewards",
                column: "task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_completion_rewards_user_profile_id_revoked_at",
                table: "completion_rewards",
                columns: new[] { "user_profile_id", "revoked_at" });

            // Preserve history that cannot be reconstructed from surviving private tasks.
            // Old team tasks never recorded the actual reward recipient: do not guess.
            migrationBuilder.Sql("""
                UPDATE pet_profiles p
                SET legacy_last_completed_at = p.last_completed_at,
                    legacy_streak_days = p.streak_days
                WHERE p.completed_task_count > (
                    SELECT count(*) FROM tasks t WHERE t.user_profile_id = p.user_profile_id
                        AND t.team_id IS NULL AND t.completion_rewarded_at IS NOT NULL);

                INSERT INTO completion_rewards (task_id, user_profile_id, awarded_at, revoked_at, experience, energy_granted)
                SELECT id, CASE WHEN team_id IS NULL THEN user_profile_id ELSE NULL END,
                    completion_rewarded_at,
                    CASE WHEN "Status" <> 2 THEN GREATEST(now(), completion_rewarded_at) ELSE NULL END,
                    25, 0
                FROM tasks WHERE completion_rewarded_at IS NOT NULL;

                DO $reconcile$
                DECLARE
                    pet_record record;
                    corrected_total integer;
                    corrected_level integer;
                    corrected_remaining integer;
                    activity_latest timestamp with time zone;
                    activity_day date;
                    activity_streak integer;
                    baseline_end date;
                    baseline_start date;
                BEGIN
                    FOR pet_record IN
                        SELECT p.*, r.reversed_count FROM pet_profiles p
                        JOIN (SELECT user_profile_id, count(*)::integer AS reversed_count
                            FROM completion_rewards WHERE revoked_at IS NOT NULL AND user_profile_id IS NOT NULL
                            GROUP BY user_profile_id) r ON r.user_profile_id = p.user_profile_id
                    LOOP
                        corrected_total := GREATEST(0, pet_record.total_experience - pet_record.reversed_count * 25);
                        corrected_remaining := corrected_total;
                        corrected_level := 1;
                        WHILE corrected_remaining >= 100 + (corrected_level - 1) * 50 LOOP
                            corrected_remaining := corrected_remaining - (100 + (corrected_level - 1) * 50);
                            corrected_level := corrected_level + 1;
                        END LOOP;

                        SELECT GREATEST(max(awarded_at), pet_record.legacy_last_completed_at)
                            INTO activity_latest FROM completion_rewards
                            WHERE user_profile_id = pet_record.user_profile_id AND revoked_at IS NULL;
                        activity_streak := 0;
                        activity_day := (activity_latest AT TIME ZONE 'UTC')::date;
                        baseline_end := (pet_record.legacy_last_completed_at AT TIME ZONE 'UTC')::date;
                        baseline_start := baseline_end - GREATEST(0, pet_record.legacy_streak_days - 1);
                        WHILE activity_day IS NOT NULL LOOP
                            IF EXISTS (SELECT 1 FROM completion_rewards
                                WHERE user_profile_id = pet_record.user_profile_id AND revoked_at IS NULL
                                    AND (awarded_at AT TIME ZONE 'UTC')::date = activity_day) THEN
                                activity_streak := activity_streak + 1;
                                activity_day := activity_day - 1;
                            ELSIF pet_record.legacy_streak_days > 0 AND activity_day BETWEEN baseline_start AND baseline_end THEN
                                activity_streak := activity_streak + (activity_day - baseline_start) + 1;
                                activity_day := baseline_start - 1;
                            ELSE
                                EXIT;
                            END IF;
                        END LOOP;

                        UPDATE pet_profiles SET total_experience = corrected_total,
                            level = corrected_level, experience = corrected_remaining,
                            completed_task_count = GREATEST(0, pet_record.completed_task_count - pet_record.reversed_count),
                            last_completed_at = activity_latest, streak_days = activity_streak,
                            mood = CASE WHEN activity_latest IS NULL THEN 'Idle' ELSE mood END,
                            updated_at = now()
                        WHERE id = pet_record.id;
                    END LOOP;
                END;
                $reconcile$;

                UPDATE tasks SET completion_rewarded_at = NULL
                WHERE "Status" <> 2 AND completion_rewarded_at IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$
                BEGIN
                    IF EXISTS (SELECT 1 FROM completion_rewards)
                        OR EXISTS (SELECT 1 FROM pet_profiles WHERE species IN ('fox', 'panda', 'dragon'))
                        OR EXISTS (SELECT 1 FROM user_profiles WHERE theme IN ('forest', 'sunset')
                            OR layout IN ('compact', 'gallery', 'focus')) THEN
                        RAISE EXCEPTION 'Progression data exists. Restore a reviewed pre-migration backup instead of discarding rewards or unlock selections.';
                    END IF;
                END;
                $guard$;
                """);

            migrationBuilder.DropTable(
                name: "completion_rewards");

            migrationBuilder.DropCheckConstraint(
                name: "CK_user_profiles_layout",
                table: "user_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_user_profiles_theme",
                table: "user_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_pet_profiles_species",
                table: "pet_profiles");

            migrationBuilder.DropColumn(
                name: "legacy_last_completed_at",
                table: "pet_profiles");

            migrationBuilder.DropColumn(
                name: "legacy_streak_days",
                table: "pet_profiles");

            migrationBuilder.AddCheckConstraint(
                name: "CK_user_profiles_layout",
                table: "user_profiles",
                sql: "layout IN ('board', 'list')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_user_profiles_theme",
                table: "user_profiles",
                sql: "theme IN ('classic', 'light', 'dark')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_pet_profiles_species",
                table: "pet_profiles",
                sql: "species IN ('dog', 'cat', 'rabbit')");
        }
    }
}
