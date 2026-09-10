(() => {
    "use strict";
    const code = document.getElementById("desktop-code");
    const approve = document.getElementById("desktop-approve");
    const deny = document.getElementById("desktop-deny");
    const login = document.getElementById("desktop-login");
    const message = document.getElementById("desktop-message");
    const pendingKey = "taskBoardDesktopReturn";
    let busy = false;
    let nextAction = "login";
    const normalize = value => value.toUpperCase().replace(/[-\s]/g, "");
    const valid = value => /^[A-HJ-NP-Z2-9]{8}$/.test(normalize(value));
    function show(text, error = false) { message.textContent = text; message.dataset.error = String(error); }
    function clearReturn() { try { sessionStorage.removeItem(pendingKey); } catch { /* No persistent credentials. */ } }
    const initial = new URLSearchParams(location.hash.slice(1)).get("code") || "";
    if (valid(initial)) code.value = normalize(initial).replace(/^(.{4})/, "$1-");

    login.addEventListener("click", () => {
        if (!valid(code.value)) { show("アプリに表示された8文字のコードを入力してください。", true); code.focus(); return; }
        try { sessionStorage.setItem(pendingKey, JSON.stringify({ code: normalize(code.value), expires: Date.now() + 600000 })); }
        catch { show("このブラウザーではログインを引き継げません。別のブラウザーでお試しください。", true); return; }
        location.assign(nextAction === "login" ? "login.html" : "auth.html");
    });
    async function decide(accepted) {
        if (busy) return;
        if (!valid(code.value)) { show("確認コードを入力し直してください。", true); code.focus(); return; }
        busy = true; approve.disabled = true; deny.disabled = true;
        try {
            const response = await TaskAuth.post("/api/auth/desktop/approve", { userCode: normalize(code.value), approve: accepted });
            if (!response.ok) throw new Error(await TaskAuth.errorMessage(response));
            clearReturn();
            document.getElementById("desktop-form").hidden = true;
            document.getElementById("desktop-home").hidden = false;
            show(accepted ? "承認しました。Windowsアプリに戻ってください。この画面は閉じて構いません。" : "ログインをキャンセルしました。");
        } catch (error) { show(TaskAuth.displayError(error), true); }
        finally { busy = false; approve.disabled = false; deny.disabled = false; }
    }
    document.getElementById("desktop-form").addEventListener("submit", event => { event.preventDefault(); void decide(true); });
    deny.addEventListener("click", () => { void decide(false); });
    (async () => {
        try {
            const response = await TaskAuth.request("/api/auth/session");
            if (response.status === 401) {
                approve.hidden = true; login.hidden = false;
                show("まずWeb版にログインしてください。Googleアカウントも利用できます。"); return;
            }
            if (!response.ok) throw new Error(await TaskAuth.errorMessage(response));
            const session = await response.json();
            nextAction = session.nextAction;
            if (nextAction !== "ready") {
                approve.hidden = true; login.hidden = false; login.textContent = "アカウントの確認を続ける";
                show("メールアドレスと利用規約の確認を完了してください。"); return;
            }
            document.getElementById("desktop-account").textContent = `${session.user.displayName}（@${session.user.userKey}）でログインします。`;
            approve.disabled = false; deny.hidden = false;
            show("コードを確認して、ログインを承認してください。");
        } catch (error) { show(TaskAuth.displayError(error), true); }
    })();
})();
