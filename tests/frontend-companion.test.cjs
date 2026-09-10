const assert = require("node:assert/strict");
const { test } = require("node:test");
const fs = require("node:fs"), path = require("node:path"), vm = require("node:vm");
const { webcrypto } = require("node:crypto");
const source = name => fs.readFileSync(path.join(__dirname, "../frontend", name), "utf8");
function client(load = true) {
    let calls = 0; const window = { location: new URL("https://task.test/index.html?demo=1") };
    const context = vm.createContext({ window, document: { addEventListener() {} }, URL, URLSearchParams, Headers, Response, crypto: webcrypto,
        fetch: async () => { calls++; throw new Error("Real API blocked"); } });
    vm.runInContext('const UNLOCK_OPTIONS={pets:[],themes:[],layouts:[]};',context);
    if (load) vm.runInContext(source("companion-demo.js"), context);
    vm.runInContext(source("demo.js"), context); vm.runInContext(source("auth-client.js"), context);
    const request = (url, method="GET", body) => window.TaskAuth.request(url,{method,...(body?{body:JSON.stringify(body)}:{})});
    const json = async url => { const r = await request(url); assert.equal(r.status,200); return r.json(); };
    const change = async (root,id,action,values={}) => { const p=`${root}/tasks/${id}`, before=await json(p); return request(p,"POST",{action,version:before.version,expectedUpdatedAt:before.updatedAt,...values}); };
    return { request, json, change, calls:()=>calls };
}
test("all five companion demo routes stay offline and are isolated",async()=>{
    const c=client(); const personal=await c.json("/api/companion"),team=await c.json("/api/teams/1/companion");
    assert.equal(personal.savepoints[0].taskId,2); assert.equal(team.help[0].help.authorId,2); assert.equal(team.handoffs[0].handoff.toUserId,1); assert.equal(team.showcase.length,1);
    assert.equal((await c.request("/api/companion/tasks/5")).status,404); assert.equal(c.calls(),0);
});
test("missing companion adapter fails closed",async()=>{ const c=client(false); assert.equal((await c.request("/api/companion")).status,503); assert.equal(c.calls(),0); });
test("cross-team demo inbox stays offline, hides private notes and only counts unanswered requests", async () => {
    const c=client(), before=await c.json("/api/work-inbox");
    assert.equal(before.directCount,1); assert.equal(before.teamCount,1);
    assert.equal(before.items.length,2);
    assert.equal(before.items.some(item=>"savepoint" in item || "people" in item),false);
    assert.deepEqual(await c.json("/api/work-inbox"),before); // Viewing does not mark a request handled.
    const task=await c.json("/api/teams/1/companion/tasks/5");
    await c.change("/api/teams/1/companion",5,"help_offer",{entryId:task.help.id});
    assert.equal((await c.json("/api/work-inbox")).teamCount,0);
    await c.change("/api/teams/1/companion",5,"help_withdraw",{entryId:task.help.id});
    assert.equal((await c.json("/api/work-inbox")).teamCount,1);
    await c.request("/api/teams/1/members/me","DELETE");
    assert.equal((await c.json("/api/work-inbox")).items.length,0);
    assert.equal(c.calls(),0);
});
test("bookmark changes do not change status or XP and keep task responses private",async()=>{
    const c=client(), before=await c.json("/api/pet"); await c.change("/api/companion",2,"savepoint_save",{nextStep:"private sentence"});
    assert.equal((await c.json("/api/companion/tasks/2")).savepoint.nextStep,"private sentence");
    const task=await c.json("/api/tasks/2"); assert.equal(task.status,"Doing"); assert.equal(JSON.stringify(task).includes("private sentence"),false); assert.equal((await c.json("/api/pet")).totalExperience,before.totalExperience);
});
test("companion rejects stale writes and unsafe links without partial mutation",async()=>{
    const c=client(), root="/api/companion", before=await c.json(`${root}/tasks/2`);
    assert.equal((await c.change(root,2,"savepoint_save",{nextStep:"bad",resourceUrl:"javascript:alert(1)"})).status,400);
    await c.change(root,2,"savepoint_save",{nextStep:"good"});
    assert.equal((await c.request(`${root}/tasks/2`,"POST",{action:"savepoint_save",nextStep:"stale",version:before.version,expectedUpdatedAt:before.updatedAt})).status,409);
    assert.equal((await c.json(`${root}/tasks/2`)).savepoint.nextStep,"good");
});
test("help thanks survive the next request without extra XP",async()=>{
    const c=client(),root="/api/teams/1/companion", h=(await c.json(`${root}/tasks/5`)).help;
    await c.change(root,5,"help_offer",{entryId:h.id}); await c.change(root,5,"help_resolve",{entryId:h.id});
    await c.change(root,5,"help_open",{kind:"decision",message:"next"});
    assert.equal((await c.json(root)).recentThanks.length,1); assert.equal((await c.json(`${root}/tasks/5`)).thanks[0].id,h.id);
});
test("handoff recipient replies but new outgoing request cannot be self-accepted",async()=>{
    const c=client(),root="/api/teams/1/companion",h=(await c.json(`${root}/tasks/4`)).handoff;
    assert.equal((await c.change(root,4,"handoff_question",{entryId:h.id,message:"size?"})).status,200);
    await c.change(root,4,"handoff_accept",{entryId:h.id});
    const sent=await(await c.change(root,4,"handoff_send",{recipientId:2,message:"design",criteria:"3 PNGs"})).json();
    assert.equal((await c.change(root,4,"handoff_accept",{entryId:sent.handoff.id})).status,403);
    assert.equal((await c.json("/api/teams/1/tasks/4")).assigneeUserProfileId,1);
});
test("notes match exact tags and not another workspace",async()=>{
    const c=client(),root="/api/teams/1/companion";
    await c.change(root,4,"note_add",{tried:"try",learned:"lesson"});
    assert.equal((await c.json(`${root}/notes?tags=${encodeURIComponent("プログラマー")}`)).length,1);
    assert.equal((await c.json(`${root}/notes?tags=${encodeURIComponent("プログラ")}`)).length,0);
    assert.equal((await c.json("/api/companion/notes?q=lesson")).length,0);
});
test("delete/Undo restores companion records and showcase hides on reopen",async()=>{
    const c=client(),root="/api/teams/1/companion",task=await c.json("/api/teams/1/tasks/6");
    const deletion=await c.request(`/api/teams/1/tasks/6?version=${task.version}`,"DELETE");
    await c.request(`/api/teams/1/tasks/undo/${deletion.headers.get("X-Task-Undo")}`,"POST"); assert.equal((await c.json(root)).showcase.length,1);
    const current=await c.json("/api/teams/1/tasks/6"); await c.request("/api/teams/1/tasks/6","PUT",{...current,status:"Todo",isCompleted:false});
    assert.equal((await c.json(root)).showcase.length,0); assert.ok((await c.json(`${root}/tasks/6`)).showcase);
});
test("companion UI does not interpret user HTML or fetch remote thumbnails",()=>{
    const js=source("companion-work.js"); assert.doesNotMatch(js,/innerHTML|insertAdjacentHTML|localStorage|sessionStorage/);
    assert.match(js,/noopener noreferrer/); assert.match(js,/drafts/); assert.match(js,/expectedUpdatedAt/);
    assert.match(source("index.html"),/<dialog id="companion-dialog"/);
});

// Execute the shipped modal and auth client, not just the demo API adapter.
// This runs in the existing Node CI job and fails if the browser sender drops
// JSON Content-Type again. The real HTTP/Chrome suite covers all 16 actions.
for (const [teamId, recipientId, fromInbox = false] of [[null, null], [7, null], [7, 2], [7, 2, true]]) test(`real companion form sends typed JSON and CSRF (${teamId ? "team" : "personal"}, recipient=${recipientId}, fromInbox=${fromInbox})`, async () => {
    const nodes = [];
    function element(tag = "div", id = "") {
        const classes = new Set();
        const n = { tagName: tag, id, children: [], listeners: {}, dataset: {}, value: "", disabled: false, isConnected: true,
            classList: { toggle(key, on) { if (on) classes.add(key); else classes.delete(key); }, contains: key => classes.has(key) },
            append(...children) { this.children.push(...children); }, replaceChildren(...children) { this.children = children; },
            add(option) { this.children.push(option); }, addEventListener(type, handler) { this.listeners[type] = handler; },
            setAttribute() {}, focus() {}, showModal() { this.open = true; }, close() { this.open = false; },
            reportValidity() { return true; }, querySelectorAll() { return this.children.flatMap(c => [c, ...(c.querySelectorAll?.() || [])]); }
        };
        n.elements = { namedItem: name => n.querySelectorAll().find(c => c.name === name) };
        nodes.push(n); return n;
    }
    const find = id => [...nodes].reverse().find(n => n.id === id) || null;
    for (const id of ["companion-dialog","companion-content","companion-tabs","companion-message","companion-scope","companion-back",
        "companion-title","companion-reload","companion-close","edit-companion-open","companion-hub-open","create-find-work-notes"]) element("div",id);
    const initial = { taskId: 11, taskTitle: "Transport", taskStatus: "Doing", version: 4, updatedAt: "2026-09-09T00:00:00Z",
        viewerId: 1, isTeamOwner: Boolean(teamId), people: {1:"Owner",2:"Member"}, notes: [], thanks: [], savepoint: null, help: null };
    const calls = [], location = new URL("https://task.test/index.html");
    const context = vm.createContext({ URL, URLSearchParams, Headers, Request, Response,
        window: { location }, document: { getElementById: find, createElement: element },
        state: { teamId: fromInbox ? null : teamId, petProfile: {}, isTaskMutation: false }, optionsState: { teams: [{id:7,name:"Team"}] },
        isTaskBusy: () => false, loadTasks: async () => {}, confirm: () => true,
        Option: function(text, value) { this.textContent = text; this.value = value; },
        FormData: class { constructor(form) { this.values = form.querySelectorAll().filter(c => c.name).map(c => [c.name,c.value]); } entries() { return this.values.values(); } },
        getDisplayErrorMessage: error => error.message,
        fetch: async (input, options) => {
            const request = new Request(input, options), pathname = new URL(request.url).pathname;
            if (pathname === "/api/auth/csrf") return Response.json({token:"test-csrf"});
            if (request.method === "GET") return Response.json(initial);
            const body = JSON.parse(await request.text());
            calls.push({path:pathname,headers:request.headers,body});
            if (request.headers.get("Content-Type")?.split(";")[0] !== "application/json")
                return Response.json({title:"Unsupported Media Type"},{status:415});
            const record = { ...initial, version: 5 };
            if (teamId) record.help = { id:"test-help",authorId:1,status:"open",kind:body.kind,message:body.message,recipientId:body.recipientId };
            else record.savepoint = {nextStep:body.nextStep,summary:body.summary};
            return Response.json(record);
        }
    });
    vm.runInContext(source("auth-client.js"),context);
    context.TaskAuth = context.window.TaskAuth;
    context.getErrorMessage = context.TaskAuth.errorMessage;
    vm.runInContext(source("companion-work.js"),context);
    await vm.runInContext(`TaskCompanion.openTask(11,"${teamId ? "help" : "resume"}",${JSON.stringify(fromInbox ? {teamId,fromInbox:true} : {})})`,context);
    find(teamId ? "work-message" : "work-nextStep").value = "Sent by actual UI";
    if (teamId) {
        const select = find("work-recipientId");
        assert.deepEqual(select.children.map(option => String(option.value)), ["", "2"], "all-team first, self excluded");
        select.value = recipientId == null ? "" : String(recipientId);
    }
    find("companion-form").listeners.submit({preventDefault(){}});
    for (let i=0;i<50 && vm.runInContext("TaskCompanion.isSaving",context);i++) await new Promise(resolve => setImmediate(resolve));
    assert.equal(calls.length,1,"no implicit retries");
    assert.equal(calls[0].headers.get("Content-Type"),"application/json");
    assert.equal(calls[0].headers.get("X-CSRF-TOKEN"),"test-csrf");
    assert.equal(calls[0].path,teamId ? "/api/teams/7/companion/tasks/11" : "/api/companion/tasks/11");
    assert.equal(calls[0].body.action,teamId ? "help_open" : "savepoint_save");
    if (teamId) assert.equal(calls[0].body.recipientId, recipientId, "empty target is null, selected member is numeric");
    assert.equal(calls[0].body.version,4);
    assert.equal(calls[0].body.expectedUpdatedAt,initial.updatedAt);
    assert.equal(find("companion-message").classList.contains("error"),false);
    assert.equal(context.state.isTaskMutation,false);
    assert.equal(context.state.teamId,fromInbox ? null : teamId);
});

test("demo directed SOS validates recipients and persists target without status or XP changes", async () => {
    const c=client(),root="/api/teams/1/companion",before=await c.json("/api/pet");
    for (const recipientId of [1,0,-1,999,"2"]) {
        assert.equal((await c.change(root,4,"help_open",{kind:"review",message:"Please",recipientId})).status,400);
        assert.equal((await c.json(root+"/tasks/4")).help,null);
    }
    const saved=await(await c.change(root,4,"help_open",{kind:"review",message:"Please",recipientId:2})).json();
    assert.equal(saved.help.recipientId,2);
    assert.equal((await c.json(root)).help.find(item=>item.taskId===4).help.recipientId,2);
    assert.equal((await c.json("/api/teams/1/tasks/4")).status,"Todo");
    assert.equal((await c.json("/api/pet")).totalExperience,before.totalExperience);
    await c.change(root,4,"help_cancel",{entryId:saved.help.id});
    const all=await(await c.change(root,4,"help_open",{kind:"review",message:"Anyone?",recipientId:null})).json();
    assert.equal(all.help.recipientId,null);
});


test("request views retain outgoing and accepted help with action metadata", async () => {
    const c=client();
    let incoming=await c.json("/api/work-inbox");
    const item=incoming.items.find(item=>item.kind==="help");
    assert.deepEqual(item.actions,["help_offer"]); assert.ok(item.authorName); assert.ok(item.recipientName);
    const before=await c.json("/api/teams/1/companion/tasks/5");
    assert.equal(item.version,before.version); assert.equal(item.updatedAt,before.updatedAt);
    await c.change("/api/teams/1/companion",5,"help_offer",{entryId:item.id});
    const helping=await c.json("/api/work-inbox?view=helping");
    assert.equal(helping.helpingCount,1); assert.deepEqual(helping.items[0].actions,["help_withdraw"]);
    const opened=await(await c.change("/api/teams/1/companion",4,"help_open",{kind:"review",message:"Please review",recipientId:2})).json();
    const sent=await c.json("/api/work-inbox?view=sent");
    assert.equal(sent.sentCount,1); assert.equal(sent.items[0].id,opened.help.id); assert.deepEqual(sent.items[0].actions,["help_resolve","help_cancel"]);
    assert.equal((await c.request("/api/work-inbox?view=unknown")).status,400);
});
