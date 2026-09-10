"""Real UI -> HTTP -> PostgreSQL SOS recipient checks. Isolated fixture only."""
import base64
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import time
import urllib.request

spec = importlib.util.spec_from_file_location("http_qa", Path(__file__).with_name("browser-companion-http.py"))
qa = importlib.util.module_from_spec(spec)
spec.loader.exec_module(qa)


def main():
    config = json.loads(subprocess.check_output(["docker", "inspect", "task-v21-qa-web"]))[0]
    assert config["Config"]["Labels"].get("task-transport-v21") == "disposable-test"
    assert config["Config"]["Image"] == "task-board:sos-v23"
    assert "Billing__Enabled=false" in config["Config"]["Env"]
    assert list(config["NetworkSettings"]["Networks"]) == ["task-v21-qa-net"]
    output = Path(tempfile.mkdtemp(prefix="task-sos-http-", dir="/private/tmp"))
    owner, recipient, observer = [qa.Actor(role) for role in ["owner", "recipient", "observer"]]
    auth = owner.request("/api/auth/config")
    for actor in [owner, recipient, observer]:
        actor.register(auth)
    created = owner.request("/api/teams", "POST", {"name": "SOS test team"}, 201)
    team_id = created["team"]["id"]
    for actor in [recipient, observer]:
        actor.request("/api/teams/join", "POST", {"inviteCode": created["inviteCode"]})
    task = owner.request(f"/api/teams/{team_id}/tasks", "POST", {"title": "SOS recipient test", "status": "Doing"}, 201)
    work = f'/api/teams/{team_id}/companion/tasks/{task["id"]}'
    checks, browser = [], None
    try:
        browser = subprocess.Popen(["/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
            "--headless=new", "--disable-gpu", "--disable-background-networking", "--disable-sync",
            "--no-first-run", "--remote-debugging-port=0", "--user-data-dir=" + str(output / "profile"),
            "about:blank"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        pages = None
        for _ in range(100):
            try:
                port = int((output / "profile/DevToolsActivePort").read_text().splitlines()[0])
                pages = json.load(urllib.request.urlopen(f"http://127.0.0.1:{port}/json/list", timeout=1))
                break
            except Exception:
                time.sleep(.1)
        assert pages
        cdp = qa.module.CDP(next(page["webSocketDebuggerUrl"] for page in pages if page["type"] == "page"))
        def check(condition, label):
            assert condition, label
            checks.append(label)
        def wait(expression, label):
            for _ in range(250):
                if cdp.evaluate("Boolean(" + expression + ")"): return
                time.sleep(.05)
            raise AssertionError(label)
        def click(id):
            cdp.evaluate("document.getElementById(" + json.dumps(id) + ").click()")
        def fill(id, value):
            cdp.evaluate("(()=>{const e=document.getElementById(" + json.dumps(id) + ");e.value=" +
                json.dumps(str(value)) + ";e.dispatchEvent(new Event('input',{bubbles:true}));})()")
        def opened():
            cdp.evaluate(f'TaskCompanion.openTask({task["id"]},"help")')
            wait('document.getElementById("companion-dialog").open&&!document.getElementById("companion-reload").disabled', "help loaded")
        def saved_action(action, button=None):
            before = cdp.evaluate("sosRequests.length")
            if button: click(button)
            else: cdp.evaluate('document.getElementById("companion-form").requestSubmit()')
            wait(f'!TaskCompanion.isSaving&&sosRequests.length>{before}', action)
            result = cdp.evaluate("sosRequests.at(-1)")
            check(result["action"] == action and result["status"] == 200, action + " HTTP saved")
            check(result["type"].split(";")[0] == "application/json", action + " uses JSON")
            return result
        def remote(actor, action, fields=None, expected=200):
            before = actor.request(work)
            return actor.request(work, "POST", dict(action=action, version=before["version"],
                expectedUpdatedAt=before["updatedAt"], **(fields or {})), expected)
        cdp.call("Page.enable")
        cdp.call("Page.addScriptToEvaluateOnNewDocument", {"source": """
window.uiErrors=[];window.sosRequests=[];window.confirm=()=>true;
addEventListener('error',e=>uiErrors.push(e.message));
addEventListener('unhandledrejection',e=>uiErrors.push(String(e.reason)));
const originalFetch=window.fetch;
window.fetch=async(input,options)=>{
 const response=await originalFetch(input,options);
 const url=new URL(String(input),location.href);
 if(url.pathname.includes('/companion/tasks/')&&options?.body) {
  const body=JSON.parse(options.body);
  sosRequests.push({action:body.action,recipientId:body.recipientId,status:response.status,
   type:new Headers(options.headers).get('Content-Type')});
 }
 return response;
};"""})
        cdp.call("Emulation.setDeviceMetricsOverride", {"width": 1440, "height": 1000, "deviceScaleFactor": 1, "mobile": False})
        cdp.call("Page.navigate", {"url": qa.ORIGIN + "/login.html"})
        wait('typeof config!=="undefined"&&config&&!isSaving', "login ready")
        fill("auth-user-key", owner.key); fill("auth-password", owner.password)
        cdp.evaluate('document.getElementById("auth-form").requestSubmit()')
        wait('typeof TaskCompanion!=="undefined"&&state.petProfile&&!optionsState.progressionLoading', "login complete")
        cdp.evaluate(f"switchWorkspace({team_id})")
        wait(f'state.teamId==={team_id}&&state.visibleTasks.length===1', "team loaded")
        opened()
        options = cdp.evaluate('[...document.getElementById("work-recipientId").options].map(o=>o.value)')
        check(options[0] == "" and str(owner.user["id"]) not in options, "all-team first, self excluded")
        check(set(options[1:]) == {str(recipient.user["id"]), str(observer.user["id"])}, "only current teammates selectable")
        fill("work-recipientId", recipient.user["id"]); fill("work-message", "Please review the implementation")
        # Pending selection is preserved when moving to another tab and back.
        click("work-tab-notes"); click("work-tab-help")
        check(cdp.evaluate('document.getElementById("work-recipientId").value') == str(recipient.user["id"]), "recipient draft retained")
        for width in [320, 1440]:
            cdp.call("Emulation.setDeviceMetricsOverride", {"width": width, "height": 950, "deviceScaleFactor": 1, "mobile": width < 600})
            check(cdp.evaluate('document.getElementById("companion-dialog").scrollWidth<=document.getElementById("companion-dialog").clientWidth+1'), f"form fits {width}px")
            picture = cdp.call("Page.captureScreenshot", {"format": "png", "captureBeyondViewport": False})
            (output / f"sos-form-{width}.png").write_bytes(base64.b64decode(picture["data"]))
        sent = saved_action("help_open")
        check(sent["recipientId"] == recipient.user["id"], "form sends numeric recipient")
        record = recipient.request(work)
        check(record["help"]["recipientId"] == recipient.user["id"], "recipient persists in PostgreSQL")
        check(observer.request(work)["help"]["message"] == "Please review the implementation", "targeted SOS stays visible to team")
        remote(observer, "help_offer", {"entryId": record["help"]["id"]}, 403)
        check(recipient.request(work)["version"] == record["version"], "rejected third-party offer has no mutation")
        remote(recipient, "help_offer", {"entryId": record["help"]["id"]})
        check(recipient.request(work)["help"]["helperId"] == recipient.user["id"], "recipient can respond")
        click("companion-close"); click("companion-hub-open")
        wait('document.querySelector(".work-help-recipient")', "hub target visible")
        check(recipient.user["displayName"] in cdp.evaluate('document.getElementById("companion-content").textContent'), "hub names recipient")
        opened(); saved_action("help_cancel", "work-help-cancel")
        # The owner is a spectator, not the sender/recipient, on the next SOS.
        remote(recipient, "help_open", {"kind": "review", "message": "Observer only", "recipientId": observer.user["id"]})
        opened()
        check(not cdp.evaluate('Boolean(document.getElementById("work-help-offer"))'), "owner cannot respond to another member's targeted SOS")
        saved_action("help_cancel", "work-help-cancel")
        # Incoming directed SOS: the recipient uses the real UI to respond.
        remote(recipient, "help_open", {"kind": "review", "message": "For owner", "recipientId": owner.user["id"]})
        opened()
        check("あなた" in cdp.evaluate('document.querySelector(".work-help-recipient").textContent'), "incoming SOS marked for you")
        saved_action("help_offer", "work-help-offer")
        saved_action("help_withdraw", "work-help-withdraw")
        saved_action("help_cancel", "work-help-cancel")
        fill("work-message", "Anyone can help"); fill("work-recipientId", "")
        check(saved_action("help_open")["recipientId"] is None, "all-team form sends null, not empty string")
        record = remote(observer, "help_offer", {"entryId": owner.request(work)["help"]["id"]})
        check(record["help"]["helperId"] == observer.user["id"], "all-team SOS accepts any teammate")
        current = owner.request(f'/api/teams/{team_id}/tasks/{task["id"]}')
        check(current["status"] == "Doing" and current["assigneeUserProfileId"] is None, "SOS does not assign task or change status")
        check(all(actor.request("/api/pet")["totalExperience"] == 0 for actor in [owner, recipient, observer]), "SOS grants no XP")
        check(cdp.evaluate("uiErrors.length===0"), "no browser errors")
        report = {"passed": len(checks), "checks": checks, "realHTTP": True, "realPostgres": True,
            "stripeCalls": False, "artifacts": str(output)}
        (output / "result.json").write_text(json.dumps(report, ensure_ascii=False, indent=2))
        print(json.dumps({k:v for k,v in report.items() if k != "checks"}, ensure_ascii=False), flush=True)
    finally:
        if browser:
            browser.terminate()
            try: browser.wait(timeout=5)
            except subprocess.TimeoutExpired: browser.kill(); browser.wait()


if __name__ == "__main__":
    main()
