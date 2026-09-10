// A fixed local return page for the explicit Windows sign-in flow; never accepts a redirect URL.
(() => {
    if (new URLSearchParams(location.search).get("demo") === "1") return;
    try {
        const pending = JSON.parse(sessionStorage.getItem("taskBoardDesktopReturn") || "null");
        if (!pending) return;
        if (!/^[A-HJ-NP-Z2-9]{8}$/.test(pending.code) || !Number.isFinite(pending.expires)
            || pending.expires <= Date.now() || pending.expires > Date.now() + 600000) {
            sessionStorage.removeItem("taskBoardDesktopReturn"); return;
        }
        location.replace("desktop.html#code=" + pending.code);
    } catch { /* Normal Web sign-in continues when storage is unavailable. */ }
})();
