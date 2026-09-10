const assert = require("node:assert/strict");
const { test } = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const frontend = path.join(__dirname, "..", "frontend");
const source = (name) => fs.readFileSync(path.join(frontend, name), "utf8");
const jsonResponse = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

test("all shipped JavaScript files have valid syntax", () => {
    for (const name of fs.readdirSync(frontend).filter((name) => !name.startsWith(".") && name.endsWith(".js"))) {
        assert.doesNotThrow(() => new vm.Script(source(name), { filename: name }), name);
    }
});

function client(fetchImpl, href = "https://task.example/login.html") {
    const removed = [];
    const window = { location: new URL(href) };
    for (const storage of ["localStorage", "sessionStorage"]) window[storage] = { removeItem: (key) => removed.push([storage, key]) };
    const context = vm.createContext({ window, URL, Headers, Response, TypeError, fetch: fetchImpl });
    vm.runInContext(source("auth-client.js"), context);
    return { auth: window.TaskAuth, removed };
}

test("legacy auth values are removed without writing browser storage", () => {
    const { removed } = client(() => assert.fail("initialization must not fetch"));
    assert.equal(removed.length, 4);
    assert.ok(removed.every(([, key]) => ["taskBoardAuthToken", "taskBoardUserKey"].includes(key)));
    for (const name of ["auth-client.js", "login.js", "auth.js", "app.js"]) assert.doesNotMatch(source(name), /(?:localStorage|sessionStorage)\.setItem/);
});

test("GET uses same-origin cookies, never legacy Authorization, and no CSRF call", async () => {
    const calls = [];
    const { auth } = client(async (url, options) => { calls.push({ url, options }); return jsonResponse({}); });
    await auth.request("/api/user", { headers: { Authorization: "Bearer old" }, credentials: "include", redirect: "follow" });
    assert.equal(calls.length, 1);
    assert.equal(calls[0].options.credentials, "same-origin");
    assert.equal(calls[0].options.cache, "no-store");
    assert.equal(calls[0].options.redirect, "error");
    assert.equal(calls[0].options.headers.get("Authorization"), null);
    assert.equal(calls[0].options.headers.get("X-CSRF-TOKEN"), null);
});

test("cross-origin and file requests are rejected before fetching", async () => {
    const { auth } = client(() => assert.fail("external API must not be called"));
    await assert.rejects(auth.request("https://attacker.example/api/user"), /同じURL/);
    await assert.rejects(auth.request("//attacker.example/api/user"), /同じURL/);
    await assert.rejects(auth.request("file:///tmp/user"), /同じURL/);
});

test("unsafe requests get CSRF protection including login and DELETE", async () => {
    const calls = [];
    const { auth } = client(async (url, options) => {
        calls.push({ url, options });
        return jsonResponse(url.endsWith("/csrf") ? { token: "csrf-value" } : {});
    });
    await auth.post("/api/auth/login", { userKey: "alice", password: "password-value" });
    await auth.request("/api/tasks/3", { method: "DELETE" });
    assert.equal(calls.filter((call) => call.url.endsWith("/csrf")).length, 2, "successful login invalidates anonymous CSRF token");
    assert.equal(calls[1].options.headers.get("X-CSRF-TOKEN"), "csrf-value");
    assert.equal(calls[3].options.headers.get("X-CSRF-TOKEN"), "csrf-value");
    assert.equal(JSON.parse(calls[1].options.body).userKey, "alice");
});

test("concurrent mutations share one CSRF request", async () => {
    const calls = [];
    let resolveToken;
    const tokenResponse = new Promise((resolve) => { resolveToken = resolve; });
    const { auth } = client(async (url) => {
        calls.push(url);
        return url.endsWith("/csrf") ? tokenResponse : jsonResponse({});
    });
    const first = auth.post("/api/tasks", { title: "one" });
    const second = auth.post("/api/tasks", { title: "two" });
    resolveToken(jsonResponse({ token: "shared-csrf" }));
    await Promise.all([first, second]);
    assert.equal(calls.filter((url) => url.endsWith("/csrf")).length, 1);
    assert.equal(calls.filter((url) => url.endsWith("/tasks")).length, 2);
});

test("failed mutations are not automatically retried; a manual retry refreshes CSRF", async () => {
    const calls = [];
    const { auth } = client(async (url) => {
        calls.push(url);
        return url.endsWith("/csrf") ? jsonResponse({ token: "fresh" }) : jsonResponse({ message: "expired" }, 403);
    });
    const response = await auth.post("/api/tasks", {});
    assert.equal(response.status, 403);
    assert.equal(calls.length, 2);
    await auth.post("/api/tasks", {});
    assert.equal(calls.length, 4);
});

test("missing or failed CSRF bootstrap never sends the mutation", async () => {
    for (const response of [jsonResponse({}), jsonResponse({ message: "unavailable" }, 503)]) {
        let calls = 0;
        const { auth } = client(async () => { calls++; return response; });
        await assert.rejects(auth.post("/api/auth/register", {}));
        assert.equal(calls, 1);
    }
});

test("API validation messages and non-JSON failures are readable", async () => {
    const { auth } = client(() => assert.fail("no fetch"));
    assert.equal(await auth.errorMessage(jsonResponse({ errors: { Email: ["メールを確認してください"] } })), "メールを確認してください");
    assert.equal(await auth.errorMessage(new Response("unavailable", { status: 503 }), "再試行"), "再試行");
});

function fakePage(name, href, taskAuth) {
    const elements = new Map();
    const handlers = new Map();
    const navigations = [];
    const replaced = [];
    const getElement = (id) => {
        if (!elements.has(id)) elements.set(id, {
            id, value: "", textContent: "", checked: false, hidden: false, disabled: false, customValidity: "",
            listeners: {}, classList: { toggle() {} }, setAttribute() {}, focus() {},
            addEventListener(type, handler) { this.listeners[type] = handler; },
            setCustomValidity(value) { this.customValidity = value; },
            reportValidity() { return !this.customValidity; }, querySelectorAll() { return []; }
        });
        return elements.get(id);
    };
    const location = new URL(href);
    location.assign = (value) => navigations.push(value);
    location.replace = (value) => navigations.push(value);
    const document = {
        getElementById: getElement, querySelectorAll: () => [], title: "",
        addEventListener: (event, handler) => handlers.set(event, handler)
    };
    const context = vm.createContext({ document, window: { location, history: { replaceState: (...args) => replaced.push(args[2]) } }, URL, URLSearchParams, TaskAuth: taskAuth, confirm: () => true });
    vm.runInContext(source(name), context);
    return { elements, getElement, handlers, navigations, replaced };
}

test("registration entry opens the real registration form without bypassing server admission", async () => {
    for (const enabled of [true, false]) {
        const page = fakePage("login.js", "https://task.example/login.html?mode=register", {
            request: async (url) => url.endsWith("/config")
                ? jsonResponse({ registrationEnabled: enabled, googleLoginEnabled: false })
                : jsonResponse({}, 401),
            errorMessage: async () => "error", displayError: (error) => error.message
        });
        await page.handlers.get("DOMContentLoaded")();
        assert.equal(page.getElement("auth-email").required, enabled);
        assert.equal(page.getElement("auth-email").disabled, !enabled);
        assert.equal(page.getElement("auth-password").minLength, enabled ? 12 : 8);
        assert.equal(page.getElement("auth-submit-button").textContent, enabled ? "確認メールを送って登録" : "ログインする");
        assert.deepEqual(page.navigations, []);
    }
});

test("Google login navigates only after POST and rejects unexpected destinations", async () => {
    let destination = "https://accounts.google.com/o/oauth2/v2/auth?state=protected";
    const posts = [];
    const auth = {
        request: async (url) => url.endsWith("config") ? jsonResponse({ googleLoginEnabled: true }) : jsonResponse({}, 401),
        post: async (url) => { posts.push(url); return jsonResponse({ url: destination }); },
        displayError: (error) => error.message
    };
    const page = fakePage("login.js", "https://task.example/login.html", auth);
    await page.handlers.get("DOMContentLoaded")();
    assert.equal(page.getElement("google-login").hidden, false);
    destination = "https://attacker.example/auth";
    await page.getElement("google-start").listeners.click();
    assert.equal(page.navigations.length, 0);
    assert.equal(page.getElement("google-start").disabled, false);
    destination = "https://accounts.google.com/o/oauth2/v2/auth?state=protected";
    await page.getElement("google-start").listeners.click();
    assert.equal(page.navigations[0], destination);
    assert.deepEqual(posts, ["/api/auth/google/start", "/api/auth/google/start"]);
});

test("disabled Google login stays hidden and makes no auth request", async () => {
    const page = fakePage("login.js", "https://task.example/login.html", {
        request: async (url) => url.endsWith("config") ? jsonResponse({ googleLoginEnabled: false }) : jsonResponse({}, 401),
        post: () => assert.fail("disabled provider must not start")
    });
    await page.handlers.get("DOMContentLoaded")();
    assert.equal(page.getElement("google-login").hidden, true);
    await page.getElement("google-start").listeners.click();
});

test("Google registration uses current consent and never publishes the provider real name automatically", async () => {
    const calls = [];
    const page = fakePage("google.js", "https://task.example/google.html", {
        request: async (url) => jsonResponse(url.endsWith("config") ? { termsVersion: "current-terms", privacyVersion: "current-privacy" }
            : { email: "someone@gmail.com", displayName: "Private Real Name", canRegister: true }),
        post: async (url, body) => { calls.push({ url, body }); return jsonResponse({ nextAction: "ready" }); },
        displayError: (error) => error.message
    });
    await page.handlers.get("DOMContentLoaded")();
    assert.equal(page.getElement("google-name").value, "");
    assert.equal(page.getElement("google-password").disabled, true);
    page.getElement("google-user-key").value = "MyNickname";
    page.getElement("google-terms").checked = true;
    await page.getElement("google-form").listeners.submit({ preventDefault() {} });
    assert.equal(calls[0].url, "/api/auth/google/register");
    assert.equal(calls[0].body.userKey, "mynickname");
    assert.equal(calls[0].body.privacyVersion, "current-privacy");
    assert.equal(calls[0].body.password, undefined);
    assert.deepEqual(page.navigations, ["index.html"]);
});

test("Google email collision shows existing-account proof and clears password after failed link", async () => {
    const page = fakePage("google.js", "https://task.example/google.html", {
        request: async (url) => jsonResponse(url.endsWith("config") ? {} : { email: "existing@gmail.com", canRegister: false }),
        post: async (url, body) => { assert.equal(url, "/api/auth/google/link"); assert.equal(body.password, "wrong-password"); return jsonResponse({}, 401); },
        errorMessage: async () => "パスワードを確認してください。", displayError: (error) => error.message
    });
    await page.handlers.get("DOMContentLoaded")();
    assert.equal(page.getElement("google-switch").hidden, true);
    assert.equal(page.getElement("google-password").required, true);
    assert.equal(page.getElement("google-name").disabled, true);
    page.getElement("google-user-key").value = "existing";
    page.getElement("google-password").value = "wrong-password";
    await page.getElement("google-form").listeners.submit({ preventDefault() {} });
    assert.equal(page.getElement("google-password").value, "");
    assert.match(page.getElement("google-message").textContent, /パスワード/);
    assert.equal(page.navigations.length, 0);
});

test("passwordless Google account can accept terms without a hidden required password", async () => {
    const calls = [];
    const page = fakePage("auth.js", "https://task.example/auth.html", {
        request: async (url) => jsonResponse(url.endsWith("config") ? { termsVersion: "terms", privacyVersion: "privacy" }
            : { email: "someone@gmail.com", emailConfirmed: true, hasPassword: false, nextAction: "acceptTerms" }),
        post: async (url, body) => { calls.push({ url, body }); return jsonResponse({ nextAction: "ready" }); },
        displayError: (error) => error.message
    });
    await page.handlers.get("DOMContentLoaded")();
    assert.equal(page.getElement("setup-password-field").hidden, true);
    assert.equal(page.getElement("setup-password").required, false);
    page.getElement("setup-terms").checked = true;
    await page.getElement("setup-form").listeners.submit({ preventDefault() {} });
    assert.equal(calls[0].url, "/api/auth/google/terms");
    assert.deepEqual(page.navigations, ["index.html"]);
});

test("confirmation removes URL secret immediately and requires an explicit action", async () => {
    const calls = [];
    const token = "abcdefghijklmnopqrstuvwx123456";
    const auth = { post: async (url, body) => { calls.push({ url, body }); return jsonResponse({ message: "confirmed" }); }, displayError: (error) => error.message };
    const page = fakePage("auth.js", `https://task.example/auth.html?mode=confirm#userId=42&token=${token}`, auth);
    assert.deepEqual(page.replaced, ["/auth.html?mode=confirm"]);
    await page.handlers.get("DOMContentLoaded")();
    assert.equal(calls.length, 0);
    assert.equal(page.getElement("confirm-form").hidden, false);
    await page.getElement("confirm-form").listeners.submit({ preventDefault() {} });
    assert.equal(calls.length, 1);
    assert.equal(calls[0].body.userId, 42);
    assert.equal(calls[0].body.token, token);
    assert.equal(page.getElement("confirm-form").hidden, true);
    await page.getElement("confirm-form").listeners.submit({ preventDefault() {} });
    assert.equal(calls.length, 1, "consumed token is discarded");
});

test("broken confirmation links offer recovery without making a request", async () => {
    const page = fakePage("auth.js", "https://task.example/auth.html?mode=confirm#userId=bad&token=short", {});
    await page.handlers.get("DOMContentLoaded")();
    assert.equal(page.getElement("confirm-form").hidden, true);
    assert.equal(page.getElement("support-recovery").hidden, false);
    assert.match(page.getElement("support-message").textContent, /有効なリンク/);
});

test("password reset sends secret in JSON and clears password fields on success", async () => {
    const calls = [];
    const auth = { post: async (url, body) => { calls.push({ url, body }); return jsonResponse({}); }, displayError: (error) => error.message };
    const page = fakePage("auth.js", "https://task.example/auth.html?mode=reset#userId=9&token=abcdefghijklmnopqrstuvwx", auth);
    await page.handlers.get("DOMContentLoaded")();
    page.getElement("reset-password").value = "a-long-new-passphrase";
    page.getElement("reset-password-confirm").value = "a-long-new-passphrase";
    await page.getElement("reset-form").listeners.submit({ preventDefault() {} });
    assert.equal(calls[0].url, "/api/auth/reset-password");
    assert.equal(calls[0].body.password, "a-long-new-passphrase");
    assert.equal(page.getElement("reset-password").value, "");
    assert.equal(page.getElement("reset-password-confirm").value, "");
    assert.equal(page.getElement("reset-form").hidden, true);
});

test("login redirects limited cookie sessions to account setup", async () => {
    const auth = { request: async (url) => jsonResponse(url.endsWith("config") ? { registrationEnabled: true } : { nextAction: "addEmail" }) };
    const page = fakePage("login.js", "https://task.example/login.html", auth);
    await page.handlers.get("DOMContentLoaded")();
    assert.deepEqual(page.navigations, ["auth.html"]);
});

test("login retains existing 8-character passwords and registration sends current policy versions", async () => {
    const calls = [];
    const auth = {
        request: async (url) => url.endsWith("config") ? jsonResponse({ registrationEnabled: true, termsVersion: "current-terms", privacyVersion: "current-privacy" }) : jsonResponse({}, 401),
        post: async (url, body) => { calls.push({ url, body }); return jsonResponse({ nextAction: "confirmEmail" }); }
    };
    const page = fakePage("login.js", "https://task.example/login.html", auth);
    await page.handlers.get("DOMContentLoaded")();
    page.getElement("auth-user-key").value = "Alice";
    page.getElement("auth-password").value = "old-pass";
    await page.getElement("auth-form").listeners.submit({ preventDefault() {} });
    assert.equal(calls[0].body.password, "old-pass");
    page.getElement("auth-switch-button").listeners.click();
    assert.equal(page.getElement("auth-password").minLength, 12);
    page.getElement("auth-password").value = "a-new-long-password";
    page.getElement("auth-email").value = "alice@example.test";
    page.getElement("auth-terms").checked = true;
    await page.getElement("auth-form").listeners.submit({ preventDefault() {} });
    assert.equal(calls[1].url, "/api/auth/register");
    assert.equal(calls[1].body.userKey, "alice");
    assert.equal(calls[1].body.email, "alice@example.test");
    assert.equal(calls[1].body.acceptTerms, true);
    assert.equal(calls[1].body.termsVersion, "current-terms");
    assert.equal(calls[1].body.privacyVersion, "current-privacy");
});

test("password visibility resets on registration and submission and cannot change while saving", async () => {
    let finish;
    const auth = {
        request: async url => url.endsWith("config") ? jsonResponse({ registrationEnabled: true }) : jsonResponse({}, 401),
        post: () => new Promise(resolve => { finish = resolve; })
    };
    const page = fakePage("login.js", "https://task.example/login.html", auth);
    await page.handlers.get("DOMContentLoaded")();
    const password = page.getElement("auth-password"), toggle = page.getElement("auth-password-toggle");
    password.value = "synthetic-long-password";
    toggle.listeners.click();
    assert.equal(password.type, "text");
    assert.equal(toggle.textContent, "非表示");
    page.getElement("auth-switch-button").listeners.click();
    assert.equal(password.type, "password");
    assert.equal(password.value, "synthetic-long-password");
    toggle.listeners.click();
    const pending = page.getElement("auth-form").listeners.submit({ preventDefault() {} });
    assert.equal(password.type, "password");
    toggle.listeners.click();
    assert.equal(password.type, "password");
    finish(jsonResponse({ nextAction: "confirmEmail" }));
    await pending;
    assert.equal(password.value, "");
});
