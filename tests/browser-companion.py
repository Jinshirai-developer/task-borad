"""Exercise v16 in an isolated Chrome profile; block all real API calls."""
import base64
import functools
import http.server
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('outfit_browser', ROOT / 'tests/browser-pet-outfits.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def main():
    output = Path(tempfile.mkdtemp(prefix='task-companion-browser-', dir='/private/tmp'))
    checks = []
    class QuietHandler(http.server.SimpleHTTPRequestHandler):
        def log_message(self, *args): pass
    server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(QuietHandler, directory=str(ROOT / 'frontend')))
    threading.Thread(target=server.serve_forever, daemon=True).start()
    browser = None
    try:
        browser = subprocess.Popen(['/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '--headless=new', '--disable-gpu', '--disable-background-networking', '--disable-sync', '--no-first-run', '--remote-debugging-port=0', '--user-data-dir=' + str(output / 'profile'), 'about:blank'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        pages = None
        for _ in range(100):
            try:
                port = int((output / 'profile/DevToolsActivePort').read_text().splitlines()[0])
                pages = json.load(urllib.request.urlopen(f'http://127.0.0.1:{port}/json/list', timeout=1)); break
            except Exception: time.sleep(.1)
        assert pages, 'Chrome failed to start'
        cdp = module.CDP(next(p['webSocketDebuggerUrl'] for p in pages if p['type'] == 'page'))
        def check(expression, label):
            assert cdp.evaluate(expression), (label, cdp.evaluate('document.getElementById("companion-message")?.textContent'))
            checks.append(label)
        def wait(expression, label):
            for _ in range(200):
                if cdp.evaluate('Boolean(' + expression + ')'): return
                time.sleep(.05)
            raise AssertionError((label, cdp.evaluate('({errors:uiErrors,message:document.getElementById("companion-message")?.textContent,body:document.getElementById("companion-content")?.innerText})')))
        def click(id): cdp.evaluate('document.getElementById(' + json.dumps(id) + ').click()')
        def fill(name, value): cdp.evaluate('(()=>{const e=document.getElementById(' + json.dumps('work-' + name) + ');e.value=' + json.dumps(value) + ';e.dispatchEvent(new Event("input",{bubbles:true}));})()')
        def submit():
            cdp.evaluate('document.getElementById("companion-form").requestSubmit()')
            wait('!TaskCompanion.isSaving && !document.getElementById("companion-message").textContent.includes("保存しています")', 'save finished')
        def screenshot(name):
            data = cdp.call('Page.captureScreenshot', {'format': 'png', 'captureBeyondViewport': False})
            (output / (name + '.png')).write_bytes(base64.b64decode(data['data']))
        def layout():
            check('document.documentElement.scrollWidth<=innerWidth', 'page contained')
            check('(()=>{const d=document.getElementById("companion-dialog");return !d.open||(d.scrollWidth<=d.clientWidth+1&&d.getBoundingClientRect().right<=innerWidth&&d.getBoundingClientRect().left>=0)})()', 'companion contained')
            check('uiErrors.length===0', 'no browser errors')
        cdp.call('Page.enable')
        cdp.call('Page.addScriptToEvaluateOnNewDocument', {'source': '''window.uiErrors=[];window.realApiCalls=[];window.confirm=()=>true;
addEventListener('error',e=>uiErrors.push(e.message));addEventListener('unhandledrejection',e=>uiErrors.push(String(e.reason)));
const originalFetch=window.fetch;window.fetch=(input,options)=>{const u=new URL(String(input),location.href);if(u.pathname.startsWith('/api/')){realApiCalls.push(u.pathname);throw new Error('Real API blocked');}return originalFetch(input,options);};'''})
        cdp.call('Emulation.setDeviceMetricsOverride', {'width':1440,'height':1000,'deviceScaleFactor':1,'mobile':False})
        origin = 'http://localhost:5097' if '--served' in sys.argv else f'http://127.0.0.1:{server.server_port}'
        cdp.call('Page.navigate', {'url':origin + '/index.html?demo=1'})
        wait('typeof TaskCompanion!=="undefined"&&state.visibleTasks.length===3&&state.petProfile&&!optionsState.progressionLoading', 'initialized')
        cdp.evaluate('window.xpBefore=state.petProfile.totalExperience;startEditTask(state.visibleTasks.find(t=>t.id===2))')
        click('edit-companion-open'); wait('document.getElementById("work-learned")&&!document.getElementById("companion-reload").disabled', 'memo is default')
        click('work-tab-resume'); wait('document.getElementById("work-summary")', 'savepoint loaded')
        check('document.getElementById("companion-content").textContent.includes("エラー表示")', 'saved next step visible')
        check('document.querySelectorAll("#companion-tabs button").length===1', 'personal has one primary memo tab')
        check('!TaskDetails.canPoll()', 'poll pauses for native dialog')
        fill('nextStep','入力エラーの表示を確認する'); fill('summary','入力フォームまで完成'); submit()
        check('document.getElementById("companion-message").textContent.includes("覚えておく")', 'savepoint saved')
        check('state.petProfile.totalExperience===xpBefore', 'bookmark grants no XP')
        screenshot('savepoint-desktop')
        # Native dialog closes alone; the parent editor and its draft survive.
        cdp.evaluate('document.getElementById("edit-title").value="親フォームの入力中"')
        cdp.call('Input.dispatchKeyEvent', {'type':'keyDown','key':'Escape','code':'Escape','windowsVirtualKeyCode':27})
        wait('!document.getElementById("companion-dialog").open','escape closes only companion')
        check('!editModal.classList.contains("hidden")&&document.getElementById("edit-title").value==="親フォームの入力中"','parent draft kept')
        click('edit-companion-open'); wait('document.getElementById("work-nextStep")&&!document.getElementById("companion-reload").disabled','reopen')
        click('work-tab-notes'); fill('tried','試した <img src=x onerror=alert(1)>'); fill('learned','この記録は文字として表示'); fill('nextStep','再利用する'); submit()
        check('!document.getElementById("companion-content").querySelector("img")','user text is not HTML')
        check('document.getElementById("companion-content").textContent.includes("文字として表示")','note saved')
        fill('tried','保存前の入力'); fill('learned','失いたくない'); click('work-tab-resume'); click('work-tab-notes')
        check('document.getElementById("work-tried").value==="保存前の入力"','draft survives section switch')
        # Simulate a separate edit without changing real data.
        cdp.evaluate('(async()=>{const t=await(await TaskAuth.request("/api/tasks/2")).json();await TaskAuth.request("/api/tasks/2",{method:"PUT",body:JSON.stringify({...t,title:"別の編集"})});window.remoteChanged=true;})()')
        wait('window.remoteChanged','concurrent edit'); submit()
        check('document.getElementById("companion-message").classList.contains("error")&&document.getElementById("work-tried").value==="保存前の入力"','conflict preserves draft')
        click('companion-reload'); wait('!document.getElementById("companion-reload").disabled','reload latest')
        check('document.getElementById("work-learned").value==="失いたくない"','reload preserves draft')
        submit(); check('!document.getElementById("companion-message").classList.contains("error")','explicit retry succeeds')
        click('companion-close'); click('edit-cancel-button')
        cdp.evaluate('switchWorkspace(1)'); wait('state.teamId===1&&state.visibleTasks.length===3','team')
        cdp.evaluate('startEditTask(state.visibleTasks.find(t=>t.id===5))'); click('edit-companion-open')
        wait('document.getElementById("work-tab-help")&&!document.getElementById("companion-reload").disabled','team tools')
        check('document.querySelectorAll("#companion-tabs button").length===3','three primary team tools')
        click('work-tab-help'); click('work-help-offer'); wait('!TaskCompanion.isSaving&&document.getElementById("work-help-withdraw")','volunteer')
        check('document.getElementById("companion-content").textContent.includes("お試しユーザーが確認中")','volunteer visible')
        click('work-help-resolve'); wait('!TaskCompanion.isSaving&&document.getElementById("companion-content").textContent.includes("解決済み")','resolve')
        check('document.getElementById("companion-content").textContent.includes("前に進めた")','thanks visible')
        click('companion-close'); click('edit-cancel-button')
        cdp.evaluate('startEditTask(state.visibleTasks.find(t=>t.id===4))'); click('edit-companion-open')
        wait('document.getElementById("work-tab-handoff")&&!document.getElementById("companion-reload").disabled','handoff tools')
        click('work-tab-handoff'); fill('message','サイズを確認したい'); submit()
        check('document.getElementById("companion-content").textContent.includes("確認事項あり")','handoff clarification')
        click('work-handoff-accept'); wait('!TaskCompanion.isSaving&&document.getElementById("companion-content").textContent.includes("受け取り済み")','accept')
        fill('recipientId','2'); fill('message','通常と笑顔をお願いします'); fill('criteria','2つのPNGがそろうこと'); submit()
        check('document.getElementById("companion-content").textContent.includes("受け取り待ち")','outgoing handoff saved')
        screenshot('handoff-desktop')
        click('companion-close'); click('edit-cancel-button')
        cdp.evaluate('startEditTask(state.visibleTasks.find(t=>t.id===6))'); click('edit-companion-open')
        wait('document.getElementById("work-tab-showcase")&&!document.getElementById("companion-reload").disabled','showcase tools')
        click('work-tab-showcase'); fill('kind','monitor'); fill('title','チームの完成画面'); fill('summary','仕様をみんなで決めました'); fill('resourceUrl','https://example.test/art'); submit()
        check('document.getElementById("companion-content").textContent.includes("チームの完成画面")','showcase saved')
        check('document.querySelector("#companion-content a").rel.includes("noreferrer")','safe external link')
        click('companion-back'); wait('document.getElementById("work-artifact-6")','hub shelf')
        check('document.querySelector(".work-object.monitor")!==null','monitor decoration')
        check('document.getElementById("companion-content").textContent.includes("保存済みの作品")','scope visible')
        screenshot('team-hub-desktop'); click('companion-close'); click('edit-cancel-button')
        # Create-time search does not expose another workspace's notes.
        click('open-create-task-button'); cdp.evaluate('document.getElementById("tags").value="アーティスト"')
        click('create-find-work-notes'); wait('document.getElementById("companion-search-results")?.textContent.includes("白い体毛")','reuse notes on create')
        check('!document.getElementById("companion-content").textContent.includes("失いたくない")','search keeps personal notes private')
        click('companion-close'); click('create-cancel-button')
        for width in [320,375,1440]:
            cdp.call('Emulation.setDeviceMetricsOverride', {'width':width,'height':950,'deviceScaleFactor':1,'mobile':width<600})
            for theme in ['classic','retro','dark']:
                cdp.evaluate('applyPreferences({theme:'+json.dumps(theme)+',layout:"board"});TaskCompanion.openTask(5)')
                wait('document.getElementById("work-nextStep")&&!document.getElementById("companion-reload").disabled','responsive dialog')
                check('!document.getElementById("work-tab-showcase")', 'new showcase creation not promoted')
                for section in ['resume','help','handoff','notes']:
                    click('work-tab-'+section); layout()
                click('companion-back'); wait('document.getElementById("work-artifact-6")','responsive hub'); layout(); screenshot(f'hub-{width}-{theme}')
                click('companion-close')
        check('realApiCalls.length===0','no real API calls in demo')
        check('state.petProfile.totalExperience===xpBefore','all companion actions preserve XP')
        report = {'passed':len(checks),'checks':checks,'artifacts':str(output)}
        (output/'result.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
        print(json.dumps(report,ensure_ascii=False),flush=True)
    finally:
        if browser:
            browser.terminate()
            try: browser.wait(timeout=5)
            except subprocess.TimeoutExpired: browser.kill(); browser.wait()
        server.shutdown(); server.server_close()


if __name__ == '__main__': main()
