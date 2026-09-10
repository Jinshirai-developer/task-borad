const content = document.getElementById("support-content");
const heading = document.getElementById("support-heading");
const description = document.getElementById("support-description");
const message = document.getElementById("support-message");
const setupForm = document.getElementById("setup-form");
const emailForm = document.getElementById("email-form");
const confirmForm = document.getElementById("confirm-form");
const resetForm = document.getElementById("reset-form");
const pendingPanel = document.getElementById("pending-panel");
const accountActions = document.getElementById("support-account-actions");
const recoveryLinks = document.getElementById("support-recovery");
const setupEmail = document.getElementById("setup-email");
const setupPassword = document.getElementById("setup-password");
const setupTerms = document.getElementById("setup-terms");
const params = new URLSearchParams(window.location.search);
const mode = ["forgot", "resend", "confirm", "reset"].includes(params.get("mode")) ? params.get("mode") : "setup";
const fragment = new URLSearchParams(window.location.hash.slice(1));
let actionToken = fragment.get("token") || "";
const actionUserId = Number(fragment.get("userId"));
// メールの秘密値を履歴やコピーしたURLへ残さない。確認操作まではメモリ内だけで保持。
if (window.location.hash) window.history.replaceState(null, "", `${window.location.pathname}${mode === "setup" ? "" : `?mode=${mode}`}`);
let session = null;
let config = null;
let busy = false;

document.addEventListener("DOMContentLoaded", initialize);

async function initialize() {
    hidePanels();
    if (mode === "forgot" || mode === "resend") {
        showHeading(mode === "forgot" ? "パスワードを再設定" : "確認メールを再送", mode === "forgot" ? "登録したメールアドレスへ再設定用リンクを送信します。" : "未確認のメールアドレスへ確認用リンクを送信します。");
        document.getElementById("email-submit").textContent = mode === "forgot" ? "再設定メールを送信" : "確認メールを送信";
        emailForm.hidden = false;
        setBusy(false);
        return;
    }
    if (mode === "confirm" || mode === "reset") {
        showHeading(mode === "confirm" ? "メールアドレスの確認" : "新しいパスワードを設定", mode === "confirm" ? "下のボタンでメールアドレスの確認を完了します。" : "新しいパスワードを入力してください。");
        if (!Number.isSafeInteger(actionUserId) || actionUserId <= 0 || !/^[A-Za-z0-9_-]{20,4096}$/.test(actionToken)) {
            actionToken = "";
            setMessage("有効なリンクがありません。メール内のリンクをもう一度開くか、新しいメールをリクエストしてください。", true);
            recoveryLinks.hidden = false;
        } else {
            (mode === "confirm" ? confirmForm : resetForm).hidden = false;
        }
        setBusy(false);
        return;
    }
    await run(async () => {
        const configResponse = await TaskAuth.request("/api/auth/config");
        if (!configResponse.ok) throw new Error(await TaskAuth.errorMessage(configResponse));
        config = await configResponse.json();
        await refreshSession();
    });
}

async function refreshSession() {
    const response = await TaskAuth.request("/api/auth/session");
    if (response.status === 401) {
        window.location.replace("login.html?message=" + encodeURIComponent("手続きを進めるにはログインしてください。"));
        return;
    }
    if (!response.ok) throw new Error(await TaskAuth.errorMessage(response));
    session = await response.json();
    if (session.nextAction === "ready") {
        window.location.replace("index.html");
        return;
    }
    renderSession();
}

function renderSession() {
    hidePanels();
    document.getElementById("pending-edit").hidden = session.hasPassword === false;
    accountActions.hidden = false;
    if (session.nextAction === "confirmEmail") {
        showHeading("確認メールをご確認ください", `${session.email || "登録したメールアドレス"} に送信したリンクから確認を完了してください。タスクは確認後に利用できます。`);
        pendingPanel.hidden = false;
    } else {
        showHeading(session.requiresEmail ? "メールアドレスを登録" : "利用規約をご確認ください", "既存のタスク・育成データは保持されています。利用を続けるための手続きをお願いします。");
        showSetupForm();
    }
}

function showSetupForm() {
    setupEmail.value = session.email || "";
    setupEmail.readOnly = Boolean(session.emailConfirmed);
    setupPassword.value = "";
    setupPassword.required = session.hasPassword !== false;
    document.getElementById("setup-password-field").hidden = session.hasPassword === false;
    setupTerms.checked = false;
    setupForm.hidden = false;
}

setupForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    if (busy || !setupForm.reportValidity() || !config) return;
    const body = { email: setupEmail.value.trim(), password: setupPassword.value, acceptTerms: setupTerms.checked, termsVersion: config.termsVersion, privacyVersion: config.privacyVersion };
    await run(async () => {
        const response = await TaskAuth.post(session.hasPassword === false ? "/api/auth/google/terms" : "/api/auth/complete-registration", body);
        if (!response.ok) throw new Error(await TaskAuth.errorMessage(response));
        session = await response.json();
        setupPassword.value = "";
        if (session.nextAction === "ready") window.location.assign("index.html");
        else {
            renderSession();
            setMessage("手続きを保存しました。メールの確認をお願いします。");
        }
    });
});

emailForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    if (busy || !emailForm.reportValidity()) return;
    const email = document.getElementById("recovery-email").value.trim();
    await run(async () => {
        const response = await TaskAuth.post(`/api/auth/${mode === "forgot" ? "forgot-password" : "resend-confirmation"}`, { email });
        if (!response.ok) throw new Error(await TaskAuth.errorMessage(response));
        const result = await response.json();
        setMessage(result.message || "対象のアカウントが存在する場合、メールを送信します。メールをご確認ください。");
    });
});

confirmForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    if (busy || !actionToken) return;
    await run(async () => {
        const response = await TaskAuth.post("/api/auth/confirm-email", { userId: actionUserId, token: actionToken });
        if (!response.ok) {
            recoveryLinks.hidden = false;
            throw new Error(await TaskAuth.errorMessage(response));
        }
        actionToken = "";
        hidePanels();
        showHeading("メールアドレスを確認しました", "ログイン画面から利用を続けてください。");
        document.getElementById("support-login-link").focus();
    });
});

const resetPassword = document.getElementById("reset-password");
const resetConfirm = document.getElementById("reset-password-confirm");
resetConfirm.addEventListener("input", () => resetConfirm.setCustomValidity(""));
resetPassword.addEventListener("input", () => resetConfirm.setCustomValidity(""));
resetForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    resetConfirm.setCustomValidity(resetPassword.value === resetConfirm.value ? "" : "パスワードが一致していません。");
    if (busy || !resetForm.reportValidity() || !actionToken) return;
    const body = { userId: actionUserId, token: actionToken, password: resetPassword.value };
    await run(async () => {
        const response = await TaskAuth.post("/api/auth/reset-password", body);
        if (!response.ok) {
            recoveryLinks.hidden = false;
            throw new Error(await TaskAuth.errorMessage(response));
        }
        actionToken = "";
        resetPassword.value = "";
        resetConfirm.value = "";
        hidePanels();
        showHeading("パスワードを再設定しました", "すべての端末でログイン状態を解除しました。新しいパスワードでログインしてください。");
        document.getElementById("support-login-link").focus();
    });
});

document.getElementById("pending-resend").addEventListener("click", () => run(async () => {
    const response = await TaskAuth.post("/api/auth/resend-confirmation", { email: session.email });
    if (!response.ok) throw new Error(await TaskAuth.errorMessage(response));
    const result = await response.json();
    setMessage(result.message || "対象の場合、確認メールを送信します。メールをご確認ください。");
}));
document.getElementById("pending-edit").addEventListener("click", () => {
    if (busy) return;
    pendingPanel.hidden = true;
    showHeading("メールアドレスを訂正", "入力したアドレスに確認メールを送信します。以前の確認リンクは利用できなくなります。");
    showSetupForm();
    setupEmail.focus();
});
document.getElementById("pending-refresh").addEventListener("click", () => run(refreshSession));
document.getElementById("support-logout").addEventListener("click", () => run(async () => {
    const response = await TaskAuth.request("/api/auth/logout", { method: "POST" });
    if (!response.ok && response.status !== 401) throw new Error(await TaskAuth.errorMessage(response, "ログアウトできませんでした。もう一度お試しください。"));
    window.location.assign("login.html?message=" + encodeURIComponent("ログアウトしました。"));
}));
document.getElementById("support-delete").addEventListener("click", async () => {
    if (busy || !confirm("アカウント、タスク、育成データをすべて削除します。この操作は元に戻せません。続けますか？")) return;
    await run(async () => {
        const response = await TaskAuth.request("/api/user", { method: "DELETE" });
        if (!response.ok) throw new Error(await TaskAuth.errorMessage(response));
        window.location.assign("login.html?message=" + encodeURIComponent("アカウントを削除しました。"));
    });
});

function hidePanels() {
    for (const panel of [setupForm, emailForm, confirmForm, resetForm, pendingPanel, accountActions, recoveryLinks]) panel.hidden = true;
}

function showHeading(title, text) {
    heading.textContent = title;
    description.textContent = text;
    document.title = `${title} | Task Board`;
}

function setMessage(text, error = false) {
    message.textContent = text;
    message.classList.toggle("error", error);
}

function setBusy(value) {
    busy = value;
    content.setAttribute("aria-busy", String(value));
    for (const element of content.querySelectorAll("input, button")) element.disabled = value;
}

async function run(action) {
    if (busy) return;
    setBusy(true);
    setMessage("処理しています。");
    try {
        await action();
        if (message.textContent === "処理しています。") setMessage("");
    } catch (error) {
        setMessage(TaskAuth.displayError(error), true);
    } finally {
        setBusy(false);
    }
}
