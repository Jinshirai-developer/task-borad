-- Dedicated fixture only. Capture before AddTaskTagsAndRetroTheme; verify after it.
\set ON_ERROR_STOP on
DO $guard$
BEGIN
    IF current_database() <> 'identitycheck' THEN
        RAISE EXCEPTION 'Tag migration fixture requires the isolated identitycheck database';
    END IF;
END;
$guard$;

\if :{?seed_task_tags}
CREATE TABLE task_tags_migration_probe (name text PRIMARY KEY, fingerprint text NOT NULL);
DO $capture$
DECLARE table_name text; fingerprint text;
BEGIN
    FOREACH table_name IN ARRAY ARRAY['user_profiles','tasks','pet_profiles','teams','team_members','completion_rewards'] LOOP
        EXECUTE format('SELECT md5(coalesce(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text)::text, '''')) FROM %I t', table_name) INTO fingerprint;
        INSERT INTO task_tags_migration_probe VALUES (table_name, fingerprint);
    END LOOP;
END;
$capture$;
\else
BEGIN;
DO $verify$
DECLARE probe record; actual text; owner_id integer; shared_id integer;
BEGIN
    IF (SELECT count(*) FROM task_tags_migration_probe) <> 6 THEN
        RAISE EXCEPTION 'Missing before-migration data fingerprints';
    END IF;
    FOR probe IN SELECT * FROM task_tags_migration_probe LOOP
        EXECUTE format('SELECT md5(coalesce(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text)::text, '''')) FROM %I t', probe.name) INTO actual;
        IF actual IS DISTINCT FROM probe.fingerprint THEN
            RAISE EXCEPTION 'Existing data changed during tag migration: %', probe.name;
        END IF;
    END LOOP;
    IF EXISTS (SELECT 1 FROM task_tag_definitions) THEN
        RAISE EXCEPTION 'Migration must not invent registered classification tags';
    END IF;
    SELECT id INTO STRICT owner_id FROM user_profiles ORDER BY id LIMIT 1;
    UPDATE user_profiles SET theme = 'retro' WHERE id = owner_id;
    INSERT INTO task_tag_definitions (user_profile_id, name, normalized_name, created_at)
        VALUES (owner_id, 'Tag migration test', 'TAG MIGRATION TEST', now());
    BEGIN
        INSERT INTO task_tag_definitions (user_profile_id, name, normalized_name, created_at)
            VALUES (owner_id, 'tag migration test', 'TAG MIGRATION TEST', now());
        RAISE EXCEPTION 'Duplicate normalized tag unexpectedly accepted';
    EXCEPTION WHEN unique_violation THEN NULL;
    END;
    BEGIN
        INSERT INTO task_tag_definitions (name, normalized_name, created_at) VALUES ('No owner', 'NO OWNER', now());
        RAISE EXCEPTION 'Ownerless tag unexpectedly accepted';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    BEGIN
        INSERT INTO task_tag_definitions (user_profile_id, name, normalized_name, created_at)
            VALUES (987654321, 'Missing owner', 'MISSING OWNER', now());
        RAISE EXCEPTION 'Missing user foreign key unexpectedly accepted';
    EXCEPTION WHEN foreign_key_violation THEN NULL;
    END;
    SELECT "Id" INTO shared_id FROM teams ORDER BY "Id" LIMIT 1;
    IF shared_id IS NOT NULL THEN
        INSERT INTO task_tag_definitions (team_id, name, normalized_name, created_at)
            VALUES (shared_id, 'Tag migration test', 'TAG MIGRATION TEST', now());
        BEGIN
            INSERT INTO task_tag_definitions (team_id, user_profile_id, name, normalized_name, created_at)
                VALUES (shared_id, owner_id, 'Two owners', 'TWO OWNERS', now());
            RAISE EXCEPTION 'Mixed-scope tag unexpectedly accepted';
        EXCEPTION WHEN check_violation THEN NULL;
        END;
    END IF;
END;
$verify$;
ROLLBACK;
DROP TABLE task_tags_migration_probe;
\echo PASS Task tag migration preserves all prior data and enforces scoped constraints.
\endif
