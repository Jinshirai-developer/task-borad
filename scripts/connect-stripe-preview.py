"""One-time connection of the existing local v17 preview to the approved test plan.

No migrations, no Stripe object writes and no user-data test mutations. Credentials
are confined to ignored, private settings/backups and the server environment.
Run only after stripe-local.rb listen-login reports readiness and matching API version.
"""
import hashlib
import hmac
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
SETTINGS = ROOT / '.local/stripe-test.env'
CURRENT = 'task-board-preview'
PREVIOUS = 'task-board-preview-before-stripe-connect'
FAILED = 'task-board-preview-stripe-connect-failed'
OLD_IMAGE = 'task-board:billing-v17'
IMAGE = 'task-board:billing-v17-500'
MIGRATION = '20260908140702_AddTeamTestBilling'
API_VERSION = '2026-08-26.dahlia'
KEYS = ['Billing__Enabled', 'Billing__MonthlyYen', 'Billing__SecretKey', 'Billing__WebhookSecret', 'Billing__PriceId']
spec = importlib.util.spec_from_file_location('preview_deployer', ROOT / 'scripts/update-usability-preview.py')
deployer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(deployer)
deployer.NEW_TASK_COLUMNS = []
deployer.NEW_TABLES = []
run, sql, digest = deployer.run, deployer.sql, deployer.digest


def read_settings(path=SETTINGS):
    assert path.is_file() and not path.is_symlink(), 'Private regular settings file required'
    assert path.stat().st_uid == os.getuid() and path.stat().st_mode & 0o777 == 0o600, 'Settings must be owned and mode 600'
    values = {}
    for raw in path.read_text().splitlines():
        line = raw.strip()
        if not line or line.startswith('#'):
            continue
        key, separator, value = line.partition('=')
        assert separator and key in KEYS and key not in values, 'Unexpected settings layout'
        values[key] = value
    assert set(values) == set(KEYS), 'Incomplete settings'
    assert values['Billing__Enabled'] == 'false' and values['Billing__MonthlyYen'] == '500', 'Expected disabled approved plan'
    assert re.fullmatch(r'rk_test_[A-Za-z0-9]+', values['Billing__SecretKey']), 'Restricted test key required'
    assert re.fullmatch(r'whsec_[A-Za-z0-9]+', values['Billing__WebhookSecret']), 'Signing secret required'
    assert re.fullmatch(r'price_[A-Za-z0-9]+', values['Billing__PriceId']), 'Price ID required'
    return values


def status(path, payload=None, signature=None):
    headers = {'Content-Type': 'application/json'}
    if signature is not None:
        headers['Stripe-Signature'] = signature
    request = urllib.request.Request('http://127.0.0.1:5097' + path, data=payload, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=3) as response:
            return response.status
    except urllib.error.HTTPError as error:
        return error.code


def signed_probe(secret, live=False, valid=True):
    # A non-business event deliberately ignored after signature/mode validation.
    payload = json.dumps({'id': 'evt_taskboard_connection_probe', 'object': 'event', 'api_version': API_VERSION,
        'type': 'taskboard.connection_probe', 'livemode': live,
        'data': {'object': {'id': 'probe', 'object': 'taskboard_probe'}}}, separators=(',', ':')).encode()
    stamp = str(int(time.time()))
    signature = hmac.new(secret.encode(), stamp.encode() + b'.' + payload, hashlib.sha256).hexdigest() if valid else '0' * 64
    return status('/api/billing/stripe-webhook', payload, f't={stamp},v1={signature}')


def main():
    os.umask(0o077)
    values = read_settings()
    # Real price GET only. The listener has separately checked the OAuth Sandbox and API version.
    check = json.loads(run('ruby', str(ROOT / 'scripts/stripe-local.rb'), 'check'))
    assert check.get('price_matches') and check.get('test_mode') and check.get('monthly_yen') == 500
    config = json.loads(run('docker', 'inspect', CURRENT))[0]
    names = run('docker', 'ps', '-a', '--format', '{{.Names}}').decode().splitlines()
    assert PREVIOUS not in names and FAILED not in names, 'Inspect existing rollback state before retrying'
    assert config['State']['Running'] and config['Config']['Image'] == OLD_IMAGE
    assert config['Config']['User'] == '1654' and config['HostConfig']['RestartPolicy']['Name'] == 'no'
    assert list(config['NetworkSettings']['Networks']) == ['task-board-preview-net']
    assert config['HostConfig']['PortBindings'] == {'8080/tcp': [{'HostIp': '127.0.0.1', 'HostPort': '5097'}]}
    assert [(m['Type'], m.get('Name'), m['Destination']) for m in config['Mounts']] == [('volume', 'task-board-preview-keys', '/keys')]
    assert sql('SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1') == MIGRATION
    assert sql('SELECT count(*) FROM team_billing') == '0', 'An existing contract requires a different connection workflow'
    run('docker', 'image', 'inspect', IMAGE)
    old_env = config['Config']['Env']
    assert not any(item == 'Billing__Enabled=true' for item in old_env)
    assert not any(item.startswith('Billing__') and item.partition('=')[0] not in KEYS for item in old_env)
    billing = dict(values, Billing__Enabled='true')
    new_env = [item for item in old_env if item.partition('=')[0] not in KEYS] + [f'{key}={billing[key]}' for key in KEYS]
    backup = Path(tempfile.mkdtemp(prefix='stripe-connect-20260909.', dir=ROOT / '.local/backups'))
    (backup / 'runtime-before.json').write_text(json.dumps(config))
    (backup / 'runtime-before.env').write_text('\n'.join(old_env) + '\n')
    env_path = backup / 'runtime-with-stripe.env'
    env_path.write_text('\n'.join(new_env) + '\n')
    stopped = renamed = created = False
    try:
        run('docker', 'stop', CURRENT)
        stopped = True
        with (backup / 'before-stripe.dump').open('xb') as output:
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
            raise RuntimeError('Connected app did not become ready')
        assert status('/api/teams/1/billing') == 401, 'Anonymous plan access must remain protected'
        secret = values['Billing__WebhookSecret']
        assert signed_probe(secret) == 200, 'Signed local probe rejected'
        assert signed_probe(secret, valid=False) == 400, 'Forged probe not rejected'
        assert signed_probe(secret, live=True) == 400, 'Live probe not rejected'
        assert digest() == before, 'Existing data changed across restart/probes'
        assert sql('SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1') == MIGRATION
        current = json.loads(run('docker', 'inspect', CURRENT))[0]
        assert sorted(current['Config']['Env']) == sorted(new_env), 'Unexpected environment changes'
        files = [path for path in (ROOT / 'frontend').iterdir() if path.suffix in ('.js', '.css', '.html')]
        for path in files:
            with urllib.request.urlopen('http://127.0.0.1:5097/' + path.name, timeout=3) as response:
                assert hashlib.sha256(response.read()).digest() == hashlib.sha256(path.read_bytes()).digest(), 'Served file mismatch'
        assert read_settings() == values, 'Local settings changed during connection'
        patch = f'*** Begin Patch\n*** Update File: {SETTINGS}\n@@\n-Billing__Enabled=false\n+Billing__Enabled=true\n*** End Patch\n'
        result = subprocess.run(['apply_patch'], input=patch.encode(), stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        assert result.returncode == 0, 'Local enabled flag could not be saved'
        report = {'image': IMAGE, 'url': 'http://localhost:5097/', 'monthly_yen': 500,
            'backup': str(backup), 'rollback_container': PREVIOUS, 'data_tables_unchanged': len(before),
            'migration_unchanged': True, 'non_billing_environment_preserved': True, 'served_files_match': len(files),
            'local_signature_probes_passed': 3, 'stripe_checkout_e2e_tested': False, 'stripe_objects_created': False}
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
        print('Connection failed; original app restarted. Private backup: ' + str(backup), flush=True)
        raise


if __name__ == '__main__':
    try:
        main()
    except Exception:
        print('Local connection did not complete; secret-bearing details withheld.', flush=True)
        raise SystemExit(1)
