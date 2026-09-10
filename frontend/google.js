const googleForm = document.getElementById("google-form");
const googleContent = document.getElementById("google-content");
const googleMessage = document.getElementById("google-message");
const googleUserKey = document.getElementById("google-user-key");
const googleName = document.getElementById("google-name");
const googlePassword = document.getElementById("google-password");
const googleTerms = document.getElementById("google-terms");
const googleSwitch = document.getElementById("google-switch");
let googleConfig = null;
let pendingGoogle = null;
let linkExisting = false;
let googleBusy = false;

document.addEventListener("DOMContentLoaded", async () => {
    setGoogleBusy(true);
    try {
        const configResponse = await TaskAuth.request("/api/auth/config");
        if (!configResponse.ok) throw new Error(await TaskAuth.errorMessage(configResponse));
        googleConfig = await configResponse.json();
        const response = await TaskAuth.request("/api/auth/google/pending");
        if (!response.ok) throw new Error(await TaskAuth.errorMessage(response));
        pendingGoogle = await response.json();
        linkExisting = !pendingGoogle.canRegister;
        document.getElementById("google-email").textContent = pendingGoogle.email;
        document.getElementById("google-account").hidden = false;
        // A public nickname is chosen here; a Google real name is never published automatically.
        renderGoogleForm();
    } catch (error) {
        document.getElementById("google-heading").textContent = "Googleログインをやり直してください";
        document.getElementById("google-description").textContent = "確認の有効期限は5分です。ログイン画面に戻ってお試しください。";
        setGoogleMessage(TaskAuth.displayError(error), true);
    } finally { setGoogleBusy(false); }
});

function renderGoogleForm() {
    document.getElementById("google-heading").textContent = linkExisting ? "登録済みのアカウントに連携" : "アカウントを作成";
    document.getElementById("google-description").textContent = linkExisting
        ? "現在のユーザーIDとパスワードを入力してください。タスクや相棒のデータを引き継ぎ、次回からGoogleでもログインできます。"
        : "ユーザーIDと表示名を決めて、Task Boardを始めましょう。次回からはGoogleでログインできます。";
    document.getElementById("google-user-hint").textContent = linkExisting ? "Task Boardに登録したユーザーIDを入力してください。" : "半角英数字・ハイフン・アンダースコアで3〜100文字。チーム内にも表示されます。";
    document.getElementById("google-name-field").hidden = linkExisting;
    document.getElementById("google-password-field").hidden = !linkExisting;
    document.getElementById("google-consent-field").hidden = linkExisting;
    document.getElementById("google-recovery").hidden = !linkExisting;
    document.getElementById("google-submit").textContent = linkExisting ? "連携してログイン" : "アカウントを作成";
    googleSwitch.hidden = !pendingGoogle.canRegister;
    googleSwitch.textContent = linkExisting ? "新しくアカウントを作成する" : "登録済みのアカウントに連携する";
    googlePassword.required = linkExisting;
    googlePassword.value = "";
    googleTerms.required = !linkExisting;
    googleForm.hidden = false;
}

googleSwitch.addEventListener("click", () => {
    if (googleBusy || !pendingGoogle?.canRegister) return;
    linkExisting = !linkExisting;
    renderGoogleForm();
    setGoogleBusy(false);
    setGoogleMessage("");
    googleUserKey.focus();
});

googleForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    if (googleBusy || !pendingGoogle || !googleConfig || !googleForm.reportValidity()) return;
    const body = { userKey: googleUserKey.value.trim().toLowerCase() };
    if (linkExisting) body.password = googlePassword.value;
    else Object.assign(body, { displayName: googleName.value.trim(), acceptTerms: googleTerms.checked,
        termsVersion: googleConfig.termsVersion, privacyVersion: googleConfig.privacyVersion });
    setGoogleBusy(true);
    setGoogleMessage(linkExisting ? "アカウントを連携しています。" : "アカウントを作成しています。");
    try {
        const response = await TaskAuth.post(`/api/auth/google/${linkExisting ? "link" : "register"}`, body);
        if (!response.ok) throw new Error(await TaskAuth.errorMessage(response));
        const session = await response.json();
        googlePassword.value = "";
        window.location.assign(session.nextAction === "ready" ? "index.html" : "auth.html");
    } catch (error) {
        googlePassword.value = "";
        setGoogleMessage(TaskAuth.displayError(error), true);
    } finally { setGoogleBusy(false); }
});

document.getElementById("google-cancel").addEventListener("click", async () => {
    if (googleBusy) return;
    setGoogleBusy(true);
    try {
        const response = await TaskAuth.post("/api/auth/google/cancel", {});
        if (!response.ok) throw new Error(await TaskAuth.errorMessage(response));
        window.location.assign("login.html");
    } catch (error) { setGoogleMessage(TaskAuth.displayError(error), true); setGoogleBusy(false); }
});

function setGoogleBusy(value) {
    googleBusy = value;
    googleContent.setAttribute("aria-busy", String(value));
    for (const element of googleContent.querySelectorAll("input, button")) element.disabled = value;
    googleName.disabled = value || linkExisting;
    googlePassword.disabled = value || !linkExisting;
    googleTerms.disabled = value || linkExisting;
}

function setGoogleMessage(text, error = false) {
    googleMessage.textContent = text;
    googleMessage.classList.toggle("error", error);
}
