// Dedicated, disposable local PostgreSQL + Mailpit fixture only; never use production.
// APP_URL=http://task-board-progression-web:8080 MAILPIT_URL=http://task-board-progression-mail:8025
// PUBLIC_APP_URL=http://localhost:5098 node tests/pet-collection-smoke.mjs
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
const app = fixtureOrigin("APP_URL", new Set([...localHosts, "task-board-progression-web"]));
const mailpit = fixtureOrigin("MAILPIT_URL", new Set([...localHosts, "task-board-progression-mail"]));
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
        this.userKey = `petcollection_${label}_${fixtureId}`;
        this.email = `${this.userKey}@taskboard.test`;
        this.password = `ProgressionSmoke-${crypto.randomUUID()}`;
        this.displayName = `Progression smoke ${label}`;
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

async function pet(actor, expectedExperience) {
    const response = await actor.request("/api/pet");
    expectStatus(response, 200, "Read pet progression");
    const result = await response.json();
    assert.equal(result.totalExperience, expectedExperience, "Correct reward receiver and total experience");
    assert.equal(result.completedTaskCount, expectedExperience / 25, "Completion count follows active rewards");
    for (const value of [result.totalExperience, result.experience, result.completedTaskCount, result.streakDays, result.energy])
        assert.ok(Number.isSafeInteger(value) && value >= 0, "Progression never becomes negative");
    assert.ok(Number.isSafeInteger(result.level) && result.level >= 1, "Level never drops below one");
    return result;
}
async function catalog(actor, expectedLevel, expectedExperience) {
    const response = await actor.request("/api/user/unlocks");
    expectStatus(response, 200, "Read unlock catalog");
    const result = await response.json();
    assert.equal(result.level, expectedLevel, "Unlock catalog level");
    assert.equal(result.totalExperience, expectedExperience, "Unlock catalog experience");
    for (const key of ["pets", "themes", "layouts"]) assert.ok(Array.isArray(result[key]), "Catalog category exists");
    return result;
}
function optionUnlocked(catalogValue, category, id, expected) {
    const matches = catalogValue[category].filter((option) => option.id === id);
    assert.equal(matches.length, 1, "Catalog contains exactly one requested option");
    assert.equal(matches[0].unlocked, expected, "Server-authoritative unlock state");
    assert.ok(matches[0].experienceRemaining >= 0, "Remaining experience is nonnegative");
}
async function locked(actor, path, body) {
    const response = await actor.mutate(path, body, "PUT");
    expectStatus(response, 403, "Locked option rejected");
    assert.equal((await response.json()).code, "unlock_required", "Locked option error code");
}
async function createTask(actor, path, title, status = "Todo") {
    const response = await actor.mutate(path, { title, status, isCompleted: status === "Done", priority: "Medium" });
    expectStatus(response, 201, "Create fixture task");
    const task = await response.json();
    assert.ok(Number.isSafeInteger(task.id) && task.id > 0, "Fixture task ID exists");
    assert.ok(Number.isSafeInteger(task.version) && task.version > 0, "Real PostgreSQL xmin is returned");
    return task;
}
async function updateTask(actor, path, task, status, title = task.title) {
    const response = await actor.mutate(`${path}/${task.id}`, {
        title, status, isCompleted: status === "Done", priority: task.priority, version: task.version
    }, "PUT");
    expectStatus(response, 200, "Update fixture task");
    const updated = await response.json();
    assert.notEqual(updated.version, task.version, "PostgreSQL advances task xmin");
    return updated;
}

const owner = new Actor("owner"), other = new Actor("other");
const actors = [owner, other];
let stage = "configuration", passed = false, cleanupIncomplete = false;
async function collection(actor = owner) {
    const response = await actor.request("/api/pet/collection");
    expectStatus(response, 200, "Collection GET");
    return response.json();
}
async function claim(level, choice, expected = 200, actor = owner) {
    const response = await actor.mutate("/api/pet/rewards", { level, choice });
    expectStatus(response, expected, "Gift claim");
    return response;
}
async function equip(body, expected = 200, actor = owner) {
    const response = await actor.mutate("/api/pet/appearance", body, "PUT");
    expectStatus(response, expected, "Appearance save");
    return response;
}
try {
    expectStatus(await mailRequest("/api/v1/info"), 200, "Dedicated Mailpit");
    expectStatus(await owner.request("/api/pet/collection"), 401, "Anonymous collection rejected");
    const configResponse = await owner.request("/api/auth/config");
    expectStatus(configResponse, 200, "Auth config");
    const config = await configResponse.json();
    for (const actor of actors) await actor.register(config);
    stage = "first completion without GET";
    let first = await createTask(owner, "/api/tasks", "First without collection GET", "Done");
    first = await updateTask(owner, "/api/tasks", first, "Todo");
    const initial = await collection();
    assert.equal(initial.level, 1);
    assert.equal(initial.rewards.length, 5);
    assert.equal(initial.maxRewardLevel, 5);
    assert.ok(initial.rewards.every(reward => !reward.isLegacy && reward.options.every(option => option.image.startsWith('assets/pet/rewards-v2/dog/'))));
    await claim(6, "hat", 400); await claim(20, "mat", 400);
    assert.ok(initial.memories.find(item => item.key === "first").unlockedAt);
    await claim(1, "hat"); await claim(1, "hat"); await claim(1, "bow", 409);
    await claim(2, "bow", 403); await claim(0, "hat", 400);
    await equip({ stage: "base", hatLevel: 1 }, 403, other);
    expectStatus(await owner.request("/api/pet/interactions", { method: "POST", body: { action: "pet" } }), 400, "Missing CSRF rejected");
    stage = "level five and all equipment slots";
    first = await updateTask(owner, "/api/tasks", first, "Done");
    for (let i=1;i<28;i++) await createTask(owner, "/api/tasks", `Pet milestone ${i}`, "Done");
    assert.equal((await collection()).level, 5);
    await claim(2, "bow"); await claim(3, "mat"); await claim(5, "hat");
    await equip({ stage: "explorer", hatLevel: 5, bowLevel: 2, matLevel: 3 });
    await equip({ stage: "grown", hatLevel: 1 }, 403);
    assert.equal((await collection()).appearance.hatLevel, 5, "Failed save is atomic");
    stage = "concurrent gift choice";
    const concurrent = await Promise.all(["hat","bow","mat"].map(choice => owner.mutate("/api/pet/rewards", { level: 4, choice })));
    assert.deepEqual(concurrent.map(response => response.status).sort(), [200,409,409]);
    stage = "undo and re-earn without farming";
    const beforeUndo = await collection();
    first = await updateTask(owner, "/api/tasks", first, "Doing");
    const dropped = await collection();
    assert.equal(dropped.level, 4);
    assert.deepEqual(dropped.appearance, { stage: "explorer", hatLevel: 5, bowLevel: 2, matLevel: 3 }, "Earned cosmetics survive XP reversal");
    assert.equal(dropped.rewards[4].claimedChoice, "hat");
    assert.equal(dropped.rewards[4].available, true, "Claimed rewards stay usable below their original level");
    assert.equal(dropped.memories.find(item => item.key === "level5").unlockedAt, beforeUndo.memories.find(item => item.key === "level5").unlockedAt);
    first = await updateTask(owner, "/api/tasks", first, "Done");
    await claim(5, "mat", 409);
    await claim(5, "hat");
    assert.equal((await collection()).appearance.stage, "explorer", "Re-earning experience keeps the chosen stage");
    stage = "interaction persistence without XP";
    const before = await pet(owner, 700);
    for (const action of ["pet","treat","rest","pet"]) expectStatus(await owner.mutate("/api/pet/interactions", { action }), 200, "Interaction");
    const after = await pet(owner, 700);
    assert.equal(after.energy, before.energy);
    assert.equal(after.streakDays, before.streakDays);
    assert.equal((await collection()).memories.filter(item => ["pet","treat","rest"].includes(item.key) && item.unlockedAt).length, 3);
    expectStatus(await owner.mutate("/api/pet/interactions", { action: "xp" }), 400, "Invalid interaction");
    assert.equal((await collection(other)).rewards.filter(item => item.claimedChoice).length, 0);
    assert.ok((await collection(other)).memories.every(item => item.unlockedAt === null));
    passed = true;
} catch (error) {
    console.error(`FAIL Pet collection at ${stage}: ${statusFailures.get(error) || "assertion or request failed"} (fixture: ${fixtureId})`);
    process.exitCode = 1;
} finally {
    for (const actor of actors) {
        try { await actor.deleteAccount(); } catch { cleanupIncomplete = true; }
        try {
            const ids = [...new Set((await messagesFor(actor)).map(message => message.ID).filter(id => typeof id === "string" && id))];
            if (ids.length) cleanupIncomplete ||= !(await mailRequest("/api/v1/messages", { method: "DELETE", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ IDs: ids }) })).ok;
        } catch { cleanupIncomplete = true; }
    }
    if (cleanupIncomplete) { console.error("FAIL Pet fixture cleanup requires review"); process.exitCode = 1; }
    else if (passed) console.log(`PASS PostgreSQL pet collection: SMTP onboarding, first-ever memory, atomic equipment, concurrent claims, undo/re-earn, interaction persistence, isolation and exact account cleanup (${appRequestCount} app requests)`);
}
