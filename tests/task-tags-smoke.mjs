// Dedicated, disposable local PostgreSQL + Mailpit fixture only; never use production.
// APP_URL=http://task-board-tags-web:8080 MAILPIT_URL=http://task-board-tags-mail:8025
// PUBLIC_APP_URL=http://localhost:5098 node tests/task-tags-smoke.mjs
// Requires Node 22. Onboarding + one join use seven auth-rate-limited requests.
import assert from "node:assert/strict";

const localHosts = new Set(["localhost", "127.0.0.1", "[::1]", "host.docker.internal"]);
function fixtureOrigin(name, hosts) {
    assert.ok(process.env[name], `Explicit dedicated ${name} is required`);
    let url;
    try { url = new URL(process.env[name]); }
    catch { throw new Error(`Invalid local ${name} origin`); }
    assert.ok(hosts.has(url.hostname) && ["http:", "https:"].includes(url.protocol), "Local fixtures only");
    assert.ok(!url.username && !url.password && url.pathname === "/" && !url.search && !url.hash,
        "Use a local origin without credentials, paths, queries or fragments");
    return url;
}
const app = fixtureOrigin("APP_URL", new Set([...localHosts, "task-board-tags-web"]));
const mailpit = fixtureOrigin("MAILPIT_URL", new Set([...localHosts, "task-board-tags-mail"]));
const publicApp = fixtureOrigin("PUBLIC_APP_URL", localHosts);
assert.notEqual(app.origin, mailpit.origin, "Application and mailbox must be distinct fixture origins");

// Do not log errors, response bodies, cookies, passwords, mail tokens or invitation codes.
const statusFailures = new WeakMap();
function expectStatus(response, expected, label) {
    try { assert.equal(response.status, expected, label); }
    catch (error) {
        statusFailures.set(error, `${label}: HTTP ${response.status}, expected ${expected}`);
        throw error;
    }
}

const fixtureId = `${Date.now().toString(36)}_${crypto.randomUUID().slice(0, 8)}`;
let appRequestCount = 0;
class Actor {
    constructor(label) {
        this.userKey = `tags_${label}_${fixtureId}`;
        this.email = `${this.userKey}@taskboard.test`;
        this.password = `TaskTagsSmoke-${crypto.randomUUID()}`;
        this.displayName = `Task tags smoke ${label}`;
        this.cookies = new Map();
        this.csrf = null;
        this.user = null;
        this.created = false;
        this.deleted = false;
    }
    async request(path, { method = "GET", body, csrf } = {}) {
        const url = new URL(path, app);
        assert.equal(url.origin, app.origin, "Requests remain on the fixture origin");
        const headers = new Headers({ Accept: "application/json" });
        if (this.cookies.size) headers.set("Cookie", [...this.cookies].map(([key, value]) => `${key}=${value}`).join("; "));
        if (body !== undefined) headers.set("Content-Type", "application/json");
        if (csrf) headers.set("X-CSRF-TOKEN", csrf);
        appRequestCount++;
        const response = await fetch(url, {
            method, headers, body: body === undefined ? undefined : JSON.stringify(body),
            redirect: "error", signal: AbortSignal.timeout(20_000)
        });
        for (const header of response.headers.getSetCookie()) {
            const [pair, ...attributes] = header.split(";");
            const separator = pair.indexOf("=");
            if (separator < 1) continue;
            const key = pair.slice(0, separator).trim();
            const value = pair.slice(separator + 1);
            const expires = attributes.find((attribute) => /^\s*expires=/i.test(attribute));
            if (!value || (expires && Date.parse(expires.slice(expires.indexOf("=") + 1)) <= Date.now())
                || attributes.some((attribute) => /^\s*max-age=0\s*$/i.test(attribute))) this.cookies.delete(key);
            else this.cookies.set(key, value);
        }
        return response;
    }
    async mutate(path, body, method = "POST") {
        if (!this.csrf) {
            const response = await this.request("/api/auth/csrf");
            expectStatus(response, 200, "CSRF bootstrap");
            this.csrf = (await response.json()).token;
            assert.ok(typeof this.csrf === "string" && this.csrf, "CSRF token exists");
        }
        return this.request(path, { method, body, csrf: this.csrf });
    }
    async register(config) {
        const registration = await this.mutate("/api/auth/register", {
            userKey: this.userKey, email: this.email, password: this.password, displayName: this.displayName,
            acceptTerms: true, termsVersion: config.termsVersion, privacyVersion: config.privacyVersion
        });
        expectStatus(registration, 200, "Registration");
        this.created = true;
        this.user = (await registration.json()).user;
        assert.ok(Number.isSafeInteger(this.user?.id) && this.user.id > 0, "Fixture account has an ID");
        this.csrf = null;
        const confirmation = await waitForConfirmation(this);
        assert.equal(confirmation.userId, this.user.id, "SMTP confirmation matches the fixture account");
        expectStatus(await this.mutate("/api/auth/confirm-email", confirmation), 200, "Email confirmation");
        this.csrf = null;
        const login = await this.mutate("/api/auth/login", { userKey: this.userKey, password: this.password });
        expectStatus(login, 200, "Confirmed login");
        assert.equal((await login.json()).nextAction, "ready", "Ready account");
        this.csrf = null;
    }
    async deleteAccount() {
        if (!this.created || this.deleted) return;
        let session = await this.request("/api/auth/session");
        if (session.status === 401) {
            this.csrf = null;
            expectStatus(await this.mutate("/api/auth/login", { userKey: this.userKey, password: this.password }),
                200, "Cleanup fixture login");
            this.csrf = null;
            session = await this.request("/api/auth/session");
        }
        expectStatus(session, 200, "Cleanup session");
        const current = (await session.json()).user;
        assert.equal(current?.userKey, this.userKey, "Cleanup is restricted to the exact fixture account");
        if (this.user) assert.equal(current.id, this.user.id, "Cleanup account ID is unchanged");
        expectStatus(await this.mutate("/api/user", undefined, "DELETE"), 204, "Delete fixture account");
        this.deleted = true;
        this.csrf = null;
    }
}

async function mailRequest(path, options = {}) {
    const url = new URL(path, mailpit);
    assert.equal(url.origin, mailpit.origin, "Mail requests remain on the fixture origin");
    return fetch(url, { ...options, redirect: "error", signal: AbortSignal.timeout(5_000) });
}
async function messagesFor(actor) {
    const query = new URLSearchParams({ query: `to:${actor.email}`, limit: "30" });
    const response = await mailRequest(`/api/v1/search?${query}`);
    expectStatus(response, 200, "Mailpit search");
    const data = await response.json();
    assert.ok(Array.isArray(data.messages), "Mailpit messages schema");
    return data.messages.filter((message) =>
        message.To?.some((recipient) => recipient.Address?.toLowerCase() === actor.email));
}
async function waitForConfirmation(actor) {
    const deadline = Date.now() + 40_000;
    const visited = new Set();
    while (Date.now() < deadline) {
        for (const summary of await messagesFor(actor)) {
            if (visited.has(summary.ID)) continue;
            visited.add(summary.ID);
            const response = await mailRequest(`/api/v1/message/${encodeURIComponent(summary.ID)}`);
            expectStatus(response, 200, "Read SMTP message");
            const message = await response.json();
            assert.ok(typeof message.Text === "string", "Mailpit plain-text message is present");
            for (const candidate of message.Text.match(/https?:\/\/[^\s<>"']+/g) || []) {
                const link = new URL(candidate);
                if (link.pathname !== "/auth.html" || link.searchParams.get("mode") !== "confirm") continue;
                assert.equal(link.origin, publicApp.origin, "Trusted email origin");
                assert.ok(!link.searchParams.has("token"), "Token stays out of URL queries");
                const secret = new URLSearchParams(link.hash.slice(1));
                const userId = Number(secret.get("userId"));
                const token = secret.get("token");
                assert.ok(Number.isSafeInteger(userId) && userId > 0
                    && /^[A-Za-z0-9_-]{20,4096}$/.test(token || ""), "Valid confirmation token shape");
                return { userId, token };
            }
        }
        await new Promise((resolve) => setTimeout(resolve, 750));
    }
    throw new Error("SMTP confirmation delivery timed out");
}


async function read(actor, path) {
    const response = await actor.request(path);
    expectStatus(response, 200, "Read fixture endpoint");
    return response.json();
}
async function createTask(actor, path, title, tags, status = "Todo") {
    const response = await actor.mutate(path, { title, tags, status, isCompleted: status === "Done", priority: "Medium" });
    expectStatus(response, 201, "Create tagged fixture task");
    const task = await response.json();
    assert.ok(Number.isSafeInteger(task.version) && task.version > 0, "PostgreSQL xmin is present");
    return task;
}
function tag(result, name) {
    const matches = result.items.filter(item => item.name.toUpperCase() === name.toUpperCase());
    assert.equal(matches.length, 1, "Each classification is counted once");
    return matches[0];
}
function counts(actual, expected) {
    assert.deepEqual([actual.total, actual.todo, actual.doing, actual.done], expected, "Full workspace tag counts");
}

const owner = new Actor("owner");
const member = new Actor("member");
const actors = [owner, member];
const teamName = `Task tags fixture ${fixtureId}`;
let stage = "configuration";
let teamId = null;
let cleanupIncomplete = false;
let checksPassed = false;

try {
    expectStatus(await mailRequest("/api/v1/info"), 200, "Local Mailpit available");
    expectStatus(await owner.request("/api/task-tags"), 401, "Anonymous tag catalog rejected");
    const config = await read(owner, "/api/auth/config");
    stage = "two SMTP-confirmed accounts";
    for (const actor of actors) await actor.register(config);

    stage = "empty classification persistence and request validation";
    expectStatus(await owner.mutate("/api/task-tags", { name: "レビュー待ち" }), 201, "Register empty classification");
    const empty = await read(owner, "/api/task-tags");
    counts(tag(empty, "レビュー待ち"), [0, 0, 0, 0]);
    assert.equal(empty.registeredTags, 1, "Empty definition persists");
    assert.equal(empty.totalTasks, 0);
    assert.equal(empty.maxRegisteredTags, 50);
    expectStatus(await owner.mutate("/api/task-tags", { name: "API" }), 201, "Register case-normalized classification");
    expectStatus(await owner.mutate("/api/task-tags", { name: " api " }), 409, "Case-insensitive duplicate rejected");
    for (const name of [" ", "one,two", "x".repeat(51), "line\nbreak"])
        expectStatus(await owner.mutate("/api/task-tags", { name }), 400, "Invalid classification rejected");
    expectStatus(await owner.request("/api/task-tags", { method: "POST", body: { name: "No CSRF" } }), 400, "Missing CSRF rejected");

    stage = "multiple task tags and complete, unfiltered counts";
    const programmer = "プログラマー";
    const artist = "アーティスト";
    const planner = "プランナー";
    await createTask(owner, "/api/tasks", "Programming", programmer);
    await createTask(owner, "/api/tasks", "Cross-discipline", programmer + ", " + artist, "Doing");
    let done = await createTask(owner, "/api/tasks", "Planning", planner, "Done");
    await createTask(owner, "/api/tasks", "Different complete token", programmer + "補助");
    await createTask(owner, "/api/tasks", "Unclassified", null);
    await createTask(owner, "/api/tasks", "Duplicate token", "api, API");
    const all = await read(owner, "/api/task-tags");
    assert.equal(all.totalTasks, 6);
    assert.equal(all.untaggedTasks, 1);
    counts(tag(all, programmer), [2, 1, 1, 0]);
    counts(tag(all, artist), [1, 0, 1, 0]);
    counts(tag(all, planner), [1, 0, 0, 1]);
    counts(tag(all, "API"), [1, 1, 0, 0]);
    counts(tag(all, "レビュー待ち"), [0, 0, 0, 0]);

    stage = "exact classification filtering before pagination";
    const exact = await read(owner, "/api/tasks?" + new URLSearchParams({ tagExact: programmer, page: "1", pageSize: "1" }));
    assert.equal(exact.totalCount, 2);
    assert.equal(exact.totalPages, 2);
    assert.equal(exact.items.length, 1);
    const next = await read(owner, "/api/tasks?" + new URLSearchParams({ tagExact: programmer, page: "2", pageSize: "1" }));
    assert.equal(next.items.length, 1);
    assert.notEqual(next.items[0].id, exact.items[0].id);
    assert.equal((await read(owner, "/api/tasks?" + new URLSearchParams({ tag: programmer }))).totalCount, 3, "Legacy substring filter preserved");
    assert.equal((await read(owner, "/api/tasks?tagExact=aPi")).totalCount, 1);
    assert.equal((await read(owner, "/api/tasks?untagged=true")).totalCount, 1);
    expectStatus(await owner.request("/api/tasks?tagExact=API&untagged=true"), 400, "Conflicting classification filters rejected");
    expectStatus(await owner.request("/api/tasks?tagExact="), 400, "Empty exact classification rejected");
    expectStatus(await owner.request("/api/tasks?tagExact=&untagged=true"), 400, "Empty classification still conflicts with untagged");
    assert.equal((await read(owner, "/api/tasks?" + new URLSearchParams({ tagExact: programmer, status: "Doing" }))).totalCount, 1);
    assert.equal((await read(owner, "/api/task-tags")).totalTasks, 6, "Counts independent of task filter/page");

    stage = "private catalogs remain isolated";
    const other = await read(member, "/api/task-tags");
    assert.equal(other.items.length, 0);
    assert.equal(other.totalTasks, 0);
    expectStatus(await member.mutate("/api/task-tags", { name: "個人メモ" }), 201, "Other private classification");
    assert.ok(!(await read(owner, "/api/task-tags")).items.some(item => item.name === "個人メモ"));

    stage = "team scope and membership validation";
    const creation = await owner.mutate("/api/teams", { name: teamName });
    expectStatus(creation, 201, "Create fixture team");
    const team = await creation.json();
    teamId = team.team.id;
    const sharedTags = `/api/teams/${teamId}/task-tags`;
    const sharedTasks = `/api/teams/${teamId}/tasks`;
    expectStatus(await member.request(sharedTags), 404, "Nonmember catalog hidden");
    expectStatus(await member.mutate(sharedTags, { name: "Unauthorized" }), 404, "Nonmember cannot add a tag");
    expectStatus(await member.mutate("/api/teams/join", { inviteCode: team.inviteCode }), 200, "Join fixture team");
    expectStatus(await member.mutate(sharedTags, { name: artist }), 201, "Members share classification definitions");
    await createTask(owner, sharedTasks, "Shared programmer", programmer);
    await createTask(member, sharedTasks, "Shared art planning", artist + ", " + planner, "Done");
    const shared = await read(owner, sharedTags);
    assert.equal(shared.totalTasks, 2);
    counts(tag(shared, programmer), [1, 1, 0, 0]);
    counts(tag(shared, artist), [1, 0, 0, 1]);
    counts(tag(shared, planner), [1, 0, 0, 1]);
    assert.ok(!shared.items.some(item => item.name === "レビュー待ち"));
    assert.equal((await read(member, sharedTasks + "?" + new URLSearchParams({ tagExact: artist }))).totalCount, 1);

    stage = "parallel classification creation has exactly one winner";
    const parallel = await Promise.all(Array.from({ length: 6 }, () => owner.mutate(sharedTags, { name: "QA" })));
    assert.equal(parallel.filter(result => result.status === 201).length, 1);
    assert.equal(parallel.filter(result => result.status === 409).length, 5);
    counts(tag(await read(member, sharedTags), "QA"), [0, 0, 0, 0]);

    stage = "retro preferences persist independently of rewards and tags";
    expectStatus(await owner.mutate("/api/user/preferences", { theme: "retro", layout: "board" }, "PUT"), 200, "Windows theme selectable at level one");
    assert.equal((await read(owner, "/api/user/preferences")).theme, "retro");
    assert.equal((await read(owner, "/api/user/unlocks")).themes.find(item => item.id === "retro").unlocked, true);
    const undo = await owner.mutate("/api/tasks/" + done.id, { ...done, status: "Todo", isCompleted: false }, "PUT");
    expectStatus(undo, 200, "Completion can still be undone with classification preserved");
    counts(tag(await read(owner, "/api/task-tags"), planner), [1, 1, 0, 0]);
    assert.equal((await read(owner, "/api/pet")).totalExperience, 0);
    assert.equal((await read(member, "/api/pet")).totalExperience, 25);
    assert.equal((await read(owner, "/api/user/preferences")).theme, "retro");

    stage = "leaving removes shared catalog access without leaking other private tags";
    expectStatus(await member.mutate(`/api/teams/${teamId}/members/me`, undefined, "DELETE"), 204, "Leave fixture team");
    expectStatus(await member.request(sharedTags), 404, "Former member catalog hidden");
    assert.ok(!(await read(member, "/api/task-tags")).items.some(item => item.name === artist));
    assert.ok(appRequestCount < 105, "Keep cleanup headroom below per-IP rate limit");
    checksPassed = true;
} catch (error) {
    const diagnostic = statusFailures.get(error) || "assertion or request failed";
    console.error(`FAIL Task tags smoke at ${stage}: ${diagnostic} (fixture: ${fixtureId})`);
    process.exitCode = 1;
} finally {
    if (teamId !== null) {
        try {
            const existing = await owner.request(`/api/teams/${teamId}`);
            expectStatus(existing, 200, "Read exact cleanup team");
            const team = await existing.json();
            assert.equal(team.id, teamId, "Cleanup team ID is unchanged");
            assert.equal(team.name, teamName, "Cleanup targets only the generated fixture team");
            assert.equal(team.ownerUserProfileId, owner.user?.id, "Only the original owner deletes the fixture team");
            expectStatus(await owner.mutate(`/api/teams/${teamId}`, undefined, "DELETE"), 204, "Delete fixture team and shared task");
            teamId = null;
        } catch { cleanupIncomplete = true; }
    }
    for (const actor of actors) {
        try { await actor.deleteAccount(); } catch { cleanupIncomplete = true; }
        try {
            const ids = [...new Set((await messagesFor(actor)).map((message) => message.ID)
                .filter((id) => typeof id === "string" && id))];
            // Empty IDs means delete-all in Mailpit; only remove exact fixture message IDs.
            if (ids.length) {
                const deletion = await mailRequest("/api/v1/messages", {
                    method: "DELETE", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ IDs: ids })
                });
                cleanupIncomplete ||= !deletion.ok;
            }
        } catch { cleanupIncomplete = true; }
    }
    if (cleanupIncomplete) {
        console.error(`WARN Task tags smoke cleanup requires review (fixture: ${fixtureId})`);
        process.exitCode = 1;
    } else if (checksPassed) {
        console.log(`PASS Task tags SMTP onboarding, multi-tag grouping, exact filtering, workspace isolation, duplicate creation, retro preferences, and exact cleanup (${appRequestCount} app requests)`);
    }
}
