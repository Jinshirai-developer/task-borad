// Dedicated local PostgreSQL + SMTP/Mailpit smoke test; never point this at production.
// Node 22: APP_URL=http://localhost:5097 MAILPIT_URL=http://localhost:8027 node tests/identity-smoke.mjs
// Mailpit API: https://mailpit.axllent.org/docs/api-v1/
// Schemas: https://raw.githubusercontent.com/axllent/mailpit/master/server/ui/api/v1/swagger.json
import assert from "node:assert/strict";

const localHosts = new Set(["localhost", "127.0.0.1", "[::1]", "host.docker.internal"]);
const testNetworkHosts = new Set([...localHosts, "task-board-identity-web", "task-board-identity-mail", "task-board-teams-web", "task-board-teams-mail", "task-board-progression-web", "task-board-progression-mail"]);
const app = new URL(process.env.APP_URL || "http://localhost:5097");
const mailpit = new URL(process.env.MAILPIT_URL || "http://localhost:8027");
const publicApp = new URL(process.env.PUBLIC_APP_URL || "http://localhost:5097");
for (const url of [app, mailpit, publicApp]) {
    assert.ok(testNetworkHosts.has(url.hostname) && ["http:", "https:"].includes(url.protocol), "Smoke test only supports local endpoints");
    assert.ok(!url.username && !url.password && url.pathname === "/" && !url.search && !url.hash, "Use a local origin, without credentials or paths");
}
assert.ok(localHosts.has(publicApp.hostname), "Public mail links must target the local browser origin");

class CookieJar {
    constructor() { this.values = new Map(); }
    store(response) {
        for (const header of response.headers.getSetCookie()) {
            const [pair, ...attributes] = header.split(";");
            const separator = pair.indexOf("=");
            if (separator < 1) continue;
            const name = pair.slice(0, separator).trim();
            const value = pair.slice(separator + 1);
            const expires = attributes.find((attribute) => /^\s*expires=/i.test(attribute));
            const expired = expires && Date.parse(expires.slice(expires.indexOf("=") + 1)) <= Date.now();
            if (!value || expired || attributes.some((attribute) => /^\s*max-age=0\s*$/i.test(attribute))) this.values.delete(name);
            else this.values.set(name, value);
        }
    }
    header() { return [...this.values].map(([name, value]) => `${name}=${value}`).join("; "); }
}

const jar = new CookieJar();
let stage = "initialization";
let created = false;
let deleted = false;
let confirmed = false;
let cleanupIncomplete = false;
const userKey = `smoke_${Date.now().toString(36)}_${crypto.randomUUID().slice(0, 8)}`;
const email = `${userKey}@taskboard.test`;
let password = `Smoke-${crypto.randomUUID()}`;
const nextPassword = `Reset-${crypto.randomUUID()}`;

async function appRequest(path, { method = "GET", body, csrf } = {}) {
    const headers = new Headers({ Accept: "application/json" });
    if (jar.values.size) headers.set("Cookie", jar.header());
    if (body !== undefined) headers.set("Content-Type", "application/json");
    if (csrf) headers.set("X-CSRF-TOKEN", csrf);
    const response = await fetch(new URL(path, app), {
        method, headers, body: body === undefined ? undefined : JSON.stringify(body),
        redirect: "error", signal: AbortSignal.timeout(10_000)
    });
    jar.store(response);
    return response;
}

async function mutate(path, body, method = "POST") {
    const csrfResponse = await appRequest("/api/auth/csrf");
    assert.equal(csrfResponse.status, 200, "CSRF bootstrap status");
    const { token } = await csrfResponse.json();
    assert.ok(typeof token === "string" && token.length > 0, "CSRF bootstrap token is present");
    return appRequest(path, { method, body, csrf: token });
}

async function mailRequest(path, options = {}) {
    return fetch(new URL(path, mailpit), { ...options, redirect: "error", signal: AbortSignal.timeout(5_000) });
}

async function findMessages() {
    const query = new URLSearchParams({ query: `to:${email}`, limit: "20" });
    const response = await mailRequest(`/api/v1/search?${query}`);
    assert.equal(response.status, 200, "Mailpit search status");
    const data = await response.json();
    assert.ok(Array.isArray(data.messages), "Mailpit messages schema");
    return data.messages.filter((message) => message.To?.some((recipient) => recipient.Address?.toLowerCase() === email));
}

async function waitForMailLink(mode) {
    const deadline = Date.now() + 30_000;
    const visited = new Set();
    while (Date.now() < deadline) {
        for (const summary of await findMessages()) {
            if (visited.has(summary.ID)) continue;
            visited.add(summary.ID);
            const response = await mailRequest(`/api/v1/message/${encodeURIComponent(summary.ID)}`);
            assert.equal(response.status, 200, "Mailpit message status");
            const message = await response.json();
            assert.ok(typeof message.Text === "string", "Mailpit plain-text body is present");
            for (const candidate of message.Text.match(/https?:\/\/[^\s<>"']+/g) || []) {
                const link = new URL(candidate);
                if (link.pathname !== "/auth.html" || link.searchParams.get("mode") !== mode) continue;
                // SMTP mail uses the browser origin even when Node calls the private Docker service.
                assert.ok(link.origin === publicApp.origin, "Mail link targets the configured local browser origin");
                assert.ok(!link.searchParams.has("token"), "Secret is not in the URL query");
                const fragment = new URLSearchParams(link.hash.slice(1));
                const userId = Number(fragment.get("userId"));
                const token = fragment.get("token");
                assert.ok(Number.isSafeInteger(userId) && userId > 0, "Mail link has a user ID");
                assert.ok(typeof token === "string" && /^[A-Za-z0-9_-]{20,4096}$/.test(token), "Mail link has a base64url fragment token");
                return { userId, token };
            }
        }
        await new Promise((resolve) => setTimeout(resolve, 1000));
    }
    throw new Error("Local SMTP delivery timed out");
}

async function cleanupAccount() {
    if (!created || deleted) return;
    let response = await appRequest("/api/auth/session");
    if (response.status === 401) {
        const login = await mutate("/api/auth/login", { userKey, password });
        if (!login.ok) { cleanupIncomplete = true; return; }
        response = await appRequest("/api/auth/session");
    }
    if (!response.ok || (await response.json()).user?.userKey !== userKey) {
        cleanupIncomplete = true;
        return;
    }
    const deletion = await mutate("/api/user", undefined, "DELETE");
    deleted = deletion.status === 204;
    cleanupIncomplete ||= !deleted;
}

try {
    const info = await mailRequest("/api/v1/info");
    assert.equal(info.status, 200, "Local Mailpit is available");
    const configResponse = await appRequest("/api/auth/config");
    assert.equal(configResponse.status, 200, "Auth configuration status");
    const config = await configResponse.json();
    assert.equal(config.registrationEnabled, true, "Registration is enabled for this dedicated test");

    stage = "registration";
    const registration = await mutate("/api/auth/register", {
        userKey, displayName: "Identity smoke", email, password, acceptTerms: true,
        termsVersion: config.termsVersion, privacyVersion: config.privacyVersion
    });
    assert.equal(registration.status, 200, "Registration status");
    created = true;
    const registered = await registration.json();
    assert.equal(registered.nextAction, "confirmEmail", "Registration grants only a setup session");
    const blockedTasks = await appRequest("/api/tasks");
    assert.equal(blockedTasks.status, 403, "Unconfirmed account cannot access tasks");
    assert.equal((await blockedTasks.json()).code, "account_setup_required", "Setup error code");

    stage = "confirmation SMTP delivery";
    const confirmation = await waitForMailLink("confirm");
    assert.equal(confirmation.userId, registered.user.id, "Confirmation mail belongs to the test account");
    stage = "email confirmation";
    assert.equal((await mutate("/api/auth/confirm-email", confirmation)).status, 200, "Email confirmation status");
    confirmed = true;

    stage = "confirmed login";
    const login = await mutate("/api/auth/login", { userKey, password });
    assert.equal(login.status, 200, "Confirmed login status");
    assert.equal((await login.json()).nextAction, "ready", "Confirmed account can use the app");
    const originalSession = jar.header();

    stage = "task creation";
    const creation = await mutate("/api/tasks", { title: "Identity smoke task", status: "Todo", priority: "Medium", isCompleted: false });
    assert.equal(creation.status, 201, "Task creation status");
    const task = await creation.json();
    assert.ok(Number.isSafeInteger(task.id), "Created task has an ID");

    stage = "password reset SMTP delivery";
    assert.equal((await mutate("/api/auth/forgot-password", { email })).status, 202, "Password recovery request status");
    const reset = await waitForMailLink("reset");
    assert.equal(reset.userId, registered.user.id, "Reset mail belongs to the test account");
    stage = "password reset";
    assert.equal((await mutate("/api/auth/reset-password", { ...reset, password: nextPassword })).status, 200, "Password reset status");
    password = nextPassword;
    const oldSession = await fetch(new URL("/api/user", app), { headers: { Cookie: originalSession }, redirect: "error", signal: AbortSignal.timeout(10_000) });
    assert.equal(oldSession.status, 401, "Password reset revokes an existing session");

    stage = "new password login and preserved task";
    const newLogin = await mutate("/api/auth/login", { userKey, password });
    assert.equal(newLogin.status, 200, "New password login status");
    assert.equal((await newLogin.json()).nextAction, "ready", "New password account remains ready");
    const taskResponse = await appRequest(`/api/tasks/${task.id}`);
    assert.equal(taskResponse.status, 200, "Task remains after password reset");
    assert.equal((await taskResponse.json()).title, "Identity smoke task", "Task contents are preserved");

    stage = "account deletion";
    assert.equal((await mutate("/api/user", undefined, "DELETE")).status, 204, "Account deletion status");
    deleted = true;
    assert.equal((await appRequest("/api/user")).status, 401, "Deleted account session is rejected");
    console.log(`PASS Identity registration, SMTP confirmation, recovery, session revocation, preserved task, and deletion (${userKey})`);
} catch (error) {
    // Do not print errors/responses: they can contain passwords, cookies or mail tokens.
    const status = typeof error?.actual === "number" && typeof error?.expected === "number"
        ? `; actual=${error.actual}; expected=${error.expected}` : "";
    console.error(`FAIL Identity smoke at ${stage} (test user: ${userKey}; confirmed: ${confirmed}${status})`);
    process.exitCode = 1;
} finally {
    try { await cleanupAccount(); } catch { cleanupIncomplete = true; }
    try {
        const ids = [...new Set((await findMessages()).map((message) => message.ID).filter((id) => typeof id === "string" && id))];
        // Empty IDs means "delete all" in Mailpit. Never issue a deletion without exact test IDs.
        if (ids.length) {
            const cleanup = await mailRequest("/api/v1/messages", { method: "DELETE", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ IDs: ids }) });
            if (!cleanup.ok) cleanupIncomplete = true;
        }
    } catch { cleanupIncomplete = true; }
    if (cleanupIncomplete) {
        console.error(`WARN Test cleanup needs review (${userKey})`);
        process.exitCode = 1;
    }
}
