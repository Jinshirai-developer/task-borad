const authForm = document.getElementById("auth-form");
const userKeyInput = document.getElementById("auth-user-key");
const displayNameInput = document.getElementById("auth-display-name");
const emailInput = document.getElementById("auth-email");
const passwordInput = document.getElementById("auth-password");
const passwordToggle = document.getElementById("auth-password-toggle");
const termsInput = document.getElementById("auth-terms");
const submitButton = document.getElementById("auth-submit-button");
const switchButton = document.getElementById("auth-switch-button");
const authMessage = document.getElementById("auth-message");
const googleStart = document.getElementById("google-start");
let config = null;
let isRegister = false;
let isSaving = false;

document.addEventListener("DOMContentLoaded", initialize);

function setPasswordVisibility(visible) {
    passwordInput.type = visible ? "text" : "password";
    passwordToggle.textContent = visible ? "非表示" : "表示";
    passwordToggle.setAttribute("aria-label", visible ? "パスワードを非表示" : "パスワードを表示");
    passwordToggle.setAttribute("aria-pressed", String(visible));
}

passwordToggle.addEventListener("click", () => {
    if (!isSaving) setPasswordVisibility(passwordInput.type === "password");
});

async function initialize() {
    const redirectMessage = new URLSearchParams(window.location.search).get("message");
    if (redirectMessage) setMessage(redirectMessage);
    if (new URLSearchParams(window.location.search).get("google") === "failed")
        setMessage("Googleログインが完了しませんでした。もう一度お試しください。", true);
    setSaving(true);
    try {
        const response = await TaskAuth.request("/api/auth/config");
        if (!response.ok) throw new Error(await TaskAuth.errorMessage(response, "設定を読み込めませんでした。再読み込みしてください。"));
        config = await response.json();
        document.getElementById("google-login").hidden = !config.googleLoginEnabled;
        switchButton.hidden = !config.registrationEnabled;
        document.getElementById("registration-closed").hidden = Boolean(config.registrationEnabled);

        const sessionResponse = await TaskAuth.request("/api/auth/session");
        if (sessionResponse.ok) {
            const session = await sessionResponse.json();
            window.location.replace(session.nextAction === "ready" ? "index.html" : "auth.html");
            return;
        }
        if (sessionResponse.status !== 401) throw new Error(await TaskAuth.errorMessage(sessionResponse, "ログイン状態を確認できませんでした。"));
    } catch (error) {
        setMessage(TaskAuth.displayError(error), true);
    } finally {
        setSaving(false);
    }
}

switchButton.addEventListener("click", () => {
    if (isSaving || !config?.registrationEnabled) return;
    isRegister = !isRegister;
    for (const element of document.querySelectorAll("[data-register-only]")) element.hidden = !isRegister;
    displayNameInput.disabled = !isRegister;
    emailInput.disabled = !isRegister;
    emailInput.required = isRegister;
    termsInput.disabled = !isRegister;
    termsInput.required = isRegister;
    passwordInput.minLength = isRegister ? 12 : 8;
    passwordInput.autocomplete = isRegister ? "new-password" : "current-password";
    document.getElementById("auth-password-hint").textContent = isRegister ? "12〜100文字。パスフレーズも使えます。" : "登録時のパスワードを入力してください。";
    document.getElementById("auth-heading").textContent = isRegister ? "アカウントを作成" : "ログイン";
    document.getElementById("auth-description").textContent = isRegister ? "登録したメールアドレスに確認リンクを送ります。" : "あなたのタスクボードを開きましょう。";
    document.title = isRegister ? "新規登録 | Task Board" : "ログイン | Task Board";
    submitButton.textContent = isRegister ? "確認メールを送って登録" : "ログインする";
    switchButton.textContent = isRegister ? "アカウントをお持ちの方 · ログイン" : "はじめての方はこちら · 新規登録";
    setPasswordVisibility(false);
    setMessage("");
    userKeyInput.focus();
});

authForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    if (isSaving || !authForm.reportValidity()) return;
    if (!config) {
        setMessage("設定を読み込めませんでした。画面を再読み込みしてください。", true);
        return;
    }
    const userKey = userKeyInput.value.trim().toLowerCase();
    const body = { userKey, password: passwordInput.value };
    if (isRegister) Object.assign(body, {
        displayName: displayNameInput.value.trim() || userKey,
        email: emailInput.value.trim(), acceptTerms: termsInput.checked,
        termsVersion: config.termsVersion, privacyVersion: config.privacyVersion
    });
    setSaving(true);
    setMessage(isRegister ? "登録しています。確認メールの送信までしばらくお待ちください。" : "ログインしています。");
    try {
        const response = await TaskAuth.post(`/api/auth/${isRegister ? "register" : "login"}`, body);
        if (!response.ok) throw new Error(await TaskAuth.errorMessage(response, isRegister ? "登録できませんでした。" : "ログインできませんでした。"));
        const session = await response.json();
        passwordInput.value = "";
        window.location.assign(session.nextAction === "ready" ? "index.html" : "auth.html");
    } catch (error) {
        setMessage(TaskAuth.displayError(error), true);
    } finally {
        setSaving(false);
    }
});

function setSaving(value) {
    isSaving = value;
    if (value) setPasswordVisibility(false);
    authForm.setAttribute("aria-busy", String(value));
    for (const input of authForm.querySelectorAll("input, button")) input.disabled = value;
    displayNameInput.disabled = value || !isRegister;
    emailInput.disabled = value || !isRegister;
    termsInput.disabled = value || !isRegister;
    switchButton.disabled = value;
    googleStart.disabled = value || !config?.googleLoginEnabled;
}

googleStart.addEventListener("click", async () => {
    if (isSaving || !config?.googleLoginEnabled) return;
    setSaving(true);
    setMessage("Googleのログイン画面を開いています。");
    try {
        const response = await TaskAuth.post("/api/auth/google/start", {});
        if (!response.ok) throw new Error(await TaskAuth.errorMessage(response));
        const { url } = await response.json();
        const destination = new URL(url);
        if (destination.origin !== "https://accounts.google.com" || destination.username || destination.password)
            throw new Error("Googleのログイン画面を開けませんでした。");
        window.location.assign(destination.href);
    } catch (error) {
        setMessage(TaskAuth.displayError(error), true);
        setSaving(false);
    }
});

function setMessage(message, error = false) {
    authMessage.textContent = message;
    authMessage.classList.toggle("error", error);
}
