\set ON_ERROR_STOP on
DO $guard$ BEGIN
    IF current_database() NOT IN ('identitycheck','billingcheck') THEN RAISE EXCEPTION 'Isolated migration database required'; END IF;
END $guard$;
\if :{?seed_billing}
INSERT INTO user_profiles (id,user_key,display_name,"UserName","NormalizedUserName","SessionVersion",created_at,updated_at)
SELECT 8900+i,'billing-fixture-'||i,'Billing fixture '||i,'billing-fixture-'||i,'BILLING-FIXTURE-'||i,gen_random_uuid()::text,now(),now() FROM generate_series(1,4) i;
INSERT INTO teams ("Id","Name","OwnerUserProfileId","InviteCodeHash","InviteExpiresAt","CreatedAt")
VALUES (8901,'Keep existing four members',8901,md5('billing-migration:8901:a')||md5('billing-migration:8901:b'),now()+interval '7 days',now());
INSERT INTO team_members ("TeamId","UserProfileId","JoinedAt") SELECT 8901,8900+i,now() FROM generate_series(1,4) i;
INSERT INTO tasks (id,user_profile_id,team_id,title,is_completed,"Status",created_at,updated_at,checklist_json,companion_json)
VALUES (8901,8901,8901,'Keep shared task',false,1,now(),now(),'[{"text":"Keep checklist","isCompleted":false}]','{"notes":[]}');
SELECT setval(pg_get_serial_sequence('user_profiles','id'),(SELECT max(id) FROM user_profiles));
SELECT setval(pg_get_serial_sequence('teams','Id'),(SELECT max("Id") FROM teams));
SELECT setval(pg_get_serial_sequence('tasks','id'),(SELECT max(id) FROM tasks));
CREATE TABLE billing_migration_probe (name text PRIMARY KEY, fingerprint text NOT NULL);
DO $probe$ DECLARE table_name text; digest text; BEGIN
    FOR table_name IN SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename NOT LIKE '%migration_probe' AND tablename <> '__EFMigrationsHistory' LOOP
        EXECUTE format('SELECT md5(COALESCE(string_agg(to_jsonb(t)::text,chr(10) ORDER BY to_jsonb(t)::text),'''')) FROM %I t',table_name) INTO digest;
        INSERT INTO billing_migration_probe VALUES(table_name,digest);
    END LOOP;
END $probe$;
\else
DO $verify$ DECLARE probe record; digest text; BEGIN
    FOR probe IN SELECT * FROM billing_migration_probe LOOP
        EXECUTE format('SELECT md5(COALESCE(string_agg(to_jsonb(t)::text,chr(10) ORDER BY to_jsonb(t)::text),'''')) FROM %I t',probe.name) INTO digest;
        IF digest <> probe.fingerprint THEN RAISE EXCEPTION 'Existing data changed in %',probe.name; END IF;
    END LOOP;
    IF EXISTS(SELECT 1 FROM team_billing) OR EXISTS(SELECT 1 FROM billing_event_receipts) THEN RAISE EXCEPTION 'Migration must not grant a contract'; END IF;
END $verify$;
SELECT count(*) AS unchanged_tables FROM billing_migration_probe;
DROP TABLE billing_migration_probe;
\endif
