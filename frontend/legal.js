"use strict";

(() => {
    const documentVersion = "2026-09-07-teams";
    const notice = document.getElementById("legal-publication-notice");
    const unknownValue = "未設定（公開前に確認が必要です）";

    function safeString(value) {
        return typeof value === "string" ? value.trim() : "";
    }

    function isContactEmail(value) {
        return /^[^<>\s@?&#]+@[^<>\s@?&#]+\.[^<>\s@?&#]+$/.test(value);
    }

    function renderConfig(config) {
        document.querySelectorAll("[data-legal-field]").forEach(element => {
            const key = element.dataset.legalField;
            const value = safeString(config[key]);
            element.textContent = value || unknownValue;

            if (key === "contactEmail" && isContactEmail(value)) {
                const link = document.createElement("a");
                link.href = `mailto:${value}`;
                link.textContent = "メールで問い合わせ";
                element.replaceChildren(link);
            }
        });

        const complete = ["operatorName", "contactEmail", "hostingProvider", "emailProvider", "logRetention", "backupRetention"]
            .every(key => safeString(config[key]) !== "") && isContactEmail(safeString(config.contactEmail));
        const matchingVersion = config.termsVersion === documentVersion && config.privacyVersion === documentVersion;
        if (!matchingVersion) {
            notice.textContent = "公開準備中：この文書の版とサーバーの公開設定が一致していません。新規登録せず、時間をおいて再確認してください。";
        } else if (config.publicReleaseReady !== true || !complete) {
            notice.textContent = "公開準備中：運営情報・利用サービス・保存期間などの確認が完了していません。未設定の項目は下記に明示しています。公開デモとしての利用開始前に、設定済みの内容をご確認ください。";
        } else {
            notice.hidden = true;
        }
    }

    async function loadConfig() {
        const controller = new AbortController();
        const timeoutId = window.setTimeout(() => controller.abort(), 10000);
        try {
            const response = await fetch("/api/auth/config", {
                cache: "no-store",
                credentials: "omit",
                redirect: "error",
                signal: controller.signal
            });
            if (!response.ok) throw new Error("Configuration unavailable");
            const config = await response.json();
            if (!config || typeof config !== "object" || Array.isArray(config)) throw new Error("Invalid configuration");
            renderConfig(config);
        } catch {
            notice.textContent = "公開情報を取得できませんでした。下記の未設定項目を確認できるまで、新規登録はお控えください。通信状態を確認し、このページを再読み込みしてください。";
        } finally {
            window.clearTimeout(timeoutId);
        }
    }

    void loadConfig();
})();
