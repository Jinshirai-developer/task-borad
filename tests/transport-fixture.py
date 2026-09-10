"""Disposable v21 HTTP/Chrome QA fixture; never copies user rows or credentials.

Usage: python3 tests/transport-fixture.py start|stop
Copies only the current local schema + EF migration names, then uses an empty DB.
Billing is disabled, outbound network blocked, SMTP captured in dedicated Mailpit.
"""
import json
from pathlib import Path
import subprocess
import sys
import time
import urllib.request

NET = 'task-v21-qa-net'
WEB, DB, MAIL = 'task-v21-qa-web', 'task-v21-qa-db', 'task-v21-qa-mail'
PROXY = 'task-v21-qa-browser-proxy'
LABEL = 'task-transport-v21=disposable-test'
IMAGE = 'task-board:transport-v21'


def run(*args, data=None):
    result = subprocess.run(args, input=data, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode:
        raise RuntimeError(f'{args[0]} operation failed ({result.returncode}); private output withheld')
    return result.stdout


def start():
    names = run('docker', 'ps', '-a', '--format', '{{.Names}}').decode().splitlines()
    assert not set(names).intersection([WEB, DB, MAIL, PROXY]), 'Inspect existing QA containers first'
    assert NET not in run('docker', 'network', 'ls', '--format', '{{.Name}}').decode().splitlines()
    # No data dump except the non-secret EF migration ledger; no sequences/users copied.
    schema = run('docker', 'exec', 'task-board-preview-db', 'pg_dump', '-U', 'preview', '-d', 'preview', '--schema-only', '--no-owner', '--no-privileges')
    migrations = run('docker', 'exec', 'task-board-preview-db', 'pg_dump', '-U', 'preview', '-d', 'preview', '--data-only', '--no-owner', '--no-privileges', '--column-inserts', '-t', '"__EFMigrationsHistory"')
    assert b'20260908183357_AccountBilling' in migrations and b'COPY public.user_profiles' not in migrations
    run('docker', 'network', 'create', '--internal', '--label', LABEL, NET)
    run('docker', 'run', '-d', '--name', DB, '--network', NET, '--label', LABEL,
        '-e', 'POSTGRES_USER=transport', '-e', 'POSTGRES_PASSWORD=isolated-test-only', '-e', 'POSTGRES_DB=transportcheck', 'postgres:16')
    for _ in range(100):
        try:
            run('docker', 'exec', DB, 'pg_isready', '-h', '127.0.0.1', '-U', 'transport', '-d', 'transportcheck')
            break
        except RuntimeError: time.sleep(.2)
    else: raise RuntimeError('Disposable PostgreSQL did not start')
    run('docker', 'exec', '-i', DB, 'psql', '-X', '-U', 'transport', '-d', 'transportcheck', '-v', 'ON_ERROR_STOP=1', data=schema + migrations)
    users = run('docker', 'exec', DB, 'psql', '-X', '-U', 'transport', '-d', 'transportcheck', '-At', '-c', 'SELECT count(*) FROM user_profiles').strip()
    assert users == b'0', 'Fixture must not contain user data'
    run('docker', 'run', '-d', '--name', MAIL, '--network', NET, '--label', LABEL,
        '--network-alias', 'task-v16-qa-mail', '--network-alias', 'task-v15-qa-mail', '--network-alias', 'task-board-identity-mail',
        'axllent/mailpit:v1.31.1')
    env = {
        'ASPNETCORE_ENVIRONMENT': 'Development', 'AllowedHosts': '*',
        'ConnectionStrings__DefaultConnection': f'Host={DB};Port=5432;Database=transportcheck;Username=transport;Password=isolated-test-only',
        'Authentication__PublicBaseUrl': 'http://localhost:5100', 'Authentication__KeyRingPath': '/tmp/transport-keys',
        'Email__Host': MAIL, 'Email__Port': '1025', 'Email__Security': 'None',
        'Email__FromAddress': 'noreply@taskboard.test', 'Email__FromName': 'Transport QA',
        'Billing__Enabled': 'false',
    }
    flags = [item for key, value in env.items() for item in ['-e', key + '=' + value]]
    run('docker', 'run', '-d', '--name', WEB, '--network', NET, '--label', LABEL,
        '--network-alias', 'task-v16-qa-web', '--network-alias', 'task-v15-qa-web', '--network-alias', 'task-board-identity-web',
        *flags, IMAGE)
    # Docker Desktop does not publish ports on an internal-only network.
    # A fixed TCP ingress proxy exposes only the two QA services; the app/DB
    # still have no external network. It does not alter HTTP headers or responses.
    proxy_code = 'const net=require("node:net");for(const [port,host] of [[8080,"'+WEB+'"],[8025,"'+MAIL+'"]])net.createServer(s=>{const r=net.connect(port,host);s.on("error",()=>r.destroy());r.on("error",()=>s.destroy());s.pipe(r).pipe(s);}).listen(port,"0.0.0.0");'
    run('docker', 'create', '--name', PROXY, '--network', 'bridge', '--label', LABEL,
        '-p', '127.0.0.1:5100:8080', '-p', '127.0.0.1:8100:8025', 'node:22', 'node', '-e', proxy_code)
    run('docker', 'network', 'connect', NET, PROXY)
    run('docker', 'start', PROXY)
    for _ in range(100):
        try:
            with urllib.request.urlopen('http://127.0.0.1:5100/health/ready', timeout=1) as response:
                if response.status == 200: break
        except OSError: pass
        time.sleep(.2)
    else: raise RuntimeError('Disposable app did not become ready')
    print(json.dumps({'ready': True, 'url': 'http://localhost:5100/', 'user_rows_copied': 0, 'billing_enabled': False, 'network_internal': True}), flush=True)


def stop():
    names = run('docker', 'ps', '-a', '--format', '{{.Names}}').decode().splitlines()
    for name in [PROXY, WEB, MAIL, DB]:
        if name not in names: continue
        config = json.loads(run('docker', 'inspect', name))[0]
        assert config['Config']['Labels'].get('task-transport-v21') == 'disposable-test'
        assert set(config['NetworkSettings']['Networks']) == ({NET, 'bridge'} if name == PROXY else {NET})
        assert all(m['Type'] == 'volume' and m['Name'] != 'task-board-preview-keys' for m in config['Mounts'])
        run('docker', 'stop', name)
        run('docker', 'rm', '-v', name)
    networks = run('docker', 'network', 'ls', '--format', '{{.Name}}').decode().splitlines()
    if NET in networks:
        config = json.loads(run('docker', 'network', 'inspect', NET))[0]
        assert config['Labels'].get('task-transport-v21') == 'disposable-test' and not config['Containers']
        run('docker', 'network', 'rm', NET)
    print('Removed only the disposable v21 test app, database, mail and network; fixture can be recreated.', flush=True)


if __name__ == '__main__':
    assert sys.argv[1:] in (['start'], ['stop'])
    (start if sys.argv[1] == 'start' else stop)()
