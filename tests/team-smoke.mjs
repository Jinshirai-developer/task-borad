// Dedicated, disposable local PostgreSQL + Mailpit fixture only; never point at production.
// APP_URL=http://task-board-teams-web:8080 MAILPIT_URL=http://task-board-teams-mail:8025
// PUBLIC_APP_URL=http://localhost:5099 node tests/team-smoke.mjs
import assert from "node:assert/strict";

const localHosts = new Set(["localhost", "127.0.0.1", "[::1]", "host.docker.internal"]);
const allowedHosts = new Set([...localHosts, "task-board-teams-web", "task-board-teams-mail",
    "task-board-identity-web", "task-board-identity-mail", "task-board-progression-web", "task-board-progression-mail"]);
assert.ok(process.env.APP_URL && process.env.MAILPIT_URL && process.env.PUBLIC_APP_URL,
    "Explicit dedicated APP_URL, MAILPIT_URL and PUBLIC_APP_URL are required");
const app = new URL(process.env.APP_URL);
const mailpit = new URL(process.env.MAILPIT_URL);
const publicApp = new URL(process.env.PUBLIC_APP_URL);
for (const url of [app, mailpit, publicApp]) {
    assert.ok(allowedHosts.has(url.hostname) && ["http:", "https:"].includes(url.protocol), "Local fixtures only");
    assert.ok(!url.username && !url.password && url.pathname === "/" && !url.search && !url.hash, "Use a local origin");
}
assert.ok(localHosts.has(publicApp.hostname), "Email links must point at the local browser origin");

// Only explicit, source-controlled labels and numeric HTTP status values may be logged.
const statusFailures = new WeakMap();
function expectStatus(response, expected, label) {
    try { assert.equal(response.status, expected, label); }
    catch (error) {
        statusFailures.set(error, `${label}: HTTP ${response.status}, expected ${expected}`);
        throw error;
    }
}

class Actor {
    constructor(label) {
        this.userKey = `team_${label}_${Date.now().toString(36)}_${crypto.randomUUID().slice(0, 6)}`;
        this.email = `${this.userKey}@taskboard.test`;
        this.password = `TeamSmoke-${crypto.randomUUID()}`;
        this.cookies = new Map();
        this.csrf = null;
        this.user = null;
        this.created = false;
        this.deleted = false;
    }
    async request(path, { method = "GET", body, csrf } = {}) {
        const headers = new Headers({ Accept: "application/json" });
        if (this.cookies.size) headers.set("Cookie", [...this.cookies].map(([key, value]) => `${key}=${value}`).join("; "));
        if (body !== undefined) headers.set("Content-Type", "application/json");
        if (csrf) headers.set("X-CSRF-TOKEN", csrf);
        const response = await fetch(new URL(path, app), {
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
            assert.ok(this.csrf, "CSRF token exists");
        }
        return this.request(path, { method, body, csrf: this.csrf });
    }
    async register(config) {
        const registration = await this.mutate("/api/auth/register", {
            userKey: this.userKey, email: this.email, password: this.password, displayName: `Team smoke ${this.userKey.split("_")[1]}`,
            acceptTerms: true, termsVersion: config.termsVersion, privacyVersion: config.privacyVersion
        });
        expectStatus(registration, 200, "Registration");
        this.created = true;
        this.user = (await registration.json()).user;
        this.csrf = null;
        const confirmation = await waitForConfirmation(this);
        assert.equal(confirmation.userId, this.user.id, "SMTP confirmation matches the fixture account");
        expectStatus(await this.mutate("/api/auth/confirm-email", confirmation), 200, "Email confirmation");
        // Confirmation revokes the restricted setup session. Obtain an anonymous
        // antiforgery token before login instead of reusing the old account token.
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
            const login = await this.mutate("/api/auth/login", { userKey: this.userKey, password: this.password });
            expectStatus(login, 200, "Cleanup fixture login");
            this.csrf = null;
            session = await this.request("/api/auth/session");
        }
        expectStatus(session, 200, "Cleanup session");
        assert.equal((await session.json()).user.userKey, this.userKey, "Cleanup is restricted to the fixture account");
        expectStatus(await this.mutate("/api/user", undefined, "DELETE"), 204, "Delete fixture account");
        this.csrf = null;
        this.deleted = true;
    }
}

async function mailRequest(path, options = {}) {
    return fetch(new URL(path, mailpit), { ...options, redirect: "error", signal: AbortSignal.timeout(5_000) });
}

async function messagesFor(actor) {
    const query = new URLSearchParams({ query: `to:${actor.email}`, limit: "30" });
    const response = await mailRequest(`/api/v1/search?${query}`);
    assert.equal(response.status, 200, "Mailpit search");
    return (await response.json()).messages.filter((message) =>
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
            assert.equal(response.status, 200, "Read SMTP message");
            const message = await response.json();
            for (const candidate of message.Text.match(/https?:\/\/[^\s<>"']+/g) || []) {
                const link = new URL(candidate);
                if (link.pathname !== "/auth.html" || link.searchParams.get("mode") !== "confirm") continue;
                assert.equal(link.origin, publicApp.origin, "Trusted email origin");
                assert.ok(!link.searchParams.has("token"), "Token stays out of URL queries");
                const secret = new URLSearchParams(link.hash.slice(1));
                const userId = Number(secret.get("userId"));
                const token = secret.get("token");
                assert.ok(Number.isSafeInteger(userId) && userId > 0 && /^[A-Za-z0-9_-]{20,4096}$/.test(token || ""), "Valid confirmation token shape");
                return { userId, token };
            }
        }
        await new Promise((resolve) => setTimeout(resolve, 750));
    }
    throw new Error("SMTP confirmation delivery timed out");
}

const owner = new Actor("owner");
const member = new Actor("member");
const outsider = new Actor("outsider");
const actors = [owner, member, outsider];
let stage = "configuration";
let teamId = null;
let currentOwner = owner;
let cleanupIncomplete = false;

try {
    const configResponse = await owner.request("/api/auth/config");
    assert.equal(configResponse.status, 200, "Auth configuration");
    const config = await configResponse.json();
    assert.equal(config.registrationEnabled, true, "Fixture registration is enabled");
    stage = "three SMTP-confirmed accounts";
    for (const actor of actors) await actor.register(config);

    stage = "team creation and explicit invitation";
    const creation = await owner.mutate("/api/teams", { name: `Smoke ${owner.userKey}` });
    assert.equal(creation.status, 201, "Create team");
    const team = await creation.json();
    teamId = team.team.id;
    assert.ok(Number.isSafeInteger(teamId) && teamId > 0, "Team has an ID");
    assert.ok(!JSON.stringify(team).includes("inviteCodeHash"), "Invite hash is never exposed");
    assert.equal((await member.mutate("/api/teams/join", { inviteCode: team.inviteCode })).status, 200, "Join team");
    const path = `/api/teams/${teamId}/tasks`;

    stage = "private and shared scope isolation";
    const privateCreation = await owner.mutate("/api/tasks", { title: "Owner private fixture", teamId });
    assert.equal(privateCreation.status, 201, "Create personal task");
    const personal = await privateCreation.json();
    assert.equal(personal.teamId, null, "Body team ID cannot change route scope");
    const sharedCreation = await owner.mutate(path, { title: "Shared fixture", status: "Todo", priority: "High" });
    assert.equal(sharedCreation.status, 201, "Create shared task");
    const shared = await sharedCreation.json();
    assert.ok(Number.isSafeInteger(shared.version) && shared.version > 0, "PostgreSQL xmin is returned");
    assert.equal((await member.request(`/api/tasks/${personal.id}`)).status, 404, "Private task stays private");
    assert.equal((await owner.request(`/api/tasks/${shared.id}`)).status, 404, "Shared task cannot use private route");
    assert.equal((await member.request(`${path}/${personal.id}`)).status, 404, "Private task cannot use team route");
    assert.equal((await outsider.request(path)).status, 404, "Outsider cannot list tasks");
    assert.equal((await outsider.request(`/api/teams/${teamId}/summary`)).status, 404, "Outsider cannot see summary");
    assert.equal((await outsider.mutate(`${path}/${shared.id}`, { title: "Forbidden", version: shared.version }, "PUT")).status, 404, "Outsider cannot update");
    assert.equal((await member.mutate(`${path}/${shared.id}`, { title: "Missing version" }, "PUT")).status, 409, "Shared update requires version");
    assert.equal((await member.mutate(`${path}/${shared.id}`, undefined, "DELETE")).status, 409, "Shared deletion requires version");
    const personalPage = await (await owner.request(`/api/tasks?teamId=${teamId}`)).json();
    assert.deepEqual(personalPage.items.map((task) => task.id), [personal.id], "Query cannot silently change scope");

    stage = "concurrent completion and one-time pet reward";
    const completions = await Promise.all(Array.from({ length: 6 }, (_, index) => member.mutate(`${path}/${shared.id}`, {
        title: `Concurrent done ${index}`, status: "Done", isCompleted: true, priority: "High", version: shared.version
    }, "PUT")));
    assert.equal(completions.filter((response) => response.status === 200).length, 1, "Exactly one update accepts the original version");
    assert.equal(completions.filter((response) => response.status === 409).length, 5, "All competing stale updates are rejected");
    const completed = await completions.find((response) => response.status === 200).json();
    assert.notEqual(completed.version, shared.version, "PostgreSQL increments task version");
    const petResponse = await member.request("/api/pet");
    assert.equal(petResponse.status, 200, "Read completer pet");
    const pet = await petResponse.json();
    assert.equal(pet.totalExperience, 25, "Completer receives exactly one reward");
    assert.equal(pet.completedTaskCount, 1, "Exactly one completion is counted");
    const ownerPet = await (await owner.request("/api/pet")).json();
    assert.equal(ownerPet.totalExperience, 0, "Creator is not also rewarded");

    stage = "stale writes and team-wide summary";
    assert.equal((await owner.mutate(`${path}/${shared.id}`, { title: "Stale title", version: shared.version }, "PUT")).status, 409, "Old update is rejected");
    assert.equal((await owner.mutate(`${path}/${shared.id}?version=${shared.version}`, undefined, "DELETE")).status, 409, "Old deletion is rejected");
    const summary = await (await member.request(`/api/teams/${teamId}/summary`)).json();
    assert.deepEqual(summary, { total: 1, todo: 0, doing: 0, done: 1, overdue: 0 }, "Summary counts the full team only");
    assert.equal((await owner.request(`/api/tasks/${personal.id}`)).status, 200, "Personal task survives shared updates");

    stage = "ownership protections and transfer";
    assert.equal((await owner.mutate(`/api/teams/${teamId}/members/me`, undefined, "DELETE")).status, 409, "Owner cannot leave directly");
    assert.equal((await owner.mutate("/api/user", undefined, "DELETE")).status, 409, "Owner cannot delete account directly");
    assert.equal((await member.mutate(`/api/teams/${teamId}`, undefined, "DELETE")).status, 403, "Member cannot delete team");
    assert.equal((await owner.mutate(`/api/teams/${teamId}/owner`, { userProfileId: outsider.user.id }, "PUT")).status, 400, "Transfer target must be a member");
    assert.equal((await owner.mutate(`/api/teams/${teamId}/owner`, { userProfileId: member.user.id }, "PUT")).status, 200, "Transfer ownership");
    currentOwner = member;
    const newOwner = await (await member.request(`/api/teams/${teamId}`)).json();
    assert.equal(newOwner.role, "owner", "New owner role is visible");

    stage = "leave revokes access and creator deletion preserves team data";
    assert.equal((await owner.mutate(`/api/teams/${teamId}/members/me`, undefined, "DELETE")).status, 204, "Former owner leaves");
    assert.equal((await owner.request(path)).status, 404, "Leaver loses access");
    assert.equal((await owner.mutate(`${path}/${shared.id}`, { title: "Leaver update", version: completed.version }, "PUT")).status, 404, "Leaver cannot update own formerly shared task");
    await owner.deleteAccount();
    assert.equal((await owner.request("/api/auth/session")).status, 401, "Deleted creator session is rejected");
    const retainedResponse = await member.request(`${path}/${shared.id}`);
    assert.equal(retainedResponse.status, 200, "Shared task survives creator account deletion");
    const retained = await retainedResponse.json();
    assert.equal(retained.createdByUserProfileId, null, "Deleted creator is anonymized");
    assert.equal(retained.createdByDisplayName, "退会したユーザー", "Deleted creator label");
    assert.equal(retained.status, "Done", "Shared task status remains intact");
    assert.equal((await member.mutate(`${path}/${shared.id}?version=${completed.version}`, undefined, "DELETE")).status, 409, "Creator anonymization also changes xmin");
    assert.equal((await member.mutate(`${path}/${shared.id}?version=${retained.version}`, undefined, "DELETE")).status, 204, "Current version can be deleted");

    stage = "fixture team deletion";
    assert.equal((await member.mutate(`/api/teams/${teamId}`, undefined, "DELETE")).status, 204, "Delete empty fixture team");
    teamId = null;
    console.log("PASS Teams SMTP onboarding, private/shared isolation, concurrent xmin updates, one-time reward, owner transfer, leave and creator deletion");
} catch (error) {
    // Never print tokens, cookie headers, passwords, invite codes, or response bodies.
    let diagnostic = statusFailures.get(error) || "assertion or request failed";
    if (!statusFailures.has(error) && error?.code === "ERR_ASSERTION"
        && Number.isInteger(error.actual) && error.actual >= 100 && error.actual <= 599
        && Number.isInteger(error.expected) && error.expected >= 100 && error.expected <= 599)
        diagnostic = `HTTP ${error.actual}, expected ${error.expected}`;
    console.error(`FAIL Teams smoke at ${stage}: ${diagnostic} (fixture: ${owner.userKey})`);
    process.exitCode = 1;
} finally {
    if (teamId !== null) {
        try {
            const existing = await currentOwner.request(`/api/teams/${teamId}`);
            if (existing.status === 200 && (await existing.json()).ownerUserProfileId === currentOwner.user?.id) {
                const removal = await currentOwner.mutate(`/api/teams/${teamId}`, undefined, "DELETE");
                cleanupIncomplete ||= removal.status !== 204;
            } else cleanupIncomplete = true;
        } catch { cleanupIncomplete = true; }
    }
    for (const actor of actors) {
        try { await actor.deleteAccount(); } catch { cleanupIncomplete = true; }
        try {
            const ids = [...new Set((await messagesFor(actor)).map((message) => message.ID).filter((id) => typeof id === "string" && id))];
            // Mailpit interprets empty IDs as delete-all; only delete these exact fixture messages.
            if (ids.length) {
                const deletion = await mailRequest("/api/v1/messages", {
                    method: "DELETE", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ IDs: ids })
                });
                cleanupIncomplete ||= !deletion.ok;
            }
        } catch { cleanupIncomplete = true; }
    }
    if (cleanupIncomplete) {
        console.error(`WARN Teams smoke cleanup requires review (fixture: ${owner.userKey})`);
        process.exitCode = 1;
    }
}
