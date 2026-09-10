"""Cat-only visual review QA at localhost:5089; no application API or database."""
import base64
import json
import os
import socket
import struct
import subprocess
import time
import urllib.request
from pathlib import Path

import tempfile
ROOT = Path(tempfile.mkdtemp(prefix='task-cat-review-', dir='/private/tmp'))
server = browser = None

class CDP:
    def __init__(self, address):
        from urllib.parse import urlparse
        parts = urlparse(address)
        self.sock = socket.create_connection((parts.hostname, parts.port), timeout=30)
        key = base64.b64encode(os.urandom(16)).decode()
        self.sock.sendall(('GET '+parts.path+' HTTP/1.1\r\nHost: 127.0.0.1:9340\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: '+key+'\r\nSec-WebSocket-Version: 13\r\nOrigin: http://127.0.0.1:9340\r\n\r\n').encode())
        header = b''
        while not header.endswith(b'\r\n\r\n'): header += self.sock.recv(1)
        assert b'101' in header
        self.serial = 0
    def exact(self, size):
        result = b''
        while len(result)<size:
            chunk = self.sock.recv(size-len(result))
            if not chunk: raise RuntimeError('CDP closed')
            result += chunk
        return result
    def call(self, method, params=None):
        self.serial += 1
        payload = json.dumps({'id':self.serial,'method':method,'params':params or {}}).encode()
        mask = os.urandom(4)
        length = len(payload)
        prefix = bytes([0x81,0x80|length]) if length<126 else bytes([0x81,0x80|126])+struct.pack('!H',length)
        self.sock.sendall(prefix+mask+bytes(v^mask[i%4] for i,v in enumerate(payload)))
        while True:
            header = self.exact(2)
            length = header[1]&127
            if length==126:length=struct.unpack('!H',self.exact(2))[0]
            elif length==127:length=struct.unpack('!Q',self.exact(8))[0]
            result=json.loads(self.exact(length))
            if result.get('id')==self.serial:
                if 'error' in result:raise RuntimeError(result['error'])
                return result.get('result',{})
    def evaluate(self, expression):
        result=self.call('Runtime.evaluate',{'expression':expression,'returnByValue':True,'awaitPromise':True})
        if result.get('exceptionDetails'):raise RuntimeError(result['exceptionDetails'])
        return result.get('result',{}).get('value')


results=[]
browser=None
try:
    browser=subprocess.Popen(['/Applications/Google Chrome.app/Contents/MacOS/Google Chrome','--headless=new','--disable-gpu','--disable-background-timer-throttling','--disable-renderer-backgrounding','--disable-backgrounding-occluded-windows','--disable-background-networking','--disable-sync','--no-first-run','--remote-debugging-port=9340','--remote-allow-origins=http://127.0.0.1:9340','--user-data-dir='+str(ROOT/'profile'),'about:blank'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    for attempt in range(100):
        try: pages=json.load(urllib.request.urlopen('http://127.0.0.1:9340/json/list',timeout=1));break
        except Exception: time.sleep(.1)
    cdp=CDP(next(p['webSocketDebuggerUrl'] for p in pages if p['type']=='page'))
    cdp.call('Page.enable');cdp.call('Network.enable')
    cdp.call('Page.addScriptToEvaluateOnNewDocument',{'source':"window.reviewErrors=[];window.reviewRequests=[];addEventListener('error',e=>reviewErrors.push(e.message));const fetchOriginal=fetch;window.fetch=(u,o)=>{reviewRequests.push(String(u));return fetchOriginal(u,o)}"})
    cdp.call('Emulation.setDeviceMetricsOverride',{'width':1440,'height':1120,'deviceScaleFactor':1,'mobile':False})
    cdp.call('Page.navigate',{'url':'http://localhost:5089/'})
    for attempt in range(100):
        if cdp.evaluate("typeof window.alphaAudit!=='undefined'"):break
        time.sleep(.1)
    alpha=cdp.evaluate("window.alphaAudit")
    print(json.dumps({'alpha':alpha,'artifacts':str(ROOT)},ensure_ascii=False),flush=True)
    for background in ['light','dark','checker']:
        cdp.evaluate("document.getElementById('background').value="+json.dumps(background)+";document.getElementById('background').dispatchEvent(new Event('change'))")
        assert cdp.evaluate("Array.from(document.querySelectorAll('.stage')).every(e=>getComputedStyle(e).transform==='none'&&getComputedStyle(e).animationName==='none')")
        assert cdp.evaluate("document.documentElement.scrollWidth<=innerWidth")
        data=cdp.call('Page.captureScreenshot',{'format':'png','captureBeyondViewport':False})
        (ROOT/('cat-review-'+background+'.png')).write_bytes(base64.b64decode(data['data']))
        if background=='dark':
            clip=cdp.evaluate("(()=>{const r=document.querySelector('.samples').getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,scale:1}})()")
            data=cdp.call('Page.captureScreenshot',{'format':'png','clip':clip})
            (ROOT/'cat-review-four.png').write_bytes(base64.b64decode(data['data']))
    for action in ['pet','treat','rest']:
        if cdp.evaluate("document.querySelector('button[data-action="+action+"]').disabled"):
            assert action=='pet' and not next(item['valid'] for item in alpha if item['id']=='sample-pet')
            results.append(action+' correctly disabled until genuine transparency is ready')
            continue
        cdp.evaluate("document.querySelector('button[data-action="+action+"]').click()")
        time.sleep(.25)
        assert cdp.evaluate("getComputedStyle(document.getElementById('demo-stage')).transform==='none'")
        assert cdp.evaluate("getComputedStyle(document.getElementById('demo-cat')).backgroundColor==='rgba(0, 0, 0, 0)'")
        assert cdp.evaluate("getComputedStyle(document.getElementById('demo-cat')).animationName!=='none'")
        results.append(action+' background fixed')
    cdp.call('Emulation.setEmulatedMedia',{'features':[{'name':'prefers-reduced-motion','value':'reduce'}]})
    assert cdp.evaluate("getComputedStyle(document.getElementById('sample-pet')).animationName==='none'")
    assert cdp.evaluate("getComputedStyle(document.getElementById('demo-cat')).animationName==='none'")
    cdp.call('Emulation.setEmulatedMedia',{'features':[{'name':'prefers-reduced-motion','value':'no-preference'}]})
    cdp.evaluate("document.getElementById('motion').checked=false;document.getElementById('motion').dispatchEvent(new Event('change'))")
    assert cdp.evaluate("getComputedStyle(document.getElementById('sample-pet')).animationName==='none'")
    assert cdp.evaluate("getComputedStyle(document.getElementById('demo-cat')).animationName==='none'")
    cdp.call('Emulation.setDeviceMetricsOverride',{'width':375,'height':900,'deviceScaleFactor':1,'mobile':True})
    assert cdp.evaluate("document.documentElement.scrollWidth<=innerWidth")
    data=cdp.call('Page.captureScreenshot',{'format':'png','captureBeyondViewport':False})
    (ROOT/'cat-review-mobile.png').write_bytes(base64.b64decode(data['data']))
    assert cdp.evaluate("reviewErrors.length===0 && reviewRequests.length===0")
    print(json.dumps({'checks':results,'allTransparent':all(item['valid'] for item in alpha),'noApiRequests':True,'artifacts':str(ROOT)},ensure_ascii=False),flush=True)
    assert len(alpha)==4 and all(item['valid'] for item in alpha), 'All four PNGs must contain genuine background transparency'
finally:
    if browser:
        browser.terminate()
        try: browser.wait(timeout=5)
        except subprocess.TimeoutExpired: browser.kill()
