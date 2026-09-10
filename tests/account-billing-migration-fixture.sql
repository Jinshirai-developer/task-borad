-- Only run against the isolated, unpublished rehearsal copy.
\set ON_ERROR_STOP on
BEGIN;
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM user_profiles WHERE id=199990001)
  THEN RAISE EXCEPTION 'Fixture identifier already exists'; END IF;
END $$;
INSERT INTO user_profiles (id,user_key,display_name,"UserName","NormalizedUserName","SessionVersion",created_at,updated_at)
VALUES (199990001,'account-migration-fixture','Migration fixture','account-migration-fixture','ACCOUNT-MIGRATION-FIXTURE',gen_random_uuid()::text,now(),now());
INSERT INTO teams ("Id","Name","OwnerUserProfileId","InviteCodeHash","InviteExpiresAt","CreatedAt")
VALUES (199990001,'Migration fixture A',199990001,repeat('a',64),now(),now()),
       (199990002,'Migration fixture B',199990001,repeat('c',64),now(),now());
INSERT INTO team_billing ("TeamId","Status","MonthlyYen","CancelAtPeriodEnd")
VALUES (199990001,'free',0,false),(199990002,'free',0,false);
COMMIT;
