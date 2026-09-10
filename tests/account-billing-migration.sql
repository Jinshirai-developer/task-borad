\set ON_ERROR_STOP on
DO $$ BEGIN
  IF current_database() NOT IN ('identitycheck','accountbillingcheck')
  THEN RAISE EXCEPTION 'Isolated migration database required'; END IF;
END $$;
\if :{?seed_account_billing}
INSERT INTO team_billing ("TeamId","Status","AttemptId","PriceId","MonthlyYen","ReturnOrigin","AttemptStartedAt","SessionId","SubscriptionId","PaidThrough","CancelAtPeriodEnd")
VALUES (8901,'active','account-migration-attempt','price_migration_fixture',500,'https://localhost',now(),'cs_test_migration','sub_migration_fixture',now()+interval '30 days',false);
INSERT INTO billing_event_receipts ("Id","TeamId","ProcessedAt") VALUES ('evt_migration_fixture',8901,now());
CREATE TABLE account_migration_probe (name text PRIMARY KEY, fingerprint text NOT NULL);
DO $$ DECLARE tab text; fingerprint text; BEGIN
  FOR tab IN SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename NOT LIKE '%migration_probe' AND tablename<>'__EFMigrationsHistory' LOOP
    EXECUTE format('SELECT md5(coalesce(string_agg(to_jsonb(t)::text,chr(10) ORDER BY to_jsonb(t)::text),'''')) FROM %I t',tab) INTO fingerprint;
    INSERT INTO account_migration_probe VALUES(tab,fingerprint);
  END LOOP;
END $$;
\else
DO $$ DECLARE probe record; fingerprint text; tab text; expr text; BEGIN
  FOR probe IN SELECT * FROM account_migration_probe LOOP
    tab := CASE WHEN probe.name='team_billing' THEN 'account_billing' ELSE probe.name END;
    expr := CASE WHEN probe.name='team_billing' THEN 'to_jsonb(t)-ARRAY[''UserProfileId'',''MetadataScope'']'
                 WHEN probe.name='billing_event_receipts' THEN 'to_jsonb(t)-''UserProfileId''' ELSE 'to_jsonb(t)' END;
    EXECUTE format('SELECT md5(coalesce(string_agg((%s)::text,chr(10) ORDER BY (%s)::text),'''')) FROM %I t',expr,expr,tab) INTO fingerprint;
    IF fingerprint <> probe.fingerprint THEN RAISE EXCEPTION 'Existing values changed in %',probe.name; END IF;
  END LOOP;
  IF NOT EXISTS (SELECT 1 FROM account_billing WHERE "UserProfileId"=8901 AND "MetadataScope"='team' AND "Status"='active')
  THEN RAISE EXCEPTION 'Legacy owner mapping missing'; END IF;
  IF NOT EXISTS (SELECT 1 FROM billing_event_receipts WHERE "Id"='evt_migration_fixture' AND "UserProfileId"=8901)
  THEN RAISE EXCEPTION 'Receipt owner mapping missing'; END IF;
END $$;
SELECT count(*) AS original_tables_preserved FROM account_migration_probe;
DROP TABLE account_migration_probe;
-- Team removal no longer cascades into an account's subscription or receipts.
BEGIN;
DELETE FROM teams WHERE "Id"=8901;
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM account_billing WHERE "UserProfileId"=8901)
     OR NOT EXISTS (SELECT 1 FROM billing_event_receipts WHERE "Id"='evt_migration_fixture')
  THEN RAISE EXCEPTION 'Team deletion removed account billing'; END IF;
END $$;
ROLLBACK;
\endif
