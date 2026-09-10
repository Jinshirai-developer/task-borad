\set ON_ERROR_STOP on
DO $guard$ BEGIN
    IF current_database() NOT IN ('usabilitycheck', 'identitycheck') THEN
        RAISE EXCEPTION 'Use an isolated usabilitycheck or identitycheck database';
    END IF;
END $guard$;
\if :{?seed_usability}
BEGIN;
INSERT INTO user_profiles (id, user_key, display_name, "UserName", "NormalizedUserName", "SessionVersion", created_at, updated_at)
VALUES (8801, 'usability-fixture', 'Usability fixture', 'usability-fixture', 'USABILITY-FIXTURE', gen_random_uuid()::text, now(), now());
INSERT INTO pet_profiles (id,user_profile_id,name,species,level,experience,total_experience,completed_task_count,streak_days,energy,mood,created_at,updated_at)
VALUES (8801,8801,'Keep pet','cat',1,25,25,1,1,92,'Happy',now(),now());
INSERT INTO tasks (id,user_profile_id,title,is_completed,"Status",priority,tags,completion_rewarded_at,created_at,updated_at)
VALUES (8801,8801,'Keep completed task',true,2,1,'art, programmer',now(),now(),now());
INSERT INTO completion_rewards (task_id,user_profile_id,awarded_at,experience,energy_granted) VALUES (8801,8801,now(),25,12);
INSERT INTO pet_collections ("PetProfileId","Stage","HatLevel") VALUES (8801,'base',1);
INSERT INTO pet_reward_choices ("PetProfileId","Level","Choice","ClaimedAt") VALUES (8801,1,'hat',now());
INSERT INTO pet_memories ("PetProfileId","Key","UnlockedAt") VALUES (8801,'first',now());
SELECT setval(pg_get_serial_sequence('user_profiles','id'), (SELECT max(id) FROM user_profiles));
SELECT setval(pg_get_serial_sequence('pet_profiles','id'), (SELECT max(id) FROM pet_profiles));
SELECT setval(pg_get_serial_sequence('tasks','id'), (SELECT max(id) FROM tasks));
CREATE TABLE usability_migration_probe (name text PRIMARY KEY, fingerprint text NOT NULL);
DO $probe$ DECLARE table_name text; digest text; BEGIN
    FOR table_name IN SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename NOT IN ('__EFMigrationsHistory','usability_migration_probe') LOOP
        EXECUTE format('SELECT md5(COALESCE(string_agg(to_jsonb(t)::text, chr(10) ORDER BY to_jsonb(t)::text),'''')) FROM %I t', table_name) INTO digest;
        INSERT INTO usability_migration_probe VALUES (table_name, digest);
    END LOOP;
END $probe$;
COMMIT;
\else
DO $verify$ DECLARE probe record; digest text; BEGIN
    FOR probe IN SELECT * FROM usability_migration_probe LOOP
        IF probe.name='tasks' THEN
            SELECT md5(COALESCE(string_agg((to_jsonb(t)-ARRAY['assignee_user_profile_id','checklist_json'])::text, chr(10)
                ORDER BY (to_jsonb(t)-ARRAY['assignee_user_profile_id','checklist_json'])::text),'')) INTO digest FROM tasks t;
        ELSE
            EXECUTE format('SELECT md5(COALESCE(string_agg(to_jsonb(t)::text, chr(10) ORDER BY to_jsonb(t)::text),'''')) FROM %I t', probe.name) INTO digest;
        END IF;
        IF digest <> probe.fingerprint THEN RAISE EXCEPTION 'Existing data changed in %', probe.name; END IF;
    END LOOP;
    IF EXISTS (SELECT 1 FROM tasks WHERE assignee_user_profile_id IS NOT NULL OR checklist_json <> '[]') THEN RAISE EXCEPTION 'Unsafe defaults'; END IF;
    IF EXISTS (SELECT 1 FROM task_undo_entries) THEN RAISE EXCEPTION 'Unexpected undo entries'; END IF;
END $verify$;
SELECT count(*) AS unchanged_tables FROM usability_migration_probe;
\endif
