const assert = require("node:assert/strict");
const { test } = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const frontend = path.join(__dirname, "..", "frontend");
const source = (name) => fs.readFileSync(path.join(frontend, name), "utf8");
const response = (data, status = 200) => new Response(JSON.stringify(data), { status });

function board(request = async () => response({}), session = new Map()) {
    const nodes = new Map();
    function element(id) {
        if (nodes.has(id)) return nodes.get(id);
        const classes = new Set(id.endsWith("-modal") ? ["hidden"] : []);
        const attributes = new Map();
        const node = {
            id, value: "", textContent: "", innerHTML: "", hidden: false, disabled: false, checked: false,
            dataset: {}, style: {}, options: [], children: [], listeners: {}, isConnected: true, scrollLeft: 0,
            classList: {
                add: (...values) => values.forEach((value) => classes.add(value)),
                remove: (...values) => values.forEach((value) => classes.delete(value)),
                contains: (value) => classes.has(value),
                toggle(value, force) { if (force ?? !classes.has(value)) classes.add(value); else classes.delete(value); }
            },
            setAttribute(name, value) { attributes.set(name, value); },
            getAttribute(name) { return attributes.get(name); },
            addEventListener(name, callback) { this.listeners[name] = callback; },
            appendChild(child) { this.children.push(child); }, append(...children) { this.children.push(...children); },
            replaceChildren(...children) { this.children = [...children]; this.options = [...children]; },
            add(option) { this.options.push(option); }, focus() { document.activeElement = this; }, select() {}, reset() {},
            contains(child) { return this.children.some((item) => item === child || item.contains?.(child)); }, closest() { return null; }, scrollIntoView() {},
            querySelectorAll(query) { return query === '[role="tab"]' ? this.children.filter((item) => item.getAttribute("role") === "tab") : []; }
        };
        nodes.set(id, node);
        return node;
    }
    const themes = ["classic", "light", "dark", "retro", "forest", "sunset"].map((value) => Object.assign(element(`theme-${value}`), { value, checked: value === "classic" }));
    const species = ["dog", "cat", "rabbit", "fox", "panda", "dragon"].map((value) => Object.assign(element(`species-${value}`), { value, checked: value === "dog" }));
    element("layout-select").options = ["board", "list", "compact", "gallery", "focus"].map((value) => ({ value, disabled: false, textContent: "" }));
    const tabs = ["display", "tasks", "pet", "account"].map((value) => {
        const tab = element(`settings-${value}-tab`);
        tab.dataset.settingsTab = value;
        tab.setAttribute("aria-controls", `settings-${value}`);
        return tab;
    });
    const documentListeners = {};
    const document = {
        body: element("body"), activeElement: null,
        getElementById: element, createElement: (name) => element(`new-${name}-${nodes.size}`),
        addEventListener(name, callback) { documentListeners[name] = callback; },
        querySelector(query) {
            if (query.includes('name="theme"')) return themes.find((input) => input.checked);
            if (query.includes('name="species"')) return species.find((input) => input.checked);
            return element(query);
        },
        querySelectorAll(query) {
            if (query.includes("data-settings-tab")) return tabs;
            if (query.includes('name="theme"')) return themes;
            if (query.includes('name="species"')) return species;
            return [];
        }
    };
    const context = vm.createContext({
        document, window: { location: { hostname: "localhost" }, requestAnimationFrame: (cb) => cb(),
            sessionStorage: { getItem: (key) => session.get(key) ?? null, setItem: (key, value) => session.set(key, value), removeItem: (key) => session.delete(key) } },
        TaskAuth: { request }, URLSearchParams, Response, console, setTimeout, clearTimeout,
        confirm: () => true, Option: function(text, value) { this.textContent = text; this.value = value; }
    });
    vm.runInContext(source("app.js"), context);
    vm.runInContext(source("options.js"), context);
    return { node: element, context, run: (code) => vm.runInContext(code, context), themes, species, documentListeners };
}

test("options markup keeps logout, account deletion, and pet naming inside settings dialog", () => {
    const html = source("index.html");
    const start = html.indexOf('id="settings-modal"');
    const end = html.indexOf('id="team-modal"');
    for (const id of ["user-logout-button", "user-delete-button", "pet-name-form", "pet-name-input"]) {
        assert.equal(html.split(`id="${id}"`).length - 1, 1);
        assert.ok(html.indexOf(`id="${id}"`) > start && html.indexOf(`id="${id}"`) < end);
    }
    assert.match(html, /role="tablist"/);
    assert.match(html, /id="pet-name-display"/);
});

test("the single task-add button lives in the TODO header, not the sidebar", () => {
    const html = source("index.html");
    assert.equal(html.split('id="open-create-task-button"').length - 1, 1);
    const sidebar = html.slice(html.indexOf('<aside class="sidebar">'), html.indexOf('</aside>'));
    assert.doesNotMatch(sidebar, /open-create-task-button/);
    const todoHeader = html.slice(html.indexOf('<h2>TODO</h2>'), html.indexOf('<ul id="task-list"'));
    assert.match(todoHeader, /id="open-create-task-button"[^>]*aria-label="TODOにタスクを追加"[^>]*aria-controls="create-task-modal"/);
    assert.match(todoHeader, /data-board-search="todo"/);
});

test("workspace tabs name each participating team and label the shared task panel", async () => {
    const page = board(async () => response([{id:42,name:"制作チーム",memberCount:2},{id:43,name:"デザインチーム",memberCount:3}]));
    await page.run("loadTeams()");
    const tabs = page.node("workspace-tabs").children;
    assert.deepEqual(tabs.map(tab=>tab.children[0].textContent),["個人","制作チーム","デザインチーム"]);
    assert.equal(tabs[0].getAttribute("aria-selected"),"true");
    assert.equal(tabs[1].getAttribute("aria-selected"),"false");
    assert.equal(tabs[1].getAttribute("aria-controls"),"workspace-task-panel");
    assert.equal(page.node("workspace-task-panel").getAttribute("aria-labelledby"),"workspace-tab-personal");
    assert.match(source("index.html"),/id="workspace-task-panel"[^>]*role="tabpanel"/);
});

function workspacePage(session, { userId = 1, teams = [{ id: 42, name: "制作チーム", memberCount: 2 }], teamStatus = 200 } = {}) {
    const calls = [];
    const page = board(async (url) => {
        calls.push(url);
        if (url === "/api/user") return response({ id: userId, userKey: `user-${userId}`, displayName: "検証ユーザー" });
        if (url === "/api/teams") return response(teams, teamStatus);
        return response({ items: [], page: 1, totalPages: 1, totalCount: 0 });
    }, session);
    page.run("refreshProgression=async()=>true");
    return { ...page, calls, start: () => page.documentListeners.DOMContentLoaded() };
}

test("reload restores the selected team before the first task request and remembers returning to personal", async () => {
    const session = new Map();
    const first = workspacePage(session);
    await first.start();
    await first.node("workspace-tabs").children[1].listeners.click();

    const reloaded = workspacePage(session);
    await reloaded.start();
    assert.equal(reloaded.run("state.teamId"), 42);
    assert.equal(reloaded.node("workspace-select").value, "42");
    assert.equal(reloaded.node("workspace-tabs").children[1].getAttribute("aria-selected"), "true");
    assert.match(reloaded.node("filter-summary").textContent, /制作チーム（共有）/);
    assert.equal(reloaded.calls.filter(url => url.startsWith("/api/teams/42/tasks?")).length, 1);
    assert.equal(reloaded.calls.some(url => url.startsWith("/api/tasks?")), false, "personal tasks must not briefly load first");

    await reloaded.node("workspace-tabs").children[0].listeners.click();
    const personal = workspacePage(session);
    await personal.start();
    assert.equal(personal.run("state.teamId"), null);
    assert.equal(personal.calls.some(url => url.startsWith("/api/teams/42/tasks?")), false);
    assert.equal(personal.calls.filter(url => url.startsWith("/api/tasks?")).length, 1);
});

test("remembered workspace never crosses accounts or changes demo reset behavior", async () => {
    const session = new Map();
    const owner = workspacePage(session);
    await owner.start();
    await owner.node("workspace-tabs").children[1].listeners.click();
    const saved = [...session];

    const other = workspacePage(session, { userId: 2 });
    await other.start();
    assert.equal(other.run("state.teamId"), null, "even a shared membership does not transfer the navigation preference");
    const demo = workspacePage(session);
    demo.run("window.TaskDemo={active:true}");
    await demo.start();
    assert.equal(demo.run("state.teamId"), null);
    await demo.node("workspace-tabs").children[1].listeners.click();
    await demo.node("workspace-tabs").children[0].listeners.click();
    assert.deepEqual([...session], saved, "demo navigation must not overwrite real selection");
    const ownerAgain = workspacePage(session);
    await ownerAgain.start();
    assert.equal(ownerAgain.run("state.teamId"), 42);
});

test("a deleted or departed team falls back to personal and clears the stale selection", async () => {
    const session = new Map();
    const first = workspacePage(session);
    await first.start();
    await first.node("workspace-tabs").children[1].listeners.click();
    const removed = workspacePage(session, { teams: [] });
    await removed.start();
    assert.equal(removed.run("state.teamId"), null);
    assert.equal(removed.node("workspace-tabs").children.length, 1);
    assert.equal(removed.calls.some(url => url.startsWith("/api/teams/42/tasks?")), false);
    const rejoined = workspacePage(session);
    await rejoined.start();
    assert.equal(rejoined.run("state.teamId"), null, "a later membership must not resurrect a stale preference");
});

test("invalid stored workspace identifiers are ignored rather than used in task routes", async () => {
    for (const value of ["NaN", "-1", "0", "42/notes", "4.2", "4.2e1", "9007199254740992", "null"]) {
        const page = workspacePage(new Map([["taskBoardWorkspace:1", value]]));
        await page.start();
        assert.equal(page.run("state.teamId"), null, value);
        assert.equal(page.calls.some(url => url.startsWith("/api/teams/42/tasks?")), false, value);
    }
});

test("storage restrictions cannot prevent startup or normal workspace navigation", async () => {
    const page = workspacePage(new Map());
    page.run("Object.defineProperty(window,'sessionStorage',{get(){throw new Error('Storage disabled')}})");
    await page.start();
    await page.node("workspace-tabs").children[1].listeners.click();
    assert.equal(page.run("state.teamId"), 42);
    await page.node("workspace-tabs").children[0].listeners.click();
    assert.equal(page.run("state.teamId"), null);
});

test("a temporary team-list failure preserves the saved choice for the next reload", async () => {
    const session = new Map([["taskBoardWorkspace:1", "42"]]);
    const failed = workspacePage(session, { teamStatus: 503 });
    await failed.start();
    assert.equal(failed.run("state.teamId"), null);
    assert.equal(failed.node("workspace-tabs-message").hidden, false);
    assert.equal(failed.calls.some(url => url.startsWith("/api/teams/42/tasks?")), false);
    const retry = workspacePage(session);
    await retry.start();
    assert.equal(retry.run("state.teamId"), 42);
});

test("an explicit personal selection during startup is not replaced by a late restore", async () => {
    const session = new Map([["taskBoardWorkspace:1", "42"]]);
    let finish;
    const page = board(() => new Promise(resolve => { finish = resolve; }), session);
    page.run("state.userProfileId=1;refreshProgression=async()=>true;renderWorkspaceTabs()");
    const pending = page.run("initializeOptions()");
    await page.node("workspace-tabs").children[0].listeners.click();
    finish(response([{ id: 42, name: "制作チーム", memberCount: 2 }]));
    await pending;
    assert.equal(page.run("state.teamId"), null);
    const reloaded = workspacePage(session);
    await reloaded.start();
    assert.equal(reloaded.run("state.teamId"), null);
});

test("late workspace restoration cannot change the destination of an open task form", async () => {
    const session = new Map([["taskBoardWorkspace:1", "42"]]);
    let finish;
    const page = board(() => new Promise(resolve => { finish = resolve; }), session);
    page.run("state.userProfileId=1;refreshProgression=async()=>true");
    const pending = page.run("initializeOptions()");
    page.run("openCreateTaskModal()");
    page.node("task-title").value = "入力中の個人タスク";
    finish(response([{ id: 42, name: "制作チーム", memberCount: 2 }]));
    await pending;
    assert.equal(page.run("state.teamId"), null);
    assert.equal(page.node("create-task-modal").classList.contains("hidden"), false);
    assert.equal(page.node("task-title").value, "入力中の個人タスク");
    assert.match(page.node("create-modal-title").textContent, /個人/);
});

test("workspace arrows move focus without loading or changing scope until activated", async () => {
    const page = board(async()=>response([{id:42,name:"制作チーム",memberCount:2}]));
    await page.run("loadTeams()");
    const [personal,team] = page.node("workspace-tabs").children;
    personal.focus();
    personal.listeners.keydown({key:"ArrowRight",currentTarget:personal,preventDefault(){}});
    assert.equal(page.run("document.activeElement.id"),"workspace-tab-team-42");
    assert.equal(page.run("state.teamId"),null);
    assert.equal(personal.tabIndex,-1);
    assert.equal(team.tabIndex,0);
    assert.equal(personal.getAttribute("aria-selected"),"true");
    team.listeners.keydown({key:"Home",currentTarget:team,preventDefault(){}});
    assert.equal(page.run("document.activeElement.id"),"workspace-tab-personal");
});

test("tab switches reset old counts immediately and preserve task endpoint separation", async () => {
    const calls=[]; let finish;
    const page=board(async(url)=>{
        calls.push(url);
        if(url==="/api/teams")return response([{id:42,name:"制作チーム",memberCount:2}]);
        if(url.startsWith("/api/teams/42/tasks?"))return new Promise(resolve=>{finish=resolve});
        return response({total:0,todo:0,doing:0,done:0});
    });
    await page.run("loadTeams()");
    page.run("state.totalCount=99;state.page=4;state.search='個人の検索';state.tag='個人タグ';");
    const pending=page.node("workspace-tabs").children[1].listeners.click();
    assert.equal(page.run("state.teamId"),42);
    assert.equal(page.run("state.totalCount"),0);
    assert.equal(page.run("state.page"),1);
    assert.equal(page.run("state.search"),"");
    assert.equal(page.node("workspace-select").value,"42");
    assert.equal(page.node("workspace-task-panel").getAttribute("aria-busy"),"true");
    assert.equal(page.node("workspace-task-panel").getAttribute("aria-labelledby"),"workspace-tab-team-42");
    finish(response({items:[],page:1,pageSize:100,totalPages:0,totalCount:0})); await pending;
    assert.equal(page.node("workspace-task-panel").getAttribute("aria-busy"),"false");
    assert.equal(calls.some(url=>url.startsWith("/api/tasks?")),false);
    assert.match(page.node("filter-summary").textContent,/制作チーム（共有）/);
});

test("selected tab clicks preserve filters and saving or dragging blocks other tabs", async () => {
    const page=board();
    page.run("optionsState.teams=[{id:42,name:'制作チーム',memberCount:2}];state.search='保持';renderWorkspaceTabs()");
    assert.equal(await page.node("workspace-tabs").children[0].listeners.click(),true);
    assert.equal(page.run("state.search"),"保持");
    for(const flag of ["isSaving","isEditSaving","isTaskMutation","isDeletingAccount","draggingTask"]){
        page.run(`state.${flag}=true`);
        assert.equal(await page.node("workspace-tabs").children[1].listeners.click(),false);
        assert.equal(page.run("state.teamId"),null);
        page.run(`state.${flag}=false`);
    }
});

test("settings workspace changes select the matching tab too", async () => {
    const page=board();
    page.run("optionsState.teams=[{id:42,name:'制作チーム',memberCount:2}];loadTasks=async()=>true;renderWorkspaceTabs()");
    page.node("workspace-select").value="42";
    await page.node("workspace-select").listeners.change();
    const tabs=page.node("workspace-tabs").children;
    assert.equal(tabs[0].getAttribute("aria-selected"),"false");
    assert.equal(tabs[1].getAttribute("aria-selected"),"true");
    assert.equal(tabs[1].tabIndex,0);
});

test("late team lists cannot replace newer membership tabs", async () => {
    let finish;let count=0;
    const page=board(()=>++count===1?new Promise(resolve=>{finish=resolve}):response([{id:43,name:"新しいチーム",memberCount:2}]));
    const pending=page.run("loadTeams()");
    await page.run("loadTeams()");
    finish(response([{id:42,name:"退出済みチーム",memberCount:2}]));await pending;
    assert.deepEqual(page.node("workspace-tabs").children.map(tab=>tab.dataset.workspaceId),["","43"]);
});

test("missing active membership never relabels shared tasks as personal", async () => {
    const page=board(async()=>response([]));
    page.run("state.teamId=42");await page.run("loadTeams()");
    const tabs=page.node("workspace-tabs").children;
    assert.equal(tabs[0].getAttribute("aria-selected"),"false");
    assert.equal(tabs[1].dataset.workspaceId,"42");
    assert.match(tabs[1].children[0].textContent,/参加状況を確認できない/);
    assert.equal(page.run("state.teamId"),42);
});

test("failed team loading preserves verified names with a visible retry message", async () => {
    const page=board(async()=>response({message:"接続できません"},503));
    page.run("optionsState.teams=[{id:42,name:'既存チーム',memberCount:2}]");
    await page.run("loadTeams()");
    assert.equal(page.node("workspace-tabs").children[1].children[0].textContent,"既存チーム");
    assert.equal(page.node("workspace-tabs-message").hidden,false);
    assert.match(page.node("workspace-tabs-message").textContent,/更新/);
    assert.equal(page.node("workspace-tabs").getAttribute("aria-busy"),"false");
});

test("membership changes add and remove tabs, including when the next list fetch fails", async () => {
    const page=board();
    page.run("ensureWorkspaceOption({id:42,name:'追加されたチーム',memberCount:1});loadTasks=async()=>true");
    await page.run("switchWorkspace(42)");
    assert.equal(page.node("workspace-tabs").children[1].getAttribute("aria-selected"),"true");
    page.run("forgetWorkspace(42)");await page.run("switchWorkspace(null)");
    assert.equal(page.node("workspace-tabs").children.length,1);
    assert.equal(page.node("workspace-select").options.length,1);
});

test("team names remain literal text and focused tabs survive list rerenders", () => {
    const page=board();
    const name='<img src=x onerror=alert(1)>'+"長い名前".repeat(15);
    page.run(`optionsState.teams=[{id:42,name:${JSON.stringify(name)},memberCount:1}];renderWorkspaceTabs()`);
    page.node("workspace-tabs").children[1].focus();page.run("renderWorkspaceTabs()");
    assert.equal(page.node("workspace-tabs").children[1].children[0].textContent,name);
    assert.equal(page.node("workspace-tabs").children[1].children[0].innerHTML,"");
    assert.equal(page.run("document.activeElement.id"),"workspace-tab-team-42");
    assert.equal(page.node("workspace-tabs").children.filter(tab=>tab.tabIndex===0).length,1);
});

test("tag shortcut opens the form in settings without changing workspace or filters and restores focus", () => {
    const page=board();
    page.run("globalThis.tagOpened=0;globalThis.TaskTags={isSaving:false,openCreateForm:()=>{tagOpened++;document.getElementById('tag-name-input').focus()}};refreshProgression=async()=>true;state.teamId=42;state.tagExact='プログラマー'");
    page.node("sidebar-tag-filter").focus();
    assert.equal(page.run("openTagSettings()"),true);
    assert.equal(page.run("tagOpened"),1);
    assert.equal(page.run("state.teamId"),42);
    assert.equal(page.run("state.tagExact"),"プログラマー");
    assert.equal(page.node("settings-tasks-tab").getAttribute("aria-selected"),"true");
    assert.equal(page.run("document.activeElement.id"),"tag-name-input");
    page.run("closeOptionsModal(settingsModal)");
    assert.equal(page.run("document.activeElement.id"),"sidebar-tag-filter");
});

test("tag shortcut cannot open over a draft, team dialog, or pending save", () => {
    const page=board();
    page.run("globalThis.TaskTags={isSaving:false,openCreateForm:()=>{throw Error('must not open')}}");
    for(const name of ["create-task-modal","edit-modal","board-search-modal","team-modal"]){
        page.node(name).classList.remove("hidden");
        assert.equal(page.run("openTagSettings()"),false);
        page.node(name).classList.add("hidden");
    }
    for(const flag of ["isSaving","isEditSaving","isTaskMutation","isDeletingAccount","draggingTask"]){
        page.run(`state.${flag}=true`);
        assert.equal(page.run("openTagSettings()"),false);
        page.run(`state.${flag}=false`);
    }
    assert.equal(page.node("settings-modal").classList.contains("hidden"),true);
});

test("modal return focus is immediate and cannot steal focus from the next dialog", () => {
    const page=board();
    page.run("window.requestAnimationFrame=()=>{throw Error('focus must not be deferred')}");
    page.node("open-settings-button").focus();
    page.run("openOptionsModal(settingsModal,document.getElementById('settings-display-tab'));closeOptionsModal(settingsModal)");
    assert.equal(page.run("document.activeElement.id"),"open-settings-button");
    page.run("openCreateTaskModal()");
    assert.equal(page.run("document.activeElement.id"),"title");
});

test("tag registration destination explicitly tracks personal and selected team", () => {
    const page=board();
    page.run("renderWorkspaceLabel()");
    assert.equal(page.node("tag-scope-label").textContent,"追加先：個人（非公開）");
    page.run("optionsState.teams=[{id:42,name:'制作チーム',memberCount:3}];state.teamId=42;renderWorkspaceLabel()");
    assert.equal(page.node("tag-scope-label").textContent,"追加先：制作チーム（チームで共有）");
});

test("modern themes keep the simple shell without overriding the retro window chrome", () => {
    const css = source("options.css");
    assert.doesNotMatch(css + source("unlocks.css"), /:not\(\[data-theme=classic\]\)/);
    assert.doesNotMatch(css + source("unlocks.css"), /body\[data-theme\]\[data-layout/);
    assert.match(css, /body\[data-ui=modern\]\[data-layout\] \.layout \{[^}]*display:grid;[^}]*grid-template-columns:260px minmax\(0,1fr\)/);
    assert.match(css, /body\[data-ui=modern\]\[data-layout\] \.sidebar::before \{ display:none; \}/);
    assert.match(css, /body\[data-ui=modern\]\[data-layout\] button \{[^}]*border:1px solid var\(--sh\);[^}]*box-shadow:none;/);
    assert.match(css, /body\[data-theme=classic\] \{[^}]*--surface:[^;]+;[^}]*--accent-soft:/);
    assert.match(css, /body\[data-ui=modern\]\[data-layout\] \.layout \{[^}]*display:flex; flex-direction:column;/);
    assert.match(css, /body\[data-ui=modern\]\[data-layout=list\] \.board-column \.task-list \{ grid-auto-rows:auto;/);
    assert.match(source("unlocks.css"), /body\[data-ui=modern\]\[data-layout=compact\] \.board-column \.task-item \{ border-radius:4px; \}/);
    assert.match(source("style.css"), /\.sidebar::before\s*\{[^}]*content:\s*"Task Board - 育成タスク管理"/);
    const html = source("index.html");
    assert.match(html, /<body[^>]*data-theme="classic"[^>]*data-ui="modern"/);
    assert.equal((html.match(/name="theme"/g) || []).length, 6);
    assert.match(html, /value="retro"[^>]*>.*?<strong>Windows風<\/strong>/);
});

test("classic retains every unlocked layout when switching theme", () => {
    const page = board();
    page.run(`optionsState.unlocks=normalizeUnlockCatalog(${JSON.stringify(catalogAt(7, 1350))})`);
    for (const layout of ["board", "list", "compact", "gallery", "focus"]) {
        page.run(`applyPreferences({theme:"dark",layout:"${layout}"});applyPreferences({theme:"classic",layout:"${layout}"})`);
        assert.equal(page.node("body").dataset.theme, "classic");
        assert.equal(page.node("body").dataset.ui, "modern");
        assert.equal(page.node("body").dataset.layout, layout);
        assert.equal(page.node("layout-select").value, layout);
    }
});

test("retro preserves all unlocked layouts and restores modern scope when switched back", () => {
    const page = board();
    page.run(`optionsState.unlocks=normalizeUnlockCatalog(${JSON.stringify(catalogAt(7, 1350))})`);
    for (const layout of ["board", "list", "compact", "gallery", "focus"]) {
        page.run(`applyPreferences({theme:"classic",layout:"${layout}"});applyPreferences({theme:"retro",layout:"${layout}"})`);
        assert.equal(page.node("body").dataset.theme, "retro");
        assert.equal(page.node("body").dataset.ui, "retro");
        assert.equal(page.node("body").dataset.layout, layout);
        assert.equal(page.node("layout-select").value, layout);
        assert.equal(page.themes.find((input) => input.checked).value, "retro");
        page.run(`applyPreferences({theme:"classic",layout:"${layout}"})`);
        assert.equal(page.node("body").dataset.ui, "modern");
        assert.equal(page.node("body").dataset.layout, layout);
    }
});

test("preferences are account-scoped and invalid server values fall back safely", () => {
    const page = board();
    page.run('applyPreferences({theme:"dark",layout:"list"})');
    assert.equal(page.node("body").dataset.theme, "dark");
    assert.equal(page.node("body").dataset.ui, "modern");
    assert.equal(page.node("body").dataset.layout, "list");
    assert.equal(page.themes.find((input) => input.checked).value, "dark");
    page.run('applyPreferences({theme:"retro",layout:"list"});applyPreferences({theme:"malicious",layout:"unknown"})');
    assert.equal(page.node("body").dataset.theme, "classic");
    assert.equal(page.node("body").dataset.ui, "modern");
    assert.equal(page.node("body").dataset.layout, "board");
    assert.doesNotMatch(page.run("applyPreferences.toString()"), /localStorage|sessionStorage/);
});

test("retro save applies the server-confirmed window appearance", async () => {
    const calls = [];
    const page = board(async (url, options) => { calls.push({ url, options }); return response({ theme: "retro", layout: "list" }); });
    page.themes.forEach((input) => { input.checked = input.value === "retro"; });
    page.node("layout-select").value = "list";
    await page.node("preferences-form").listeners.submit({ preventDefault() {} });
    assert.deepEqual(JSON.parse(calls[0].options.body), { theme: "retro", layout: "list" });
    assert.equal(page.node("body").dataset.theme, "retro");
    assert.equal(page.node("body").dataset.ui, "retro");
    assert.equal(page.node("body").dataset.layout, "list");
});

test("theme save persists both fields and applies only the server-confirmed result", async () => {
    const calls = [];
    const page = board(async (url, options) => { calls.push({ url, options }); return response({ theme: "light", layout: "list" }); });
    page.themes.forEach((input) => { input.checked = input.value === "light"; });
    page.node("layout-select").value = "list";
    await page.node("preferences-form").listeners.submit({ preventDefault() {} });
    assert.equal(calls[0].url, "/api/user/preferences");
    assert.equal(calls[0].options.method, "PUT");
    assert.deepEqual(JSON.parse(calls[0].options.body), { theme: "light", layout: "list" });
    assert.equal(page.node("body").dataset.theme, "light");
    assert.match(page.node("preferences-message").textContent, /保存しました/);
});

test("failed theme save does not change the active theme", async () => {
    for (const [active, selected] of [["classic", "dark"], ["classic", "retro"], ["retro", "classic"]]) {
        const page = board(async () => response({ message: "保存できません" }, 503));
        page.run(`applyPreferences({theme:"${active}",layout:"board"})`);
        page.themes.forEach((input) => { input.checked = input.value === selected; });
        await page.node("preferences-form").listeners.submit({ preventDefault() {} });
        assert.equal(page.node("body").dataset.theme, active);
        assert.equal(page.node("body").dataset.ui, active === "retro" ? "retro" : "modern");
        assert.equal(page.node("preferences-save-button").disabled, false);
        assert.equal(page.node("preferences-message").classList.contains("error"), true);
    }
});

test("settings tabs expose one panel and support arrow-key navigation", () => {
    const page = board();
    page.run('selectSettingsTab("pet")');
    assert.equal(page.node("settings-pet").hidden, false);
    assert.equal(page.node("settings-account").hidden, true);
    assert.equal(page.node("settings-display").hidden, true);
    assert.equal(page.node("settings-pet-tab").getAttribute("aria-selected"), "true");
    page.node("settings-pet-tab").listeners.keydown({ key: "ArrowRight", preventDefault() {} });
    assert.equal(page.node("settings-account").hidden, false);
    assert.equal(page.node("settings-account-tab").tabIndex, 0);
    assert.equal(page.node("settings-pet-tab").tabIndex, -1);
});

test("personal and team tasks use separate endpoints, without changing private routes", () => {
    const page = board();
    assert.equal(page.run("getTasksApiUrl()"), "/api/tasks");
    page.run("state.teamId = 42");
    assert.equal(page.run("getTasksApiUrl()"), "/api/teams/42/tasks");
    assert.equal(page.run("getTasksApiUrl(null)"), "/api/tasks");
});

test("task settings hide unrelated unlocks and team dialog returns to the same tab", async () => {
    const page = board();
    page.run('settingsModal.classList.remove("hidden"); selectSettingsTab("tasks")');
    assert.equal(page.node("settings-tasks").hidden, false);
    assert.equal(page.node("unlock-overview").hidden, true);
    await page.node("open-team-button").listeners.click();
    assert.equal(page.node("settings-modal").classList.contains("hidden"), true);
    assert.equal(page.node("team-modal").classList.contains("hidden"), false);
    page.run("closeOptionsModalOnEscape()");
    assert.equal(page.node("team-modal").classList.contains("hidden"), true);
    assert.equal(page.node("settings-modal").classList.contains("hidden"), false);
    assert.equal(page.node("settings-tasks-tab").getAttribute("aria-selected"), "true");
    page.run('selectSettingsTab("display")');
    assert.equal(page.node("unlock-overview").hidden, true);
    page.run('selectSettingsTab("pet")');
    assert.equal(page.node("unlock-overview").hidden, false);
});

test("existing board summary makes the current sharing scope explicit without another panel", () => {
    const page = board();
    page.run("renderFilterSummary()");
    assert.match(page.node("filter-summary").textContent, /個人（非公開）/);
    page.run("state.teamId=42;optionsState.teams=[{id:42,name:'制作チーム'}];renderWorkspaceLabel()");
    assert.match(page.node("filter-summary").textContent, /制作チーム（共有）/);
});

test("team creation lives only in settings and returns to the same settings tab", async () => {
    const html = source("index.html");
    const sidebar = html.slice(html.indexOf('<aside class="sidebar">'),html.indexOf('</aside>'));
    assert.match(sidebar, /id="sidebar-tag-filter"/);
    assert.doesNotMatch(sidebar, /open-team-hub-button|settings-team-create-button/);
    assert.match(html, /data-settings-tab="tasks">タグ・チーム<\/button>/);
    const page = board();
    page.run('settingsModal.classList.remove("hidden");selectSettingsTab("tasks")');
    page.node("settings-team-create-button").focus();
    await page.node("settings-team-create-button").listeners.click();
    assert.equal(page.node("settings-modal").classList.contains("hidden"), true);
    assert.equal(page.node("team-create-panel").hidden, false);
    assert.equal(page.node("team-join-panel").hidden, true);
    assert.equal(page.node("team-manage-tab").hidden, true);
    page.node("team-create-tab").listeners.keydown({key:"ArrowRight",preventDefault(){}});
    assert.equal(page.node("team-create-panel").hidden, true);
    assert.equal(page.node("team-join-panel").hidden, false);
    assert.equal(page.run("document.activeElement.id"), "team-join-tab");
    page.run("closeOptionsModalOnEscape()");
    assert.equal(page.run("document.activeElement.id"), "settings-team-create-button");
    assert.equal(page.node("settings-modal").classList.contains("hidden"), false);
});

test("successful team creation shows invitation and a next step, without exposing destructive management", async () => {
    const calls=[]; const team={id:42,name:"制作チーム",role:"owner",memberCount:1,members:[]};
    const page=board(async (url,options) => {
        calls.push({url,options});
        if (url==="/api/teams" && options.method==="POST") return response({team,inviteCode:"test-only-code",expiresAt:"2026-09-15T00:00:00Z"},201);
        return response([team]);
    });
    page.run("loadTasks=async()=>true");
    await page.node("settings-team-create-button").listeners.click();
    page.node("team-name-input").value=" 制作チーム ";
    await page.node("team-create-form").listeners.submit({preventDefault(){}});
    assert.deepEqual(JSON.parse(calls[0].options.body),{name:"制作チーム"});
    assert.equal(page.run("state.teamId"),42);
    assert.equal(page.node("team-complete-panel").hidden,false);
    assert.equal(page.node("team-manage-panel").hidden,true);
    assert.equal(page.node("team-invite-code").value,"test-only-code");
    assert.match(page.node("team-complete-title").textContent,/制作チーム.*作成しました/);
    page.node("team-open-board-button").listeners.click();
    assert.equal(page.node("team-modal").classList.contains("hidden"),true);
    assert.equal(page.node("settings-modal").classList.contains("hidden"),true);
    assert.equal(page.node("team-invite-code").value,"");
    assert.equal(page.run("document.activeElement.id"),"workspace-tab-team-42");
});

test("completed create or join closes to board with X, Escape and backdrop, not settings", () => {
    for (const created of [true, false]) for (const close of ["x", "escape", "backdrop"]) {
        const page=board();
        page.run("settingsModal.classList.remove('hidden');returnToTaskSettings=true;teamSettingsOrigin=document.getElementById('settings-team-create-button');settingsModal.classList.add('hidden');teamModal.classList.remove('hidden');state.teamId=42;optionsState.teams=[{id:42,name:'Done'}];renderWorkspaceTabs()");
        page.run(`showTeamComplete({id:42,name:'Done'},${created})`);
        if (close==="x") page.node("team-close-button").listeners.click();
        else if (close==="escape") page.run("closeOptionsModalOnEscape()");
        else page.node("team-modal").listeners.click({target:page.node("team-modal")});
        assert.equal(page.node("settings-modal").classList.contains("hidden"),true);
        assert.equal(page.node("team-modal").classList.contains("hidden"),true);
        assert.equal(page.run("document.activeElement.id"),"workspace-tab-team-42");
        assert.equal(page.run("state.teamId"),42);
    }
    assert.match(source("index.html"),/id="team-open-board-button"[^>]*>OK<\/button>/);
});

test("invalid invitations retain the join form and never switch workspace", async () => {
    const page=board(async()=>response({message:"招待コードが無効です。"},400));
    await page.node("settings-team-join-button").listeners.click();
    page.node("team-join-input").value="wrong-code";
    await page.node("team-join-form").listeners.submit({preventDefault(){}});
    assert.equal(page.run("state.teamId"),null);
    assert.equal(page.node("team-join-panel").hidden,false);
    assert.equal(page.node("team-join-input").value,"wrong-code");
    assert.match(page.node("team-message").textContent,/無効/);
});

test("invitation copying is user-triggered and falls back to manual selection", async () => {
    const page=board();
    page.run("renderInvitation({inviteCode:'test-only-code'})");
    page.run("globalThis.copied=[];globalThis.navigator={clipboard:{writeText:async(code)=>copied.push(code)}}");
    assert.equal(page.run("copied.length"),0);
    await page.node("team-copy-invite-button").listeners.click();
    assert.equal(page.run("copied[0]"),"test-only-code");
    assert.match(page.node("team-copy-message").textContent,/コピーしました/);
    page.run("navigator.clipboard.writeText=async()=>{throw new Error('denied')}");
    await page.node("team-copy-invite-button").listeners.click();
    assert.match(page.node("team-copy-message").textContent,/選択中のコード/);
    assert.equal(page.run("document.activeElement.id"),"team-invite-code");
});

test("late management reads cannot change a new create flow or steal focus", async () => {
    let finish; const page=board(()=>new Promise(resolve=>{finish=resolve}));
    page.run("state.teamId=42");
    const opening=page.node("open-team-button").listeners.click();
    page.node("team-create-tab").focus();
    await page.node("team-create-tab").listeners.click();
    finish(response({id:42,name:"古い応答",role:"owner",members:[]})); await opening;
    assert.equal(page.node("team-create-panel").hidden,false);
    assert.equal(page.node("current-team-panel").hidden,true);
    assert.equal(page.run("document.activeElement.id"),"team-create-tab");
    page.run("state.isSaving=true");
    await page.node("settings-team-create-button").listeners.click();
    assert.equal(page.run("teamMode"),"create");
});

test("new workspace stays named and selected when post-create team-list refresh fails", async () => {
    const page=board(async (url,options)=>options.method==="POST" ? response({team:{id:42,name:"新しいチーム",memberCount:1,role:"owner"},inviteCode:"test-code"},201) : response({message:"一覧を取得できません"},503));
    page.run("loadTasks=async()=>true");
    await page.node("settings-team-create-button").listeners.click();
    page.node("team-name-input").value="新しいチーム";
    await page.node("team-create-form").listeners.submit({preventDefault(){}});
    assert.equal(page.node("workspace-select").value,"42");
    assert.equal(page.node("workspace-select").options.some(option=>option.value==="42"),true);
    assert.match(page.node("filter-summary").textContent,/新しいチーム（共有）/);
});

test("a closed management dialog ignores late membership details and clears invitations", async () => {
    let finish; const page=board(()=>new Promise(resolve=>{finish=resolve}));
    page.run("state.teamId=42");
    const opening=page.node("open-team-button").listeners.click();
    page.run("renderInvitation({inviteCode:'test-code'});closeOptionsModalOnEscape()");
    finish(response({id:42,name:"遅い応答",role:"owner",members:[]})); await opening;
    assert.equal(page.node("team-modal").classList.contains("hidden"),true);
    assert.equal(page.node("current-team-panel").hidden,true);
    assert.equal(page.node("team-invite-code").value,"");
});

test("in-flight results from a previous workspace are never rendered", async () => {
    let resolve;
    const pending = new Promise((done) => { resolve = done; });
    const page = board(async () => pending);
    page.run("globalThis.rendered = []; renderTasks = (tasks) => rendered.push(tasks); renderPagination = () => {}; renderFilterSummary = () => {};");
    const loading = page.run("loadTasks()");
    page.run("state.teamId = 9; state.taskLoadVersion++");
    resolve(response({ items: [{ title: "Private" }], page: 1, pageSize: 100, totalPages: 1, totalCount: 1 }));
    assert.equal(await loading, false);
    assert.equal(page.run("rendered.length"), 0);
});

test("workspace changes are blocked during task mutations", () => {
    const page = board();
    for (const flag of ["isSaving", "isEditSaving", "isTaskMutation", "isDeletingAccount"]) {
        page.run(`state.${flag} = true`);
        assert.equal(page.run("canChangeWorkspace()"), false, flag);
        page.run(`state.${flag} = false`);
    }
    assert.equal(page.run("canChangeWorkspace()"), true);
});

test("team progress is fetched independently of task search and page filters", async () => {
    const calls = [];
    const page = board(async (url) => { calls.push(url); return response({ total: 40, todo: 10, doing: 10, done: 20, overdue: 2 }); });
    page.run('state.teamId=3; state.search="filtered"; state.page=2; state.pageSize=5;');
    await page.run("loadWorkspaceProgress()");
    assert.deepEqual(calls, ["/api/teams/3/summary"]);
    assert.equal(page.node("workspace-progress-bar").getAttribute("aria-valuenow"), "50");
    assert.match(page.node("workspace-progress-counts").textContent, /全 40件/);
    assert.match(page.node("workspace-progress-counts").textContent, /期限超過 2件/);
});

test("task mutations include optimistic concurrency versions", async () => {
    const calls = [];
    const page = board(async (url, options) => { calls.push({ url, options }); return new Response(null, { status: 204 }); });
    page.run("state.teamId=7; loadTasks=async()=>true; refreshProgression=async()=>true;");
    await page.run('moveTaskStatus({id:8,title:"shared",description:"",status:0,version:123},1)');
    assert.equal(calls[0].url, "/api/teams/7/tasks/8");
    assert.equal(JSON.parse(calls[0].options.body).version, 123);
    await page.run('deleteTask({id:8,version:124})');
    assert.equal(calls[1].url, "/api/teams/7/tasks/8?version=124");
});

test("overdue dates use Tokyo calendar days and never mark today's deadline as overdue", () => {
    const page = board();
    assert.equal(page.run('getTokyoDateKey(new Date("2026-09-07T14:59:59Z"))'), "2026-09-07");
    assert.equal(page.run('getTokyoDateKey(new Date("2026-09-07T15:00:00Z"))'), "2026-09-08");
    assert.equal(page.run('isTaskOverdue({dueDate:"2026-09-07T00:00:00Z",status:0},new Date("2026-09-07T14:59:59Z"))'), false);
    assert.equal(page.run('isTaskOverdue({dueDate:"2026-09-07T00:00:00Z",status:0},new Date("2026-09-07T15:00:00Z"))'), true);
    assert.equal(page.run('isTaskOverdue({dueDate:"2026-09-08T00:00:00Z",status:1},new Date("2026-09-07T15:00:00Z"))'), false);
    assert.equal(page.run('isTaskOverdue({dueDate:"2026-09-08T00:00:00Z",status:1},new Date("2026-09-08T14:59:59Z"))'), false);
    assert.equal(page.run('isTaskOverdue({dueDate:"2026-09-07T00:00:00Z",status:2},new Date("2026-09-07T15:00:00Z"))'), false);
    assert.equal(page.run('isTaskOverdue({dueDate:"invalid",status:0},new Date("2026-09-07T15:00:00Z"))'), false);
});

test("date-only deadline labels preserve the encoded UTC day in every browser timezone", () => {
    const page = board();
    assert.equal(page.run('formatDueDate("2026-09-08T00:00:00Z")'), "2026/09/08");
    assert.equal(page.run('formatDueDate("2026-01-01T00:00:00Z")'), "2026/01/01");
    assert.equal(page.run('formatDueDate("invalid")'), "");
    assert.match(source("index.html"), /期限（JST）/);
    assert.match(source("options.js"), /JST基準/);
});

function catalogAt(level, totalExperience = 0) {
    const thresholds = [0, 0, 100, 250, 450, 700, 1000, 1350];
    const entries = (items) => items.map(([id, name, requiredLevel]) => ({ id, name, requiredLevel, unlocked: level >= requiredLevel, experienceRemaining: Math.max(0, thresholds[requiredLevel] - totalExperience) }));
    return { level, totalExperience,
        pets: entries([["dog", "いぬ", 1], ["cat", "ねこ", 1], ["rabbit", "うさぎ", 1], ["fox", "きつね", 1], ["panda", "パンダ", 1], ["dragon", "ドラゴン", 1]]),
        themes: entries([["classic", "クラシック", 1], ["light", "ライト", 1], ["dark", "ダーク", 1], ["retro", "Windows風", 1], ["forest", "フォレスト", 1], ["sunset", "サンセット", 1]]),
        layouts: entries([["board", "ボード", 1], ["list", "リスト", 1], ["compact", "コンパクト", 1], ["gallery", "ギャラリー", 1], ["focus", "集中", 1]]) };
}

test("display preferences and species remain available without progression data", () => {
    const page = board();
    page.run("renderUnlockCatalog()");
    assert.equal(page.run('isChoiceUnlocked("pets","cat")'), true);
    assert.equal(page.run('isChoiceUnlocked("pets","fox")'), true);
    assert.equal(page.themes.find((item) => item.value === "retro").disabled, false);
    assert.equal(page.themes.find((item) => item.value === "forest").disabled, false);
    assert.equal(page.species.find((item) => item.value === "rabbit").disabled, false);
    assert.equal(page.node("layout-select").options.find((item) => item.value === "focus").disabled, false);
    page.run('applyPreferences({theme:"sunset",layout:"focus"})');
    assert.equal(page.node("body").dataset.theme, "sunset");
    assert.equal(page.node("body").dataset.layout, "focus");
});

test("older server catalogs without retro keep the new Lv1 theme available", () => {
    const page = board();
    const catalog = catalogAt(1);
    catalog.themes = catalog.themes.filter((item) => item.id !== "retro");
    page.run(`optionsState.unlocks=normalizeUnlockCatalog(${JSON.stringify(catalog)});renderUnlockCatalog();applyPreferences({theme:"retro",layout:"board"})`);
    assert.equal(page.run('isChoiceUnlocked("themes","retro")'), true);
    assert.equal(page.themes.find((item) => item.value === "retro").disabled, false);
    assert.equal(page.node("body").dataset.ui, "retro");
    assert.equal(page.node("body").dataset.theme, "retro");
});

test("server catalog governs unlocks and required experience even when level appears sufficient", () => {
    const page = board();
    const catalog = catalogAt(7, 1350);
    catalog.themes.find((item) => item.id === "sunset").unlocked = false;
    page.run(`optionsState.unlocks=normalizeUnlockCatalog(${JSON.stringify(catalog)});renderUnlockCatalog()`);
    assert.equal(page.run('isChoiceUnlocked("pets","dragon")'), true);
    assert.equal(page.run('isChoiceUnlocked("themes","sunset")'), false);
    assert.equal(page.node("layout-select").options.find((item) => item.value === "focus").disabled, false);
    page.run(`optionsState.unlocks=normalizeUnlockCatalog(${JSON.stringify(catalogAt(2, 125))});renderUnlockCatalog()`);
    assert.match(page.node("unlock-next-label").textContent, /いつでも自由/);
    assert.equal(page.node("unlock-progress").getAttribute("aria-valuenow"), "100");
});

test("locked selections cannot be saved through the normal settings handler", async () => {
    const page = board(() => assert.fail("server-rejected choice must not be sent"));
    const catalog = catalogAt(1); catalog.themes.find(item => item.id === "forest").unlocked = false;
    page.run(`optionsState.unlocks=normalizeUnlockCatalog(${JSON.stringify(catalog)})`);
    page.themes.forEach((input) => { input.checked = input.value === "forest"; });
    page.node("layout-select").value = "board";
    await page.node("preferences-form").listeners.submit({ preventDefault() {} });
    assert.match(page.node("preferences-message").textContent, /まだ解放されていない/);
});

test("level loss refreshes pet and catalog without resetting saved display choices", async () => {
    const calls = [];
    const profile = { name: "モカ", species: "fox", level: 1, totalExperience: 75, experience: 75, experienceToNextLevel: 100, energy: 80, completedTaskCount: 3, streakDays: 1 };
    const page = board(async (url) => {
        calls.push(url);
        return response(url.endsWith("unlocks") ? catalogAt(1, 75) : url.endsWith("preferences") ? { theme: "forest", layout: "focus" } : profile);
    });
    page.run(`optionsState.unlocks=normalizeUnlockCatalog(${JSON.stringify(catalogAt(7, 1350))});applyPreferences({theme:"forest",layout:"focus"})`);
    assert.equal(await page.run("refreshProgression()"), true);
    assert.deepEqual(calls.sort(), ["/api/pet", "/api/user/preferences", "/api/user/unlocks"]);
    assert.equal(page.node("body").dataset.theme, "forest");
    assert.equal(page.node("body").dataset.layout, "focus");
    assert.equal(page.node("pet-sprite").dataset.species, "fox");
    assert.equal(page.species.find((item) => item.value === "fox").disabled, false);
    assert.equal(page.run("state.petProfile.totalExperience"), 75);
});

test("catalog failure reports error while display choices remain available", async () => {
    const page = board(async (url) => url.endsWith("unlocks") ? response({ message: "更新できません" }, 503) : response(url.endsWith("preferences") ? { theme: "forest", layout: "gallery" } : { species: "panda", level: 4, experienceToNextLevel: 100 }));
    page.run(`optionsState.unlocks=normalizeUnlockCatalog(${JSON.stringify(catalogAt(7, 1350))})`);
    assert.equal(await page.run("refreshProgression()"), false);
    assert.equal(page.run('isChoiceUnlocked("pets","dragon")'), true);
    assert.equal(page.node("body").dataset.theme, "forest");
    assert.match(page.node("unlock-message").textContent, /更新できません/);
});

test("reversed completion reports negative experience, level loss and fallback without a gain message", () => {
    const page = board();
    page.run('globalThis.events=[];showPetEvent=(text)=>events.push(text);showTemporaryPetState=()=>{};state.petProfile={level:1,totalExperience:75,species:"dog"};optionsState.preferences={theme:"classic",layout:"board"}');
    assert.equal(page.run('showPetReward({level:2,totalExperience:100,species:"fox",theme:"classic",layout:"board"},-1)'), true);
    assert.match(page.run("events[0]"), /-25 EXP/);
    assert.match(page.run("events[0]"), /Lv\.2 → Lv\.1/);
    assert.match(page.run("events[0]"), /最新の保存内容/);
    assert.doesNotMatch(page.run("events[0]"), /\+25|レベルアップ/);
    assert.equal(page.run('showPetReward({level:1,totalExperience:50},-1)'), false, "unrelated increases cannot masquerade as reversal rewards");
});

test("TODO to DOING clears stale reward feedback and never announces unrelated XP changes", async () => {
    const page = board(async () => response({ id: 4 }));
    page.run('globalThis.clears=0;globalThis.rewards=0;clearPetFeedback=()=>clears++;showPetReward=()=>rewards++;loadTasks=async()=>true;state.petProfile={level:1,totalExperience:25};refreshProgression=async()=>{state.petProfile={level:1,totalExperience:50};return true}');
    await page.run('moveTaskStatus({id:4,title:"Work",status:0},1)');
    assert.equal(page.run("clears"), 1);
    assert.equal(page.run("rewards"), 0);
});

test("steady pet rendering never replays the server's old experience-award message", () => {
    const page = board();
    page.run('state.petProfile={species:"dog",level:1,totalExperience:75,experience:75,experienceToNextLevel:100,energy:80,mood:"Happy",message:"経験値を獲得しました"};renderPetProfile(state.petProfile)');
    assert.doesNotMatch(page.node("pet-status").textContent, /経験値|獲得|EXP/);
    page.run('clearPetFeedback()');
    assert.doesNotMatch(page.node("pet-status").textContent, /経験値|獲得|EXP/);
    assert.equal(page.node("pet-event").textContent, "");
});

test("focus chooses at most three DOING cards and clearly reports hidden filtered tasks", () => {
    const page = board();
    const tasks = [{ id: 1, status: "Todo" }, ...[2, 3, 4, 5].map((id) => ({ id, status: "Doing" })), { id: 6, status: "Done" }];
    page.run(`state.visibleTasks=${JSON.stringify(tasks)};state.totalCount=12;document.body.dataset.layout="focus";renderFocusLayout()`);
    assert.equal(page.run("getFocusSelection().tasks.length"), 3);
    assert.equal(page.run("getFocusSelection().status"), 1);
    assert.match(page.node("focus-layout-description").textContent, /全 12件/);
    assert.match(page.node("focus-layout-description").textContent, /9件は非表示/);
    assert.match(page.node("focus-layout-description").textContent, /他ページを含む/);
    page.run('state.status="Done"');
    assert.equal(page.run("getFocusSelection().status"), 2);
    assert.equal(page.run("getFocusSelection().tasks.length"), 1);
});
