"""Actual frontend -> HTTP -> PostgreSQL regression, not demo/mocked TaskAuth.

Run only after tests/transport-fixture.py start. Docker label/port guards prohibit
the user's preview. Random test credentials/cookies are never printed or stored.
Fetch is observed without rewriting its arguments or faking any response.
"""
import base64
import http.cookiejar
import importlib.util
import json
from pathlib import Path
import re
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]
ORIGIN, MAIL = 'http://localhost:5100', 'http://localhost:8100'
spec = importlib.util.spec_from_file_location('browser_cdp', ROOT / 'tests/browser-pet-outfits.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class Actor:
    def __init__(self, role):
        self.key = 'httpqa_' + role + '_' + uuid.uuid4().hex[:8]
        self.email, self.password = self.key + '@taskboard.test', 'Http-qa-' + uuid.uuid4().hex
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        self.csrf = None

    def request(self, path, method='GET', body=None, expected=200, attempt=0):
        headers = {'Accept': 'application/json'}
        if method != 'GET':
            if not self.csrf: self.csrf = self.request('/api/auth/csrf')['token']
            headers['X-CSRF-TOKEN'] = self.csrf
        if body is not None: headers['Content-Type'] = 'application/json'
        request = urllib.request.Request(ORIGIN + path, method=method, headers=headers,
            data=json.dumps(body).encode() if body is not None else None)
        try: response = self.opener.open(request, timeout=10)
        except urllib.error.HTTPError as error: response = error
        # Only middleware-rejected 429 is retryable. Never retry a timeout/5xx
        # where a mutation might already have committed.
        if response.status == 429 and attempt < 2:
            delay = min(60, max(1, int(response.headers.get('Retry-After') or 60)))
            print('QA request rejected by rate limiter; waiting for the next test window.', flush=True)
            response.close()
            for seconds in range(delay): time.sleep(1)
            return self.request(path, method, body, expected, attempt + 1)
        assert response.status == expected, (path, response.status, expected)
        data = response.read()
        if path.startswith('/api/auth/') and method != 'GET': self.csrf = None
        return json.loads(data) if data else None

    def register(self, config):
        self.user = self.request('/api/auth/register', 'POST', dict(userKey=self.key, displayName=self.key,
            email=self.email, password=self.password, acceptTerms=True, termsVersion=config['termsVersion'], privacyVersion=config['privacyVersion']))['user']
        secret = None
        for _ in range(80):
            messages = json.load(urllib.request.urlopen(MAIL + '/api/v1/search?query=' + urllib.parse.quote('to:' + self.email)))
            for entry in messages.get('messages') or []:
                message = json.load(urllib.request.urlopen(MAIL + '/api/v1/message/' + entry['ID']))
                for candidate in re.findall(r'https?://[^\s<>"\x27]+', message['Text']):
                    link = urllib.parse.urlparse(candidate)
                    assert link.scheme + '://' + link.netloc == ORIGIN, 'Mail must stay in the isolated origin'
                    if urllib.parse.parse_qs(link.query).get('mode') == ['confirm']:
                        values = urllib.parse.parse_qs(link.fragment)
                        secret = dict(userId=int(values['userId'][0]), token=values['token'][0])
            if secret: break
            time.sleep(.25)
        assert secret, 'Test email not delivered'
        self.request('/api/auth/confirm-email', 'POST', secret)
        self.request('/api/auth/login', 'POST', dict(userKey=self.key, password=self.password))


def main():
    # Refuse to run on a similarly numbered user server or a demo page.
    config = json.loads(subprocess.check_output(['docker', 'inspect', 'task-v21-qa-web']))[0]
    assert config['Config']['Labels'].get('task-transport-v21') == 'disposable-test'
    assert 'Billing__Enabled=false' in config['Config']['Env']
    assert list(config['NetworkSettings']['Networks']) == ['task-v21-qa-net']
    output = Path(tempfile.mkdtemp(prefix='task-companion-http-', dir='/private/tmp'))
    owner, helper = Actor('owner'), Actor('helper')
    auth = owner.request('/api/auth/config')
    for actor in [owner, helper]: actor.register(auth)
    team = owner.request('/api/teams', 'POST', {'name': 'HTTP regression team'}, 201)
    team_id = team['team']['id']
    helper.request('/api/teams/join', 'POST', {'inviteCode': team['inviteCode']})
    personal = owner.request('/api/tasks', 'POST', {'title': 'Private HTTP regression', 'status': 'Doing'}, 201)
    task = owner.request(f'/api/teams/{team_id}/tasks', 'POST', {'title': 'Team HTTP regression', 'status': 'Doing', 'tags': 'transport'}, 201)
    private_path = f'/api/companion/tasks/{personal["id"]}'
    team_path = f'/api/teams/{team_id}/companion/tasks/{task["id"]}'
    checks = []
    browser = None
    try:
        browser = subprocess.Popen(['/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '--headless=new', '--disable-gpu',
            '--disable-background-networking', '--disable-sync', '--no-first-run', '--remote-debugging-port=0',
            '--user-data-dir=' + str(output / 'profile'), 'about:blank'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        pages = None
        for _ in range(100):
            try:
                port = int((output / 'profile/DevToolsActivePort').read_text().splitlines()[0])
                pages = json.load(urllib.request.urlopen(f'http://127.0.0.1:{port}/json/list', timeout=1)); break
            except OSError: time.sleep(.1)
        assert pages, 'Chrome failed to start'
        cdp = module.CDP(next(p['webSocketDebuggerUrl'] for p in pages if p['type'] == 'page'))
        cdp.call('Page.enable')
        cdp.call('Page.addScriptToEvaluateOnNewDocument', {'source': '''window.uiErrors=[];window.httpObserved=[];window.confirm=()=>true;
addEventListener('error',e=>uiErrors.push(e.message));addEventListener('unhandledrejection',e=>uiErrors.push(String(e.reason)));
const originalFetch=window.fetch;window.fetch=async(input,options)=>{
 const url=new URL(input instanceof Request?input.url:String(input),location.href);
 if(url.origin!==location.origin)throw new Error('External fetch prohibited in QA');
 const request=new Request(url,input instanceof Request?input:options);
 const row={path:url.pathname,method:request.method,type:request.headers.get('content-type'),control:!!window.transportControl};
 if(url.pathname.includes('/companion/tasks/')&&options?.body)row.action=JSON.parse(options.body).action;
 const response=await originalFetch(input,options);row.status=response.status;httpObserved.push(row);return response;
};'''})
        def check(value, label):
            assert value, label
            checks.append(label)
        def wait(expression, label):
            for _ in range(200):
                if cdp.evaluate('Boolean(' + expression + ')'): return
                time.sleep(.05)
            raise AssertionError((label, cdp.evaluate('({message:document.getElementById("companion-message")?.textContent,errors:uiErrors,requests:httpObserved.slice(-5)})')))
        def click(id): cdp.evaluate('document.getElementById(' + json.dumps(id) + ').click()')
        def fill(id, value):
            cdp.evaluate('(()=>{const e=document.getElementById(' + json.dumps(id) + ');e.value=' + json.dumps(str(value)) + ';e.dispatchEvent(new Event("input",{bubbles:true}));})()')
        def opened(id, tab):
            cdp.evaluate('TaskCompanion.openTask(' + str(id) + ',' + json.dumps(tab) + ')')
            wait('document.getElementById("companion-dialog").open&&!document.getElementById("companion-reload").disabled&&!document.getElementById("companion-message").classList.contains("error")', 'record loaded')
        def action(name, fields=None, button=None, expected=200):
            before = cdp.evaluate('httpObserved.length')
            for key, value in (fields or {}).items(): fill('work-' + key, value)
            if button: click(button)
            else: cdp.evaluate('document.getElementById("companion-form").requestSubmit()')
            wait('!TaskCompanion.isSaving&&httpObserved.slice(' + str(before) + ').some(r=>r.action===' + json.dumps(name) + ')', name + ' response')
            result = cdp.evaluate('httpObserved.slice(' + str(before) + ').find(r=>r.action===' + json.dumps(name) + ')')
            check(result['status'] == expected, name + ' HTTP ' + str(expected))
            check(result['type'].split(';')[0] == 'application/json', name + ' sends JSON Content-Type')
            check(cdp.evaluate('document.getElementById("companion-message").classList.contains("error")') == (expected != 200), name + ' feedback')
        def remote(action_name, fields=None):
            record = helper.request(team_path)
            return helper.request(team_path, 'POST', dict(action=action_name, version=record['version'], expectedUpdatedAt=record['updatedAt'], **(fields or {})))
        def saved(path=team_path): return owner.request(path)
        cdp.call('Emulation.setDeviceMetricsOverride', {'width':1440, 'height':1000, 'deviceScaleFactor':1, 'mobile':False})
        cdp.call('Page.navigate', {'url': ORIGIN + '/login.html'})
        wait('typeof config!=="undefined"&&config&&!isSaving&&httpObserved.some(r=>r.path==="/api/auth/session")', 'login ready')
        fill('auth-user-key', owner.key); fill('auth-password', owner.password)
        cdp.evaluate('document.getElementById("auth-form").requestSubmit()')
        wait('typeof TaskCompanion!=="undefined"&&state.petProfile&&!optionsState.progressionLoading&&state.visibleTasks.length===1', 'real login initialized')
        check(cdp.evaluate('!new URL(location.href).searchParams.has("demo")'), 'not demo mode')
        opened(personal['id'], 'resume')
        action('savepoint_save', {'nextStep':'Private bookmark', 'summary':'Saved from browser'})
        check(saved(private_path)['savepoint']['nextStep'] == 'Private bookmark', 'private bookmark persisted')
        action('savepoint_clear', button='work-clear-savepoint')
        check(saved(private_path)['savepoint'] is None, 'bookmark removal persisted')
        click('work-tab-notes')
        action('note_add', {'tried':'Private trial', 'learned':'Private lesson'})
        note = saved(private_path)['notes'][0]
        click('work-note-edit-' + note['id'])
        action('note_edit', {'tried':'Edited trial', 'learned':'Edited private lesson'})
        check(saved(private_path)['notes'][0]['learned'] == 'Edited private lesson', 'note edit persisted')
        action('note_delete', button='work-note-delete-' + note['id'])
        check(saved(private_path)['notes'] == [], 'note deletion persisted')
        click('companion-close')
        cdp.evaluate('switchWorkspace(' + str(team_id) + ')')
        wait('state.teamId===' + str(team_id) + '&&state.visibleTasks.some(t=>t.id===' + str(task['id']) + ')', 'team loaded')
        opened(task['id'], 'help')
        # Negative control demonstrates that the server still rejects old browser text/plain.
        before = saved()
        bad_body = dict(action='help_open', kind='review', message='Must not save', version=before['version'], expectedUpdatedAt=before['updatedAt'])
        status = cdp.evaluate('(async()=>{window.transportControl=true;try{return(await TaskAuth.request(' + json.dumps(team_path) + ',{method:"POST",body:JSON.stringify(' + json.dumps(bad_body) + ')})).status;}finally{window.transportControl=false;}})()')
        check(status == 415 and saved()['version'] == before['version'], 'old missing-header request rejected without mutation')
        action('help_open', {'message':'Please review from the real browser', 'kind':'review'})
        check(helper.request(team_path)['help']['message'] == 'Please review from the real browser', 'teammate can read help request')
        action('help_cancel', button='work-help-cancel')
        check(saved()['help']['status'] == 'cancelled', 'help cancellation persisted')
        remote('help_open', {'kind':'decision', 'message':'Help from teammate'})
        opened(task['id'], 'help')
        action('help_offer', button='work-help-offer')
        check(helper.request(team_path)['help']['helperId'] == owner.user['id'], 'teammate sees volunteer')
        action('help_withdraw', button='work-help-withdraw')
        check(saved()['help']['helperId'] is None, 'volunteer withdrawal persisted')
        action('help_offer', button='work-help-offer')
        action('help_resolve', button='work-help-resolve')
        check(helper.request(team_path)['thanks'][-1]['helperId'] == owner.user['id'], 'thanks persisted and shared')
        screenshot = cdp.call('Page.captureScreenshot', {'format':'png', 'captureBeyondViewport':False})
        (output / 'help-real-http.png').write_bytes(base64.b64decode(screenshot['data']))
        # Preserve production rate limits; split this broad audit into two windows.
        print('Real HTTP: bookmarks, notes, help/volunteer/thanks passed. Waiting for the next rate-limit window.', flush=True)
        for _ in range(3): time.sleep(20)
        click('work-tab-handoff')
        action('handoff_send', {'recipientId':helper.user['id'], 'message':'Please prepare art', 'criteria':'One PNG', 'assignOnAccept':'true'})
        check(saved()['handoff']['assignOnAccept'] is True, 'browser sends typed assignment consent')
        check(helper.request(team_path)['handoff']['toUserId'] == helper.user['id'], 'recipient sees handoff')
        action('handoff_cancel', button='work-handoff-cancel')
        check(saved()['handoff']['status'] == 'cancelled', 'handoff cancellation persisted')
        h = remote('handoff_send', {'recipientId':owner.user['id'], 'message':'Please review', 'criteria':'Review confirmed'})['handoff']
        opened(task['id'], 'handoff')
        action('handoff_question', {'message':'Which dimensions?'})
        check(helper.request(team_path)['handoff']['reply'] == 'Which dimensions?', 'handoff question persisted')
        action('handoff_accept', button='work-handoff-accept')
        check(helper.request(team_path)['handoff']['status'] == 'accepted', 'handoff receipt shared')
        click('work-tab-notes')
        # Change version as another member while the actual form remains open.
        remote('note_add', {'tried':'Teammate trial', 'learned':'Teammate lesson'})
        action('note_add', {'tried':'Browser trial', 'learned':'Browser team lesson'}, expected=409)
        check(cdp.evaluate('document.getElementById("work-learned").value==="Browser team lesson"'), 'real conflict retains draft')
        click('companion-reload'); wait('!document.getElementById("companion-reload").disabled', 'conflict reload')
        action('note_add')
        check(any(n['learned'] == 'Browser team lesson' for n in helper.request(team_path)['notes']), 'team note shared after explicit retry')
        check(owner.request('/api/companion/notes?q=Browser%20team%20lesson') == [], 'team notes excluded from personal search')
        check(owner.request('/api/pet')['totalExperience'] == 0, 'companion actions do not grant XP')
        current = owner.request(f'/api/teams/{team_id}/tasks/{task["id"]}')
        check(current['status'] == 'Doing' and current['assigneeUserProfileId'] is None, 'handoff keeps status and assignment')
        owner.request(f'/api/teams/{team_id}/tasks/{task["id"]}', 'PUT', dict(current, status='Done', isCompleted=True))
        opened(task['id'], 'showcase')
        action('showcase_save', {'kind':'monitor', 'title':'Real HTTP showcase', 'summary':'Shared completed work'})
        check(helper.request(team_path)['showcase']['title'] == 'Real HTTP showcase', 'showcase shared')
        action('showcase_remove', button='work-showcase-remove')
        check(saved()['showcase'] is None and owner.request('/api/pet')['totalExperience'] == 25, 'removing showcase preserves task reward')
        # v22: test the actual board entry and one-field memo on a fresh task.
        click('companion-close')
        assigned = owner.request(f'/api/teams/{team_id}/tasks', 'POST',
            {'title':'Assigned review task', 'status':'Todo', 'assigneeUserProfileId':helper.user['id']}, 201)
        task_url = f'/api/teams/{team_id}/tasks/{assigned["id"]}'
        work_url = f'/api/teams/{team_id}/companion/tasks/{assigned["id"]}'
        cdp.evaluate('loadTasks()')
        wait('state.visibleTasks.some(t=>t.id===' + str(assigned['id']) + ')', 'new card loaded')
        selector = '[data-task-id="' + str(assigned['id']) + '"]'
        cdp.evaluate('document.querySelector(' + json.dumps(selector + ' .task-memo-button') + ').click()')
        wait('document.getElementById("work-learned")&&!document.getElementById("companion-reload").disabled', 'card opens memo directly')
        check(cdp.evaluate('document.querySelectorAll("#companion-tabs button").length===3'), 'three primary team tools')
        action('note_add', {'learned':'One field shared memo'})
        check(helper.request(work_url)['notes'][0]['tried'] == '', 'memo needs only body')
        click('companion-close')
        wait('document.querySelector(' + json.dumps(selector + ' .task-work-badge') + ')', 'memo badge on card')
        click('companion-hub-open')
        wait('document.getElementById("companion-dialog").open&&!document.getElementById("companion-reload").disabled', 'board opens shared hub')
        click('companion-close')
        current = owner.request(task_url)
        completed = owner.request(task_url, 'PUT', dict(current, status='Done', isCompleted=True))
        check(helper.request('/api/pet')['totalExperience'] == 25, 'assigned member receives completion XP')
        check(owner.request('/api/pet')['totalExperience'] == 25, 'operator does not receive assigned task XP')
        reopened = owner.request(task_url, 'PUT', dict(completed, status='Doing', isCompleted=False))
        check(helper.request('/api/pet')['totalExperience'] == 0, 'reopen reverses original recipient XP')
        # A recipient accepts a real HTTP handoff in the browser.
        record = helper.request(work_url)
        helper.request(work_url, 'POST', dict(action='handoff_send', version=record['version'],
            expectedUpdatedAt=record['updatedAt'], recipientId=owner.user['id'],
            message='Take over this task', criteria='Reviewed', assignOnAccept=True))
        opened(assigned['id'], 'handoff')
        check(cdp.evaluate('document.getElementById("work-handoff-accept").textContent.includes("担当を引き継ぐ")'), 'explicit assignment consent label')
        action('handoff_accept', button='work-handoff-accept')
        current = owner.request(task_url)
        check(current['assigneeUserProfileId'] == owner.user['id'] and current['status'] == 'Doing', 'accept changes assignee only')
        check(not current['handoffPending'] and current['handoffRecipientId'] is None, 'handoff badge cleared')
        # Real PostgreSQL grouping/filtering before pagination (no fake counts).
        page = owner.request(f'/api/teams/{team_id}/tasks?page=1&pageSize=1&sortOrder=desc')
        check(page['totalCount'] == 2 and page['statusCounts'] == {'todo':0,'doing':1,'done':1}, 'counts span PostgreSQL pages')
        exact = owner.request(f'/api/teams/{team_id}/tasks?tagExact=transport&pageSize=1')
        check(exact['statusCounts'] == {'todo':0,'doing':0,'done':1}, 'exact tag counts exclude unrelated tasks')
        click('companion-close')
        cdp.evaluate('state.pageSize=1;state.page=1;state.sortOrder="asc";loadTasks()')
        wait('document.getElementById("doing-count").textContent==="0 / 1"', 'off-page Doing count is not falsely zero')
        check(cdp.evaluate('document.getElementById("doing-list").textContent.includes("このページにはありません")'), 'empty column explains paging')
        observed = cdp.evaluate('httpObserved')
        successful = {r['action'] for r in observed if r.get('action') and r['status'] == 200}
        expected = {'savepoint_save','savepoint_clear','help_open','help_offer','help_withdraw','help_resolve','help_cancel',
            'handoff_send','handoff_cancel','handoff_question','handoff_accept','note_add','note_edit','note_delete','showcase_save','showcase_remove'}
        check(successful == expected, 'all 16 companion mutation actions exercised through actual UI sender')
        check(not any(r['status'] >= 400 and r['status'] != 409 and not r['control'] for r in observed), 'no unexpected HTTP errors')
        check(cdp.evaluate('uiErrors.length===0'), 'no unhandled browser errors')
        report = {'passed':len(checks), 'actions':sorted(successful), 'checks':checks, 'realHTTP':True, 'realPostgres':True,
            'demo':False, 'stripeCalls':False, 'artifacts':str(output), 'requests':observed}
        (output / 'result.json').write_text(json.dumps(report, ensure_ascii=False, indent=2))
        print(json.dumps({k:v for k,v in report.items() if k not in ('checks','requests')}, ensure_ascii=False), flush=True)
    finally:
        if browser:
            browser.terminate()
            try: browser.wait(timeout=5)
            except subprocess.TimeoutExpired: browser.kill(); browser.wait()
        # Test rows intentionally stay only in the disposable fixture for inspection.


if __name__ == '__main__': main()
