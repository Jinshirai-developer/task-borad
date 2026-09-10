"""v17/v18 billing and purchase browser checks; no real API or Stripe calls."""
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
    output = Path(tempfile.mkdtemp(prefix='task-billing-browser-', dir='/private/tmp'))
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
            assert cdp.evaluate(expression), (label, cdp.evaluate('document.getElementById("team-message")?.textContent'))
            checks.append(label)
        def wait(expression, label):
            for _ in range(200):
                if cdp.evaluate('Boolean(' + expression + ')'): return
                time.sleep(.05)
            raise AssertionError((label, cdp.evaluate('({errors:uiErrors,message:document.getElementById("team-message")?.textContent,billing:document.getElementById("team-billing-panel")?.innerText})')))
        def click(id): cdp.evaluate('document.getElementById(' + json.dumps(id) + ').click()')
        def change(id):
            click(id)
            if id == 'billing-checkout':
                wait('document.getElementById("billing-purchase-checkout")','purchase confirmation')
                click('billing-purchase-checkout')
            wait('!optionsState.busy', id)
            if id == 'billing-checkout':
                check('document.getElementById("billing-flow").dataset.result==="pro"','confirmed demo result')
                click('billing-flow-back')
        def screenshot(name):
            data = cdp.call('Page.captureScreenshot', {'format': 'png', 'captureBeyondViewport': False})
            (output / (name + '.png')).write_bytes(base64.b64decode(data['data']))
        def layout():
            check('document.documentElement.scrollWidth<=innerWidth','page contained')
            check('document.getElementById("team-billing-panel").scrollWidth<=document.getElementById("team-billing-panel").clientWidth','billing contained')
            check('uiErrors.length===0','no browser errors')
        cdp.call('Page.enable')
        cdp.call('Page.addScriptToEvaluateOnNewDocument', {'source': '''window.uiErrors=[];window.realApiCalls=[];window.confirm=()=>true;
addEventListener('error',e=>uiErrors.push(e.message));addEventListener('unhandledrejection',e=>uiErrors.push(String(e.reason)));
const originalFetch=window.fetch;window.fetch=(input,options)=>{const u=new URL(String(input),location.href);if(u.pathname.startsWith('/api/')||u.origin!==location.origin){realApiCalls.push(u.pathname);throw new Error('Real API blocked');}return originalFetch(input,options);};'''})
        cdp.call('Emulation.setDeviceMetricsOverride', {'width':1440,'height':1000,'deviceScaleFactor':1,'mobile':False})
        origin = 'http://localhost:5097' if '--served' in sys.argv else f'http://127.0.0.1:{server.server_port}'
        cdp.call('Page.navigate', {'url':origin + '/index.html?demo=1'})
        wait('typeof TeamBilling!=="undefined"&&state.petProfile&&!optionsState.progressionLoading&&optionsState.teams.length===1','ready')
        cdp.evaluate('window.beforeXp=state.petProfile.totalExperience;switchWorkspace(1)'); wait('state.teamId===1&&state.visibleTasks.length===3','team')
        click('open-settings-button'); wait('!optionsState.progressionLoading','settings'); click('settings-tasks-tab'); click('open-team-button')
        wait('document.getElementById("billing-checkout")&&!document.getElementById("billing-checkout").disabled','free plan')
        check('document.getElementById("team-billing-panel").textContent.includes("2 / 3人")','owner-inclusive count')
        check('document.getElementById("team-billing-panel").textContent.includes("Stripeに接続しません")','simulation explicit')
        check('document.getElementById("team-billing-panel").textContent.includes("月額500円")','approved 500 yen price')
        cdp.evaluate('window.prePurchaseAuth=TaskAuth;window.purchaseCalls=[];window.TaskAuth={...prePurchaseAuth,request:async(p,o)=>{purchaseCalls.push([p,o?.method||"GET"]);return prePurchaseAuth.request(p,o);}}')
        click('billing-checkout')
        check('purchaseCalls.length===0','plan view does not create Checkout')
        check('document.getElementById("billing-flow").textContent.includes("サンプル制作チーム")','purchase team visible')
        check('document.querySelectorAll(".billing-plan-card").length===2','free and Pro comparison')
        check('document.getElementById("billing-flow").textContent.includes("月額500円")','purchase approved price')
        check('document.getElementById("billing-flow").textContent.includes("所有するすべてのチーム")','account purchase scope explicit')
        check('document.getElementById("billing-flow").textContent.includes("毎月更新")','recurring plan explicit')
        check('document.getElementById("billing-flow").textContent.includes("既存メンバー・タスク・経験値・ごほうびは残り")','downgrade retention explained')
        check('document.getElementById("billing-flow").textContent.includes("Stripeには接続しません")','purchase simulation explicit')
        check('document.querySelectorAll(".billing-steps li").length===3','three checkout steps')
        check('document.querySelector(".billing-steps [aria-current=step]").textContent.includes("プランを確認")','current step explicit')
        check('document.getElementById("billing-destination").textContent.includes("このデモではカード入力画面は開きません")','demo card input limitation explicit')
        check('new URL(document.getElementById("billing-demo-login").href).pathname==="/login.html"&&!new URL(document.getElementById("billing-demo-login").href).searchParams.has("demo")','demo offers real login without sample scope')
        wait('document.querySelector(".billing-hero-pet").complete&&document.querySelector(".billing-hero-pet").naturalWidth>0','existing pet illustration loads')
        check('document.querySelector(".billing-hero-pet").getAttribute("alt")===""','decorative pet is ignored by screen reader')
        check('document.querySelector(".billing-scope-chip").textContent.includes("お試しユーザーのアカウント")','purchase account also visible in hero')
        check('!document.querySelector("#billing-flow input")','app never captures card data')
        check('document.activeElement.id==="billing-flow-title"','purchase heading focused')
        check('document.getElementById("team-mode-tabs").getClientRects().length===0','team tabs hidden during purchase')
        check('document.querySelectorAll("#billing-flow a[rel*=noopener]").length===2','legal links available')
        screenshot('purchase-desktop')
        click('billing-flow-back')
        check('document.activeElement.id==="billing-checkout"&&document.getElementById("billing-flow").hidden','back restores focus')
        check('purchaseCalls.length===0','back never creates Checkout')
        cdp.evaluate('window.TaskAuth=prePurchaseAuth')
        change('billing-demo-member'); change('billing-demo-member')
        check('document.getElementById("team-message").textContent.includes("3人まで")','fourth member rejected')
        screenshot('free-cap-desktop'); change('billing-checkout')
        check('document.getElementById("team-billing-panel").textContent.includes("プラン上限なし")','Pro enabled')
        change('billing-demo-member'); wait('document.getElementById("billing-cancel")&&!optionsState.busy','fourth member')
        check('document.getElementById("team-member-count").textContent.includes("4人")','fourth member visible')
        change('billing-cancel'); check('document.getElementById("team-billing-panel").textContent.includes("期間末で解約予定")','cancel reserved')
        change('billing-cancel'); check('!document.getElementById("team-billing-panel").textContent.includes("期間末で解約予定")','cancel undone')
        change('billing-demo-fail'); check('document.getElementById("team-billing-panel").textContent.includes("無料プラン")','expiry removes Pro')
        check('document.getElementById("team-billing-panel").textContent.includes("4 / 3人")','over-limit members kept')
        check('state.visibleTasks.length===3&&state.petProfile.totalExperience===beforeXp','tasks and XP unchanged')
        change('billing-end-now'); check('document.getElementById("billing-checkout")!==null','can purchase after end')
        for width in [320,375,1440]:
            cdp.call('Emulation.setDeviceMetricsOverride', {'width':width,'height':950,'deviceScaleFactor':1,'mobile':width<600})
            for theme in ['classic','retro','dark']:
                cdp.evaluate('applyPreferences({theme:'+json.dumps(theme)+',layout:"board"})')
                for stage in ['free','pro','cancelled']:
                    if stage=='pro': change('billing-checkout')
                    if stage=='cancelled': change('billing-end-now')
                    cdp.evaluate('document.getElementById("team-billing-panel").scrollIntoView({block:"start"})'); layout()
                    if stage=='pro': screenshot(f'pro-{width}-{theme}')
                click('billing-checkout')
                check('document.getElementById("billing-flow").scrollWidth<=document.getElementById("billing-flow").clientWidth','purchase contained '+str(width)+' '+theme)
                check('document.documentElement.scrollWidth<=innerWidth','purchase page contained')
                check('getComputedStyle(document.getElementById("billing-purchase-checkout")).fontSize==="14px"','readable purchase button')
                check('document.getElementById("billing-purchase-checkout").getBoundingClientRect().bottom<=innerHeight','purchase action visible '+str(width)+' '+theme)
                screenshot(f'purchase-{width}-{theme}')
                click('billing-flow-back')
        # Exercise unavailable/member/error states by replacing only the offline client wrapper.
        cdp.evaluate('window.originalAuth=TaskAuth;window.billingOverride={checkoutAvailable:false,simulation:false};window.TaskAuth={...originalAuth,request:async(p,o)=>{const r=await originalAuth.request(p,o);if(String(p).endsWith("/billing")){if(window.failBilling)return new Response("{}",{status:503});return new Response(JSON.stringify({...await r.json(),...billingOverride}));}return r;}};TeamBilling.show(optionsState.teamDetail)')
        wait('document.getElementById("team-billing-panel").textContent.includes("Stripe未接続")','unconfigured')
        check('document.getElementById("team-billing-panel").textContent.includes("Stripe未接続")','unconfigured explicit')
        click('billing-checkout'); check('document.getElementById("billing-purchase-checkout").disabled','unconfigured payment disabled but plans visible');click('billing-flow-back')
        cdp.evaluate('billingOverride={checkoutAvailable:true,simulation:false};TeamBilling.show(optionsState.teamDetail)')
        wait('document.getElementById("billing-checkout")','configured UI fixture')
        click('billing-checkout')
        check('document.getElementById("billing-purchase-checkout").textContent.includes("Stripeでカード入力へ")','real mode names the Stripe card step')
        check('!document.getElementById("billing-demo-login")&&!document.getElementById("billing-login-link")','real mode does not ask signed-in owner to log in')
        check('document.getElementById("billing-destination").textContent.includes("カード番号・有効期限・CVC")','actual input destination explained')
        check('document.querySelector(".billing-steps li:nth-child(2)").textContent.includes("Stripeでカード入力")','real stepper distinguishes hosted card page')
        check('!document.querySelector("#billing-flow input, #billing-flow iframe")','no homemade card input or fake payment frame')
        screenshot('purchase-configured-ui-fixture')
        click('billing-flow-back')
        cdp.evaluate('billingOverride={isOwner:false,simulation:false};TeamBilling.show(optionsState.teamDetail)')
        wait('!document.getElementById("billing-checkout")&&document.getElementById("team-billing-panel").textContent.includes("所有者だけ")','member controls')
        check('!document.getElementById("billing-end-now")','member cannot cancel')
        click('billing-plan-view');check('!document.getElementById("billing-purchase-checkout")','member can compare but not purchase');click('billing-flow-back')
        cdp.evaluate('window.failBilling=true;TeamBilling.show(optionsState.teamDetail)');wait('document.getElementById("billing-retry")','failed read')
        cdp.evaluate('window.failBilling=false;billingOverride={}');click('billing-retry');wait('document.getElementById("billing-checkout")','retry')
        # Tampered return query can navigate but never enables Pro.
        click('team-close-button');cdp.evaluate('window.TaskAuth=originalAuth;history.replaceState(null,"","?demo=1&billing=return&team=1");TeamBilling.handleReturn()')
        wait('document.getElementById("team-message").textContent.includes("URLだけでは")','return guarded')
        check('document.getElementById("team-billing-panel").textContent.includes("無料プラン")','return URL cannot grant entitlement')
        check('document.getElementById("billing-flow").dataset.result==="ended"','tampered return preserves ended status, not success')
        check('!new URL(location.href).searchParams.has("billing")','return query cleared')
        screenshot('result-ended')
        click('billing-flow-back')
        # Request failures keep the confirmation and recoverable controls; double clicks send only once.
        click('billing-checkout')
        cdp.evaluate('window.checkoutPosts=0;window.TaskAuth={...originalAuth,request:async(p,o)=>{if(p.endsWith("/billing/checkout")){checkoutPosts++;await new Promise(r=>window.releaseCheckout=r);return new Response(JSON.stringify({message:"テスト用の通信エラー"}),{status:503});}return originalAuth.request(p,o);}}')
        click('billing-purchase-checkout');click('billing-purchase-checkout')
        check('checkoutPosts===1&&document.getElementById("billing-purchase-checkout").disabled','double click guarded')
        check('document.getElementById("billing-flow").getAttribute("aria-busy")==="true"','busy announced')
        cdp.evaluate('releaseCheckout()');wait('!optionsState.busy','checkout error')
        check('document.getElementById("billing-flow-message").classList.contains("billing-error")','error visible in purchase view')
        check('document.activeElement.id==="billing-flow-message"','error receives focus')
        check('document.getElementById("billing-flow-message").getBoundingClientRect().bottom<=document.querySelector(".billing-flow-footer").getBoundingClientRect().top','error is above sticky actions')
        check('!document.getElementById("billing-purchase-checkout").disabled','retry restored')
        check('document.getElementById("billing-flow").dataset.result!=="pro"','error never success')
        screenshot('purchase-error')
        # Invalid redirect must remain on the app, even if a response includes it.
        cdp.evaluate('window.TaskAuth={...originalAuth,request:async(p,o)=>p.endsWith("/billing/checkout")?new Response(JSON.stringify({billing:await(await originalAuth.request("/api/teams/1/billing")).json(),url:"https://checkout.stripe.com.evil.test/pay"})):originalAuth.request(p,o)}')
        click('billing-purchase-checkout');wait('!optionsState.busy','bad redirect')
        check('document.getElementById("billing-flow-message").textContent.includes("決済先URL")','invalid redirect rejected visibly')
        # A confirmed result comes from billing data, not the query; inviting does not rotate a code automatically.
        cdp.evaluate('window.TaskAuth=originalAuth')
        click('billing-purchase-checkout');wait('!optionsState.busy&&document.getElementById("billing-result-invite")','demo success')
        check('document.getElementById("billing-flow").textContent.includes("実際の購入ではありません")','success simulation explicit')
        screenshot('result-pro')
        click('billing-result-invite')
        check('document.activeElement.id==="team-invite-button"&&document.getElementById("team-invite-result").hidden','invite focuses action without regenerating code')
        click('team-close-button');cdp.evaluate('history.replaceState(null,"","?demo=1&billing=cancel&team=1");TeamBilling.handleReturn()')
        wait('document.getElementById("billing-result-invite")','paid cancel return')
        check('document.getElementById("billing-flow").dataset.result==="pro"','paid state wins over cancel query')
        click('billing-flow-back');change('billing-end-now')
        click('team-close-button');cdp.evaluate('history.replaceState(null,"","?demo=1&billing=cancel&team=1");TeamBilling.handleReturn()')
        wait('document.getElementById("billing-flow").dataset.result==="cancel"','unpaid cancel return')
        check('!document.getElementById("billing-result-invite")','cancel without payment never grants Pro')
        screenshot('result-cancel')
        click('team-close-button');cdp.evaluate('history.replaceState(null,"","?demo=1&billing=return&team=999999");TeamBilling.handleReturn()')
        check('document.getElementById("billing-flow").hidden','non-member return cannot open purchase view')
        # Unconfirmed Checkout stays pending; manual sync uses the existing endpoint and can confirm it.
        cdp.evaluate('window.syncPosts=0;window.pendingCalls=[];window.TaskAuth={...originalAuth,request:async(p,o)=>{pendingCalls.push([p,o?.method||"GET"]);const r=await originalAuth.request(p,o);if(p.endsWith("/billing"))return new Response(JSON.stringify({...await r.json(),status:"checkout",hasContract:true,plan:"free"}));if(p.endsWith("/billing/sync")){syncPosts++;return new Response(JSON.stringify({billing:{...(await r.json()).billing,status:"active",hasContract:true,plan:"pro",memberLimit:null,paidThrough:new Date(Date.now()+86400000).toISOString()}}));}return r;}};history.replaceState(null,"","?demo=1&billing=return&team=1");TeamBilling.handleReturn()')
        wait('document.getElementById("billing-flow").dataset.result==="pending"','pending checkout return')
        check('pendingCalls.every(c=>c[1]==="GET")','return never auto-purchases or mutates a contract')
        screenshot('result-pending')
        click('billing-result-review');check('document.getElementById("billing-purchase-checkout")!==null','pending checkout can be reviewed')
        click('billing-flow-back');click('team-close-button')
        cdp.evaluate('history.replaceState(null,"","?demo=1&billing=return&team=1");TeamBilling.handleReturn()');wait('document.getElementById("billing-result-sync")','return again')
        click('billing-result-sync');wait('!optionsState.busy&&document.getElementById("billing-result-invite")','sync confirmation')
        check('syncPosts===1&&document.getElementById("billing-flow").dataset.result==="pro"','manual sync confirms server result')
        click('team-close-button')
        cdp.evaluate('window.TaskAuth=originalAuth;history.replaceState(null,"","?demo=1&billing=plans&team=1");TeamBilling.handleReturn()')
        wait('document.getElementById("billing-purchase-checkout")','direct plan view')
        check('!new URL(location.href).searchParams.has("billing")','plan query consumed')
        # The visible view, not the hidden team management, owns keyboard navigation.
        cdp.evaluate('document.getElementById("billing-purchase-checkout").focus()')
        cdp.call('Input.dispatchKeyEvent',{'type':'keyDown','key':'Tab','code':'Tab','windowsVirtualKeyCode':9})
        check('document.activeElement.id==="team-close-button"','Tab wraps within visible purchase dialog')
        cdp.call('Input.dispatchKeyEvent',{'type':'keyDown','key':'Tab','code':'Tab','windowsVirtualKeyCode':9,'modifiers':8})
        check('document.activeElement.id==="billing-purchase-checkout"','Shift Tab wraps to purchase action')
        cdp.evaluate('document.getElementById("billing-flow").style.zoom="2"')
        check('document.documentElement.scrollWidth<=innerWidth','purchase contained at 200 percent zoom')
        cdp.evaluate('document.getElementById("billing-flow").style.zoom=""')
        # Late responses cannot re-open a closed or different workspace.
        click('billing-flow-back')
        cdp.evaluate('window.TaskAuth={...originalAuth,request:async(p,o)=>{const r=await originalAuth.request(p,o);if(p.endsWith("/billing"))await new Promise(resolve=>window.releaseBillingRead=resolve);return r;}};void TeamBilling.show(optionsState.teamDetail)')
        wait('typeof releaseBillingRead==="function"','delayed billing read')
        click('team-close-button');cdp.evaluate('releaseBillingRead();new Promise(r=>setTimeout(r,0))')
        wait('document.getElementById("team-modal").classList.contains("hidden")','closed billing dialog')
        check('document.getElementById("team-billing-panel").hidden&&document.getElementById("billing-flow").hidden','late read cannot restore closed view')
        cdp.evaluate('window.TaskAuth=originalAuth')
        # The account entry works from personal scope, without creating a team or Checkout.
        cdp.evaluate('switchWorkspace(null)');wait('state.teamId===null','personal scope')
        click('open-settings-button');wait('!optionsState.progressionLoading','account settings');click('settings-account-tab');click('settings-billing-button')
        wait('!document.getElementById("account-billing-panel").hidden&&document.getElementById("billing-checkout")','account plan loaded')
        check('document.getElementById("team-modal").classList.contains("account-billing-open")','account view isolated')
        check('getComputedStyle(document.getElementById("team-mode-tabs")).display==="none"','account view hides team tabs')
        check('state.teamId===null','account plan does not change workspace')
        click('billing-checkout')
        check('document.getElementById("billing-flow").textContent.includes("1アカウント")','account purchase unit explicit')
        screenshot('account-purchase-desktop')
        click('billing-purchase-checkout');wait('!optionsState.busy&&document.getElementById("billing-flow").dataset.result==="pro"','account purchase simulation')
        check('!document.getElementById("billing-result-invite")','no invite action without selected team')
        click('billing-flow-back')
        check('document.getElementById("account-billing-panel").textContent.includes("アカウントPro")','account contract can be managed')
        click('team-close-button')
        check('!document.getElementById("settings-modal").classList.contains("hidden")&&document.getElementById("settings-account-tab").getAttribute("aria-selected")==="true"','account management returns to correct settings tab')
        check('document.activeElement.id==="settings-billing-button"','account return focus restored')
        # Newly-created teams inherit the same account; all completion dismissals end at the board.
        for close in ['team-close-button','team-open-board-button','escape','backdrop']:
            if document_open := cdp.evaluate('document.getElementById("settings-modal").classList.contains("hidden")'):
                click('open-settings-button');wait('!optionsState.progressionLoading','completion settings')
            click('settings-tasks-tab');click('settings-team-create-button')
            cdp.evaluate('document.getElementById("team-name-input").value="作成完了テスト";document.getElementById("team-create-form").requestSubmit()')
            wait('!optionsState.busy&&!document.getElementById("team-complete-panel").hidden','team created')
            check('document.getElementById("team-open-board-button").textContent==="OK"','completion button is OK')
            if close=='escape': cdp.call('Input.dispatchKeyEvent',{'type':'keyDown','key':'Escape','code':'Escape','windowsVirtualKeyCode':27})
            elif close=='backdrop': click('team-modal')
            else: click(close)
            check('document.getElementById("settings-modal").classList.contains("hidden")&&document.getElementById("team-modal").classList.contains("hidden")','completion dismisses both dialogs '+close)
            check('document.activeElement.id==="workspace-tab-team-"+state.teamId','completion focuses active board '+close)
            cdp.evaluate('openTeamModal("manage")')
            wait('document.getElementById("team-billing-panel").textContent.includes("アカウントPro")','new team inherits account Pro')
            check('!document.getElementById("billing-checkout")','new Pro team offers no duplicate purchase')
            click('team-close-button')
        # Account purchase and management remain contained on narrow screens.
        for width in [320,375]:
            cdp.call('Emulation.setDeviceMetricsOverride',{'width':width,'height':950,'deviceScaleFactor':1,'mobile':True})
            cdp.evaluate('openAccountBillingModal()')
            wait('document.getElementById("billing-plan-view")','mobile account')
            check('document.documentElement.scrollWidth<=innerWidth','account management contained '+str(width))
            screenshot('account-management-'+str(width))
            click('billing-plan-view')
            check('document.documentElement.scrollWidth<=innerWidth','account comparison contained '+str(width))
            screenshot('account-purchase-'+str(width))
            click('team-close-button')
        check('realApiCalls.length===0&&uiErrors.length===0','no real API calls or errors')
        report={'passed':len(checks),'checks':checks,'artifacts':str(output)}
        (output/'result.json').write_text(json.dumps(report,ensure_ascii=False,indent=2));print(json.dumps(report,ensure_ascii=False),flush=True)
    finally:
        if browser:
            browser.terminate()
            try: browser.wait(timeout=5)
            except subprocess.TimeoutExpired: browser.kill();browser.wait()
        server.shutdown();server.server_close()


if __name__=='__main__': main()
