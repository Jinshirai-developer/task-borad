-- Synthetic fixture only. Seed after AddTeamsAndPreferences, verify after latest.
\set ON_ERROR_STOP on
DO $guard$
BEGIN
    IF current_database() <> 'identitycheck' THEN
        RAISE EXCEPTION 'Progression fixture requires the isolated identitycheck database';
    END IF;
END;
$guard$;

\if :{?seed_progression}
BEGIN;
INSERT INTO user_profiles (id, user_key, display_name, "UserName", "NormalizedUserName", "SessionVersion", created_at, updated_at)
VALUES
    (8001, 'reversal-fixture', 'Reversal fixture', 'reversal-fixture', 'REVERSAL-FIXTURE', gen_random_uuid()::text, now(), now()),
    (8002, 'zero-fixture', 'Zero fixture', 'zero-fixture', 'ZERO-FIXTURE', gen_random_uuid()::text, now(), now()),
    (8003, 'unknown-team-fixture', 'Unknown team fixture', 'unknown-team-fixture', 'UNKNOWN-TEAM-FIXTURE', gen_random_uuid()::text, now(), now()),
    (8004, 'mixed-legacy-fixture', 'Mixed legacy fixture', 'mixed-legacy-fixture', 'MIXED-LEGACY-FIXTURE', gen_random_uuid()::text, now(), now());
INSERT INTO pet_profiles (id, user_profile_id, name, species, level, experience, total_experience,
    completed_task_count, streak_days, energy, mood, last_completed_at, created_at, updated_at)
VALUES
    (8201, 8001, 'Keep my name', 'cat', 2, 0, 100, 4, 1, 100, 'Happy', '2026-08-03+00', now(), now()),
    (8202, 8002, 'Zero pet', 'dog', 1, 25, 25, 1, 1, 92, 'Happy', '2026-08-03+00', now(), now()),
    (8203, 8003, 'Unknown pet', 'rabbit', 1, 50, 50, 2, 2, 100, 'Happy', '2026-08-03+00', now(), now()),
    (8204, 8004, 'Mixed legacy pet', 'cat', 1, 75, 75, 3, 2, 100, 'Happy', '2026-08-03+00', now(), now());
INSERT INTO teams ("Id", "Name", "OwnerUserProfileId", "InviteCodeHash", "InviteExpiresAt", "CreatedAt")
VALUES (8301, 'Unknown recipient team', 8003, repeat('b',64), now() + interval '7 days', now());
INSERT INTO team_members ("TeamId", "UserProfileId", "JoinedAt") VALUES (8301, 8003, now());
INSERT INTO tasks (id, user_profile_id, team_id, title, is_completed, "Status", priority,
    completion_rewarded_at, created_at, updated_at)
VALUES
    (8101, 8001, NULL, 'Keep done 1', true, 2, 1, '2026-08-03+00', now(), now()),
    (8102, 8001, NULL, 'Keep done 2', true, 2, 1, '2026-08-03+00', now(), now()),
    (8103, 8001, NULL, 'Keep done 3', true, 2, 1, '2026-08-03+00', now(), now()),
    (8104, 8001, NULL, 'Already reopened', false, 1, 1, '2026-08-03+00', now(), now()),
    (8105, 8002, NULL, 'Already todo', false, 0, 1, '2026-08-03+00', now(), now()),
    (8106, 8003, 8301, 'Unknown old shared credit', false, 1, 1, '2026-08-03+00', now(), now()),
    (8107, 8004, NULL, 'Reopened with unknown legacy history', false, 1, 1, '2026-08-03+00', now(), now());
SELECT setval(pg_get_serial_sequence('user_profiles','id'), (SELECT max(id) FROM user_profiles));
SELECT setval(pg_get_serial_sequence('pet_profiles','id'), (SELECT max(id) FROM pet_profiles));
SELECT setval(pg_get_serial_sequence('tasks','id'), (SELECT max(id) FROM tasks));
SELECT setval(pg_get_serial_sequence('teams','Id'), (SELECT max("Id") FROM teams));
COMMIT;
\else
DO $verify$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pet_profiles WHERE id = 8201 AND name = 'Keep my name' AND species = 'cat'
        AND level = 1 AND experience = 75 AND total_experience = 75 AND completed_task_count = 3
        AND streak_days = 1 AND last_completed_at = '2026-08-03+00'
        AND legacy_last_completed_at IS NULL AND legacy_streak_days = 0)
    THEN RAISE EXCEPTION 'Known old reopened credit was not reversed correctly'; END IF;
    IF NOT EXISTS (SELECT 1 FROM pet_profiles WHERE id = 8202 AND total_experience = 0 AND completed_task_count = 0
        AND level = 1 AND experience = 0 AND streak_days = 0 AND last_completed_at IS NULL)
    THEN RAISE EXCEPTION 'Full reversal must clear XP and activity without dropping below level one'; END IF;
    IF NOT EXISTS (SELECT 1 FROM pet_profiles WHERE id = 8203 AND total_experience = 50 AND completed_task_count = 2
        AND legacy_last_completed_at = '2026-08-03+00' AND legacy_streak_days = 2)
    THEN RAISE EXCEPTION 'Unknown legacy credit must not be taken from a guessed receiver'; END IF;
    IF NOT EXISTS (SELECT 1 FROM pet_profiles WHERE id = 8204 AND name = 'Mixed legacy pet' AND species = 'cat'
        AND level = 1 AND experience = 50 AND total_experience = 50 AND completed_task_count = 2
        AND streak_days = 2 AND last_completed_at = '2026-08-03+00'
        AND legacy_last_completed_at = '2026-08-03+00' AND legacy_streak_days = 2)
    THEN RAISE EXCEPTION 'Known reversal must preserve the unknown legacy streak baseline'; END IF;
    IF (SELECT count(*) FROM completion_rewards WHERE task_id BETWEEN 8101 AND 8107) <> 7
        OR (SELECT count(*) FROM completion_rewards WHERE task_id BETWEEN 8101 AND 8103 AND user_profile_id = 8001 AND revoked_at IS NULL) <> 3
        OR EXISTS (SELECT 1 FROM completion_rewards WHERE task_id BETWEEN 8104 AND 8107 AND revoked_at IS NULL)
        OR NOT EXISTS (SELECT 1 FROM completion_rewards WHERE task_id = 8106 AND user_profile_id IS NULL)
        OR NOT EXISTS (SELECT 1 FROM completion_rewards WHERE task_id = 8107 AND user_profile_id = 8004 AND revoked_at IS NOT NULL)
    THEN RAISE EXCEPTION 'Reward attribution and active state did not migrate correctly'; END IF;
    IF EXISTS (SELECT 1 FROM tasks WHERE id BETWEEN 8104 AND 8107 AND completion_rewarded_at IS NOT NULL)
    THEN RAISE EXCEPTION 'Reopened task still has an active reward marker'; END IF;
    IF (SELECT count(*) FROM tasks WHERE id BETWEEN 8101 AND 8107) <> 7
    THEN RAISE EXCEPTION 'Migration must not delete existing tasks'; END IF;
END;
$verify$;

BEGIN;
DO $constraints$
BEGIN
    BEGIN
        INSERT INTO completion_rewards (task_id, user_profile_id, awarded_at, experience, energy_granted)
        VALUES (8101, 8001, now(), 25, 0);
        RAISE EXCEPTION 'Duplicate task reward unexpectedly accepted';
    EXCEPTION WHEN unique_violation THEN NULL;
    END;
    BEGIN
        UPDATE completion_rewards SET experience = -25 WHERE task_id = 8101;
        RAISE EXCEPTION 'Invalid reward amount unexpectedly accepted';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    BEGIN
        UPDATE completion_rewards SET revoked_at = awarded_at - interval '1 second' WHERE task_id = 8101;
        RAISE EXCEPTION 'Invalid reversal timestamp unexpectedly accepted';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
END;
$constraints$;
DELETE FROM tasks WHERE id = 8101;
DO $history$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM completion_rewards WHERE user_profile_id = 8001 AND task_id IS NULL AND revoked_at IS NULL)
    THEN RAISE EXCEPTION 'Deleting a completed task lost the retained receipt'; END IF;
END;
$history$;
ROLLBACK;
SELECT 'progression_migration_and_constraints_passed' AS result;
\endif
