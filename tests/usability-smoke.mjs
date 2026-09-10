// Disposable v15 PostgreSQL/Mailpit environment only. Never production or the user preview.
import assert from "node:assert/strict";
const app = new URL(process.env.APP_URL || "http://task-v15-qa-web:8080");
const mail = new URL(process.env.MAILPIT_URL || "http://task-v15-qa-mail:8025");
assert.equal(app.hostname, "task-v15-qa-web"); assert.equal(mail.hostname, "task-v15-qa-mail");
let checks = 0, requests = 0;
function status(response, expected, label) { assert.equal(response.status, expected, `${label}: ${response.status}`); checks++; return response; }
class Actor {
    constructor(label) {
        this.key = `v15_${label}_${crypto.randomUUID().slice(0, 8)}`; this.email = `${this.key}@taskboard.test`;
        this.password = `V15-test-${crypto.randomUUID()}`; this.cookies = new Map(); this.csrf = null;
    }
    async request(path, method = "GET", body, skipCsrf = false) {
        if (method !== "GET" && !skipCsrf && !this.csrf) this.csrf = (await (await this.request("/api/auth/csrf")).json()).token;
        const headers = { Accept: "application/json", Cookie: [...this.cookies].map(([key, value]) => `${key}=${value}`).join("; ") };
        if (this.csrf && !skipCsrf) headers["X-CSRF-TOKEN"] = this.csrf;
        if (body !== undefined) headers["Content-Type"] = "application/json";
        requests++;
        const response = await fetch(new URL(path, app), { method, headers, body: body === undefined ? undefined : JSON.stringify(body), redirect: "error", signal: AbortSignal.timeout(15000) });
        for (const cookie of response.headers.getSetCookie()) {
            const pair = cookie.split(";")[0], equal = pair.indexOf("=");
            if (equal > 0) { const key = pair.slice(0, equal), value = pair.slice(equal + 1); if (value) this.cookies.set(key, value); else this.cookies.delete(key); }
        }
        return response;
    }
    async register(config) {
        const registration = status(await this.request("/api/auth/register", "POST", { userKey: this.key, displayName: this.key, email: this.email, password: this.password, acceptTerms: true, termsVersion: config.termsVersion, privacyVersion: config.privacyVersion }), 200, "register");
        this.user = (await registration.json()).user; this.csrf = null;
        let secret;
        for (let i = 0; i < 80 && !secret; i++) {
            const list = await (await fetch(new URL(`/api/v1/search?query=${encodeURIComponent(`to:${this.email}`)}`, mail))).json();
            for (const summary of list.messages || []) {
                const message = await (await fetch(new URL(`/api/v1/message/${summary.ID}`, mail))).json();
                for (const candidate of message.Text.match(/https?:\/\/[^\s<>"']+/g) || []) {
                    const link = new URL(candidate); assert.equal(link.origin, "http://localhost:5100");
                    if (link.searchParams.get("mode") === "confirm") { const params = new URLSearchParams(link.hash.slice(1)); secret = { userId: Number(params.get("userId")), token: params.get("token") }; }
                }
            }
            if (!secret) await new Promise(resolve => setTimeout(resolve, 500));
        }
        assert.ok(secret, "local SMTP confirmation delivered");
        status(await this.request("/api/auth/confirm-email", "POST", secret), 200, "confirm"); this.csrf = null;
        status(await this.request("/api/auth/login", "POST", { userKey: this.key, password: this.password }), 200, "login"); this.csrf = null;
    }
    async json(path) { return (status(await this.request(path), 200, "get")).json(); }
}
const owner = new Actor("owner"), member = new Actor("member"), outsider = new Actor("outside");
let teamId = null;
try {
    let ready = false;
    for (let i = 0; i < 40 && !ready; i++) {
        try { ready = (await fetch(new URL("/health/ready", app), { signal: AbortSignal.timeout(1000) })).ok; } catch { }
        if (!ready) await new Promise(resolve => setTimeout(resolve, 500));
    }
    assert.ok(ready, "isolated app ready");
    const config = await owner.json("/api/auth/config");
    for (const actor of [owner, member, outsider]) await actor.register(config);
    const team = await (status(await owner.request("/api/teams", "POST", { name: "Usability QA" }), 201, "team")).json(); teamId = team.team.id;
    status(await member.request("/api/teams/join", "POST", { inviteCode: team.inviteCode }), 200, "join");
    const route = `/api/teams/${teamId}/tasks`, tags = `/api/teams/${teamId}/task-tags`;
    const task = await (status(await owner.request(route, "POST", { title: "Assigned", assigneeUserProfileId: member.user.id, tags: "art, review", checklist: [{ text: "API", isCompleted: true }] }), 201, "assigned task")).json();
    assert.equal(task.assigneeUserProfileId, member.user.id); assert.equal(task.checklist[0].isCompleted, true);
    status(await outsider.request(`${route}/${task.id}`), 404, "outsider read denied");
    status(await owner.request(`${route}/${task.id}`, "PUT", { ...task, assigneeUserProfileId: outsider.user.id }), 400, "outsider assignment denied");
    status(await owner.request(`${route}/${task.id}`, "PUT", { ...task, checklist: [{ text: "" }] }), 400, "invalid checklist");
    status(await owner.request(`${route}/${task.id}`, "PUT", { ...task, checklist: Array.from({ length: 21 }, () => ({ text: "too many" })) }), 400, "checklist limit");
    status(await owner.request(`${route}/${task.id}`, "DELETE", undefined, true), 400, "delete CSRF required");
    let current = await owner.json(`${route}/${task.id}`);
    assert.equal(current.assigneeUserProfileId, member.user.id); assert.equal(current.version, task.version);
    const moved = await (status(await owner.request(`${route}/${task.id}`, "PUT", { ...current, status: "Done", isCompleted: true }), 200, "complete")).json();
    assert.equal((await member.json("/api/pet")).totalExperience, 25);
    status(await member.request(`${route}/undo/${moved.undo.token}`, "POST"), 409, "other actor undo denied");
    status(await owner.request(`/api/tasks/undo/${moved.undo.token}`, "POST"), 409, "other scope undo denied");
    const undoResults = await Promise.all(Array.from({ length: 4 }, () => owner.request(`${route}/undo/${moved.undo.token}`, "POST")));
    assert.equal(undoResults.filter(r => r.status === 200).length, 1); assert.equal(undoResults.filter(r => r.status === 409).length, 3); checks += 2;
    assert.equal((await owner.json("/api/pet")).totalExperience, 0);
    current = await owner.json(`${route}/${task.id}`);
    const done = await (status(await owner.request(`${route}/${task.id}`, "PUT", { ...current, status: "Done", isCompleted: true }), 200, "complete again")).json();
    const opened = await (status(await member.request(`${route}/${task.id}`, "PUT", { ...done, status: "Todo", isCompleted: false }), 200, "member reopens")).json();
    const restored = await (status(await member.request(`${route}/undo/${opened.undo.token}`, "POST"), 200, "member undo")).json();
    assert.equal((await member.json("/api/pet")).totalExperience, 25); assert.equal((await owner.json("/api/pet")).totalExperience, 0);
    const deleted = status(await member.request(`${route}/${task.id}?version=${restored.version}`, "DELETE"), 204, "delete completed");
    const token = deleted.headers.get("X-Task-Undo"); assert.ok(token);
    const restoredDelete = await (status(await member.request(`${route}/undo/${token}`, "POST"), 200, "restore deleted")).json();
    assert.equal(restoredDelete.id, task.id); assert.equal(restoredDelete.checklist[0].text, "API");
    assert.equal((await member.json("/api/pet")).totalExperience, 25);
    const review = await member.json("/api/pet/weekly-review"); assert.equal(review.thisWeek, 1); assert.equal(review.days.length, 7);
    const reportAfter = await owner.json("/api/pet/weekly-review"); assert.equal(reportAfter.thisWeek, 0);
    const reopen = await (status(await owner.request(`${route}/${task.id}`, "PUT", { ...restoredDelete, status: "Todo", isCompleted: false }), 200, "reopen after restore")).json();
    assert.equal((await owner.json("/api/pet")).totalExperience, 0);
    status(await member.request(`${route}/${task.id}`, "PUT", { ...reopen, title: "Updated by teammate" }), 200, "concurrent content edit");
    status(await owner.request(`${route}/undo/${reopen.undo.token}`, "POST"), 409, "cannot undo over newer edit");
    status(await owner.request(tags, "POST", { name: "art" }), 201, "register art");
    status(await owner.request(tags, "POST", { name: "review" }), 201, "register review");
    status(await owner.request(`${tags}/manage`, "POST", { name: "art", action: "rename", targetName: "design" }), 200, "rename");
    current = await owner.json(`${route}/${task.id}`); assert.equal(current.tags, "design, review");
    status(await owner.request(`${tags}/manage`, "POST", { name: "design", action: "merge", targetName: "review" }), 200, "merge");
    status(await outsider.request(`${tags}/manage`, "POST", { name: "review", action: "delete" }), 404, "outsider tag edit denied");
    status(await owner.request(`${tags}/manage`, "POST", { name: "review", action: "delete" }), 200, "remove tag");
    assert.equal((await owner.json(`${route}/${task.id}`)).tags, null);
    status(await member.request(`/api/teams/${teamId}/members/me`, "DELETE"), 204, "leave");
    assert.equal((await owner.json(`${route}/${task.id}`)).assigneeUserProfileId, null);
    status(await member.request(`${route}/${task.id}`), 404, "former member denied");
    console.log(JSON.stringify({ passed: checks, apiRequests: requests, realPostgres: true }));
} finally {
    if (teamId) status(await owner.request(`/api/teams/${teamId}`, "DELETE"), 204, "delete test team");
    for (const actor of [owner, member, outsider]) if (actor.user) status(await actor.request("/api/user", "DELETE"), 204, "delete test account");
}
