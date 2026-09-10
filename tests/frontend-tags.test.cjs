const assert = require("node:assert/strict");
const { test } = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const source = (name) => fs.readFileSync(path.join(__dirname, "..", "frontend", name), "utf8");
const response = (data, status = 200) => new Response(JSON.stringify(data), { status });
const catalog = {
    items: [{ name: "プログラマー", total: 7, todo: 3, doing: 2, done: 2 }, { name: "アーティスト", total: 2, todo: 1, doing: 1, done: 0 }, { name: "空の分類", total: 0, todo: 0, doing: 0, done: 0 }],
    totalTasks: 12, untaggedTasks: 4, registeredTags: 3, maxRegisteredTags: 50
};

function page(request = async () => response(catalog)) {
    const nodes = new Map();
    function element(id) {
        if (nodes.has(id)) return nodes.get(id);
        const classes = new Set(id.endsWith("-modal") ? ["hidden"] : []);
        const attrs = new Map();
        const node = {
            id, value: "", textContent: "", hidden: id.endsWith("-tag-panel"), disabled: false, dataset: {}, children: [], listeners: {}, style: {}, options: [],
            classList: { add: (...items) => items.forEach((v) => classes.add(v)), remove: (...items) => items.forEach((v) => classes.delete(v)), contains: (v) => classes.has(v), toggle(v, enabled) { if (enabled ?? !classes.has(v)) classes.add(v); else classes.delete(v); } },
            setAttribute(k, v) { attrs.set(k, v); }, getAttribute(k) { return attrs.get(k); },
            addEventListener(k, v) { this.listeners[k] = v; }, appendChild(child) { this.children.push(child); }, append(...children) { this.children.push(...children); },
            replaceChildren(...children) { this.children = children; }, querySelectorAll(query) { return query === "input" ? this.children.flatMap((child) => child.children).filter((child) => child.type === "checkbox") : this.children; },
            contains(child) { return this.children.some((item) => item === child || item.contains(child)); }, focus() { document.activeElement = this; }, scrollIntoView(options) { this.lastScroll = options; }, select() {}, reset() {}, closest() { return null; }
        };
        nodes.set(id, node);
        return node;
    }
    const document = {
        body: element("body"), activeElement: null, getElementById: element, addEventListener() {},
        createElement: (name) => element(`new-${name}-${nodes.size}`), querySelector: element, querySelectorAll: () => []
    };
    const calls = [];
    const context = vm.createContext({
        document, window: { location: { hostname: "localhost" } }, console, URLSearchParams, Response, setTimeout, clearTimeout,
        TaskAuth: { request: async (url, options) => { calls.push({ url, options }); return request(url, options); } }, confirm: () => true
    });
    vm.runInContext(source("app.js"), context);
    vm.runInContext(`const optionsState={busy:false}; function isTaskBusy(){return state.isSaving || state.isEditSaving || state.isTaskMutation || TaskTags.isSaving;} async function loadWorkspaceProgress(){}; function syncSearchFormFromState(){}; function renderFocusLayout(){};`, context);
    vm.runInContext(source("task-tags.js"), context);
    return { node: element, context, calls, run: (script) => vm.runInContext(script, context) };
}

test("classification markup has separate registered-tag form and multi-select task candidates", () => {
    const html = source("index.html");
    for (const id of ["task-classifications", "classification-chips", "classification-summary", "tag-create-form", "tag-name-input", "create-tag-choices", "edit-tag-choices"]) assert.equal(html.split(`id="${id}"`).length - 1, 1);
    assert.match(html, /複数タグのタスクは各タグで数えるため、合計は重複/);
    assert.match(html, /task-tags\.js\?v=/);
    assert.match(html, /task-tags\.css\?v=/);
    const settings = html.slice(html.indexOf('id="settings-tasks"'), html.indexOf('id="settings-display"'));
    for (const id of ["workspace-select", "workspace-progress", "tag-create-form", "classification-chips"]) assert.ok(settings.includes(`id="${id}"`));
    const main = html.slice(html.indexOf('<main class="app">'), html.indexOf('id="create-task-modal"'));
    assert.doesNotMatch(main, /workspace-bar|task-classifications|workspace-progress/);
    for (const prefix of ["create", "edit"]) {
        assert.match(html, new RegExp(`id="${prefix}-tag-panel"[^>]* hidden`));
        assert.match(html, new RegExp(`id="${prefix}-tags-toggle"[^>]*aria-expanded="false"`));
    }
    assert.match(html, /id="tags" type="hidden"/);
    assert.match(html, /id="edit-tags" type="hidden"/);
});

test("catalog uses workspace-wide counts and retains registered empty categories", async () => {
    const ui = page();
    ui.run("state.visibleTasks = [{tags:'プログラマー'}]; state.totalCount = 1");
    await ui.run("TaskTags.refresh()");
    const chips = ui.node("classification-chips").children;
    assert.deepEqual(chips.map((chip) => chip.children.map((c) => c.textContent).join(":")), ["すべて:12", "未分類:4", "プログラマー:7", "アーティスト:2", "空の分類:0"]);
    assert.equal(ui.calls[0].url, "/api/task-tags");
});

test("exact classification resets page while retaining legacy filters and server queries", async () => {
    const ui = page(async (url) => url.startsWith("/api/tasks?") ? response({ items: [], page: 1, pageSize: 10, totalPages: 0, totalCount: 0 }) : response(catalog));
    await ui.run("TaskTags.refresh()");
    ui.run("state.page=4;state.pageSize=10;state.tag='frontend';state.search='UI';");
    await ui.run("TaskTags.select('tag','プログラマー')");
    const url = new URL(ui.calls.find((call) => call.url.startsWith("/api/tasks?")).url, "http://localhost");
    assert.equal(url.searchParams.get("tagExact"), "プログラマー");
    assert.equal(url.searchParams.get("tag"), "frontend");
    assert.equal(url.searchParams.get("search"), "UI");
    assert.equal(url.searchParams.get("page"), "1");
    assert.equal(url.searchParams.has("untagged"), false);
    assert.match(ui.node("classification-summary").textContent, /7件.*TODO 3.*DOING 2.*DONE 2/);
    assert.match(ui.node("filter-summary").textContent, /分類: プログラマー/);
});

test("untagged and all classifications never combine incompatible query parameters", async () => {
    const ui = page(async (url) => url.startsWith("/api/tasks?") ? response({ items: [], page: 1, pageSize: 100, totalPages: 0, totalCount: 0 }) : response(catalog));
    ui.run("state.tagExact='プログラマー'");
    await ui.run("TaskTags.select('untagged')");
    await ui.run("TaskTags.select('all')");
    const urls = ui.calls.filter((call) => call.url.startsWith("/api/tasks?")).map((call) => new URL(call.url, "http://localhost"));
    assert.equal(urls[0].searchParams.get("untagged"), "true");
    assert.equal(urls[0].searchParams.has("tagExact"), false);
    assert.equal(urls[1].searchParams.has("untagged"), false);
    assert.equal(urls[1].searchParams.has("tagExact"), false);
});

test("late personal catalog cannot expose private chips or drafts after switching team", async () => {
    let finishPrivate;
    const ui = page((url) => url === "/api/task-tags" ? new Promise((resolve) => { finishPrivate = resolve; }) : response({ ...catalog, items: [{ name: "チームのみ", total: 1, todo: 1, doing: 0, done: 0 }] }));
    ui.node("tags").value = "個人の秘密";
    ui.node("edit-tags").value = "個人の秘密";
    const pending = ui.run("TaskTags.refresh()");
    ui.run("state.teamId=42;TaskTags.resetScope(42)");
    assert.equal(ui.node("tags").value, "");
    assert.equal(ui.node("edit-tags").value, "");
    await ui.run("TaskTags.refresh()");
    finishPrivate(response(catalog));
    await pending;
    const labels = ui.node("classification-chips").children.map((chip) => chip.children[0].textContent);
    assert.deepEqual(labels, ["すべて", "未分類", "チームのみ"]);
    assert.equal(ui.calls[1].url, "/api/teams/42/task-tags");
});

test("late requests within the same scope cannot replace newer counts", async () => {
    let finishOld; let count = 0;
    const ui = page(() => ++count === 1 ? new Promise((resolve) => { finishOld = resolve; }) : response({ ...catalog, totalTasks: 20 }));
    const pending = ui.run("TaskTags.refresh()");
    await ui.run("TaskTags.refresh()");
    finishOld(response(catalog)); await pending;
    assert.match(ui.node("classification-summary").textContent, /20件/);
});

test("dropdown checkboxes toggle multiple tags, dedupe ASCII case, and preserve legacy CSV tags", async () => {
    const ui = page();
    await ui.run("TaskTags.refresh()");
    ui.node("tags").value = "API, api, 独自タグ";
    ui.run("TaskTags.renderPickers()");
    assert.equal(JSON.stringify(ui.run("TaskTags.parse(' API,api, , 独自タグ ')")), JSON.stringify(["API", "独自タグ"]));
    let chip = ui.node("create-tag-choices").querySelectorAll("input").find((item) => item.dataset.tagName === "プログラマー");
    chip.focus(); chip.listeners.change();
    assert.equal(ui.node("tags").value, "API, 独自タグ, プログラマー");
    chip = ui.node("create-tag-choices").querySelectorAll("input").find((item) => item.dataset.tagName === "プログラマー");
    assert.equal(chip.checked, true);
    assert.equal(ui.run("document.activeElement.dataset.tagName"), "プログラマー");
    assert.equal(ui.node("create-tags-selection").textContent, "API、独自タグ、プログラマー");
    chip.listeners.change();
    assert.equal(ui.node("tags").value, "API, 独自タグ");
});

test("candidate additions cannot exceed CSV's existing 300-character limit", async () => {
    const ui = page(); await ui.run("TaskTags.refresh()");
    const value = "x".repeat(299);
    ui.node("tags").value = value; ui.run("TaskTags.renderPickers()");
    const checkbox = ui.node("create-tag-choices").querySelectorAll("input").find((item) => item.dataset.tagName === "プログラマー");
    checkbox.checked = true; checkbox.listeners.change();
    assert.equal(checkbox.checked, false);
    assert.equal(ui.node("tags").value, value);
    assert.match(ui.node("create-tag-message").textContent, /300文字/);
});

test("new task inherits selected classification but editing keeps the task's own tags", async () => {
    const ui = page();
    ui.run("state.tagExact='プログラマー';openCreateTaskModal()");
    assert.equal(ui.node("tags").value, "プログラマー");
    ui.run("startEditTask({id:8,title:'Task',tags:'アーティスト, api',status:0,priority:1})");
    assert.equal(ui.node("edit-tags").value, "アーティスト, api");
});

test("registering a tag uses same-origin authenticated POST and does not create or retag tasks", async () => {
    const ui = page(async (url, options) => options?.method === "POST" ? response({ name: "プランナー" }, 201) : response(catalog));
    ui.node("tag-name-input").value = "  プランナー  ";
    await ui.node("tag-create-form").listeners.submit({ preventDefault() {} });
    assert.equal(ui.calls[0].url, "/api/task-tags");
    assert.equal(ui.calls[0].options.method, "POST");
    assert.deepEqual(JSON.parse(ui.calls[0].options.body), { name: "プランナー" });
    assert.equal(ui.calls[0].options.headers["Content-Type"], "application/json");
    assert.equal(ui.calls.every((call) => call.url === "/api/task-tags"), true);
    assert.match(ui.node("classification-message").textContent, /登録しました/);
});

test("tag name validation rejects empty, long, comma, and control characters", () => {
    const ui = page();
    for (const name of ["", "a".repeat(51), "a,b", "a\nb", "a\u007fb"]) assert.notEqual(ui.run(`TaskTags.validateName(${JSON.stringify(name)})`), "");
    assert.equal(ui.run('TaskTags.validateName("プログラマー")'), "");
});

test("duplicate registration and unauthorized catalog fail explicitly without stale counts", async () => {
    let fail = false;
    const ui = page(async (url, options) => options?.method === "POST" ? response({ message: "同じタグが登録済みです。" }, 409) : fail ? response({ message: "チームに参加していません。" }, 403) : response(catalog));
    await ui.run("TaskTags.refresh()");
    ui.node("tag-name-input").value = "プログラマー";
    await ui.node("tag-create-form").listeners.submit({ preventDefault() {} });
    assert.match(ui.node("classification-message").textContent, /登録済み/);
    fail = true;
    await ui.run("TaskTags.refresh()");
    assert.deepEqual(ui.node("classification-chips").children.map((chip) => chip.children[0].textContent), ["すべて", "未分類"]);
    assert.match(ui.node("classification-message").textContent, /参加していません/);
    assert.equal(ui.node("classification-message").classList.contains("error"), true);
});

test("scope-changing controls and classification selection are blocked during registration", async () => {
    let finish;
    const ui = page((url, options) => options?.method === "POST" ? new Promise((resolve) => { finish = resolve; }) : response(catalog));
    ui.node("tag-name-input").value = "新しいタグ";
    const pending = ui.node("tag-create-form").listeners.submit({ preventDefault() {} });
    assert.equal(ui.run("TaskTags.isSaving"), true);
    assert.equal(ui.run("isTaskBusy()"), true);
    assert.equal(await ui.run("TaskTags.select('tag','プログラマー')"), false);
    assert.equal(ui.run("state.tagExact"), "");
    finish(response({ name: "新しいタグ" }, 201)); await pending;
    assert.equal(ui.run("TaskTags.isSaving"), false);
    assert.equal(ui.node("tag-create-submit").disabled, false);
});

test("failed catalog keeps count-free controls to remove the selected classification", async () => {
    const ui = page((url) => url.startsWith("/api/tasks?") ? response({ items: [], page: 1, pageSize: 100, totalPages: 0, totalCount: 0 }) : response({ message: "集計の取得に失敗しました。" }, 503));
    ui.run("state.tagExact='プログラマー'");
    await ui.run("TaskTags.refresh()");
    assert.equal(ui.run("state.tagExact"), "プログラマー");
    const all = ui.node("classification-chips").children[0];
    assert.equal(all.children.length, 1);
    assert.equal(all.children[0].textContent, "すべて");
    await all.listeners.click();
    assert.equal(ui.run("state.tagExact"), "");
    assert.equal(ui.calls.some((call) => call.url.startsWith("/api/tasks?") && !call.url.includes("tagExact")), true);
});

test("workspace reset and complete task loading are hooked into classification refresh", () => {
    assert.match(source("options.js"), /state\.teamId = teamId;\s+if \(typeof TaskTags !== "undefined"\) TaskTags\.resetScope\(teamId\);/);
    assert.match(source("app.js"), /TaskTags\.refresh\(scopeTeamId\)/);
    assert.match(source("task-tags.css"), /body\[data-ui=modern\] \.task-classifications/);
    assert.match(source("task-tags.css"), /overflow-wrap:anywhere/);
});

test("dropdowns start closed, retain multi-selection on close, and reset for the next dialog", async () => {
    const ui = page(); await ui.run("TaskTags.refresh()");
    for (const prefix of ["create", "edit"]) {
        assert.equal(ui.node(`${prefix}-tag-panel`).hidden, true);
        ui.node(`${prefix}-tags-toggle`).listeners.click();
        assert.equal(ui.node(`${prefix}-tag-panel`).hidden, false);
        assert.equal(ui.node(`${prefix}-tags-toggle`).getAttribute("aria-expanded"), "true");
        assert.equal(ui.run("TaskTags.closePickers(true)"), true);
        assert.equal(ui.run("document.activeElement.id"), `${prefix}-tags-toggle`);
        assert.equal(ui.run("TaskTags.closePickers(true)"), false);
    }
    ui.node("tags").value = "プログラマー, アーティスト";
    ui.node("create-tags-toggle").listeners.click();
    ui.node("create-tags-toggle").listeners.click();
    assert.equal(ui.node("tags").value, "プログラマー, アーティスト");
    ui.node("create-tags-toggle").listeners.click();
    ui.run("closeCreateTaskModal();openCreateTaskModal()");
    assert.equal(ui.node("create-tag-panel").hidden, true);
    assert.equal(ui.node("tags").value, "");
});

test("saving blocks dropdown changes and scope switching closes open candidates", async () => {
    const ui = page(); await ui.run("TaskTags.refresh()");
    ui.node("create-tags-toggle").listeners.click();
    ui.run("state.isSaving=true");
    const box = ui.node("create-tag-choices").querySelectorAll("input")[0];
    box.checked = true; box.listeners.change();
    assert.equal(box.checked, false);
    assert.equal(ui.node("tags").value, "");
    ui.run("state.isSaving=false;state.teamId=42;TaskTags.resetScope(42)");
    assert.equal(ui.node("create-tag-panel").hidden, true);
});

test("sidebar dropdown shows full-scope counts and shares the exact filter with settings", async () => {
    const ui = page(async (url) => url.startsWith("/api/tasks?") ? response({items:[],totalCount:0,page:1,pageSize:100,totalPages:0}) : response(catalog));
    await ui.run("TaskTags.refresh()");
    assert.deepEqual(ui.node("sidebar-tag-filter").children.map((option) => option.textContent), ["すべてのタグ（12件）", "未分類（4件）", "プログラマー（7件）", "アーティスト（2件）", "空の分類（0件）", "＋ 新しいタグを追加…"]);
    ui.node("sidebar-tag-filter").value = "tag:プログラマー";
    await ui.node("sidebar-tag-filter").listeners.change();
    assert.equal(ui.run("state.tagExact"), "プログラマー");
    assert.equal(ui.node("classification-chips").children[2].getAttribute("aria-pressed"), "true");
    await ui.run("TaskTags.select('untagged')");
    assert.equal(ui.node("sidebar-tag-filter").value, "untagged");
});

test("add-tag dropdown action navigates without filtering, fetching tasks, or losing selection", async () => {
    const ui=page(); await ui.run("TaskTags.refresh()");
    ui.run("state.tagExact='プログラマー';globalThis.opened=0;function openTagSettings(){opened++;return true}");
    const requests=ui.calls.length;
    ui.node("sidebar-tag-filter").value="action:add-tag";
    await ui.node("sidebar-tag-filter").listeners.change();
    assert.equal(ui.run("opened"),1);
    assert.equal(ui.run("state.tagExact"),"プログラマー");
    assert.equal(ui.node("sidebar-tag-filter").value,"tag:プログラマー");
    assert.equal(ui.calls.length,requests);
    ui.run("openTagSettings=()=>false");
    ui.node("sidebar-tag-filter").value="action:add-tag";
    await ui.node("sidebar-tag-filter").listeners.change();
    assert.equal(ui.node("sidebar-tag-filter").value,"tag:プログラマー");
    assert.match(ui.node("sidebar-tag-message").textContent,/保存や開いている画面/);
});

test("empty verified catalogs explain the first tag, without treating a load failure as empty", async () => {
    const ui=page(()=>response({...catalog,items:[],registeredTags:0}));
    await ui.run("TaskTags.refresh()");
    assert.equal(ui.node("tag-empty-hint").hidden,false);
    ui.run("TaskTags.openCreateForm()");
    assert.equal(ui.node("tag-create-form").hidden,false);
    assert.equal(ui.node("tag-create-form").lastScroll.block,"nearest");
    assert.equal(ui.run("document.activeElement.id"),"tag-name-input");
    ui.run("state.teamId=42;TaskTags.resetScope(42)");
    assert.equal(ui.node("tag-empty-hint").hidden,true);
    const failed=page(()=>response({message:'unavailable'},503));
    await failed.run("TaskTags.refresh()");
    assert.equal(failed.node("tag-empty-hint").hidden,true);
    assert.equal(failed.node("sidebar-tag-filter").children.at(-1).value,"action:add-tag");
});

test("sidebar rejects changes while saving, restores selection and shows a visible reason", async () => {
    const ui = page(); await ui.run("TaskTags.refresh()");
    ui.run("state.isSaving=true");
    ui.node("sidebar-tag-filter").value = "tag:プログラマー";
    await ui.node("sidebar-tag-filter").listeners.change();
    assert.equal(ui.node("sidebar-tag-filter").value, "all");
    assert.equal(ui.run("state.tagExact"), "");
    assert.match(ui.node("sidebar-tag-message").textContent, /保存・更新/);
});

test("sidebar removes private options immediately on scope reset and keeps a clear-filter fallback on failure", async () => {
    const ui = page(); await ui.run("TaskTags.refresh()");
    ui.run("state.teamId=42;TaskTags.resetScope(42)");
    assert.deepEqual(ui.node("sidebar-tag-filter").children.map((option) => option.value), ["all", "untagged", "action:add-tag"]);
    const failed = page(() => response({message:"集計を取得できません"},503));
    failed.run("state.tagExact='既存タグ'"); await failed.run("TaskTags.refresh()");
    assert.equal(failed.node("sidebar-tag-filter").value, "tag:既存タグ");
    assert.equal(failed.node("sidebar-tag-filter").children[0].textContent, "すべてのタグ");
    assert.match(failed.node("sidebar-tag-message").textContent, /取得できません/);
});
