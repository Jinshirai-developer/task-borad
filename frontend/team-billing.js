// Account-scoped test billing (team views show the owner's entitlement). The server owns prices, limits and paid state.
const TeamBilling = (() => {
    const teamRoot = document.getElementById("team-billing-panel"), accountRoot = document.getElementById("account-billing-panel");
    let root = teamRoot;
    const inScope = team => team?.id === 0 || team?.id === state.teamId;
    const apiPath = team => team.id === 0 ? "/api/user/billing" : `/api/teams/${team.id}/billing`;
    const flow = document.getElementById("billing-flow");
    let generation = 0, detail = null, current = null, view = null, returnKind = null;
    let flowMessage = "", flowError = false;
    const node = (tag, text, cls) => { const n = document.createElement(tag); if (text != null) n.textContent = text; if (cls) n.className = cls; return n; };
    const button = (label, id, action, disabled = false) => { const b = node("button", label); b.type = "button"; b.id = id; b.disabled = disabled; b.dataset.billingDisabled = String(disabled); b.addEventListener("click", action); return b; };
    const formatDate = value => value ? new Date(value).toLocaleString("ja-JP", { timeZone: "Asia/Tokyo" }) + " JST" : "—";
    function restoreDisabled() {
        [root, flow].forEach(container => container.querySelectorAll("button").forEach(b => { b.disabled = optionsState.busy || b.dataset.billingDisabled === "true"; }));
        flow.setAttribute("aria-busy", String(optionsState.busy));
    }
    async function request(path, method = "GET") {
        const r = await TaskAuth.request(path, { method });
        if (!r.ok) throw new Error(await getErrorMessage(r, "チームプランを確認できませんでした。"));
        return r.json();
    }
    function reset() {
        generation++; closeFlow(false); detail = current = null;
        for (const panel of [teamRoot, accountRoot]) { panel.replaceChildren(); panel.hidden = true; }
        root = teamRoot; document.getElementById("team-modal").classList.remove("account-billing-open");
        document.getElementById("team-title").textContent = "チームでタスクを共有";
    }
    function closeFlow(focus = true) {
        view = returnKind = null; flowMessage = ""; flowError = false; delete flow.dataset.result;
        flow.hidden = true; flow.replaceChildren();
        document.getElementById("team-modal").classList.remove("billing-flow-open");
        document.getElementById("team-title").textContent = detail?.id === 0 ? "アカウント · プラン・契約管理" : "チームでタスクを共有";
        if (focus) (document.getElementById("billing-checkout") || document.getElementById("billing-plan-view") || document.getElementById("team-manage-tab")).focus();
    }
    function openFlow(next, kind = null) {
        if (!current || !detail || !inScope(detail) || optionsState.busy || isTaskBusy()) return;
        view = next; returnKind = kind; flowMessage = ""; flowError = false;
        renderFlow(); focusFlow();
    }
    function focusFlow() { document.getElementById("billing-flow-title")?.focus(); }
    function setFlowMessage(message, error = false) {
        flowMessage = message; flowError = error;
        const target = document.getElementById("billing-flow-message");
        if (target) {
            target.textContent = message; target.classList.toggle("billing-error", error);
            if (error) { target.focus({ preventScroll: true }); target.scrollIntoView({ block: "center" }); }
        }
    }
    const price = () => Number(current.monthlyYen).toLocaleString("ja-JP");
    function canCheckout() { return current?.isOwner && current.checkoutAvailable && (!current.hasContract || current.status === "checkout"); }
    function renderFlow() {
        if (!view || !current || !detail) return;
        const data = current, simulation = data.simulation === true;
        flow.hidden = false; flow.replaceChildren(); delete flow.dataset.result;
        document.getElementById("team-modal").classList.add("billing-flow-open");
        document.getElementById("team-title").textContent = view === "purchase" ? "アカウントPro · プランの確認" : "アカウントPro · 決済の確認";
        const banner = node("p", simulation ? "画面シミュレーション · Stripeには接続しません" : "テスト決済専用 · 実際の請求は発生しません", "billing-flow-banner");
        if (simulation) {
            const login = node("a", "カード入力を試すにはログイン →"); login.id = "billing-demo-login"; login.href = "login.html"; banner.append(login);
        }
        flow.append(banner);
        const steps = node("ol", null, "billing-steps"); steps.setAttribute("aria-label", "お申し込みの流れ");
        ["プランを確認", simulation ? "デモではカード入力なし" : "Stripeでカード入力", "結果を確認"].forEach((label, index) => {
            const step = node("li"); step.append(node("span", String(index + 1), "billing-step-number"), node("span", label));
            if (index === (view === "purchase" ? 0 : 2)) step.setAttribute("aria-current", "step");
            steps.append(step);
        }); flow.append(steps);
        if (view === "purchase") renderPurchase(); else renderResult();
        const message = node("p", flowMessage, "billing-flow-message"); message.id = "billing-flow-message";
        message.setAttribute("role", "status"); message.setAttribute("aria-live", "polite"); message.tabIndex = -1;
        message.classList.toggle("billing-error", flowError); flow.insertBefore(message, flow.querySelector(".billing-flow-footer"));
        restoreDisabled();
    }
    function flowTitle(text, container = flow) { const title = node("h3", text); title.id = "billing-flow-title"; title.tabIndex = -1; container.append(title); }
    function targetSummary(container = flow) {
        const target = node("section", null, "billing-target"); target.setAttribute("aria-label", "購入対象のアカウント");
        target.append(node("span", "対象のアカウント", "billing-label"), node("strong", current.billingDisplayName || (current.isOwner ? "自分のアカウント" : "チーム所有者のアカウント")), node("p", `所有チーム：${current.ownedTeamCount ?? "—"}件${detail.id ? ` · 表示中：${detail.name}` : ""}`), node("p", "このアカウントが所有するすべてのチームが対象です。新しく作るチームにも適用されます。他の人が所有するチームは対象外です。", "billing-fine-print"));
        container.append(target);
    }
    function renderPurchase() {
        const data = current, simulation = data.simulation === true;
        const hero = node("header", null, "billing-hero"), intro = node("div");
        intro.append(node("span", "ACCOUNT PRO", "billing-eyebrow"));
        flowTitle("仲間が増えても、\nいつものボードで。", intro);
        intro.append(node("p", "1つのアカウント契約で、所有するすべてのチームの参加人数制限を解除。", "billing-lead"));
        const scope = node("p", `対象：${current.billingDisplayName || "チーム所有者"}のアカウント`, "billing-scope-chip"); scope.title = "所有するすべてのチーム（新規チームを含む）"; intro.append(scope);
        const pet = node("img", null, "billing-hero-pet"); pet.src = "assets/pet/portfolio-cat-idle-v3.png"; pet.alt = ""; pet.width = 120; pet.height = 120;
        hero.append(intro, pet); flow.append(hero);
        const grid = node("div", null, "billing-purchase-grid"), main = node("section", null, "billing-purchase-main"), aside = node("aside", null, "billing-order");
        main.setAttribute("aria-label", "プランの比較と利用条件"); aside.setAttribute("aria-label", "購入内容とカード入力の案内");
        main.append(node("h4", "あなたに合ったプランを", "billing-section-heading"));
        const comparison = node("div", null, "billing-comparison");
        for (const pro of [false, true]) {
            const card = node("section", null, "billing-plan-card" + (pro ? " billing-plan-pro" : ""));
            const cardHeader = node("div", null, "billing-plan-card-heading");
            cardHeader.append(node("h4", pro ? "アカウントPro" : "無料プラン"), node("span", pro ? "PRO" : "FREE", "billing-tier")); card.append(cardHeader);
            card.append(node("p", pro ? `月額${price()}円` : "0円", "billing-price"));
            card.append(node("p", pro ? "参加人数のプラン上限なし" : "所有者を含めて3人まで", "billing-plan-benefit"));
            card.append(node("p", pro ? "人数を気にせず仲間を招待" : "小さなチームで、気軽に始める", "billing-plan-description"));
            card.append(node("p", data.plan === (pro ? "pro" : "free") ? "現在のプラン" : pro ? "所有する全チームに適用" : "少人数のチーム向け", "billing-fine-print"));
            comparison.append(card);
        }
        main.append(comparison, node("p", "タスク共有・担当者・タグ・ペットの育成は、どちらのプランでも利用できます。", "billing-shared-features"));
        const notes = node("section", null, "billing-purchase-notes"); notes.append(node("h4", "お申し込みの前に"));
        const list = node("ul");
        for (const text of [
            `月額${price()}円のテスト用の継続契約です。解約するまで毎月更新されますが、実際のお金は動きません。`,
            "期間末で解約しても、確認済みの利用期限まではProを使えます。無料へ戻った後も既存メンバー・タスク・経験値・ごほうびは残り、無料枠を超える新規参加だけが制限されます。",
            "人数は招待コードを送った数ではなく、現在の参加者数です。サービス全体の登録上限・参加チーム数の上限は別に適用されます。"
        ]) list.append(node("li", text));
        notes.append(list);
        const help = node("details"); help.append(node("summary", "テスト決済に使う情報"), node("p", "カード番号：4242 4242 4242 4242 ／ 有効期限：未来の日付 ／ CVC：任意の3桁。実カードや実際の個人情報は入力しないでください。")); notes.append(help);
        const legal = node("p", null, "billing-fine-print");
        for (const [label, href] of [["利用規約", "terms.html"], ["プライバシーポリシー", "privacy.html"]]) { const link = node("a", label); link.href = href; link.target = "_blank"; link.rel = "noopener noreferrer"; link.setAttribute("aria-label", label + "（新しいタブで開く）"); legal.append(link, document.createTextNode("　")); }
        notes.append(legal); main.append(notes);
        aside.append(node("h4", "購入内容", "billing-section-heading")); targetSummary(aside);
        const total = node("div", null, "billing-order-total"); total.append(node("span", "アカウントPro / 1アカウント"), node("strong", `月額${price()}円`), node("span", "テスト価格・実請求なし")); aside.append(total);
        const destination = node("section", null, "billing-destination"); destination.id = "billing-destination";
        destination.append(node("span", "次のステップ", "billing-label"), node("h4", simulation ? "カード入力を試したい方へ" : "カード情報はStripeで入力"));
        if (simulation) {
            destination.append(node("p", "このデモではカード入力画面は開きません。Stripeのテスト決済を試すには、通常のアプリにログインして、自分が所有するチームを選んでください。"));
            const login = node("a", "ログインしてStripeのテスト決済を試す →", "billing-login-link"); login.id = "billing-login-link"; login.href = "login.html"; destination.append(login);
        } else {
            destination.append(node("p", "ボタンを押すと、Stripe Checkoutのカード入力画面へ移動します。カード番号・有効期限・CVCはそちらで入力します。"), node("p", "カード情報をこのアプリで入力・保存することはありません。", "billing-fine-print"));
        }
        aside.append(destination); grid.append(main, aside); flow.append(grid);
        if (!data.isOwner) flow.append(node("p", "お申し込みはチームの所有者だけが行えます。", "billing-notice"));
        else if (!data.checkoutAvailable) flow.append(node("p", "Stripe未接続のため、現在はテスト決済を開始できません。チーム管理へ戻り、接続設定後に再度開いてください。", "billing-notice"));
        else if (data.hasContract && data.status !== "checkout") flow.append(node("p", "このアカウントには契約があります。追加購入は不要です。状態の確認や解約は契約管理で行えます。", "billing-notice"));
        const footer = node("div", null, "billing-flow-footer");
        const footerPrice = node("div", null, "billing-footer-price"); footerPrice.append(node("span", simulation ? "画面シミュレーション" : "Stripe Sandbox / 実請求なし"), node("strong", `月額${price()}円`)); footer.append(footerPrice);
        footer.append(button(detail.id === 0 ? "契約管理へ戻る" : "チーム管理へ戻る", "billing-flow-back", () => closeFlow()));
        if (data.isOwner && (!data.hasContract || data.status === "checkout")) {
            const checkout = button(simulation ? "Proへの変更をシミュレーション" : data.status === "checkout" ? "Stripeのカード入力を再開 →" : "Stripeでカード入力へ →", "billing-purchase-checkout", () => run("checkout"), !canCheckout());
            checkout.classList.add("billing-primary"); footer.append(checkout);
        }
        flow.append(node("p", simulation ? "次の操作はブラウザー内のシミュレーションです。再読み込みでリセットされます。" : "次はStripeのテスト決済画面です。カード情報はこのアプリでは入力・保存しません。", "billing-fine-print"), footer);
    }
    function renderResult() {
        const data = current, simulation = data.simulation === true, pro = data.plan === "pro";
        // Only the current team's server response (or explicitly labelled offline demo) grants this display.
        const ended = ["canceled", "expired", "incomplete_expired"].includes(data.status);
        const failed = ["past_due", "unpaid", "incomplete", "paused"].includes(data.status);
        const title = pro ? "所有する全チームがProになりました" : returnKind === "cancel" ? "決済画面から戻りました" : ended ? "この決済・契約は終了しています" : failed ? "支払いの完了を確認できませんでした" : "Proへの変更は、まだ確認できていません";
        flow.dataset.result = pro ? "pro" : returnKind === "cancel" ? "cancel" : ended ? "ended" : failed ? "incomplete" : "pending";
        const mark = node("span", pro ? "✓" : "…", "billing-result-mark"); mark.setAttribute("aria-hidden", "true"); flow.append(mark);
        flowTitle(title);
        flow.append(node("p", pro ? (simulation ? "シミュレーションでProの状態になりました。実際の購入ではありません。" : "サーバーでアカウントProの有効状態を確認しました。仲間を招待して、同じボードで作業を始めましょう。") : "画面から戻っただけでは購入完了にはなりません。支払い後は反映に時間がかかる場合があります。最新の契約状態を確認してください。", "billing-lead"));
        targetSummary();
        if (pro) flow.append(node("p", `アカウントPro · 月額${price()}円（テスト価格）`, "billing-result-plan"), node("p", `確認済みの利用期限：${formatDate(data.paidThrough)}`, "billing-fine-print"));
        if (pro && data.cancelAtPeriodEnd) flow.append(node("p", "期間末で解約予定です。確認済みの利用期限まではProを使えます。", "billing-notice"));
        if (!pro && !data.checkoutAvailable) flow.append(node("p", "Stripeに接続できる設定ではありません。チーム管理で設定状況を確認してください。", "billing-notice"));
        const actions = node("div", null, "billing-flow-footer");
        actions.append(button(detail.id === 0 ? "契約管理へ戻る" : "チーム管理へ戻る", "billing-flow-back", () => closeFlow()));
        if (pro && data.isOwner && detail.id !== 0) {
            const invite = button("メンバーを招待する", "billing-result-invite", () => {
                closeFlow(false); const target = document.getElementById("team-invite-button"); target.scrollIntoView({ block: "center" }); target.focus();
                setOptionsMessage("team-message", "「招待コードを再発行」からコードを作り、メンバーへ送ってください。再発行すると以前のコードは無効になります。");
            }); invite.classList.add("billing-primary"); actions.append(invite);
        } else if (data.isOwner && !pro) {
            const sync = button("最新の契約状態を確認", "billing-result-sync", () => run("sync"), !data.checkoutAvailable); sync.classList.add("billing-primary"); actions.append(sync);
            if (canCheckout()) actions.append(button(data.status === "checkout" ? "同じ決済を再開する" : "プランを確認する", "billing-result-review", () => openFlow("purchase")));
        }
        flow.append(node("p", "既存のメンバー・タスク・経験値・ごほうびは維持されます。", "billing-fine-print"), actions);
    }
    async function show(team) {
        reset(); if (!team) return;
        if (team.id === 0) { root = accountRoot; document.getElementById("team-modal").classList.add("account-billing-open"); document.getElementById("team-title").textContent = "アカウント · プラン・契約管理"; }
        detail = team; const version = generation; root.hidden = false; root.append(node("p", "チームプランを確認しています…"));
        try {
            const data = await request(apiPath(team));
            if (version !== generation || !inScope(team)) return;
            if (data.teamId !== team.id) throw new Error("対象チームを確認できませんでした。");
            current = data; render();
        } catch (error) {
            if (version !== generation || !inScope(team)) return;
            root.replaceChildren(node("p", getDisplayErrorMessage(error, "チームプランの確認に失敗しました。"), "billing-error"), button("プランを再読み込み", "billing-retry", () => show(team)));
        }
    }
    async function showAccount() { await show({ id: 0, name: "自分のアカウント" }); }
    function render() {
        if (!current || !detail) return;
        const data = current, simulation = data.simulation === true;
        root.replaceChildren();
        const header = node("div", null, "billing-heading");
        header.append(node("h3", data.plan === "pro" ? "アカウントPro" : "無料プラン"), node("span", simulation ? "画面シミュレーション" : "テスト決済専用", "billing-badge")); root.append(header);
        root.append(node("p", detail.id === 0 ? `所有チーム：${data.ownedTeamCount ?? 0}件 · 新しく作るチームにも自動適用` : `参加人数：${data.memberCount} / ${data.memberLimit == null ? "プラン上限なし" : data.memberLimit + "人（所有者を含む）"}`, "billing-count"));
        root.append(node("p", "実際の請求は発生しません。既存のメンバー・タスク・経験値・ごほうびは維持されます。", "field-hint"));
        if (simulation) root.append(node("p", "このお試しはStripeに接続しません。状態変化だけをブラウザー内で再現し、再読み込みでリセットします。", "field-hint"));
        else if (!data.checkoutAvailable) root.append(node("p", "Stripe未接続：テスト用のキー・月額価格・Webhookの設定後に決済を試せます。", "billing-notice"));
        if (!data.canJoin) root.append(node("p", "現在は無料枠に達しているため、新しいメンバーは参加できません。既存メンバーは引き続き作業できます。", "billing-notice"));
        const status = { free: "未契約", checkout: "決済の完了待ち", active: "契約中", past_due: "更新の支払いを確認できません", canceled: "契約終了", incomplete: "支払い未完了", incomplete_expired: "未完了のまま失効", expired: "決済の有効期限終了", unpaid: "支払い未確認", paused: "一時停止中" }[data.status] || "状態を確認してください";
        root.append(node("p", `状態：${status}${data.cancelAtPeriodEnd ? "／期間末で解約予定" : ""}`));
        if (data.paidThrough) root.append(node("p", `支払い確認済みの利用期限：${formatDate(data.paidThrough)}`, "field-hint"));
        root.append(node("p", `アカウントPro：月額${Number(data.monthlyYen).toLocaleString("ja-JP")}円（テスト価格・実請求なし）。所有するすべてのチームが対象です。他の人が所有するチームは対象外です。`, "field-hint"));
        const actions = node("div", null, "billing-actions"); root.append(actions);
        if (!data.isOwner) actions.append(node("p", "購入・契約管理はチームの所有者だけが行えます。"), button("プラン内容を見る", "billing-plan-view", () => openFlow("purchase")));
        else {
            if (!data.hasContract || data.status === "checkout") actions.append(button(data.status === "checkout" ? "未完了の決済を確認する" : "アカウントProのプランを見る", "billing-checkout", () => openFlow("purchase")));
            else actions.append(button("プラン内容を見る", "billing-plan-view", () => openFlow("purchase")));
            if (data.status === "checkout") actions.append(button("未完了の決済を中止", "billing-abandon", () => run("abandon"), !data.checkoutAvailable));
            if (data.hasContract && data.status !== "checkout") {
                actions.append(button(data.cancelAtPeriodEnd ? "解約予約を取り消す" : "期間末で解約する", "billing-cancel", () => run(data.cancelAtPeriodEnd ? "resume" : "cancel"), !data.checkoutAvailable));
                const end = node("details", null, "billing-test-tools"); end.append(node("summary", "テスト用の契約終了"), node("p", "テストを片付けるため、契約を今すぐ終了できます。返金操作ではありません。無料枠超過時は新規参加だけ停止します。", "field-hint"), button("テスト契約を今すぐ終了", "billing-end-now", () => run("end_now"), !data.checkoutAvailable)); root.append(end);
            }
            actions.append(button("最新の契約状態を確認", "billing-sync", () => run("sync"), !data.checkoutAvailable));
        }
        root.append(node("p", "所有者を変更すると、新しい所有者のプランが適用されます。チームを削除してもアカウントの契約は続きます。契約終了・退会は設定 → アカウントから行ってください。", "field-hint"));
        const help = node("details"); help.append(node("summary", "テスト方法と人数の数え方"), node("p", "無料は3人、Proはプラン上の人数制限なし。サービス全体の登録上限・参加チーム数の上限・不正利用対策は別に適用されます。人数は招待コードの送付数ではなく、現在の参加者数です。", "field-hint"), node("p", "Stripeではテストカード 4242 4242 4242 4242、有効期限は未来、CVCは任意の3桁を使用します。実際のカード番号・個人情報は入力しないでください。", "field-hint")); root.append(help);
        if (simulation && detail.id !== 0) {
            const demo = node("div", null, "billing-actions"); demo.append(button("見本メンバーの参加を試す", "billing-demo-member", () => run("demo-member")), button("更新失敗・期限切れを再現", "billing-demo-fail", () => run("demo-fail"), !data.hasContract)); root.append(demo);
        }
        restoreDisabled(); renderFlow();
    }
    async function run(action) {
        if (!detail || !current || !inScope(detail) || !current.isOwner || optionsState.busy || isTaskBusy()) return;
        const team = detail, version = generation;
        if (action === "checkout" && (view !== "purchase" || !canCheckout())) return;
        if (["cancel", "end_now", "abandon"].includes(action) && !confirm(action === "end_now" ? "アカウントのテスト契約を今すぐ終了しますか？所有するすべてのチームが無料へ戻りますが、既存メンバー・タスクは残ります。" : action === "abandon" ? "未完了のテスト決済を中止しますか？" : "所有するすべてのチームに適用されている契約を、確認済み期間の終了時に解約しますか？")) return;
        await performTeamAction(async () => {
            restoreDisabled(); setFlowMessage("処理中です。画面をそのままお待ちください。");
            let result;
            try { result = await request(`${apiPath(team)}/${action}`, "POST"); }
            catch (error) {
                if (version === generation && inScope(team)) setFlowMessage(getDisplayErrorMessage(error, "決済の状態を確認できませんでした。再度お試しください。"), true);
                throw error;
            }
            if (version !== generation || !inScope(team)) return "表示先が変わりました。対象チームで状態を確認してください。";
            if (!result.billing || result.billing.teamId !== team.id) { setFlowMessage("対象チームを確認できません。チーム管理から再読み込みしてください。", true); throw new Error("対象チームを確認できませんでした。"); }
            current = result.billing; flowMessage = ""; flowError = false;
            if (result.url) {
                let url;
                try {
                    url = new URL(result.url);
                    if (url.protocol !== "https:" || url.hostname !== "checkout.stripe.com" || url.port || url.username || url.password) throw new Error("Invalid destination");
                } catch {
                    render(); setFlowMessage("決済先URLを確認できませんでした。チーム管理から状態を確認してください。", true);
                    throw new Error("決済先URLを確認できませんでした。");
                }
                render(); setFlowMessage("Stripeのテスト決済画面を開きます…");
                window.location.assign(url.href);
                return "Stripeのテスト決済画面を開きます…";
            }
            if (action === "checkout") { view = "result"; returnKind = "return"; }
            render(); if (view) focusFlow();
            if (action === "demo-member") await loadTeamDetail(team.id);
            return "チームプランの状態を確認しました。";
        });
        restoreDisabled();
    }
    async function handleReturn() {
        const url = new URL(window.location.href), result = url.searchParams.get("billing"), id = Number(url.searchParams.get("team"));
        if (!["return", "cancel", "plans"].includes(result)) return;
        const accountReturn = url.searchParams.get("account") === "1";
        url.searchParams.delete("billing"); url.searchParams.delete("team"); url.searchParams.delete("account"); window.history.replaceState(null, "", url);
        if (accountReturn) {
            await openAccountBillingModal();
            if (detail?.id === 0 && current) openFlow(result === "plans" ? "purchase" : "result", result);
            setOptionsMessage("team-message", result === "plans" ? "アカウント全体のプランを表示しています。" : "Stripeから戻りました。URLだけでは購入済みと判定しません。最新の契約状態を確認してください。");
            return;
        }
        if (!Number.isSafeInteger(id) || !optionsState.teams.some(t => t.id === id)) { setMessage("決済元のチームを確認できません。設定から対象チームを選んでください。", true); return; }
        await switchWorkspace(id); await openTeamModal("manage");
        if (optionsState.teamDetail?.id === id) await show(optionsState.teamDetail);
        if (detail?.id === id && current) openFlow(result === "plans" ? "purchase" : "result", result);
        setOptionsMessage("team-message", result === "plans" ? "対象チームのプラン内容を表示しています。" : result === "return" ? "Stripeから戻りました。URLだけでは購入済みと判定しません。最新の契約状態を確認してください。" : "決済画面から戻りました。未完了の決済は再開または中止できます。");
    }
    return { show, showAccount, reset, restoreDisabled, handleReturn };
})();
