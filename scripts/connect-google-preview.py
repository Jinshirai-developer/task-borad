"""Enable an existing Google web OAuth client in local v28, preserving rows/keys/contracts.

Input is a downloaded Google client JSON path. Secret contents are never printed.
Run --check first to validate the file without modifying the app. One-time activation.
"""
import argparse
import http.cookiejar
import importlib.util
import json
import os
from pathlib import Path
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
ORIGIN = 'http://localhost:5097'
REDIRECT = ORIGIN + '/signin-google'
PREFIX = 'Authentication__Google__'
spec = importlib.util.spec_from_file_location('google_activation_release', Path(__file__).with_name('update-transport-preview.py'))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


def read_credentials(path):
    content = json.loads(Path(path).read_text())
    client = content.get('web', {})
    identifier, secret = client.get('client_id'), client.get('client_secret')
    if not isinstance(identifier, str) or not identifier.endswith('.apps.googleusercontent.com'):
        raise ValueError('A Google web application client JSON is required.')
    if not isinstance(secret, str) or not secret.strip():
        raise ValueError('The downloaded file has no client secret. Download it immediately after creating the secret.')
    if any(character in identifier + secret for character in '\r\n\0'):
        raise ValueError('Invalid credential characters.')
    if REDIRECT not in client.get('redirect_uris', []):
        raise ValueError('Register http://localhost:5097/signin-google before downloading the client JSON.')
    return {PREFIX + 'Enabled': 'true', PREFIX + 'ClientId': identifier, PREFIX + 'ClientSecret': secret}


def verify_enabled(identifier):
    # Only contact our local app. Do not follow the generated Google authorization URL.
    client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    with client.open(ORIGIN + '/api/auth/config', timeout=5) as response:
        assert json.load(response)['googleLoginEnabled'] is True
    with client.open(ORIGIN + '/api/auth/csrf', timeout=5) as response:
        token = json.load(response)['token']
    request = urllib.request.Request(ORIGIN + '/api/auth/google/start', data=b'{}', method='POST',
        headers={'Content-Type': 'application/json', 'X-CSRF-TOKEN': token})
    with client.open(request, timeout=5) as response:
        destination = urllib.parse.urlparse(json.load(response)['url'])
    assert destination.scheme == 'https' and destination.netloc == 'accounts.google.com'
    query = urllib.parse.parse_qs(destination.query)
    assert query['client_id'] == [identifier]
    assert query['redirect_uri'] == [REDIRECT]
    assert query['code_challenge_method'] == ['S256'] and query['code_challenge'][0]
    assert set(query['scope'][0].split()) == {'openid', 'email', 'profile'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('client_json', type=Path)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    updates = read_credentials(args.client_json)
    if args.check:
        print(json.dumps({'web_client': True, 'client_secret_present': True, 'redirect_uri': REDIRECT,
            'app_modified': False}))
        return
    config = json.loads(release.run('docker', 'inspect', release.CURRENT))[0]
    current = dict(entry.split('=', 1) for entry in config['Config']['Env'])
    assert current.get('Authentication__PublicBaseUrl', '').rstrip('/') == ORIGIN
    assert current.get(PREFIX + 'Enabled', 'false').lower() == 'false', 'Google is already configured; inspect before changing credentials'
    os.umask(0o077)
    folder = ROOT / '.local' / 'google-login'
    folder.mkdir(mode=0o700, exist_ok=True)
    folder.chmod(0o700)
    saved = folder / 'client.json'
    if saved.exists():
        assert read_credentials(saved) == updates, 'Different Google credentials already exist; inspect before overwriting'
    else:
        with saved.open('x') as handle:
            handle.write(args.client_json.read_text())
    saved.chmod(0o600)
    release.OLD_IMAGE = release.IMAGE = 'task-board:google-v28'
    release.PREVIOUS = 'task-board-preview-before-google-connect-v28'
    release.FAILED = 'task-board-preview-google-connect-v28-failed'
    release.BACKUP_PREFIX = 'google-connect-v28-'
    release.DUMP_NAME = 'before-google-connect.dump'
    release.ENVIRONMENT_UPDATES = updates
    release.PREVIEW_VALIDATOR = lambda: verify_enabled(updates[PREFIX + 'ClientId'])
    release.main()


if __name__ == '__main__':
    try:
        main()
    except Exception:
        print('Google activation not completed; secret-bearing details withheld. Inspect protected backup and container state before retrying.')
        raise SystemExit(1)
