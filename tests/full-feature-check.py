"""Run real HTTP suites against request-fixture.py's disposable services only.

Each suite has its own client container/IP and synthetic users. Application
rate limits remain enabled. No credentials or user rows are copied.
"""
from concurrent.futures import ThreadPoolExecutor, as_completed
import json
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
NET = 'task-v21-qa-net'
LABEL = 'task-full-feature-check=disposable-test'
SUITES = {
    'identity': ('identity-smoke.mjs', 'task-board-identity'),
    'teams': ('team-smoke.mjs', 'task-board-identity'),
    'progression': ('progression-smoke.mjs', 'task-board-progression'),
    'tags': ('task-tags-smoke.mjs', 'task-board-tags'),
    'collection': ('pet-collection-smoke.mjs', 'task-board-progression'),
    'usability': ('usability-smoke.mjs', 'task-v15-qa'),
    'companion': ('companion-smoke.mjs', 'task-v16-qa'),
    'requests': ('requests-smoke.mjs', 'task-v15-qa'),
    'billing': ('billing-cap-smoke.mjs', 'task-v17-qa'),
}

def run(*args):
    result = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode:
        raise RuntimeError(f'{args[0]} failed ({result.returncode}); output withheld')
    return result.stdout

def service_ip(name):
    item = json.loads(run('docker', 'inspect', name))[0]
    assert item['Config']['Labels'].get('task-transport-v21') == 'disposable-test'
    assert set(item['NetworkSettings']['Networks']) == {NET}
    return item['NetworkSettings']['Networks'][NET]['IPAddress']

def main():
    selected = sys.argv[1:] or list(SUITES)
    assert selected and len(set(selected)) == len(selected) and set(selected) <= SUITES.keys()
    web, mail = service_ip('task-v21-qa-web'), service_ip('task-v21-qa-mail')
    network = json.loads(run('docker', 'network', 'inspect', NET))[0]
    assert network['Internal'] and network['Labels'].get('task-transport-v21') == 'disposable-test'
    logs = Path(tempfile.mkdtemp(prefix='task-full-feature-', dir='/private/tmp'))
    containers = {}
    results = {}
    try:
        # Keep clients running through the entire run. Stopped containers release
        # their address, which would wrongly share a prior suite's rate window.
        for key in selected:
            script, alias = SUITES[key]
            name = 'task-full-check-' + key
            run('docker', 'create', '--name', name, '--network', NET, '--label', LABEL,
                '--add-host', alias + '-web:' + web, '--add-host', alias + '-mail:' + mail,
                '-e', 'APP_URL=http://' + alias + '-web:8080',
                '-e', 'MAILPIT_URL=http://' + alias + '-mail:8025',
                '-e', 'PUBLIC_APP_URL=http://localhost:5100', 'node:22', 'sleep', '900')
            containers[key] = name
            run('docker', 'cp', str(ROOT / 'tests' / script), name + ':/tmp/check.mjs')
            run('docker', 'start', name)
        def check(key):
            with (logs / (key + '.log')).open('wb') as output:
                code = subprocess.run(['docker', 'exec', containers[key], 'node', '/tmp/check.mjs'], stdout=output, stderr=subprocess.STDOUT).returncode
            print(f'{key}: {"PASS" if code == 0 else "FAIL"}', flush=True)
            return key, code
        with ThreadPoolExecutor(max_workers=3) as pool:
            for future in as_completed([pool.submit(check, key) for key in selected]):
                key, code = future.result()
                results[key] = {'exitCode': code, 'log': str(logs / (key + '.log'))}
        (logs / 'result.json').write_text(json.dumps(results, indent=2) + '\n')
        print(json.dumps({'results': results, 'logDirectory': str(logs)}), flush=True)
    finally:
        for name in containers.values():
            item = json.loads(run('docker', 'inspect', name))[0]
            assert item['Config']['Labels'].get('task-full-feature-check') == 'disposable-test' and not item['Mounts']
            if item['State']['Running']:
                run('docker', 'stop', name)
            run('docker', 'rm', name)
    return int(any(value['exitCode'] for value in results.values()))

if __name__ == '__main__':
    raise SystemExit(main())
