"""One-time local v20 -> v21 frontend transport fix; no migration/Stripe writes.

Preserves active account contracts. Backs up DB, env and login keys privately.
Do not rerun after success. Rollback changes only the app container, never rows.
"""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('account_backup_helpers', Path(__file__).with_name('update-account-preview.py'))
helpers = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helpers)
run, sql, digest = helpers.run, helpers.sql, helpers.digest
CURRENT, DB = 'task-board-preview', 'task-board-preview-db'
PREVIOUS, FAILED = 'task-board-preview-before-transport-v21', 'task-board-preview-transport-v21-failed'
OLD_IMAGE, IMAGE = 'task-board:account-v20', 'task-board:transport-v21'
MIGRATION = '20260908183357_AccountBilling'
BACKUP_PREFIX = 'transport-v21-'
DUMP_NAME = 'before-transport.dump'
ENVIRONMENT_UPDATES = {}
PREVIEW_VALIDATOR = None


def runtime_environment(original, updates):
    """Preserve the complete existing environment while applying explicit settings."""
    assert all('=' in entry for entry in original)
    result = dict(entry.split('=', 1) for entry in original)
    assert len(result) == len(original), 'Duplicate environment names require inspection'
    for key, value in updates.items():
        assert key and '=' not in key and all(c not in key + value for c in '\r\n\0')
        result[key] = value
    return [key + '=' + value for key, value in result.items()]


def main():
    os.umask(0o077)
    config = json.loads(run('docker', 'inspect', CURRENT))[0]
    assert config['State']['Running'] and config['Config']['Image'] == OLD_IMAGE
    assert config['Config']['User'] == '1654' and config['HostConfig']['RestartPolicy']['Name'] == 'no'
    assert list(config['NetworkSettings']['Networks']) == ['task-board-preview-net']
    assert config['HostConfig']['PortBindings'] == {'8080/tcp': [{'HostIp': '127.0.0.1', 'HostPort': '5097'}]}
    assert [(m['Type'], m.get('Name'), m['Destination']) for m in config['Mounts']] == [('volume', 'task-board-preview-keys', '/keys')]
    assert helpers.last_migration(DB) == MIGRATION
    names = run('docker', 'ps', '-a', '--format', '{{.Names}}').decode().splitlines()
    assert PREVIOUS not in names and FAILED not in names, 'Inspect rollback state before retrying'
    run('docker', 'image', 'inspect', IMAGE)
    old_env = config['Config']['Env']
    assert 'Billing__Enabled=true' in old_env and 'Billing__MonthlyYen=500' in old_env
    runtime_env = runtime_environment(old_env, ENVIRONMENT_UPDATES)
    folder = Path(tempfile.mkdtemp(prefix=BACKUP_PREFIX, dir=ROOT / '.local/backups'))
    (folder / 'runtime-before.json').write_text(json.dumps(config))
    (folder / 'runtime.env').write_text('\n'.join(runtime_env) + '\n')
    stopped = renamed = created = False
    try:
        run('docker', 'stop', CURRENT); stopped = True
        assert sql(DB, 'SELECT count(*) FROM account_billing WHERE "OperationUntil">now()') == '0', 'Billing operation in flight; retry later'
        helpers.dump(DB, folder / DUMP_NAME)
        run('docker', 'cp', CURRENT + ':/keys', str(folder / 'keys'))
        for path in (folder / 'keys').rglob('*'): path.chmod(0o700 if path.is_dir() else 0o600)
        before = digest(DB)
        (folder / 'before-digests.json').write_text(json.dumps(before))
        run('docker', 'rename', CURRENT, PREVIOUS); renamed = True
        run('docker', 'run', '-d', '--name', CURRENT, '--network', 'task-board-preview-net', '--user', '1654', '--restart', 'no',
            '-p', '127.0.0.1:5097:8080', '-v', 'task-board-preview-keys:/keys', '--env-file', str(folder / 'runtime.env'), IMAGE)
        created = True
        for _ in range(60):
            try:
                if helpers.status('/health/ready') == 200: break
            except (OSError, urllib.error.URLError): pass
            time.sleep(.5)
        else: raise RuntimeError('New app did not become ready')
        assert helpers.status('/api/user/billing') == 401 and helpers.status('/api/companion') == 401
        # The background billing reader first runs after two minutes. Compare
        # immediately so even the existing contract's sync timestamps must match.
        assert digest(DB) == before, 'Existing rows changed across restart'
        assert helpers.last_migration(DB) == MIGRATION
        current = json.loads(run('docker', 'inspect', CURRENT))[0]
        assert sorted(current['Config']['Env']) == sorted(runtime_env)
        if PREVIEW_VALIDATOR is not None:
            PREVIEW_VALIDATOR()
        files = [p for p in (ROOT / 'frontend').iterdir() if p.suffix in ('.js', '.css', '.html')]
        for path in files:
            with urllib.request.urlopen('http://127.0.0.1:5097/' + path.name, timeout=3) as response:
                assert hashlib.sha256(response.read()).digest() == hashlib.sha256(path.read_bytes()).digest(), 'Served frontend mismatch'
        report = {'image': IMAGE, 'url': 'http://localhost:5097/', 'backup': str(folder), 'rollback_container': PREVIOUS,
            'data_tables_unchanged': len(before), 'migration_unchanged': True, 'environment_and_key_volume_preserved': not bool(ENVIRONMENT_UPDATES),
            'only_declared_environment_changes': True, 'changed_environment_keys': sorted(ENVIRONMENT_UPDATES),
            'key_volume_preserved': True,
            'served_files_match': len(files), 'stripe_writes': False,
            'active_test_accounts_preserved': int(sql(DB, "SELECT count(*) FROM account_billing WHERE \"PaidThrough\">now() AND \"Status\" IN ('active','past_due')"))}
        (folder / 'verification.json').write_text(json.dumps(report, indent=2))
        print(json.dumps(report, indent=2), flush=True)
    except Exception:
        if created: run('docker', 'stop', CURRENT); run('docker', 'rename', CURRENT, FAILED)
        if renamed: run('docker', 'rename', PREVIOUS, CURRENT)
        if stopped: run('docker', 'start', CURRENT)
        print('Update failed; original app restarted without restoring/overwriting rows. Protected backup: ' + str(folder), flush=True)
        raise


if __name__ == '__main__':
    try: main()
    except Exception:
        print('Update not completed; secret-bearing details withheld.', flush=True)
        raise SystemExit(1)
