"""Offline UI QA for the whole-pet artwork gallery. No app/API/account access."""
import base64
import functools
import http.server
import json
import os
from pathlib import Path
import socket
import struct
import subprocess
import tempfile
import threading
import time
import urllib.request


class CDP:
    def __init__(self, address):
        from urllib.parse import urlparse
        parts = urlparse(address)
        self.sock = socket.create_connection((parts.hostname, parts.port), timeout=30)
        key = base64.b64encode(os.urandom(16)).decode()
        self.sock.sendall(('GET '+parts.path+' HTTP/1.1\r\nHost: '+parts.hostname+':'+str(parts.port)+'\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: '+key+'\r\nSec-WebSocket-Version: 13\r\n\r\n').encode())
        header = b''
        while not header.endswith(b'\r\n\r\n'):
            header += self.sock.recv(1)
        assert b'101' in header
        self.serial = 0

    def exact(self, count):
        result = b''
        while len(result) < count:
            part = self.sock.recv(count - len(result))
            if not part:
                raise RuntimeError('Browser closed')
            result += part
        return result

    def call(self, method, params=None):
        self.serial += 1
        data = json.dumps({'id': self.serial, 'method': method, 'params': params or {}}).encode()
        mask = os.urandom(4)
        count = len(data)
        prefix = bytes([0x81, 0x80 | count]) if count < 126 else bytes([0x81, 0xfe]) + struct.pack('!H', count)
        self.sock.sendall(prefix + mask + bytes(value ^ mask[i % 4] for i, value in enumerate(data)))
        while True:
            header = self.exact(2)
            count = header[1] & 127
            if count == 126:
                count = struct.unpack('!H', self.exact(2))[0]
            elif count == 127:
                count = struct.unpack('!Q', self.exact(8))[0]
            response = json.loads(self.exact(count))
            if response.get('id') == self.serial:
                if 'error' in response:
                    raise RuntimeError(response['error'])
                return response.get('result', {})

    def evaluate(self, expression):
        response = self.call('Runtime.evaluate', {'expression': expression, 'returnByValue': True, 'awaitPromise': True})
        if response.get('exceptionDetails'):
            raise RuntimeError(response['exceptionDetails'])
        return response.get('result', {}).get('value')


def main():
    repo = Path(__file__).resolve().parents[1]
    output = Path(tempfile.mkdtemp(prefix='task-outfit-browser-', dir='/private/tmp'))
    server = browser = None
    checks = 0
    try:
        # OS-assigned ports avoid disrupting any existing preview or debugger.
        class QuietHandler(http.server.SimpleHTTPRequestHandler):
            def log_message(self, *args):
                pass
        server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(QuietHandler, directory=str(repo / 'frontend')))
        threading.Thread(target=server.serve_forever, daemon=True).start()
        browser = subprocess.Popen(['/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '--headless=new', '--disable-gpu', '--disable-background-networking', '--disable-sync', '--no-first-run', '--remote-debugging-port=0', '--user-data-dir=' + str(output / 'profile'), 'about:blank'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        pages = None
        for _ in range(100):
            try:
                debug_port = int((output / 'profile/DevToolsActivePort').read_text().splitlines()[0])
                pages = json.load(urllib.request.urlopen(f'http://127.0.0.1:{debug_port}/json/list', timeout=1))
                break
            except Exception:
                time.sleep(.1)
        assert pages, 'Browser failed to start'
        cdp = CDP(next(page['webSocketDebuggerUrl'] for page in pages if page['type'] == 'page'))
        cdp.call('Page.enable')
        cdp.call('Page.addScriptToEvaluateOnNewDocument', {'source': 'window.uiErrors=[];addEventListener("error",e=>uiErrors.push(String(e.message)));'})
        cdp.call('Emulation.setDeviceMetricsOverride', {'width': 1440, 'height': 1320, 'deviceScaleFactor': 1, 'mobile': False})
        cdp.call('Page.navigate', {'url': f'http://127.0.0.1:{server.server_port}/previews/pet-outfits-v1/'})
        for _ in range(150):
            if cdp.evaluate('typeof OutfitCatalog !== "undefined" && document.querySelectorAll(".card").length === 15'):
                break
            time.sleep(.1)
        assert cdp.evaluate('OutfitCatalog.length === 90')
        selected_species = os.environ.get('OUTFIT_REVIEW_SPECIES', 'cat,dog,rabbit,fox,panda,dragon').split(',')
        assert all(species in ['cat', 'dog', 'rabbit', 'fox', 'panda', 'dragon'] for species in selected_species)
        for species in selected_species:
            cdp.evaluate('document.getElementById("species").value=' + json.dumps(species) + ';document.getElementById("species").dispatchEvent(new Event("change"))')
            assert cdp.evaluate('Promise.all([...document.images].map(i=>i.decode().then(()=>true,()=>false))).then(r=>r.length===15&&r.every(Boolean))'), species
            assert cdp.evaluate('document.documentElement.scrollWidth <= innerWidth')
            assert cdp.evaluate('uiErrors.length === 0')
            checks += 3
            image = cdp.call('Page.captureScreenshot', {'format': 'png', 'captureBeyondViewport': False})
            (output / ('outfits-v1-' + species + '.png')).write_bytes(base64.b64decode(image['data']))
        for width in [320, 375]:
            cdp.call('Emulation.setDeviceMetricsOverride', {'width': width, 'height': 900, 'deviceScaleFactor': 1, 'mobile': True})
            assert cdp.evaluate('document.documentElement.scrollWidth <= innerWidth')
            checks += 1
        print(json.dumps({'passed': checks, 'imagesLoaded': len(selected_species) * 15, 'artifacts': str(output)}), flush=True)
    finally:
        for process in [browser]:
            if process:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
        if server:
            server.shutdown()
            server.server_close()


if __name__ == '__main__':
    main()
