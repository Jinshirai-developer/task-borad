// Dedicated, disposable local PostgreSQL + Mailpit fixture only; never use production.
// APP_URL=http://task-board-progression-web:8080 MAILPIT_URL=http://task-board-progression-mail:8025
// PUBLIC_APP_URL=http://localhost:5098 node tests/progression-smoke.mjs
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
        this.userKey = `progress_${label}_${fixtureId}`;
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

const owner = new Actor("owner");
const member = new Actor("member");
const actors = [owner, member];
const teamName = `Progression fixture ${fixtureId}`;
let stage = "configuration";
let teamId = null;
let cleanupIncomplete = false;
let checksPassed = false;

try {
    expectStatus(await mailRequest("/api/v1/info"), 200, "Local Mailpit available");
    const configResponse = await owner.request("/api/auth/config");
    expectStatus(configResponse, 200, "Auth configuration");
    const config = await configResponse.json();
    assert.equal(config.registrationEnabled, true, "Dedicated fixture registration is enabled");
    stage = "two SMTP-confirmed accounts";
    for (const actor of actors) await actor.register(config);

    stage = "base choices are available at level one and cannot forge experience";
    const initialCatalog = await catalog(owner, 1, 0);
    for (const category of ["pets", "themes", "layouts"])
        for (const option of initialCatalog[category]) optionUnlocked(initialCatalog, category, option.id, true);
    expectStatus(await owner.mutate("/api/pet", { name: "Initial dragon", species: "dragon", level: 99, totalExperience: 999999 }, "PUT"), 200, "Base pet selection");
    expectStatus(await owner.mutate("/api/user/preferences", { theme: "forest", layout: "compact", level: 99, totalExperience: 999999 }, "PUT"), 200, "Base preferences selection");
    await pet(owner, 0);
    await pet(member, 0);

    stage = "team fixture and a single invitation";
    const creation = await owner.mutate("/api/teams", { name: teamName });
    expectStatus(creation, 201, "Create fixture team");
    const team = await creation.json();
    teamId = team.team.id;
    assert.ok(Number.isSafeInteger(teamId) && teamId > 0, "Fixture team ID exists");
    assert.equal(team.team.ownerUserProfileId, owner.user.id, "Original owner controls fixture cleanup");
    expectStatus(await member.mutate("/api/teams/join", { inviteCode: team.inviteCode }), 200, "Join fixture team");
    const sharedPath = `/api/teams/${teamId}/tasks`;
    let shared = await createTask(owner, sharedPath, "Progression shared fixture");

    stage = "Todo to Doing grants no experience";
    shared = await updateTask(owner, sharedPath, shared, "Doing");
    await pet(owner, 0);
    await pet(member, 0);

    stage = "completion rewards the acting member only";
    shared = await updateTask(member, sharedPath, shared, "Done");
    await pet(member, 25);
    await pet(owner, 0);

    stage = "six concurrent undo requests revoke exactly one reward";
    const undos = await Promise.all(Array.from({ length: 6 }, () => owner.mutate(`${sharedPath}/${shared.id}`, {
        title: shared.title, status: "Doing", isCompleted: false, priority: shared.priority, version: shared.version
    }, "PUT")));
    assert.equal(undos.filter((response) => response.status === 200).length, 1, "Exactly one concurrent undo succeeds");
    assert.equal(undos.filter((response) => response.status === 409).length, 5, "Five stale concurrent undo requests conflict");
    const priorVersion = shared.version;
    shared = await undos.find((response) => response.status === 200).json();
    assert.notEqual(shared.version, priorVersion, "Undo updates PostgreSQL xmin");
    await pet(member, 0);
    await pet(owner, 0);

    stage = "recompletion rewards the new completer and Done to Done is idempotent";
    shared = await updateTask(owner, sharedPath, shared, "Done");
    await pet(owner, 25);
    await pet(member, 0);
    shared = await updateTask(member, sharedPath, shared, "Done", "Same completion edited by another member");
    await pet(owner, 25);
    await pet(member, 0);

    stage = "undo still debits the original recipient after they leave the team";
    shared = await updateTask(owner, sharedPath, shared, "Todo");
    await pet(owner, 0);
    shared = await updateTask(member, sharedPath, shared, "Done");
    await pet(member, 25);
    expectStatus(await member.mutate(`/api/teams/${teamId}/members/me`, undefined, "DELETE"), 204, "Recipient leaves fixture team");
    expectStatus(await member.request(sharedPath), 404, "Departed member loses shared access");
    shared = await updateTask(owner, sharedPath, shared, "Todo");
    await pet(owner, 0);
    await pet(member, 0);
    shared = await updateTask(owner, sharedPath, shared, "Todo");
    shared = await updateTask(owner, sharedPath, shared, "Doing");
    await pet(owner, 0);
    await pet(member, 0);

    stage = "four personal completions reach level two";
    const personal = [];
    for (let index = 0; index < 4; index++)
        personal.push(await createTask(owner, "/api/tasks", `Personal progression fixture ${index}`, "Done"));
    assert.equal((await pet(owner, 100)).level, 2, "Four completions reach level two");
    optionUnlocked(await catalog(owner, 2, 100), "pets", "fox", true);
    const selectedFox = await owner.mutate("/api/pet", { name: "Progression fox", species: "fox" }, "PUT");
    expectStatus(selectedFox, 200, "Unlocked fox can be selected");
    assert.equal((await selectedFox.json()).species, "fox", "Selected pet is a fox");

    stage = "undo drops the level and preserves the chosen pet";
    personal[0] = await updateTask(owner, "/api/tasks", personal[0], "Doing");
    const petAfterUndo = await pet(owner, 75);
    assert.equal(petAfterUndo.level, 1, "Undo recalculates level one");
    assert.equal(petAfterUndo.species, "fox", "The chosen species remains usable after undo");
    assert.equal(petAfterUndo.name, "Progression fox", "Undo preserves pet name");
    optionUnlocked(await catalog(owner, 1, 75), "pets", "fox", true);

    // This branch adds fewer than 20 calls; keep headroom for the 120/min global limit and cleanup.
    assert.ok(appRequestCount < 90, "Dedicated smoke remains within its request budget");
    stage = "ten personal completions reach level three";
    personal[0] = await updateTask(owner, "/api/tasks", personal[0], "Done");
    for (let index = 4; index < 10; index++)
        personal.push(await createTask(owner, "/api/tasks", `Personal progression fixture ${index}`, "Done"));
    const levelThreePet = await pet(owner, 250);
    assert.equal(levelThreePet.level, 3, "Ten completions reach level three");
    assert.equal(levelThreePet.species, "fox", "Level changes do not change the selected species");
    assert.equal(levelThreePet.name, "Progression fox", "Level changes preserve the name");
    const levelThreeCatalog = await catalog(owner, 3, 250);
    optionUnlocked(levelThreeCatalog, "themes", "forest", true);
    optionUnlocked(levelThreeCatalog, "layouts", "compact", true);
    expectStatus(await owner.mutate("/api/user/preferences", { theme: "forest", layout: "compact" }, "PUT"),
        200, "Unlocked forest and compact can be selected");

    stage = "level drop preserves selected preferences";
    personal[0] = await updateTask(owner, "/api/tasks", personal[0], "Todo");
    assert.equal((await pet(owner, 225)).level, 2, "Undo recalculates level two");
    const preferencesResponse = await owner.request("/api/user/preferences");
    expectStatus(preferencesResponse, 200, "Read preferences retained after undo");
    const preferences = await preferencesResponse.json();
    assert.equal(preferences.theme, "forest", "Undo preserves the selected theme");
    assert.equal(preferences.layout, "compact", "Undo preserves the selected layout");
    const levelTwoCatalog = await catalog(owner, 2, 225);
    optionUnlocked(levelTwoCatalog, "pets", "fox", true);
    optionUnlocked(levelTwoCatalog, "themes", "forest", true);
    optionUnlocked(levelTwoCatalog, "layouts", "compact", true);
    await pet(member, 0);
    checksPassed = true;
} catch (error) {
    const diagnostic = statusFailures.get(error) || "assertion or request failed";
    console.error(`FAIL Progression smoke at ${stage}: ${diagnostic} (fixture: ${fixtureId})`);
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
        console.error(`WARN Progression smoke cleanup requires review (fixture: ${fixtureId})`);
        process.exitCode = 1;
    } else if (checksPassed) {
        console.log(`PASS Progression SMTP onboarding, reversible recipient rewards, concurrent xmin undo, level-one choices, preference preservation, and exact cleanup (${appRequestCount} app requests)`);
    }
}
