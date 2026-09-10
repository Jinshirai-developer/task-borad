"""Isolated local demo/UI checks. Every real API fetch is blocked by the fixture."""
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
    output = Path(tempfile.mkdtemp(prefix='task-usability-browser-', dir='/private/tmp'))
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
        cdp = module.CDP(next(page['webSocketDebuggerUrl'] for page in pages if page['type'] == 'page'))
        def check(expression, label):
            assert cdp.evaluate(expression), label
            checks.append(label)
        def wait(expression, label):
            for _ in range(200):
                if cdp.evaluate('Boolean(' + expression + ')'): return
                time.sleep(.05)
            raise AssertionError((label, cdp.evaluate('({errors:uiErrors, message:document.getElementById("message")?.textContent, edit:document.getElementById("edit-message")?.textContent})')))
        def click(id): cdp.evaluate('document.getElementById(' + json.dumps(id) + ').click()')
        def screenshot(name):
            data = cdp.call('Page.captureScreenshot', {'format': 'png', 'captureBeyondViewport': False})
            (output / (name + '.png')).write_bytes(base64.b64decode(data['data']))
        def layout():
            check('document.documentElement.scrollWidth <= innerWidth', 'page contained')
            check('[...document.querySelectorAll("[role=dialog]")].filter(e=>e.getClientRects().length).every(e=>e.scrollWidth<=e.clientWidth)', 'dialog contained')
            check('uiErrors.length === 0', 'no browser errors')
        cdp.call('Page.enable')
        cdp.call('Page.addScriptToEvaluateOnNewDocument', {'source': '''window.uiErrors=[];window.realApiCalls=[];window.confirm=()=>true;
addEventListener('error', e=>uiErrors.push(e.message));addEventListener('unhandledrejection',e=>uiErrors.push(String(e.reason)));
const originalFetch=window.fetch;window.fetch=(input,options)=>{const url=new URL(String(input),location.href);if(url.pathname.startsWith('/api/')){realApiCalls.push(url.pathname);throw new Error('Real API blocked');}return originalFetch(input,options);};'''})
        cdp.call('Emulation.setDeviceMetricsOverride', {'width': 1440, 'height': 1000, 'deviceScaleFactor': 1, 'mobile': False})
        origin = 'http://localhost:5097' if '--served' in sys.argv else f'http://127.0.0.1:{server.server_port}'
        cdp.call('Page.navigate', {'url': origin + '/index.html?demo=1'})
        wait('typeof TaskDetails !== "undefined" && state.visibleTasks.length===3 && state.petProfile && !optionsState.progressionLoading', 'demo initialized')
        check('realApiCalls.length===0 && document.body.dataset.demo==="true"', 'no real API calls')
        layout(); screenshot('demo-desktop')
        click('open-create-task-button')
        check('document.getElementById("create-assignee-field").hidden', 'personal assignment hidden')
        click('create-checklist-add')
        cdp.evaluate('document.getElementById("title").value="ブラウザー確認";document.querySelector("#create-checklist input[type=text]").value="項目1";document.querySelector("#create-checklist input[type=checkbox]").checked=true;document.getElementById("task-form").requestSubmit()')
        wait('createTaskModal.classList.contains("hidden") && state.visibleTasks.some(t=>t.title==="ブラウザー確認")', 'created task')
        check('state.visibleTasks.find(t=>t.title==="ブラウザー確認").checklist[0].isCompleted', 'checklist persisted')
        cdp.evaluate('window.beforeXp=state.petProfile.totalExperience;window.testTask=state.visibleTasks.find(t=>t.title==="ブラウザー確認");moveTaskStatus(testTask,2)')
        wait('!state.isTaskMutation && !document.getElementById("task-undo-toast").hidden', 'move offers undo')
        check('state.petProfile.totalExperience===beforeXp+25', 'done adds 25')
        click('task-undo-button')
        wait('!state.isTaskMutation && document.getElementById("task-undo-toast").hidden', 'undo finished')
        check('state.petProfile.totalExperience===beforeXp && state.visibleTasks.find(t=>t.id===testTask.id).status==="Todo"', 'undo restores status and XP')
        cdp.evaluate('deleteTask(state.visibleTasks.find(t=>t.id===testTask.id))')
        wait('!state.isTaskMutation && !state.visibleTasks.some(t=>t.id===testTask.id)', 'deleted')
        click('task-undo-button'); wait('!state.isTaskMutation && state.visibleTasks.some(t=>t.id===testTask.id)', 'delete restored')
        check('state.petProfile.totalExperience===beforeXp', 'restore no duplicate XP')
        cdp.evaluate('switchWorkspace(1)'); wait('state.visibleTasks.length===3 && state.teamId===1', 'team loaded')
        cdp.evaluate('startEditTask(state.visibleTasks.find(t=>t.id===4))')
        wait('!document.getElementById("edit-assignee").disabled', 'members loaded')
        check('document.getElementById("edit-assignee").options.length===3', 'team assignee options')
        check('!TaskDetails.canPoll()', 'poll pauses for edit')
        cdp.evaluate('document.getElementById("edit-assignee").value="2";document.getElementById("edit-title").value="担当者テスト";document.getElementById("edit-task-form").requestSubmit()')
        wait('editModal.classList.contains("hidden") && !state.isEditSaving', 'assignment saved')
        check('state.visibleTasks.find(t=>t.id===4).assigneeUserProfileId===2 && state.visibleTasks.find(t=>t.id===4).assigneeDisplayName.includes("デザイナー")', 'assigned member shown')
        cdp.evaluate('document.activeElement.blur();startEditTask(state.visibleTasks.find(t=>t.id===4));document.getElementById("edit-title").value="入力中の下書き"')
        cdp.evaluate('(async()=>{const t=await (await TaskAuth.request("/api/teams/1/tasks/4")).json();await TaskAuth.request("/api/teams/1/tasks/4",{method:"PUT",body:JSON.stringify({...t,title:"別の人の変更"})});await TaskDetails.poll();})()')
        check('document.getElementById("edit-title").value==="入力中の下書き"', 'poll preserves draft')
        cdp.evaluate('document.getElementById("edit-task-form").requestSubmit()')
        wait('!state.isEditSaving && document.getElementById("edit-message").textContent.includes("更新")', 'stale draft conflict')
        check('!editModal.classList.contains("hidden") && document.getElementById("edit-title").value==="入力中の下書き"', 'conflict keeps draft open')
        click('edit-cancel-button')
        cdp.evaluate('document.activeElement.blur();TaskDetails.poll()')
        wait('state.visibleTasks.find(t=>t.id===4).title==="別の人の変更"', 'background update applied')
        click('open-settings-button'); wait('!optionsState.progressionLoading', 'settings ready')
        click('settings-tasks-tab'); cdp.evaluate('document.getElementById("tag-manage-details").open=true')
        wait('document.getElementById("tag-manage-source").options.length===4', 'tag manager ready')
        cdp.evaluate('document.getElementById("tag-manage-source").value="プログラマー";document.getElementById("tag-rename-input").value="開発";document.getElementById("tag-manage-form").requestSubmit()')
        wait('!TaskTags.isSaving && state.visibleTasks.some(t=>t.tags?.includes("開発"))', 'rename applies')
        check('[...document.getElementById("sidebar-tag-filter").options].some(option=>option.value==="tag:開発")', 'sidebar updated')
        screenshot('tag-management-desktop')
        cdp.evaluate('document.getElementById("tag-manage-source").value="開発";document.getElementById("tag-manage-action").value="merge";document.getElementById("tag-manage-action").dispatchEvent(new Event("change"));document.getElementById("tag-merge-target").value="プランナー";document.getElementById("tag-manage-form").requestSubmit()')
        wait('!TaskTags.isSaving && !state.visibleTasks.some(t=>t.tags?.includes("開発"))', 'merge applies')
        check('state.visibleTasks.find(t=>t.id===4).tags==="プランナー"', 'merge deduplicates')
        cdp.evaluate('document.getElementById("tag-manage-source").value="プランナー";document.getElementById("tag-manage-action").value="delete";document.getElementById("tag-manage-action").dispatchEvent(new Event("change"));document.getElementById("tag-manage-form").requestSubmit()')
        wait('!TaskTags.isSaving && !state.visibleTasks.some(t=>t.tags?.includes("プランナー"))', 'tag deletion applies')
        check('state.visibleTasks.length===3', 'tag deletion keeps tasks')
        click('settings-pet-tab'); cdp.evaluate('document.getElementById("weekly-review-details").open=true')
        wait('document.querySelectorAll(".weekly-days li").length===7', 'weekly loaded')
        check('document.getElementById("weekly-review-message").textContent.includes("こむぎ")', 'pet tells weekly review')
        cdp.evaluate('document.getElementById("weekly-review-details").scrollIntoView({block:"center"})'); screenshot('weekly-desktop')
        click('settings-close-button')
        for width in [320, 375, 1440]:
            cdp.call('Emulation.setDeviceMetricsOverride', {'width': width, 'height': 950, 'deviceScaleFactor': 1, 'mobile': width < 600})
            for theme in ['classic', 'retro', 'dark']:
                cdp.evaluate('applyPreferences({theme:' + json.dumps(theme) + ',layout:"board"})')
                layout()
                cdp.evaluate('startEditTask(state.visibleTasks.find(t=>t.id===5))'); wait('!document.getElementById("edit-assignee").disabled', 'mobile members')
                layout(); screenshot(f'details-{width}-{theme}')
                click('edit-cancel-button')
        check('realApiCalls.length===0', 'all workflows remain offline')
        print(json.dumps({'passed': len(checks), 'checks': checks, 'artifacts': str(output)}, ensure_ascii=False), flush=True)
    finally:
        if browser:
            browser.terminate()
            try: browser.wait(timeout=5)
            except subprocess.TimeoutExpired: browser.kill(); browser.wait()
        server.shutdown(); server.server_close()


if __name__ == '__main__': main()
