"""One-time local v17-500 -> v18 UI upgrade. No migrations or Stripe writes.

Keep the already-authorized Stripe listener running. Private backups include
secrets and must never be printed, shared, or committed.
"""
import hashlib
import importlib.util
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
CURRENT = 'task-board-preview'
PREVIOUS = 'task-board-preview-before-purchase-v18'
FAILED = 'task-board-preview-purchase-v18-failed'
OLD_IMAGE = 'task-board:billing-v17-500'
IMAGE = 'task-board:purchase-v18'
MIGRATION = '20260908140702_AddTeamTestBilling'
spec = importlib.util.spec_from_file_location('preview_deployer', ROOT / 'scripts/update-usability-preview.py')
deployer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(deployer)
deployer.NEW_TASK_COLUMNS = []
deployer.NEW_TABLES = []
run, sql, digest = deployer.run, deployer.sql, deployer.digest


def status(path):
    try:
        with urllib.request.urlopen('http://127.0.0.1:5097' + path, timeout=3) as response:
            return response.status
    except urllib.error.HTTPError as error:
        return error.code


def main():
    os.umask(0o077)
    config = json.loads(run('docker', 'inspect', CURRENT))[0]
    names = run('docker', 'ps', '-a', '--format', '{{.Names}}').decode().splitlines()
    assert PREVIOUS not in names and FAILED not in names, 'Inspect existing rollback state before retrying'
    assert config['State']['Running'] and config['Config']['Image'] == OLD_IMAGE
    assert config['Config']['User'] == '1654' and config['HostConfig']['RestartPolicy']['Name'] == 'no'
    assert list(config['NetworkSettings']['Networks']) == ['task-board-preview-net']
    assert config['HostConfig']['PortBindings'] == {'8080/tcp': [{'HostIp': '127.0.0.1', 'HostPort': '5097'}]}
    assert [(m['Type'], m.get('Name'), m['Destination']) for m in config['Mounts']] == [('volume', 'task-board-preview-keys', '/keys')]
    assert sql('SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1') == MIGRATION
    # Avoid attributing a legitimate background subscription sync to this UI-only deployment.
    assert sql('SELECT count(*) FROM team_billing') == '0', 'A new contract exists; review before interrupting the preview'
    old_env = config['Config']['Env']
    assert 'Billing__Enabled=true' in old_env and 'Billing__MonthlyYen=500' in old_env
    run('docker', 'image', 'inspect', IMAGE)
    backup = Path(tempfile.mkdtemp(prefix='purchase-20260909.', dir=ROOT / '.local/backups'))
    (backup / 'runtime-before.json').write_text(json.dumps(config))
    env_path = backup / 'runtime.env'
    env_path.write_text('\n'.join(old_env) + '\n')
    stopped = renamed = created = False
    try:
        run('docker', 'stop', CURRENT)
        stopped = True
        assert sql('SELECT count(*) FROM team_billing') == '0', 'A checkout started during preparation; original app will be restored'
        with (backup / 'before-purchase.dump').open('xb') as output:
            result = subprocess.run(['docker', 'exec', 'task-board-preview-db', 'pg_dump', '-U', 'preview', '-d', 'preview', '-Fc'], stdout=output, stderr=subprocess.PIPE)
            assert result.returncode == 0 and output.tell() > 0, 'Backup failed'
        run('docker', 'cp', CURRENT + ':/keys', str(backup / 'keys'))
        for path in (backup / 'keys').rglob('*'):
            path.chmod(0o700 if path.is_dir() else 0o600)
        before = digest()
        (backup / 'before-digests.json').write_text(json.dumps(before))
        run('docker', 'rename', CURRENT, PREVIOUS)
        renamed = True
        run('docker', 'run', '-d', '--name', CURRENT, '--network', 'task-board-preview-net', '--user', '1654', '--restart', 'no',
            '-p', '127.0.0.1:5097:8080', '-v', 'task-board-preview-keys:/keys', '--env-file', str(env_path), IMAGE)
        created = True
        for _ in range(60):
            try:
                if status('/health/ready') == 200:
                    break
            except (OSError, urllib.error.URLError):
                pass
            time.sleep(.5)
        else:
            raise RuntimeError('New app did not become ready')
        assert status('/api/teams/1/billing') == 401, 'Anonymous plan access must remain protected'
        assert digest() == before, 'Existing data changed across restart'
        assert sql('SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1') == MIGRATION
        current = json.loads(run('docker', 'inspect', CURRENT))[0]
        assert sorted(current['Config']['Env']) == sorted(old_env), 'Environment must be unchanged'
        files = [path for path in (ROOT / 'frontend').iterdir() if path.suffix in ('.js', '.css', '.html')]
        for path in files:
            with urllib.request.urlopen('http://127.0.0.1:5097/' + path.name, timeout=3) as response:
                assert hashlib.sha256(response.read()).digest() == hashlib.sha256(path.read_bytes()).digest(), 'Served file mismatch'
        report = {'image': IMAGE, 'url': 'http://localhost:5097/',
            'demo_purchase': 'http://localhost:5097/index.html?demo=1&billing=plans&team=1',
            'backup': str(backup), 'rollback_container': PREVIOUS, 'data_tables_unchanged': len(before),
            'migration_unchanged': True, 'all_environment_preserved': True, 'served_files_match': len(files),
            'stripe_checkout_e2e_tested': False, 'stripe_objects_created': False}
        (backup / 'verification.json').write_text(json.dumps(report, indent=2))
        print(json.dumps(report, indent=2), flush=True)
    except Exception:
        if created:
            run('docker', 'stop', CURRENT)
            run('docker', 'rename', CURRENT, FAILED)
        if renamed:
            run('docker', 'rename', PREVIOUS, CURRENT)
        if stopped:
            run('docker', 'start', CURRENT)
        print('Update failed; original app restarted. Private backup: ' + str(backup), flush=True)
        raise


if __name__ == '__main__':
    try:
        if sys.argv[1:] == ['--focus-refinement']:
            OLD_IMAGE = 'task-board:purchase-v18'
            IMAGE = 'task-board:purchase-v18-1'
            PREVIOUS = 'task-board-preview-before-purchase-v18-1'
            FAILED = 'task-board-preview-purchase-v18-1-failed'
        else:
            assert not sys.argv[1:], 'Unsupported deployment arguments'
        main()
    except Exception:
        print('Local update did not complete; secret-bearing details withheld.', flush=True)
        raise SystemExit(1)
