const assert = require("node:assert/strict");
const { test } = require("node:test");
const fs = require("node:fs"), vm = require("node:vm"), path = require("node:path");

function harness() {
    class Element {
        constructor() { this.children = []; this.options = []; this.events = {}; this.attrs = {}; this.value = ""; this.hidden = false; this.disabled = false; this.textContent = ""; this.open = false; this.dataset = {}; this.classes = new Set(); this.classList = { contains: name => this.classes.has(name) }; }
        addEventListener(name, fn) { this.events[name] = fn; }
        setAttribute(name, value) { this.attrs[name] = value; }
        append(...children) { this.children.push(...children); }
        replaceChildren(...children) { this.children = children; this.options = children; }
        add(option) { this.options.push(option); }
        querySelectorAll() { return this.children; }
        showModal() { this.open = true; }
        close() { this.open = false; }
        focus() { this.focused = true; }
    }
    const elements = new Map(), el = id => { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); };
    const listeners = {}, controls = [], state = { teamId:1, assignee:"", due:"", sortOrder:"desc", visibleTasks:[] };
    const context = vm.createContext({ document:{ getElementById:el, createElement:()=>new Element(), addEventListener(){}, querySelectorAll:()=>controls, querySelector:()=>controls[0] },
        window:{addEventListener:(name,fn)=>{listeners[name]=fn;}}, setInterval(){}, Date, Option:function(text,value){return {text,value};},
        state, optionsState:{busy:false,teams:[{id:1}]}, taskForm:el("create-form"), editTaskForm:el("edit-form"), createTaskModal:el("create-modal"), editModal:el("edit-modal"),
        confirm:()=>false, hasActiveTaskFilter:()=>false, isTaskBusy:()=>false, getTaskStatus:task=>task.status, TASK_STATUS:{Done:2},
        getErrorMessage:async(response,fallback)=>fallback, getDisplayErrorMessage:error=>error.message,
        TaskAuth:{request:async()=>({ok:true,json:async()=>({items:[],directCount:0,teamCount:0})})},
        moveTaskStatus:async()=>{}, canChangeWorkspace:()=>true, loadTeams:async()=>true, switchWorkspace:async()=>{}, TaskCompanion:{openTask:async()=>{}} });
    vm.runInContext(fs.readFileSync(path.join(__dirname,"../frontend/task-workflow.js"),"utf8")+"\nthis.workflow=TaskWorkflow;",context);
    return {context,el,controls,state,listeners,workflow:context.workflow, Element};
}
const settle = () => new Promise(resolve=>setImmediate(resolve));

test("task drafts protect changed text, checklist and unload, while unchanged or restored values close freely", () => {
    const h = harness(), title = {id:"title",type:"text",value:"Original"}, check = {id:"",type:"checkbox",checked:false};
    h.el("edit-form").append(title,check); h.workflow.rememberForm("edit");
    assert.equal(h.workflow.allowClose("edit"),true);
    title.value="Draft"; assert.equal(h.workflow.allowClose("edit"),false);
    let prevented = false; const event={preventDefault(){prevented=true;}}; h.listeners.beforeunload(event); assert.equal(prevented,true);
    title.value="Original"; assert.equal(h.workflow.hasChanges("edit"),false);
    check.checked=true; assert.equal(h.workflow.hasChanges("edit"),true);
    h.context.confirm=()=>true; assert.equal(h.workflow.allowClose("edit"),true);
    h.el("edit-modal").classes.add("hidden"); assert.equal(h.workflow.hasChanges("edit"),false);
});

test("failed quick status changes restore the current status and enable controls for retry", async () => {
    const h=harness(), task={id:7,title:"Review",status:0}, actions=new h.Element(); h.state.visibleTasks=[task];
    h.workflow.addQuickControls(task,actions); h.controls.push(...actions.children[0].children);
    let finish; h.context.moveTaskStatus=()=>new Promise(resolve=>{finish=resolve;});
    h.controls[0].value="2"; h.controls[0].events.change(); assert.ok(h.controls.every(control=>control.disabled));
    finish(false); await settle(); assert.equal(h.controls[0].value,"0"); assert.ok(h.controls.every(control=>!control.disabled)); assert.equal(h.controls[0].focused,true);
});
