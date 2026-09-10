"""Isolated mock-API browser QA. Only local frontend files; never the preview or a DB."""
import base64
import json
import os
import socket
import struct
import sys
import subprocess
import time
import urllib.request
from pathlib import Path

import tempfile
ROOT = Path(tempfile.mkdtemp(prefix='task-pet-browser-', dir='/private/tmp'))
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

def wait(expression, label):
    deadline=time.monotonic()+15
    while time.monotonic()<deadline:
        # CDP serializes a DOM element as {}, which is falsey in Python even when
        # the JavaScript predicate succeeded. Normalize the predicate in JS first.
        if cdp.evaluate('Boolean(' + expression + ')'): return
        time.sleep(.04)
    raise AssertionError((label,cdp.evaluate("({errors:uiErrors,message:document.getElementById('pet-play-message')?.textContent,requests:petFixture.requests.slice(-5),fixture:collect(),sprite:document.getElementById('pet-sprite')?.outerHTML})")))

def click(id):
    cdp.evaluate('document.getElementById('+json.dumps(id)+').click()')

def check(expression, label):
    assert cdp.evaluate(expression),label
    results.append(label)

def screenshot(name):
    time.sleep(.1)
    data=cdp.call('Page.captureScreenshot',{'format':'png','captureBeyondViewport':False})
    (ROOT/(name+'.png')).write_bytes(base64.b64decode(data['data']))

def layout_check():
    check("document.documentElement.scrollWidth<=innerWidth",'no page overflow')
    check("Array.from(document.querySelectorAll('[role=dialog]')).filter(e=>e.getClientRects().length).every(e=>e.scrollWidth<=e.clientWidth)",'no dialog overflow')
    check("uiErrors.length===0",'no browser errors')
    check("[...document.querySelectorAll('.pet-reward-sprite')].filter(e=>e.getClientRects().length).every(e=>{const r=e.getBoundingClientRect(),a=Number(e.style.getPropertyValue('--art-ratio'));return Math.abs(r.width/r.height/a-1)<.04})",'reward previews preserve actual image proportions')
    check("(()=>{const p=document.getElementById('pet-interact-button').getBoundingClientRect(),s=document.getElementById('pet-sprite').getBoundingClientRect();return s.top>=p.top&&s.bottom<=p.bottom&&s.left>=p.left&&s.right<=p.right})()",'sprite contained by its button')

results=[]
try:
    target_origin=os.environ.get('PET_BROWSER_ORIGIN','http://127.0.0.1:5088')
    assert target_origin in ['http://127.0.0.1:5088','http://localhost:5097'], 'Only isolated local fixture origins are supported'
    for port in [5088,9340]:
        with socket.socket() as probe: assert probe.connect_ex(('127.0.0.1',port))!=0,'Fixture port in use'
    repo=Path(__file__).resolve().parents[1]
    server=subprocess.Popen(['python3','-m','http.server','5088','--bind','127.0.0.1','--directory',str(repo/'frontend')],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    browser=subprocess.Popen(['/Applications/Google Chrome.app/Contents/MacOS/Google Chrome','--headless=new','--disable-gpu','--disable-background-timer-throttling','--disable-renderer-backgrounding','--disable-backgrounding-occluded-windows','--disable-background-networking','--disable-sync','--no-first-run','--remote-debugging-port=9340','--remote-allow-origins=http://127.0.0.1:9340','--user-data-dir='+str(ROOT/'profile'),'about:blank'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    for attempt in range(100):
        try: pages=json.load(urllib.request.urlopen('http://127.0.0.1:9340/json/list',timeout=1));break
        except Exception: time.sleep(.1)
    cdp=CDP(next(p['webSocketDebuggerUrl'] for p in pages if p['type']=='page'))
    cdp.call('Page.enable');cdp.call('Network.enable');cdp.call('Network.setCacheDisabled',{'cacheDisabled':True})
    cdp.call('Page.addScriptToEvaluateOnNewDocument',{'source':(repo/'tests/pet-play-browser-fixture.js').read_text()})
    cdp.call('Emulation.setDeviceMetricsOverride',{'width':1440,'height':1000,'deviceScaleFactor':1,'mobile':False})
    if os.environ.get('PET_REVIEW_ONLY') == '1':
        cdp.call('Page.navigate',{'url':target_origin+'/previews/rewards-v14/'})
        wait("typeof PetRewardArt !== 'undefined' && document.querySelectorAll('.card').length===5", 'reward review loaded')
        for species in os.environ.get('PET_REVIEW_SPECIES','cat,dog,rabbit,fox,panda,dragon').split(','):
            for row in range(4):
                cdp.evaluate("document.getElementById('species').value="+json.dumps(species)+";document.getElementById('stage').value="+str(row)+";show()")
                check("Promise.all([...document.images].map(i=>i.decode().then(()=>true,()=>false))).then(r=>r.every(Boolean))", 'review images loaded '+species+str(row))
                time.sleep(.3)
                screenshot('rewards-v14-'+species+'-'+str(row))
        print(json.dumps({'artifacts':str(ROOT),'checks':results},ensure_ascii=False),flush=True)
        sys.exit(0)
    cdp.call('Page.navigate',{'url':target_origin+'/index.html'})
    wait("typeof PetPlay!=='undefined' && document.getElementById('pet-gift-badge').textContent==='🎁 5'",'initial collection')
    if os.environ.get('PET_LEGACY_ONLY') == '1':
        cdp.evaluate("petFixture.setLevel(20);pet.species='cat';choices.set(1,'hat');choices.set(2,'bow');choices.set(3,'mat');appearance={stage:'base',hatLevel:1,bowLevel:2,matLevel:3};PetPlay.onProfile({...pet});PetPlay.refresh(true)")
        cdp.evaluate("petFixture.setLevel(1);PetPlay.onProfile({...pet});PetPlay.refresh(true)")
        cdp.evaluate("petFixture.setLevel(20);PetPlay.onProfile({...pet});PetPlay.refresh(true)")
        cdp.evaluate("choices.set(6,'hat');appearance.hatLevel=6;PetPlay.onMood('idle');PetPlay.refresh(true)")
        print(json.dumps(cdp.evaluate("({level:collect().level,appearance:collect().appearance,rewards:collect().rewards.filter(r=>r.level>5),sprite:document.getElementById('pet-sprite').outerHTML,requests:petFixture.requests.slice(-3)})"),ensure_ascii=False),flush=True)
        sys.exit(0)
    check("document.querySelectorAll('#pet-interact-button').length===1 && !document.querySelector('#pet-play-root').closest('aside')",'main has one portrait shortcut only')
    cdp.evaluate("document.getElementById('pet-interact-button').focus()")
    click('pet-interact-button');wait("!document.getElementById('settings-pet').hidden",'portrait opens settings pet')
    check("!document.getElementById('pet-panel-gifts').hidden",'gift badge opens gifts directly')
    check("!document.getElementById('unlock-overview').hidden",'growth information is in pet settings')
    click('pet-tab-gifts');check("document.querySelectorAll('.pet-gift-card').length===3",'three gifts per level')
    check("document.querySelectorAll('#pet-gift-level option').length===5",'new gifts stop at Lv5')
    screenshot('pet-gifts-desktop-v14')
    cdp.evaluate("petFixture.confirm=false;document.querySelector('.pet-gift-card button').click()")
    check("petFixture.requests.filter(r=>r.method==='POST').length===0",'cancel does not claim')
    cdp.evaluate("petFixture.confirm=true;document.querySelector('.pet-gift-card button').click()")
    wait("document.querySelector('.pet-gift-card.is-owned')!==null",'gift claimed')
    check("document.querySelectorAll('.pet-gift-card button:disabled').length===3",'claimed level cannot be chosen again')
    click('pet-tab-dress')
    cdp.evaluate("document.getElementById('pet-dress-hatLevel').value='1';document.getElementById('pet-dress-hatLevel').dispatchEvent(new Event('change'));document.getElementById('pet-dress-stage').value='explorer';document.getElementById('pet-dress-stage').dispatchEvent(new Event('change'))")
    check("document.querySelector('#pet-play-preview .reward-hat') && document.getElementById('pet-play-preview').dataset.stage==='1'",'appearance draft previews before saving')
    check("document.getElementById('pet-sprite').dataset.stage==='0'",'main remains saved appearance while trying on')
    click('pet-dress-save');wait("document.getElementById('pet-sprite').dataset.stage==='1'",'saved growth applied')
    screenshot('pet-dress-desktop-v14')
    cdp.evaluate("petFixture.setLevel(4);PetPlay.onProfile({...pet});PetPlay.refresh()")
    wait("document.getElementById('pet-sprite').dataset.stage==='1' && !document.getElementById('pet-play-retry')",'level drop keeps earned growth')
    click('pet-tab-dress');check("document.getElementById('pet-dress-hatLevel').value==='1'",'eligible cosmetic survives drop')
    cdp.evaluate("petFixture.setLevel(20);PetPlay.onProfile({...pet})")
    wait("document.getElementById('pet-gift-badge').textContent==='🎁 4'",'level20 gifts ready')
    click('pet-tab-dress')
    check("!document.querySelector('#pet-dress-stage option[value=festival]').disabled",'Lv20 optional festival stage unlocked')
    click('pet-tab-touch')
    for action,pose in [('pet','1'),('treat','2'),('rest','4')]:
        click('pet-action-'+action)
        wait("document.getElementById('pet-play-preview').dataset.pose==="+json.dumps(pose),'interaction '+action)
        check("pet.totalExperience===10450",'interaction does not mint XP')
        wait("document.getElementById('pet-play-preview').dataset.interaction===''",'interaction ends')
    click('pet-tab-album');check("document.querySelectorAll('.pet-memory:not(.is-locked)').length===6",'three interactions plus growth memories')
    screenshot('pet-album-desktop-v14')
    click('pet-tab-touch')
    cdp.evaluate("petFixture.holdWrite=true;document.getElementById('pet-action-pet').click()")
    wait("PetPlay.isSaving",'write held')
    check("Array.from(document.querySelectorAll('.pet-touch-actions button')).every(e=>e.disabled)",'double click disabled')
    cdp.evaluate("document.getElementById('settings-close-button').click()")
    check("!document.getElementById('settings-modal').classList.contains('hidden')",'cannot dismiss during write')
    cdp.evaluate("petFixture.releaseWrite()")
    wait("!PetPlay.isSaving",'write finished')
    wait("document.getElementById('pet-play-preview').dataset.interaction===''",'write animation finished')
    cdp.evaluate("petFixture.failWrite=true;document.getElementById('pet-action-treat').click()")
    wait("document.getElementById('pet-play-message').textContent.includes('保存失敗')",'write error visible')
    check("document.querySelectorAll('.pet-touch-actions button:disabled').length===3",'unknown write outcome disables mutations')
    click('pet-play-retry');wait("!document.getElementById('pet-action-treat').disabled",'deliberate retry refreshes only')
    cdp.evaluate("petFixture.failRead=true;PetPlay.refresh()")
    wait("document.getElementById('pet-play-message').textContent.includes('読み込み失敗')",'read error visible')
    click('pet-play-retry');wait("!document.getElementById('pet-action-treat').disabled",'read recovered')
    cdp.evaluate("petFixture.holdRead=true;PetPlay.refresh(true);void 0")
    wait("typeof petFixture.releaseRead==='function'",'old read held')
    cdp.evaluate("petFixture.setLevel(1);PetPlay.onProfile({...pet})")
    wait("document.getElementById('pet-gift-badge').hidden",'new level1 read settled')
    cdp.evaluate("petFixture.releaseRead()")
    check("document.getElementById('pet-gift-badge').hidden",'stale Lv20 response cannot restore unlocks')
    click('pet-tab-touch')
    cdp.evaluate("document.getElementById('pet-tab-touch').focus();document.getElementById('pet-tab-touch').dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowRight',bubbles:true}))")
    check("document.activeElement.id==='pet-tab-gifts' && document.getElementById('pet-tab-gifts').getAttribute('aria-selected')==='true'",'keyboard tabs update focus and selection')
    cdp.evaluate("document.getElementById('pet-tab-gifts').dispatchEvent(new KeyboardEvent('keydown',{key:'End',bubbles:true}))")
    check("document.activeElement.id==='pet-tab-album'",'End reaches album')
    # All six images, all four stages and six emotions use distinct, valid crop positions.
    cdp.evaluate("petFixture.setLevel(20)")
    for species in ['dog','cat','rabbit','fox','panda','dragon']:
        cdp.evaluate("pet.species="+json.dumps(species)+";PetPlay.onProfile({...pet})")
        wait("document.getElementById('pet-play-preview').dataset.species==="+json.dumps(species),'species '+species)
        cdp.evaluate("PetPlay.refresh()")
        click('pet-tab-dress')
        for stage,row in [('base','0'),('explorer','1'),('grown','2'),('festival','3')]:
            cdp.evaluate("document.getElementById('pet-dress-stage').value="+json.dumps(stage)+";document.getElementById('pet-dress-stage').dispatchEvent(new Event('change'))")
            check("document.getElementById('pet-play-preview').dataset.stage==="+json.dumps(row),'stage '+species+' '+stage)
        click('pet-tab-touch')
        for pose,col in [('idle','0'),('happy','1'),('working','2'),('proud','3'),('sleepy','4'),('sad','5')]:
            cdp.evaluate("PetPlay.onMood("+json.dumps(pose)+")")
            check("document.getElementById('pet-play-preview').dataset.pose==="+json.dumps(col),'expression '+species+' '+pose)
        check("new Promise(resolve=>{let i=new Image();i.onload=()=>{const c=document.createElement('canvas');c.width=i.width;c.height=i.height;const x=c.getContext('2d');x.drawImage(i,0,0);const d=x.getImageData(0,0,i.width,i.height).data;let a=0;for(let n=3;n<d.length;n+=4)if(d[n]===0)a++;resolve(i.width===1536&&i.height===1024&&a>i.width*i.height*.35)};i.onerror=()=>resolve(false);i.src='assets/pet/portfolio-"+species+"-atlas-v2-alpha.png';})",'transparent atlas loaded '+species)
    # Approved cat cutouts: actual image alpha, all equipment slots, fixed frame,
    # saved appearance/reload, and the existing interaction API/XP behavior.
    cdp.evaluate("choices.set(2,'bow');choices.set(3,'mat');appearance={stage:'base',hatLevel:null,bowLevel:null,matLevel:null};pet.species='cat';PetPlay.onProfile({...pet});void PetPlay.refresh(true)")
    wait("document.querySelector('#pet-sprite .pet-character') && !document.getElementById('pet-play-retry')",'cat cutout ready')
    click('pet-tab-gifts')
    check("document.querySelectorAll('.pet-gift-card .pet-reward-sprite img').length===3",'three generated reward thumbnails for current species')
    screenshot('cat-integrated-gifts-v14')
    for variant in ['idle','pet','hat','bow']:
        check("new Promise(resolve=>{const i=new Image();i.onload=()=>{const c=document.createElement('canvas');c.width=i.width;c.height=i.height;const x=c.getContext('2d');x.drawImage(i,0,0);const d=x.getImageData(0,0,c.width,c.height).data;let transparent=0,visible=0;for(let p=3;p<d.length;p+=4){if(d[p]===0)transparent++;if(d[p]>=128)visible++;}resolve(i.width===1254&&i.height===1254&&transparent>c.width*c.height*.25&&visible>c.width*c.height*.1)};i.onerror=()=>resolve(false);i.src='assets/pet/portfolio-cat-"+variant+"-v3.png';})",'genuine cat alpha '+variant)
    click('pet-tab-dress')
    for name,hat,bow in [('normal','',''),('hat','1',''),('bow','','2'),('both','1','2')]:
        cdp.evaluate("for(const [key,value] of Object.entries("+json.dumps({'hatLevel':hat,'bowLevel':bow,'matLevel':'3'})+")){const e=document.getElementById('pet-dress-'+key);e.value=value;e.dispatchEvent(new Event('change'));}")
        check("document.getElementById('pet-play-preview').dataset.art==='cat-cutout' && !!document.querySelector('#pet-play-preview .pet-character')",'cat draft '+name)
        check("!document.querySelector('#pet-play-preview .pet-accessory')",'no geometric accessories on cat '+name)
        check("(!!document.querySelector('#pet-play-preview .reward-hat') && !!document.querySelector('#pet-play-preview .reward-bow'))==="+str(name=='both').lower(),'cat simultaneous equipment '+name)
        click('pet-dress-save')
        wait("!PetPlay.isSaving && !!document.querySelector('#pet-sprite .pet-character')",'cat save '+name)
        screenshot('cat-integrated-'+name+'-v14')
    cdp.evaluate("void PetPlay.refresh(true)")
    wait("document.querySelector('#pet-sprite .reward-bow') && document.querySelector('#pet-sprite .reward-mat') && !document.getElementById('pet-play-retry')",'cat saved slots reload')
    click('pet-tab-touch')
    for action in ['pet','treat','rest']:
        click('pet-action-'+action)
        wait("document.getElementById('pet-play-preview').dataset.interaction==="+json.dumps(action),'cat action '+action)
        check("getComputedStyle(document.getElementById('pet-play-preview')).animationName==='none' && getComputedStyle(document.getElementById('pet-sprite')).transform==='none'",'cat background fixed '+action)
        check("getComputedStyle(document.querySelector('#pet-play-preview .pet-character')).animationName!=='none'",'only transparent cat moves '+action)
        check("getComputedStyle(document.querySelector('#pet-play-preview .reward-mat')).transform==='none'",'cushion fixed '+action)
        check("document.querySelector('#pet-play-preview .reward-hat img').src.includes('/cat/lv-1-hat.png') && document.querySelector('#pet-play-preview .reward-bow img').src.includes('/cat/lv-2-bow.png')",'equipment stays during '+action)
        check("pet.totalExperience===10450",'cat interactions preserve XP '+action)
        screenshot('cat-integrated-'+action+'-v14')
        wait("document.getElementById('pet-play-preview').dataset.interaction===''",'cat action ends '+action)
    cdp.evaluate("petFixture.setLevel(1);PetPlay.onProfile({...pet});void PetPlay.refresh(true)")
    wait("document.querySelector('#pet-sprite .reward-bow') && document.querySelector('#pet-sprite .reward-mat') && !document.getElementById('pet-play-retry')",'cat earned bow and mat retained')
    check("document.querySelector('#pet-sprite .reward-hat img').src.includes('/cat/lv-1-hat.png')",'cat eligible hat retained after level drop')
    cdp.evaluate("petFixture.setLevel(20);PetPlay.onProfile({...pet});void PetPlay.refresh(true)")
    wait("!document.getElementById('pet-play-retry')",'cat level restored')
    # All 90 deliverables must load with real alpha, not just a painted checkerboard.
    for species in ['dog','cat','rabbit','fox','panda','dragon']:
        for level in range(1,6):
            for kind in ['hat','bow','mat']:
                asset=f'assets/pet/rewards-v2/{species}/lv-{level}-{kind}.png'
                check("new Promise(resolve=>{const i=new Image();i.onload=()=>{const c=document.createElement('canvas');c.width=i.width;c.height=i.height;const x=c.getContext('2d');x.drawImage(i,0,0);const d=x.getImageData(0,0,c.width,c.height).data;let a=0,v=0;for(let n=3;n<d.length;n+=4){if(d[n]===0)a++;if(d[n]>=128)v++;}resolve(a>i.width*i.height*.2&&v>i.width*i.height*.015)};i.onerror=()=>resolve(false);i.src="+json.dumps(asset)+"})",'generated alpha '+species+str(level)+kind)
    cdp.evaluate("choices.set(6,'hat');appearance.hatLevel=6;PetPlay.onMood('idle');PetPlay.refresh(true)")
    wait("!document.getElementById('pet-play-retry') && document.querySelector('#pet-sprite .accessory-hat')",'legacy Lv6 item retained')
    click('pet-tab-gifts')
    check("document.querySelectorAll('#pet-gift-level option').length===5",'legacy item cannot reopen new claim above Lv5')
    click('pet-tab-dress')
    check("document.querySelector('#pet-dress-hatLevel option[value=\"6\"]').textContent.includes('旧ごほうび')",'legacy item remains equippable and labelled')
    click('pet-tab-album')
    check("document.getElementById('pet-panel-album').textContent.includes('旧ごほうび 1 点')",'legacy record archived separately from five new rewards')
    for width,height in [(375,812),(320,740),(1440,1000)]:
        cdp.call('Emulation.setDeviceMetricsOverride',{'width':width,'height':height,'deviceScaleFactor':1,'mobile':width<500})
        for theme in ['classic','retro','dark']:
            cdp.evaluate("applyPreferences({theme:"+json.dumps(theme)+",layout:'board'})")
            for section in ['touch','gifts','dress','album']:
                click('pet-tab-'+section);layout_check()
            if width==375 and theme=='classic': screenshot('pet-album-mobile-v14')
    cdp.call('Emulation.setEmulatedMedia',{'features':[{'name':'prefers-reduced-motion','value':'reduce'}]})
    cdp.evaluate("document.getElementById('pet-play-preview').dataset.interaction='pet'")
    check("getComputedStyle(document.getElementById('pet-play-preview')).animationName==='none'",'reduced motion respected')
    check("getComputedStyle(document.querySelector('#pet-play-preview .pet-character')).animationName==='none'",'cat child respects reduced motion')
    click('settings-close-button');check("document.activeElement.id==='pet-interact-button'",'focus returns to portrait')
    cdp.evaluate("refreshProgression()")
    cdp.evaluate("PetPlay.refresh()")
    screenshot('pet-clean-board-desktop-v14')
    print(json.dumps({'passed':len(results),'artifacts':str(ROOT),'checks':results},ensure_ascii=False),flush=True)
finally:
    for process in [browser,server]:
        if process:
            process.terminate()
            try: process.wait(timeout=5)
            except subprocess.TimeoutExpired: process.kill()
