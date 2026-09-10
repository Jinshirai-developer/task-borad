\set ON_ERROR_STOP on
DO $guard$ BEGIN
    IF current_database() NOT IN ('identitycheck','companioncheck') THEN RAISE EXCEPTION 'Isolated migration database required'; END IF;
END $guard$;
\if :{?seed_companion}
CREATE TABLE companion_migration_probe (name text PRIMARY KEY, fingerprint text NOT NULL);
DO $probe$ DECLARE table_name text; digest text; BEGIN
    FOR table_name IN SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename NOT LIKE '%migration_probe' AND tablename <> '__EFMigrationsHistory' LOOP
        EXECUTE format('SELECT md5(COALESCE(string_agg(to_jsonb(t)::text, chr(10) ORDER BY to_jsonb(t)::text),'''')) FROM %I t',table_name) INTO digest;
        INSERT INTO companion_migration_probe VALUES(table_name,digest);
    END LOOP;
END $probe$;
\else
DO $verify$ DECLARE probe record; digest text; BEGIN
    FOR probe IN SELECT * FROM companion_migration_probe LOOP
        IF probe.name='tasks' THEN
            SELECT md5(COALESCE(string_agg((to_jsonb(t)-'companion_json')::text,chr(10) ORDER BY (to_jsonb(t)-'companion_json')::text),'')) INTO digest FROM tasks t;
        ELSE
            EXECUTE format('SELECT md5(COALESCE(string_agg(to_jsonb(t)::text,chr(10) ORDER BY to_jsonb(t)::text),'''')) FROM %I t',probe.name) INTO digest;
        END IF;
        IF digest <> probe.fingerprint THEN RAISE EXCEPTION 'Existing data changed in %',probe.name; END IF;
    END LOOP;
    IF EXISTS(SELECT 1 FROM tasks WHERE companion_json <> '{}') THEN RAISE EXCEPTION 'Unsafe initial state'; END IF;
    IF (SELECT character_maximum_length FROM information_schema.columns WHERE table_name='task_undo_entries' AND column_name='Snapshot') <> 1000000 THEN RAISE EXCEPTION 'Undo capacity not expanded'; END IF;
END $verify$;
SELECT count(*) AS unchanged_tables FROM companion_migration_probe;
DROP TABLE companion_migration_probe;
\endif
