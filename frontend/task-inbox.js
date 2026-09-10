// Request overview. List actions use the version that the user actually saw.
const TaskInbox = (() => {
    const el = id => document.getElementById(id), panel = el("work-inbox-panel");
    const node = (tag, text, cls) => { const n = document.createElement(tag); if (text != null) n.textContent = text; if (cls) n.className = cls; return n; };
    const button = (text, action) => { const b = node("button", text); b.type = "button"; b.addEventListener("click", action); return b; };
    const kinds = { decision:"仕様の相談", review:"レビュー", material:"素材の依頼", together:"作業の相談" };
    let view = "incoming", data = null, initialized = false, pending = false, busy = false, sequence = 0, nextReadAt = 0, renderRequested = false;
    const isOpen = () => !panel.hidden;
    const message = text => { el("work-inbox-message").textContent = text; };
    function actionLabel(action, item) {
        return { help_offer:"引き受ける", help_withdraw:"対応を辞退", help_resolve:"解決済みにする", help_cancel:"依頼を取り下げる",
            handoff_accept:item.assignOnAccept ? "担当を引き継ぐ" : "確認を引き受ける", handoff_cancel:"依頼を取り下げる" }[action];
    }
    function statusLabel(item) {
        if (item.status === "question") return "質問への返答待ち";
        if (item.status === "helping") return `${item.helperName || "メンバー"}が対応中`;
        return view === "sent" ? "返答待ち" : item.audience === "team" ? "チームで募集中" : "あなたへの依頼";
    }
    function render() {
        const root = el("work-inbox-items"); root.replaceChildren();
        for (const name of ["incoming", "helping", "sent"]) el(`inbox-view-${name}`).setAttribute("aria-pressed", String(name === view));
        if (!data) return;
        if (!data.items.length) {
            const empty = node("div", null, "inbox-empty");
            empty.append(node("h3", { incoming:"新しい依頼はありません", helping:"対応中の依頼はありません", sent:"未完了の依頼はありません" }[view]),
                node("p", view === "sent" ? "チームのタスクカードから「お助けを依頼」を選んで送信できます。" : view === "helping" ? "引き受けたお助け依頼はここに表示されます。" : "あなた宛ての依頼と、チーム全員への募集がここに届きます。"));
            root.append(empty); return;
        }
        for (const item of data.items) {
            const card = node("article", null, "work-inbox-item"); card.dataset.requestId = item.id;
            const meta = node("div", null, "inbox-item-meta");
            meta.append(node("span", item.kind === "help" ? kinds[item.requestKind] || "お助け依頼" : item.assignOnAccept ? "担当の引き継ぎ" : "確認依頼", "inbox-kind"),
                node("span", statusLabel(item), "inbox-state"), node("span", item.teamName));
            card.append(meta, node("h3", item.taskTitle), node("p", item.message, "inbox-request-text"));
            if (item.criteria) card.append(node("p", `完了の条件：${item.criteria}`, "inbox-people"));
            const people = item.status === "question" ? `${item.recipientName} → ${item.authorName}`
                : `${item.authorName || "メンバー"} → ${item.recipientName || "チーム全員"}`;
            card.append(node("p", people, "inbox-people"));
            if (view === "helping") card.append(node("p", "解決済みへの変更は、依頼者またはチーム管理者が行います。", "inbox-people"));
            const row = node("div", null, "work-actions");
            for (const action of item.actions || []) {
                const label = actionLabel(action, item); if (!label) continue;
                const control = button(label, () => void respond(item, action));
                control.dataset.inboxAction = action; control.setAttribute("aria-label", `${item.taskTitle}：${label}`);
                if (["help_offer", "help_resolve", "handoff_accept"].includes(action)) control.className = "inbox-primary";
                row.append(control);
            }
            const detail = button(item.status === "question" && view !== "helping" ? "質問を確認・返信" : "詳細・メモ", () => void openDetails(item));
            detail.setAttribute("aria-label", `${item.taskTitle}の依頼詳細`); row.append(detail); card.append(row); root.append(card);
        }
    }
    function counts(result) {
        const incoming = result.directCount + result.teamCount;
        el("work-inbox-count").textContent = String(incoming);
        el("work-inbox-open").setAttribute("aria-label", `お助け依頼：届いた依頼${incoming}件`);
        el("work-inbox-status").textContent = incoming ? `届いた依頼 ${incoming}件` : "";
        for (const [name, count] of [["incoming", incoming], ["helping", result.helpingCount || 0], ["sent", result.sentCount || 0]])
            el(`inbox-count-${name}`).textContent = String(count);
    }
    async function refresh(redraw = false, notice = "") {
        if (redraw) renderRequested = true;
        if (!initialized || pending || busy || document.hidden) return;
        const version = ++sequence, requestedView = view;
        pending = true; el("work-inbox-refresh").disabled = true; el("work-inbox-items").setAttribute("aria-busy", "true");
        try {
            const response = await TaskAuth.request(`/api/work-inbox?view=${requestedView}`);
            if (version !== sequence) return;
            if (!response.ok) {
                if (response.status === 429) nextReadAt = Date.now() + 60000;
                throw new Error(await getErrorMessage(response, "依頼を読み込めませんでした。更新して再試行してください。"));
            }
            const result = await response.json(); if (version !== sequence) return;
            const changed = JSON.stringify(data) !== JSON.stringify(result); data = result; counts(result);
            if (isOpen() && renderRequested) { render(); message(notice); }
            else if (isOpen() && changed) message("依頼に変更があります。更新すると最新の内容を表示します。");
        } catch (error) {
            if (version !== sequence) return;
            data = null; el("work-inbox-items").replaceChildren(); el("work-inbox-count").textContent = "!";
            for (const name of ["incoming", "helping", "sent"]) el(`inbox-count-${name}`).textContent = "—";
            el("work-inbox-open").setAttribute("aria-label", "お助け依頼：読み込みに失敗しました");
            const text = getDisplayErrorMessage(error, "依頼を読み込めませんでした。更新して再試行してください。");
            el("work-inbox-status").textContent = text; message(notice ? `${notice} ${text}` : text);
        } finally {
            if (version === sequence) { pending = false; renderRequested = false; el("work-inbox-refresh").disabled = false; el("work-inbox-items").setAttribute("aria-busy", "false"); }
        }
    }
    async function respond(item, action) {
        if (busy || isTaskBusy() || !item.actions?.includes(action)) return;
        sequence++; pending = false; renderRequested = false;
        busy = true; state.isTaskMutation = true;
        panel.querySelectorAll("button").forEach(control => { control.disabled = true; }); message("保存中…");
        let notice;
        try {
            const response = await TaskAuth.request(`/api/teams/${item.teamId}/companion/tasks/${item.taskId}`, { method:"POST",
                headers:{"Content-Type":"application/json"}, body:JSON.stringify({action, entryId:item.id, version:item.version, expectedUpdatedAt:item.updatedAt}) });
            if (!response.ok) {
                if (response.status === 409) throw new Error("依頼の内容が変わったため、操作できませんでした。最新の内容を確認して、もう一度操作してください。");
                throw new Error(await getErrorMessage(response, "保存できませんでした。更新して状態を確認してください。"));
            }
            notice = { help_offer:"引き受けました。「対応中」で確認できます。", help_withdraw:"対応を辞退しました。", help_resolve:"解決済みにしました。",
                help_cancel:"依頼を取り下げました。", handoff_cancel:"依頼を取り下げました。", handoff_accept:item.assignOnAccept ? "あなたを担当者に変更しました。" : "確認依頼を引き受けました。" }[action];
            if (state.teamId === item.teamId) await loadTasks();
        } catch (error) { notice = getDisplayErrorMessage(error, "保存結果を確認できませんでした。更新して状態を確認してください。"); }
        finally {
            busy = false; state.isTaskMutation = false; panel.querySelectorAll("button").forEach(control => { control.disabled = false; });
            await refresh(true, notice); el("work-inbox-message").focus();
        }
    }
    async function openDetails(item) {
        if (busy || isTaskBusy()) return;
        await TaskCompanion.openTask(item.taskId, item.kind, { teamId:item.teamId, fromInbox:true });
    }
    function open() {
        if (isTaskBusy() || TaskCompanion.isOpen) return;
        panel.hidden = false; document.body.classList.add("is-inbox-view"); el("work-inbox-open").setAttribute("aria-expanded", "true");
        render(); message("読み込み中…"); el("work-inbox-title").focus(); void refresh(true);
    }
    function close() {
        if (busy) return;
        panel.hidden = true; document.body.classList.remove("is-inbox-view"); el("work-inbox-open").setAttribute("aria-expanded", "false"); el("work-inbox-open").focus();
    }
    function selectView(next) {
        if (busy || next === view) return;
        view = next; sequence++; pending = false; data = null; render(); message("読み込み中…"); void refresh(true);
    }
    el("work-inbox-open").addEventListener("click", open);
    el("work-inbox-close").addEventListener("click", close);
    el("work-inbox-refresh").addEventListener("click", () => void refresh(true));
    for (const name of ["incoming", "helping", "sent"]) el(`inbox-view-${name}`).addEventListener("click", () => selectView(name));
    setInterval(() => { if (Date.now() >= nextReadAt) void refresh(); }, 30000);
    document.addEventListener("visibilitychange", () => { if (!document.hidden && Date.now() >= nextReadAt) void refresh(); });
    window.addEventListener("online", () => void refresh(isOpen()));
    return { initialize() { initialized = true; void refresh(); }, refresh, open, close, get isOpen() { return isOpen(); } };
})();
