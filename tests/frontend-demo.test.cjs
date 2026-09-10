const assert = require("node:assert/strict");
const { test } = require("node:test");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");
const { webcrypto } = require("node:crypto");
const source = name => fs.readFileSync(path.join(__dirname, "../frontend", name), "utf8");

function client(demo = true, loadDemo = true) {
    let calls = 0;
    const window = { location: new URL("https://task.test/index.html" + (demo ? "?demo=1" : "")) };
    const context = vm.createContext({ window, document: { addEventListener() {} }, URL, URLSearchParams, Headers, Response,
        crypto: webcrypto, fetch: async () => { calls++; return new Response("{}"); } });
    vm.runInContext('const UNLOCK_OPTIONS = { pets: [{ id:"cat", name:"ねこ", requiredLevel:1 }], themes:[], layouts:[] };', context);
    if (loadDemo) vm.runInContext(source("demo.js"), context);
    vm.runInContext(source("auth-client.js"), context);
    const request = (url, method = "GET", body) => window.TaskAuth.request(url, { method, ...(body ? { body: JSON.stringify(body) } : {}) });
    return { request, calls: () => calls, context };
}

test("demo never accesses real APIs, including logout and account deletion", async () => {
    const c = client();
    for (const url of ["/api/user", "/api/pet", "/api/tasks", "/api/teams", "/api/pet/collection", "/api/user/unlocks", "/api/pet/weekly-review"]) assert.equal((await c.request(url)).status, 200, url);
    assert.equal((await c.request("/api/auth/logout", "POST")).status, 204);
    assert.equal((await c.request("/api/user", "DELETE")).status, 204);
    assert.equal((await c.request("/api/unknown", "POST")).status, 404);
    assert.equal(c.calls(), 0);
});
test("missing demo script fails closed instead of using a real session", async () => {
    const c = client(true, false);
    await assert.rejects(c.request("/api/user"), /お試しモード/); assert.equal(c.calls(), 0);
});
test("normal app does not enable demo", async () => {
    const c = client(false); await c.request("/api/user"); assert.equal(c.calls(), 1);
});
test("demo data is isolated between page instances", async () => {
    const a = client(), b = client();
    await a.request("/api/tasks", "POST", { title: "Only A", status: "Todo" });
    const list = await (await b.request("/api/tasks")).json(); assert.equal(list.totalCount, 3); assert.equal(a.calls() + b.calls(), 0);
});
test("demo move undo reverses XP; deleting/restoring completed task grants nothing extra", async () => {
    const c = client(), before = await (await c.request("/api/pet")).json();
    const task = await (await c.request("/api/tasks/1")).json();
    const moved = await (await c.request("/api/tasks/1", "PUT", { ...task, status: "Done", isCompleted: true })).json();
    assert.equal((await (await c.request("/api/pet")).json()).totalExperience, before.totalExperience + 25);
    await c.request(`/api/tasks/undo/${moved.undo.token}`, "POST");
    assert.equal((await (await c.request("/api/pet")).json()).totalExperience, before.totalExperience);
    assert.equal((await c.request(`/api/tasks/undo/${moved.undo.token}`, "POST")).status, 409);
    const done = await (await c.request("/api/tasks/3")).json();
    const deletion = await c.request(`/api/tasks/3?version=${done.version}`, "DELETE");
    await c.request(`/api/tasks/undo/${deletion.headers.get("X-Task-Undo")}`, "POST");
    assert.equal((await (await c.request("/api/pet")).json()).totalExperience, before.totalExperience);
});
test("demo undo refuses another scope and edits made after the original move", async () => {
    const c = client(), task = await (await c.request("/api/teams/1/tasks/4")).json();
    const moved = await (await c.request("/api/teams/1/tasks/4", "PUT", { ...task, status: "Doing" })).json();
    assert.equal((await c.request(`/api/tasks/undo/${moved.undo.token}`, "POST")).status, 409);
    await c.request("/api/teams/1/tasks/4", "PUT", { ...moved, title: "New edit" });
    assert.equal((await c.request(`/api/teams/1/tasks/undo/${moved.undo.token}`, "POST")).status, 409);
});
test("demo assignment and checklist validate membership and preserve fields on moves", async () => {
    const c = client();
    assert.equal((await c.request("/api/tasks", "POST", { title: "No", assigneeUserProfileId: 2 })).status, 400);
    const task = await (await c.request("/api/teams/1/tasks", "POST", { title: "Review", assigneeUserProfileId: 2, checklist: [{ text: "Check", isCompleted: true }] })).json();
    const moved = await (await c.request(`/api/teams/1/tasks/${task.id}`, "PUT", { ...task, status: "Doing" })).json();
    assert.equal(moved.assigneeUserProfileId, 2); assert.equal(moved.checklist[0].isCompleted, true);
});
test("demo tags rename merge and delete only their own scope", async () => {
    const c = client();
    await c.request("/api/teams/1/task-tags/manage", "POST", { name: "プログラマー", action: "rename", targetName: "開発" });
    assert.match((await (await c.request("/api/teams/1/tasks/4")).json()).tags, /開発/);
    assert.match((await (await c.request("/api/tasks/2")).json()).tags, /プログラマー/);
    await c.request("/api/teams/1/task-tags/manage", "POST", { name: "開発", action: "merge", targetName: "プランナー" });
    assert.equal((await (await c.request("/api/teams/1/tasks/4")).json()).tags, "プランナー");
    await c.request("/api/teams/1/task-tags/manage", "POST", { name: "プランナー", action: "delete" });
    assert.equal((await (await c.request("/api/teams/1/tasks/4")).json()).tags, "");
});
test("demo rewards remain one choice per level across species changes", async () => {
    const c = client(); await c.request("/api/pet/rewards", "POST", { level: 1, choice: "hat" });
    await c.request("/api/pet", "PUT", { name: "Pochi", species: "dog" });
    assert.equal((await c.request("/api/pet/rewards", "POST", { level: 1, choice: "bow" })).status, 409);
    const collection = await (await c.request("/api/pet/collection")).json();
    assert.equal(collection.rewards[0].claimedChoice, "hat"); assert.match(collection.rewards[0].options[0].image, /\/dog\//);
});
test("extra features stay in task details/settings; demo and undo are explicit", () => {
    const html = source("index.html");
    for (const id of ["create-checklist", "edit-checklist", "create-assignee", "edit-assignee", "tag-manage-form", "weekly-review-details", "task-undo-toast"]) assert.equal(html.split(`id="${id}"`).length - 1, 1);
    assert.match(source("login.html"), /index\.html\?demo=1/);
    assert.match(source("task-details.js"), /document\.hidden/);
    assert.match(source("task-details.js"), /state\.taskLoadVersion/);
});

test("demo task search combines assignee and JST deadlines before paging and keeps undated tasks last", async () => {
    const c = client(), root = "/api/teams/1/tasks";
    const today = new Date(Date.now() + 9 * 3600000).toISOString().slice(0,10) + "T00:00:00Z";
    const yesterday = new Date(Date.parse(today) - 86400000).toISOString();
    const create = async body => (await c.request(root, "POST", { title:"Workflow", description:"needle", tags:"review", ...body })).json();
    const late = await create({ dueDate:yesterday, assigneeUserProfileId:1 });
    const due = await create({ dueDate:today, assigneeUserProfileId:1, status:"Doing" });
    const noDate = await create({ dueDate:null, assigneeUserProfileId:1 });
    await create({ dueDate:yesterday, assigneeUserProfileId:2 });
    await create({ dueDate:yesterday, assigneeUserProfileId:1, status:"Done" });
    const list = async query => (await c.request(root + "?search=needle&" + query)).json();
    const filtered = await list("assignee=me&due=through_today&sortOrder=due&pageSize=1");
    assert.equal(filtered.totalCount,2); assert.equal(filtered.items[0].id,late.id); assert.equal(filtered.totalPages,2);
    assert.deepEqual(filtered.statusCounts,{todo:1,doing:1,done:0});
    assert.equal((await list("assignee=me&due=through_today&sortOrder=due&pageSize=1&page=2")).items[0].id,due.id);
    assert.equal((await list("assignee=me&sortOrder=due")).items.at(-1).id,noDate.id);
    assert.equal((await list("assignee=me&due=today")).totalCount,1);
    assert.equal((await list("assignee=me&due=overdue")).totalCount,1);
    assert.equal((await c.request(root+"?assignee=99999")).status,400);
    assert.equal((await c.request(root+"?due=unknown")).status,400);
    assert.equal(c.calls(),0);
});
