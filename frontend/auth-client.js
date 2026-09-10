// 認証Cookieはブラウザーのみが保持する。Bearer tokenやメールはStorageへ保存しない。
window.TaskAuth = (() => {
    let csrfToken = null;
    let csrfRequest = null;
    const identityChangingPaths = new Set([
        "/api/auth/login", "/api/auth/register", "/api/auth/complete-registration",
        "/api/auth/confirm-email", "/api/auth/reset-password", "/api/auth/logout", "/api/user"
    ]);

    for (const storageName of ["localStorage", "sessionStorage"]) {
        try {
            for (const key of ["taskBoardAuthToken", "taskBoardUserKey"]) window[storageName].removeItem(key);
        } catch {
            // Storageを使えないブラウザーでもCookie認証は利用できる。
        }
    }

    function sameOriginUrl(path) {
        const url = new URL(path, window.location.href);
        if (!["http:", "https:"].includes(url.protocol) || url.origin !== window.location.origin) {
            throw new Error("アプリと同じURLからAPIに接続してください。TaskApiのURLでこの画面を開いてください。");
        }
        return url;
    }

    async function errorMessage(response, fallback = "処理に失敗しました。時間をおいて再度お試しください。") {
        try {
            const data = await response.json();
            if (typeof data.message === "string") return data.message;
            if (data.errors) {
                const first = Object.values(data.errors).flat().find((value) => typeof value === "string");
                if (first) return first;
            }
            return data.detail || data.title || fallback;
        } catch {
            return fallback;
        }
    }

    function displayError(error, fallback = "処理に失敗しました。") {
        if (error instanceof TypeError || /Failed to fetch|NetworkError|Load failed/.test(error?.message || "")) {
            return "サービスに接続できません。接続状況を確認し、時間をおいて再度お試しください。";
        }
        return error?.message || fallback;
    }

    async function getCsrfToken() {
        if (csrfToken) return csrfToken;
        if (!csrfRequest) {
            csrfRequest = (async () => {
                const response = await fetch(sameOriginUrl("/api/auth/csrf").href, {
                    credentials: "same-origin", cache: "no-store", redirect: "error", headers: { Accept: "application/json" }
                });
                if (!response.ok) throw new Error(await errorMessage(response, "安全な接続を準備できませんでした。再読み込みしてください。"));
                const data = await response.json();
                if (typeof data.token !== "string" || !data.token) throw new Error("安全な接続を準備できませんでした。再読み込みしてください。");
                csrfToken = data.token;
                return csrfToken;
            })();
        }
        try {
            return await csrfRequest;
        } finally {
            csrfRequest = null;
        }
    }

    async function request(path, options = {}) {
        const url = sameOriginUrl(path);
        // Fail closed: a missing demo script must never fall through to real APIs.
        if (new URL(window.location.href).searchParams.get("demo") === "1") {
            if (!window.TaskDemo?.active) throw new Error("お試しモードを準備できませんでした。再読み込みしてください。");
            return window.TaskDemo.request(url, options);
        }
        const method = (options.method || "GET").toUpperCase();
        const headers = new Headers(options.headers || {});
        headers.set("Accept", "application/json");
        headers.delete("Authorization");
        if (!["GET", "HEAD", "OPTIONS"].includes(method)) headers.set("X-CSRF-TOKEN", await getCsrfToken());
        const response = await fetch(url.href, { ...options, method, headers, credentials: "same-origin", cache: "no-store", redirect: "error" });
        if (response.ok && !["GET", "HEAD", "OPTIONS"].includes(method) && identityChangingPaths.has(url.pathname)) csrfToken = null;
        // 副作用のあるリクエストは自動再送しない。次の手動操作ではCSRF tokenを再取得する。
        if (response.status === 400 || response.status === 401 || response.status === 403) csrfToken = null;
        return response;
    }

    function post(path, data) {
        return request(path, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(data) });
    }

    return Object.freeze({ request, post, errorMessage, displayError });
})();
