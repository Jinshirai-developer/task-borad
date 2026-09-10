"""Local v19 -> v20 account billing migration, with isolated rehearsal and guarded rollback.

No Stripe writes. Never print runtime env, Stripe identifiers, database rows or keys.
Existing backup/rollback containers are preserved. --deploy briefly restarts localhost:5097.
"""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
CURRENT, PREVIOUS, FAILED = 'task-board-preview', 'task-board-preview-before-account-v20', 'task-board-preview-account-v20-failed'
DB, CHECK = 'task-board-preview-db', 'task-account-v20-migration-check'
IMAGE, OLD_IMAGE = 'task-board:account-v20', 'task-board:purchase-v19'
TOOLS = 'task-account-v20-build'
OLD, NEW = '20260908140702_AddTeamTestBilling', '20260908183357_AccountBilling'


def run(*args, data=None):
    result = subprocess.run(args, input=data, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode:
        raise RuntimeError(f'{args[0]} operation failed (exit {result.returncode}); no private output displayed')
    return result.stdout


def sql(db, statement):
    return run('docker', 'exec', db, 'psql', '-X', '-U', 'preview', '-d', 'preview', '-At', '-v', 'ON_ERROR_STOP=1', '-c', statement).decode().strip()


def execute(db, script):
    return run('docker', 'exec', '-i', db, 'psql', '-X', '-U', 'preview', '-d', 'preview', '-v', 'ON_ERROR_STOP=1', data=script)


def digest(db, migrated=False):
    names = sql(db, "SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename <> '__EFMigrationsHistory' ORDER BY tablename").splitlines()
    values = {}
    for name in names:
        row = "to_jsonb(t)"
        key = name
        if migrated and name == 'account_billing':
            key = 'team_billing'
            row += "-ARRAY['UserProfileId','MetadataScope']"
        elif migrated and name == 'billing_event_receipts':
            row += "-'UserProfileId'"
        identifier = '"' + name.replace('"', '""') + '"'
        values[key] = sql(db, "SELECT count(*) || ':' || md5(coalesce(string_agg(md5((" + row + ")::text),'' ORDER BY md5((" + row + ")::text)),'')) FROM " + identifier + " t")
    return values


def verify_mapping(db):
    assert sql(db, """SELECT count(*) FROM account_billing b LEFT JOIN teams t ON t."Id"=b."TeamId"
        WHERE b."UserProfileId" IS DISTINCT FROM t."OwnerUserProfileId" OR b."MetadataScope"<>'team'""") == '0'
    assert sql(db, """SELECT count(*) FROM billing_event_receipts r LEFT JOIN teams t ON t."Id"=r."TeamId"
        WHERE r."UserProfileId" IS DISTINCT FROM t."OwnerUserProfileId" """) == '0'


def last_migration(db):
    return sql(db, 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1')


def dump(db, destination):
    with destination.open('xb') as output:
        result = subprocess.run(['docker', 'exec', db, 'pg_dump', '-U', 'preview', '-d', 'preview', '-Fc'], stdout=output, stderr=subprocess.PIPE)
        assert result.returncode == 0 and output.tell() > 0, 'Database backup failed'


def rehearse(folder, up, down):
    names = run('docker', 'ps', '-a', '--format', '{{.Names}}').decode().splitlines()
    assert CHECK not in names, 'Inspect existing rehearsal container before retrying'
    snapshot = folder / 'rehearsal.dump'
    dump(DB, snapshot)
    created = False
    try:
        run('docker', 'run', '-d', '--name', CHECK, '--network', 'none', '--label', 'task-account-v20=disposable-rehearsal',
            '-e', 'POSTGRES_USER=preview', '-e', 'POSTGRES_PASSWORD=isolated-fixture-only', '-e', 'POSTGRES_DB=preview', 'postgres:16')
        created = True
        for _ in range(80):
            try:
                run('docker', 'exec', CHECK, 'pg_isready', '-h', '127.0.0.1', '-U', 'preview', '-d', 'preview')
                break
            except RuntimeError: time.sleep(.25)
        else: raise RuntimeError('Isolated database did not become ready')
        with snapshot.open('rb') as source:
            restored = subprocess.run(['docker', 'exec', '-i', CHECK, 'pg_restore', '-U', 'preview', '-d', 'preview', '--no-owner', '--no-acl', '--exit-on-error'], stdin=source, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        (folder / 'restore-private.log').write_bytes(restored.stderr)
        assert restored.returncode == 0, 'Snapshot restore failed'
        assert last_migration(CHECK) == OLD
        before = digest(CHECK)
        execute(CHECK, up)
        assert digest(CHECK, True) == before, 'Migration changed existing values'
        verify_mapping(CHECK)
        assert last_migration(CHECK) == NEW
        execute(CHECK, down)
        assert last_migration(CHECK) == OLD and digest(CHECK) == before, 'Guarded rollback changed existing values'
        # Duplicate contracts must abort atomically, without deleting/merging any record.
        execute(CHECK, (ROOT / 'tests/account-billing-migration-fixture.sql').read_bytes())
        duplicate_before = digest(CHECK)
        rejected = False
        try: execute(CHECK, up)
        except RuntimeError: rejected = True
        assert rejected and last_migration(CHECK) == OLD and digest(CHECK) == duplicate_before, 'Duplicate contract migration must stop without changes'
        print(json.dumps({'rehearsal': 'passed', 'original_tables_preserved': len(before), 'legacy_contracts_preserved': True,
            'up_down_verified': True, 'duplicate_contracts_rejected_atomically': True, 'stripe_writes': False}), flush=True)
    finally:
        if created:
            inspect = json.loads(run('docker', 'inspect', CHECK))[0]
            assert inspect['Config']['Labels'].get('task-account-v20') == 'disposable-rehearsal'
            assert inspect['HostConfig']['NetworkMode'] == 'none' and all(m['Type'] == 'volume' for m in inspect['Mounts'])
            run('docker', 'stop', CHECK)
            run('docker', 'rm', '-v', CHECK)


def status(path):
    try:
        with urllib.request.urlopen('http://127.0.0.1:5097' + path, timeout=3) as response: return response.status
    except urllib.error.HTTPError as error: return error.code


def main(deploy):
    os.umask(0o077)
    config = json.loads(run('docker', 'inspect', CURRENT))[0]
    assert config['State']['Running'] and config['Config']['Image'] == OLD_IMAGE
    assert config['Config']['User'] == '1654' and config['HostConfig']['RestartPolicy']['Name'] == 'no'
    assert list(config['NetworkSettings']['Networks']) == ['task-board-preview-net']
    assert config['HostConfig']['PortBindings'] == {'8080/tcp': [{'HostIp': '127.0.0.1', 'HostPort': '5097'}]}
    assert [(m['Type'], m.get('Name'), m['Destination']) for m in config['Mounts']] == [('volume', 'task-board-preview-keys', '/keys')]
    assert last_migration(DB) == OLD
    names = run('docker', 'ps', '-a', '--format', '{{.Names}}').decode().splitlines()
    assert PREVIOUS not in names and FAILED not in names, 'Inspect existing rollback state before retrying'
    up = run('docker', 'exec', TOOLS, 'dotnet', 'ef', 'migrations', 'script', OLD, NEW, '--no-build', '--configuration', 'Release')
    down = run('docker', 'exec', TOOLS, 'dotnet', 'ef', 'migrations', 'script', NEW, OLD, '--no-build', '--configuration', 'Release')
    assert b'UPDATE account_billing' in up and NEW.encode() in up and b'START TRANSACTION' in up
    assert b'Account activity prevents automatic downgrade' in down
    folder = Path(tempfile.mkdtemp(prefix='account-v20-', dir=ROOT / '.local/backups'))
    (folder / 'up.sql').write_bytes(up)
    (folder / 'down.sql').write_bytes(down)
    rehearse(folder, up, down)
    if not deploy:
        print(json.dumps({'deployed': False, 'private_rehearsal_backup': str(folder)}), flush=True)
        return
    run('docker', 'image', 'inspect', IMAGE)
    old_env = config['Config']['Env']
    assert 'Billing__Enabled=true' in old_env and 'Billing__MonthlyYen=500' in old_env
    (folder / 'runtime-before.json').write_text(json.dumps(config))
    (folder / 'runtime.env').write_text('\n'.join(old_env) + '\n')
    stopped = migrated = renamed = created = False
    try:
        run('docker', 'stop', CURRENT); stopped = True
        assert sql(DB, 'SELECT count(*) FROM team_billing WHERE "OperationUntil">now()') == '0', 'Billing still in flight; retry later'
        dump(DB, folder / 'before-account.dump')
        run('docker', 'cp', CURRENT + ':/keys', str(folder / 'keys'))
        for path in (folder / 'keys').rglob('*'): path.chmod(0o700 if path.is_dir() else 0o600)
        before = digest(DB)
        (folder / 'before-digests.json').write_text(json.dumps(before))
        execute(DB, up); migrated = True
        assert last_migration(DB) == NEW and digest(DB, True) == before
        verify_mapping(DB)
        run('docker', 'rename', CURRENT, PREVIOUS); renamed = True
        run('docker', 'run', '-d', '--name', CURRENT, '--network', 'task-board-preview-net', '--user', '1654', '--restart', 'no',
            '-p', '127.0.0.1:5097:8080', '-v', 'task-board-preview-keys:/keys', '--env-file', str(folder / 'runtime.env'), IMAGE)
        created = True
        for _ in range(60):
            try:
                if status('/health/ready') == 200: break
            except (OSError, urllib.error.URLError): pass
            time.sleep(.5)
        else: raise RuntimeError('New app did not become ready')
        assert status('/api/user/billing') == 401 and status('/api/teams/1/billing') == 401
        after = digest(DB, True)
        assert {k:v for k,v in after.items() if k not in ('team_billing','billing_event_receipts')} == {k:v for k,v in before.items() if k not in ('team_billing','billing_event_receipts')}
        current = json.loads(run('docker', 'inspect', CURRENT))[0]
        assert sorted(current['Config']['Env']) == sorted(old_env)
        files = [p for p in (ROOT / 'frontend').iterdir() if p.suffix in ('.js','.css','.html')]
        for path in files:
            with urllib.request.urlopen('http://127.0.0.1:5097/' + path.name, timeout=3) as response:
                assert hashlib.sha256(response.read()).digest() == hashlib.sha256(path.read_bytes()).digest(), 'Served file mismatch'
        report = {'deployed': True, 'image': IMAGE, 'url': 'http://localhost:5097/', 'backup': str(folder),
            'rollback_container': PREVIOUS, 'all_original_values_preserved_during_migration': len(before),
            'environment_preserved': True, 'served_files_match': len(files), 'stripe_writes': False,
            'accounts_with_pro': int(sql(DB, """SELECT count(*) FROM account_billing WHERE "PaidThrough">now() AND "Status" IN ('active','past_due')""")),
            'teams_with_owner_pro': int(sql(DB, """SELECT count(*) FROM teams t JOIN account_billing b ON b."UserProfileId"=t."OwnerUserProfileId" WHERE b."PaidThrough">now() AND b."Status" IN ('active','past_due')"""))}
        (folder / 'verification.json').write_text(json.dumps(report, indent=2))
        print(json.dumps(report, indent=2), flush=True)
    except Exception:
        if created:
            run('docker', 'stop', CURRENT); run('docker', 'rename', CURRENT, FAILED)
        if migrated:
            # No dump overwrite. If new account activity exists, this intentionally
            # refuses downgrade and leaves the app stopped for reviewed recovery.
            execute(DB, down)
            assert last_migration(DB) == OLD
        if renamed: run('docker', 'rename', PREVIOUS, CURRENT)
        if stopped: run('docker', 'start', CURRENT)
        print('Update failed; original app/schema restored. Protected backup: ' + str(folder), flush=True)
        raise


if __name__ == '__main__':
    assert sys.argv[1:] in (['--rehearse'], ['--deploy']), 'Use --rehearse or --deploy'
    main(sys.argv[1] == '--deploy')
