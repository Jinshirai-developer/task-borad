"""Deploy v15 only to the existing local preview, preserving data, configuration and keys.

The migration is additive. Rollback starts the previous app without dropping new columns.
Backups include secrets: keep them under ignored .local with restrictive permissions.
"""
import hashlib
import json
import os
import re
from pathlib import Path
import subprocess
import tempfile
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
CURRENT = 'task-board-preview'
PREVIOUS = 'task-board-preview-before-usability-v15'
IMAGE = 'task-board:usability-v15'
OLD_IMAGE = 'task-board:rewards-v14'
TOOLS_IMAGE = 'task-board:usability-v15-tools'
MIGRATION_MARKER = b'CREATE TABLE task_undo_entries'
NEW_TASK_COLUMNS = ['assignee_user_profile_id', 'checklist_json']
NEW_TABLES = []
DEFAULTS_SQL = "SELECT count(*) FROM tasks WHERE assignee_user_profile_id IS NOT NULL OR checklist_json <> '[]'"
BACKUP_PREFIX = 'usability-20260908.'
FAILED_CONTAINER = 'task-board-preview-usability-v15-failed'
OLD_MIGRATION = '20260907195957_AddPetCollections'
MIGRATION = '20260908100945_AddTaskUsability'


def run(*args):
    result = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode:
        raise RuntimeError(f'{args[0]} operation failed (exit {result.returncode}); inspect protected backup/logs')
    return result.stdout


def sql(statement):
    return run('docker', 'exec', 'task-board-preview-db', 'psql', '-X', '-U', 'preview', '-d', 'preview', '-At', '-v', 'ON_ERROR_STOP=1', '-c', statement).decode().strip()


def digest():
    names = sql("SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename NOT IN ('__EFMigrationsHistory','task_undo_entries') ORDER BY tablename").splitlines()
    result = {}
    for name in names:
        if name in NEW_TABLES: continue
        identifier = '"' + name.replace('"', '""') + '"'
        row = 'to_jsonb(t)-ARRAY[' + ','.join("'" + column + "'" for column in NEW_TASK_COLUMNS) + ']' if name == 'tasks' and NEW_TASK_COLUMNS else 'to_jsonb(t)'
        result[name] = sql("SELECT count(*) || ':' || md5(coalesce(string_agg(md5((" + row + ")::text), '' ORDER BY md5((" + row + ")::text)), '')) FROM " + identifier + ' t')
    return result


def main():
    os.umask(0o077)
    config = json.loads(run('docker', 'inspect', CURRENT))[0]
    names = run('docker', 'ps', '-a', '--format', '{{.Names}}').decode().splitlines()
    assert PREVIOUS not in names, 'A v15 rollback container already exists; inspect before repeating'
    assert config['State']['Running'] and config['Config']['Image'] == OLD_IMAGE
    assert config['Config']['User'] == '1654'
    assert list(config['NetworkSettings']['Networks']) == ['task-board-preview-net']
    assert config['HostConfig']['PortBindings'] == {'8080/tcp': [{'HostIp': '127.0.0.1', 'HostPort': '5097'}]}
    assert [(m['Type'], m.get('Name'), m['Destination']) for m in config['Mounts']] == [('volume', 'task-board-preview-keys', '/keys')]
    assert config['HostConfig']['RestartPolicy']['Name'] == 'no'
    assert sql('SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1') == OLD_MIGRATION
    run('docker', 'image', 'inspect', IMAGE)
    script = run('docker', 'run', '--rm', '--network', 'none', '--entrypoint', 'dotnet', TOOLS_IMAGE, 'ef', 'migrations', 'script', OLD_MIGRATION, MIGRATION, '--no-build', '--configuration', 'Release')
    assert MIGRATION.encode() in script and MIGRATION_MARKER in script
    assert not re.search(rb'^\s*(?:DROP|DELETE|TRUNCATE|UPDATE)\s', script, re.MULTILINE | re.IGNORECASE), 'Migration must be additive'
    backup = Path(tempfile.mkdtemp(prefix=BACKUP_PREFIX, dir=ROOT / '.local/backups'))
    (backup / 'runtime-inspect.json').write_text(json.dumps(config))
    (backup / 'runtime.env').write_text('\n'.join(config['Config']['Env']) + '\n')
    (backup / 'migration.sql').write_bytes(script)
    stopped = renamed = created = False
    try:
        run('docker', 'stop', CURRENT); stopped = True
        with (backup / 'before-usability.dump').open('xb') as output:
            dumped = subprocess.run(['docker', 'exec', 'task-board-preview-db', 'pg_dump', '-U', 'preview', '-d', 'preview', '-Fc'], stdout=output, stderr=subprocess.PIPE)
            assert dumped.returncode == 0 and output.tell() > 0, 'Database backup failed'
        run('docker', 'cp', CURRENT + ':/keys', str(backup / 'keys'))
        for path in (backup / 'keys').rglob('*'): path.chmod(0o700 if path.is_dir() else 0o600)
        before = digest(); (backup / 'before-digests.json').write_text(json.dumps(before))
        migration = subprocess.run(['docker', 'exec', '-i', 'task-board-preview-db', 'psql', '-X', '-U', 'preview', '-d', 'preview', '-v', 'ON_ERROR_STOP=1'], input=script, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        (backup / 'migration-output.txt').write_bytes(migration.stdout + migration.stderr)
        assert migration.returncode == 0, 'Migration failed; previous app will be restarted'
        assert digest() == before, 'Existing data changed during migration'
        assert sql(DEFAULTS_SQL) == '0'
        run('docker', 'rename', CURRENT, PREVIOUS); renamed = True
        run('docker', 'run', '-d', '--name', CURRENT, '--network', 'task-board-preview-net', '--user', config['Config']['User'], '--restart', 'no',
            '-p', '127.0.0.1:5097:8080', '-v', 'task-board-preview-keys:/keys', '--env-file', str(backup / 'runtime.env'), IMAGE)
        created = True
        for _ in range(60):
            try:
                with urllib.request.urlopen('http://localhost:5097/health/ready', timeout=2) as response:
                    if response.status == 200: break
            except Exception: time.sleep(.5)
        else: raise RuntimeError('New app did not become ready')
        assert before == digest(), 'Existing data changed across app restart'
        current = json.loads(run('docker', 'inspect', CURRENT))[0]
        assert sorted(current['Config']['Env']) == sorted(config['Config']['Env'])
        files = [path.relative_to(ROOT / 'frontend').as_posix() for path in (ROOT / 'frontend').iterdir() if path.suffix in ('.js', '.css', '.html')]
        for file in files:
            with urllib.request.urlopen('http://localhost:5097/' + file) as response:
                assert hashlib.sha256(response.read()).digest() == hashlib.sha256((ROOT / 'frontend' / file).read_bytes()).digest(), file
        report = {'image': IMAGE, 'url': 'http://localhost:5097/', 'demo': 'http://localhost:5097/index.html?demo=1',
            'backup': str(backup), 'rollback_container': PREVIOUS, 'data_tables_unchanged': len(before),
            'migration': MIGRATION, 'environment_preserved': True, 'served_files_match': len(files), 'new_outfits_integrated': False}
        (backup / 'verification.json').write_text(json.dumps(report, indent=2))
        print(json.dumps(report, indent=2), flush=True)
    except Exception:
        if created: run('docker', 'stop', CURRENT); run('docker', 'rename', CURRENT, FAILED_CONTAINER)
        if renamed: run('docker', 'rename', PREVIOUS, CURRENT)
        if stopped: run('docker', 'start', CURRENT)
        print('Update failed; original app restarted. Protected backup: ' + str(backup), flush=True)
        raise


if __name__ == '__main__': main()
