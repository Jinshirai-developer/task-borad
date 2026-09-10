-- Synthetic data only. Never run against a real user database.
-- Dedicated database name: identitycheck.
-- Before migration: psql -v seed_legacy=1 -f tests/identity-migration.sql
-- After migration:  psql -f tests/identity-migration.sql
-- Login fixture: migration-legacy / LegacyMigration!2026 (not a real credential).
-- The deterministic legacy PBKDF2-SHA256 hash uses 600000 iterations, a 16-byte
-- salt (00..0f), and a 32-byte hash. Deterministic salts are for this fixture only.
\set ON_ERROR_STOP on

DO $guard$
BEGIN
    IF current_database() <> 'identitycheck' THEN
        RAISE EXCEPTION 'This fixture may run only in the isolated identitycheck database';
    END IF;
END;
$guard$;

\if :{?seed_legacy}
BEGIN;
INSERT INTO user_profiles
    (id, user_key, display_name, password_hash, auth_token_hash, auth_token_expires_at,
     created_at, updated_at)
VALUES
    (7001, 'migration-legacy', 'Legacy Migration Fixture',
     '600000.AAECAwQFBgcICQoLDA0ODw==.oLkX7DC0czq1A5HOAwJKit+9ocvtdzOzGaPjHqB0paQ=',
     'synthetic-legacy-token-hash', '2099-01-01 00:00:00+00',
     '2026-08-01 00:00:00+00', '2026-08-02 00:00:00+00');
INSERT INTO tasks
    (id, user_profile_id, title, description, is_completed, "Status", priority, tags,
     completion_rewarded_at, created_at, updated_at)
VALUES
    (7101, 7001, 'Preserved migration task', 'Synthetic migration fixture', true, 2, 2,
     'migration, legacy', '2026-08-02 00:00:00+00',
     '2026-08-01 00:00:00+00', '2026-08-02 00:00:00+00');
INSERT INTO pet_profiles
    (id, user_profile_id, name, level, experience, total_experience, completed_task_count,
     streak_days, energy, mood, last_completed_at, created_at, updated_at)
VALUES
    (7201, 7001, 'Migration Pet', 3, 42, 142, 5, 2, 85, 'Happy',
     '2026-08-02 00:00:00+00', '2026-08-01 00:00:00+00', '2026-08-02 00:00:00+00');
SELECT setval(pg_get_serial_sequence('user_profiles', 'id'), (SELECT max(id) FROM user_profiles));
SELECT setval(pg_get_serial_sequence('tasks', 'id'), (SELECT max(id) FROM tasks));
SELECT setval(pg_get_serial_sequence('pet_profiles', 'id'), (SELECT max(id) FROM pet_profiles));
COMMIT;
SELECT 'legacy_fixture_seeded' AS result, id, user_key FROM user_profiles WHERE id = 7001;
\else
DO $verify$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM user_profiles WHERE id = 7001
        AND user_key = 'migration-legacy' AND display_name = 'Legacy Migration Fixture'
        AND password_hash = '600000.AAECAwQFBgcICQoLDA0ODw==.oLkX7DC0czq1A5HOAwJKit+9ocvtdzOzGaPjHqB0paQ='
        AND "UserName" = 'migration-legacy' AND "NormalizedUserName" = 'MIGRATION-LEGACY'
        AND length("SecurityStamp") = 36 AND length("ConcurrencyStamp") = 36
        AND length("SessionVersion") = 36 AND "LockoutEnabled"
        AND "Email" IS NULL AND "NormalizedEmail" IS NULL AND NOT "EmailConfirmed"
        AND "TermsAcceptedAt" IS NULL AND "AcceptedTermsVersion" IS NULL
        AND "AcknowledgedPrivacyVersion" IS NULL
        AND theme = 'classic' AND layout = 'board'
        AND created_at = '2026-08-01 00:00:00+00' AND updated_at = '2026-08-02 00:00:00+00'
    ) THEN RAISE EXCEPTION 'Legacy identity fields or consent migration did not match'; END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'user_profiles'
          AND column_name IN ('auth_token_hash', 'auth_token_expires_at')
    ) THEN RAISE EXCEPTION 'Legacy bearer token columns remain'; END IF;

    IF NOT EXISTS (
        SELECT 1 FROM tasks WHERE id = 7101 AND user_profile_id = 7001
        AND team_id IS NULL
        AND title = 'Preserved migration task' AND is_completed AND "Status" = 2
        AND priority = 2 AND tags = 'migration, legacy'
        AND completion_rewarded_at = '2026-08-02 00:00:00+00'
    ) THEN RAISE EXCEPTION 'Task identity, ownership, or content changed'; END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pet_profiles WHERE id = 7201 AND user_profile_id = 7001
        AND name = 'Migration Pet' AND level = 3 AND experience = 42
        AND total_experience = 142 AND completed_task_count = 5 AND streak_days = 2
        AND energy = 85 AND mood = 'Happy' AND species = 'dog'
    ) THEN RAISE EXCEPTION 'Pet identity, ownership, or progression changed'; END IF;

    IF (SELECT count(*) FROM pg_constraint WHERE contype = 'f'
        AND confrelid = 'user_profiles'::regclass AND confdeltype = 'c') <> 7
    THEN RAISE EXCEPTION 'Expected seven account-owned cascading foreign keys'; END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'task_tag_definitions'::regclass
        AND confrelid = 'user_profiles'::regclass AND confdeltype = 'c')
    THEN RAISE EXCEPTION 'Private tag definitions must cascade on account deletion'; END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'tasks'::regclass
        AND confrelid = 'user_profiles'::regclass AND confdeltype = 'n')
    THEN RAISE EXCEPTION 'Shared task creator must be nullable on account deletion'; END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'teams'::regclass
        AND confrelid = 'user_profiles'::regclass AND confdeltype = 'r')
    THEN RAISE EXCEPTION 'Team ownership must restrict account deletion'; END IF;
END;
$verify$;

-- Destructive probes are restricted to synthetic fixture rows and rolled back.
BEGIN;
UPDATE user_profiles SET "Email" = 'legacy@example.test', "NormalizedEmail" = 'LEGACY@EXAMPLE.TEST'
WHERE id = 7001;
DO $unique_email$
BEGIN
    BEGIN
        INSERT INTO user_profiles
            (id, user_key, display_name, "UserName", "NormalizedUserName", "NormalizedEmail",
             "SessionVersion", created_at, updated_at)
        VALUES (7002, 'duplicate-email-fixture', 'Duplicate email fixture', 'duplicate-email-fixture',
                'DUPLICATE-EMAIL-FIXTURE', 'LEGACY@EXAMPLE.TEST', gen_random_uuid()::text, now(), now());
        RAISE EXCEPTION 'Duplicate normalized email unexpectedly accepted';
    EXCEPTION WHEN unique_violation THEN NULL;
    END;
END;
$unique_email$;

DO $unique_user$
BEGIN
    BEGIN
        INSERT INTO user_profiles
            (id, user_key, display_name, "UserName", "NormalizedUserName", "SessionVersion", created_at, updated_at)
        VALUES (7002, 'duplicate-name-fixture', 'Duplicate name fixture', 'MIGRATION-LEGACY',
                'MIGRATION-LEGACY', gen_random_uuid()::text, now(), now());
        RAISE EXCEPTION 'Duplicate normalized user name unexpectedly accepted';
    EXCEPTION WHEN unique_violation THEN NULL;
    END;
END;
$unique_user$;

INSERT INTO "AspNetUserClaims" ("UserId", "ClaimType", "ClaimValue") VALUES (7001, 'fixture', 'fixture');
INSERT INTO "AspNetUserLogins" ("LoginProvider", "ProviderKey", "UserId") VALUES ('fixture', 'fixture', 7001);
INSERT INTO "AspNetUserTokens" ("UserId", "LoginProvider", "Name", "Value") VALUES (7001, 'fixture', 'fixture', 'fixture');
INSERT INTO email_outbox ("UserProfileId", "Purpose", "ProtectedPayload", "Attempts", "NextAttemptAt", "ExpiresAt")
VALUES (7001, 'confirm', 'synthetic-never-delivered-payload', 0, '2099-01-01+00', '2099-01-02+00');
INSERT INTO user_profiles (id, user_key, display_name, "UserName", "NormalizedUserName",
    "SessionVersion", created_at, updated_at)
VALUES (7003, 'team-owner-fixture', 'Team owner fixture', 'team-owner-fixture',
    'TEAM-OWNER-FIXTURE', gen_random_uuid()::text, now(), now());
INSERT INTO teams ("Id", "Name", "OwnerUserProfileId", "InviteCodeHash", "InviteExpiresAt", "CreatedAt")
VALUES (7301, 'Migration team', 7003, repeat('a', 64), now() + interval '7 days', now());
INSERT INTO team_members ("TeamId", "UserProfileId", "JoinedAt") VALUES (7301, 7003, now()), (7301, 7001, now());
INSERT INTO tasks (id, user_profile_id, team_id, title, is_completed, "Status", priority, created_at, updated_at)
VALUES (7102, 7001, 7301, 'Shared task survives creator deletion', false, 0, 1, now(), now());
DO $team_constraints$
BEGIN
    BEGIN
        DELETE FROM user_profiles WHERE id = 7003;
        RAISE EXCEPTION 'Team owner account deletion unexpectedly accepted';
    EXCEPTION WHEN foreign_key_violation THEN NULL;
    END;
    BEGIN
        UPDATE tasks SET user_profile_id = NULL WHERE id = 7101;
        RAISE EXCEPTION 'Orphaned private task unexpectedly accepted';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    BEGIN
        UPDATE user_profiles SET theme = 'invalid' WHERE id = 7001;
        RAISE EXCEPTION 'Invalid theme unexpectedly accepted';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    BEGIN
        UPDATE user_profiles SET layout = 'invalid' WHERE id = 7001;
        RAISE EXCEPTION 'Invalid layout unexpectedly accepted';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    BEGIN
        UPDATE pet_profiles SET species = 'invalid' WHERE id = 7201;
        RAISE EXCEPTION 'Invalid pet species unexpectedly accepted';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
END;
$team_constraints$;
-- Account deletion explicitly removes private tasks before deleting the account.
DELETE FROM tasks WHERE user_profile_id = 7001 AND team_id IS NULL;
DELETE FROM user_profiles WHERE id = 7001;
DO $cascade$
BEGIN
    IF EXISTS (SELECT 1 FROM tasks WHERE user_profile_id = 7001)
       OR EXISTS (SELECT 1 FROM pet_profiles WHERE user_profile_id = 7001)
       OR EXISTS (SELECT 1 FROM "AspNetUserClaims" WHERE "UserId" = 7001)
       OR EXISTS (SELECT 1 FROM "AspNetUserLogins" WHERE "UserId" = 7001)
       OR EXISTS (SELECT 1 FROM "AspNetUserTokens" WHERE "UserId" = 7001)
       OR EXISTS (SELECT 1 FROM email_outbox WHERE "UserProfileId" = 7001)
       OR EXISTS (SELECT 1 FROM team_members WHERE "UserProfileId" = 7001)
    THEN RAISE EXCEPTION 'Account cascade left dependent fixture records'; END IF;
    IF NOT EXISTS (SELECT 1 FROM tasks WHERE id = 7102 AND team_id = 7301 AND user_profile_id IS NULL)
    THEN RAISE EXCEPTION 'Shared task was lost or creator was not anonymized'; END IF;
END;
$cascade$;
DELETE FROM teams WHERE "Id" = 7301;
DO $team_cascade$
BEGIN
    IF EXISTS (SELECT 1 FROM tasks WHERE id = 7102)
       OR EXISTS (SELECT 1 FROM team_members WHERE "TeamId" = 7301)
    THEN RAISE EXCEPTION 'Team deletion left shared tasks or members'; END IF;
END;
$team_cascade$;
ROLLBACK;

SELECT 'migration_preservation_and_constraints_passed' AS result, id, user_key,
       "UserName", "NormalizedUserName", "EmailConfirmed", "TermsAcceptedAt"
FROM user_profiles WHERE id = 7001;
SELECT indexname, indexdef FROM pg_indexes
WHERE schemaname = 'public' AND tablename IN ('user_profiles', 'email_outbox') ORDER BY indexname;
\endif
