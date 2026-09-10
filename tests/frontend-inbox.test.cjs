const assert = require("node:assert/strict"), {test} = require("node:test");
const fs = require("node:fs"), path = require("node:path"), vm = require("node:vm");
const item = {id:"request-1",teamId:7,taskId:11,teamName:"Design",taskTitle:"Check layout",kind:"help",audience:"team",
    message:"Please check the mobile layout",authorName:"Designer",recipientName:"チーム全員",requestKind:"review",status:"waiting",version:4,updatedAt:"2026-09-09T01:00:00Z",actions:["help_offer"]};
const result = (items=[item]) => ({items,directCount:0,teamCount:items.length,helpingCount:0,sentCount:0});
const ok = data => ({ok:true,status:200,json:async()=>data});
const tick = () => new Promise(resolve=>setImmediate(resolve));
function harness() {
    class Element {
        constructor(tag="div") { this.tag=tag; this.children=[];this.events={};this.attrs={};this.dataset={};this.hidden=false;this.disabled=false;this.textContent="";this.isConnected=true;
            const classes=new Set();this.classList={add:name=>classes.add(name),remove:name=>classes.delete(name),contains:name=>classes.has(name)}; }
        addEventListener(name,action) { this.events[name]=action; }
        append(...nodes) { this.children.push(...nodes); }
        replaceChildren(...nodes) { this.children=nodes; }
        setAttribute(name,value) { this.attrs[name]=value; }
        querySelectorAll(selector) { return this.children.flatMap(child=>[child,...child.querySelectorAll("*")]).filter(child=>selector!=="button" || child.tag==="button"); }
        focus() { this.focused=true; }
    }
    const elements=new Map(), el=id=>{if(!elements.has(id))elements.set(id,new Element());return elements.get(id);};
    const calls=[], details=[], state={teamId:null,isTaskMutation:false};
    let handle=async()=>ok(result());
    const document={hidden:false,body:new Element(),getElementById:el,createElement:tag=>new Element(tag),addEventListener(){}};
    el("work-inbox-panel").hidden=true;
    el("work-inbox-panel").append(el("work-inbox-items"));
    const context=vm.createContext({document,window:{addEventListener(){}},setInterval(){},Date,state,
        isTaskBusy:()=>state.isTaskMutation,loadTasks:async()=>{},getErrorMessage:async(response,fallback)=>fallback,getDisplayErrorMessage:error=>error.message,
        TaskCompanion:{isOpen:false,openTask:async(...args)=>{details.push(args);}},TaskAuth:{request:async(url,options)=>{calls.push({url,options});return handle(url,options);}}});
    vm.runInContext(fs.readFileSync(path.join(__dirname,"../frontend/task-inbox.js"),"utf8")+"\nthis.inbox=TaskInbox;",context);
    return {el,calls,details,state,document,inbox:context.inbox,setHandler(fn){handle=fn;},action(name){return el("work-inbox-items").querySelectorAll("button").find(button=>button.dataset.inboxAction===name);}};
}
test("the request list opens inline and an initial in-flight read renders the requested view",async()=>{
    const h=harness();let finish;h.setHandler(()=>new Promise(resolve=>{finish=resolve;}));
    h.inbox.initialize();h.inbox.open();assert.equal(h.el("work-inbox-panel").hidden,false);assert.equal(h.document.body.classList.contains("is-inbox-view"),true);
    finish(ok(result()));await tick();assert.equal(h.el("work-inbox-count").textContent,"1");assert.ok(h.action("help_offer"));assert.equal(h.details.length,0);
    h.inbox.close();assert.equal(h.el("work-inbox-panel").hidden,true);assert.equal(h.document.body.classList.contains("is-inbox-view"),false);
});
test("accepting from the list submits the displayed version once without changing workspace or opening a modal",async()=>{
    const h=harness();h.inbox.initialize();await tick();h.inbox.open();await tick();let finish;
    h.setHandler((url,options)=>options ? new Promise(resolve=>{finish=resolve;}) : ok(result([])));
    const action=h.action("help_offer");action.events.click();action.events.click();assert.equal(h.state.isTaskMutation,true);
    finish(ok({}));await tick();await tick();
    const writes=h.calls.filter(call=>call.options);assert.equal(writes.length,1);
    assert.equal(writes[0].url,"/api/teams/7/companion/tasks/11");assert.equal(writes[0].options.headers["Content-Type"],"application/json");
    assert.deepEqual(JSON.parse(writes[0].options.body),{action:"help_offer",entryId:item.id,version:item.version,expectedUpdatedAt:item.updatedAt});
    assert.equal(h.state.teamId,null);assert.equal(h.details.length,0);assert.equal(h.state.isTaskMutation,false);
    assert.match(h.el("work-inbox-message").textContent,/対応中/);
});
test("a conflict refreshes the request for review without retrying the write",async()=>{
    const h=harness();h.inbox.initialize();await tick();h.inbox.open();await tick();
    h.setHandler((url,options)=>options ? {ok:false,status:409} : ok(result([{...item,message:"Updated request",version:5}])));
    h.action("help_offer").events.click();await tick();await tick();
    assert.equal(h.calls.filter(call=>call.options).length,1);assert.match(h.el("work-inbox-message").textContent,/内容が変わった/);
    assert.ok(h.el("work-inbox-items").querySelectorAll("*").some(node=>node.textContent==="Updated request"));
});
test("late responses cannot replace another view and failed reads clear private list content",async()=>{
    const h=harness();h.inbox.initialize();await tick();h.inbox.open();await tick();let old,newer;
    h.setHandler(url=>new Promise(resolve=>{if(url.includes("helping"))old=resolve;else newer=resolve;}));
    h.el("inbox-view-helping").events.click();h.el("inbox-view-sent").events.click();
    newer(ok({...result([{...item,taskTitle:"Outgoing",actions:["help_resolve"]}]),sentCount:1}));await tick();
    old(ok(result([{...item,taskTitle:"Obsolete"}])));await tick();
    assert.equal(h.el("inbox-view-sent").attrs["aria-pressed"],"true");
    assert.ok(h.el("work-inbox-items").querySelectorAll("*").some(node=>node.textContent==="Outgoing"));
    assert.ok(!h.el("work-inbox-items").querySelectorAll("*").some(node=>node.textContent==="Obsolete"));
    h.setHandler(()=>({ok:false,status:403}));await h.inbox.refresh(true);
    assert.equal(h.el("work-inbox-items").children.length,0);assert.equal(h.el("work-inbox-count").textContent,"!");
});
test("details pass the request scope while preserving the visible board and list",async()=>{
    const h=harness();h.inbox.initialize();await tick();h.inbox.open();await tick();
    h.el("work-inbox-items").querySelectorAll("button").find(button=>button.textContent==="詳細・メモ").events.click();await tick();
    assert.equal(h.details.length,1);assert.deepEqual(JSON.parse(JSON.stringify(h.details[0])),[11,"help",{teamId:7,fromInbox:true}]);
    assert.equal(h.state.teamId,null);assert.equal(h.el("work-inbox-panel").hidden,false);
});
test("a list action can interrupt a background read and ignores its late response",async()=>{
    const h=harness();h.inbox.initialize();await tick();h.inbox.open();await tick();let read,write;
    h.setHandler((url,options)=>new Promise(resolve=>{if(options)write=resolve;else read=resolve;}));
    const pending=h.inbox.refresh();h.action("help_offer").events.click();assert.equal(h.state.isTaskMutation,true);
    h.setHandler(()=>ok(result([])));write(ok({}));await tick();await tick();
    read(ok(result([{...item,taskTitle:"Old response"}])));await pending;
    assert.equal(h.calls.filter(call=>call.options).length,1);
    assert.ok(!h.el("work-inbox-items").querySelectorAll("*").some(node=>node.textContent==="Old response"));
    assert.equal(h.el("work-inbox-count").textContent,"0");
});
test("a returned question identifies its author and keeps acceptance criteria visible",async()=>{
    const h=harness();h.setHandler(()=>ok(result([{...item,kind:"handoff",status:"question",authorName:"Requester",recipientName:"Reviewer",criteria:"Check error states",actions:[]}])));
    h.inbox.initialize();await tick();h.inbox.open();await tick();
    const texts=h.el("work-inbox-items").querySelectorAll("*").map(node=>node.textContent);
    assert.ok(texts.includes("Reviewer → Requester"));assert.ok(texts.includes("完了の条件：Check error states"));assert.ok(texts.includes("質問を確認・返信"));
});
