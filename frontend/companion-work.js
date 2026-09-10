// Opt-in tools: no extra board panel, automatic sharing or external AI.
const TaskCompanion = (() => {
    const el = id => document.getElementById(id);
    const dialog = el("companion-dialog"), content = el("companion-content"), nav = el("companion-tabs");
    const tabs = { notes: "作業メモ", help: "お助け依頼", handoff: "確認・引き継ぎ" };
    const helpKinds = { decision: "仕様の相談", review: "レビュー", material: "素材の依頼", together: "作業の相談" };
    const decorations = { frame: "額縁", monitor: "モニター", book: "本" };
    let teamId = null, taskId = null, record = null, mode = "hub", tab = "notes", seq = 0, busy = false, loading = false;
    let originScope = null, returnTo = null, returnFocus = null;
    let drafts = new Map(), noteId = null, searchQuery = "", searchTags = "", changed = false;
    const node = (tag, text, cls) => { const n = document.createElement(tag); if (text != null) n.textContent = text; if (cls) n.className = cls; return n; };
    const button = (text, click, id) => { const b = node("button", text); b.type = "button"; if (id) b.id = id; b.addEventListener("click", click); return b; };
    const apiRoot = () => teamId ? `/api/teams/${teamId}/companion` : "/api/companion";
    const key = () => `${teamId ?? "personal"}/${taskId}/${tab}`;
    const person = id => record?.people?.[id] || "退出・退会したメンバー";
    const canManage = author => author === record?.viewerId || record?.isTeamOwner;
    const scopeName = () => teamId ? `チーム：${optionsState.teams.find(t => t.id === teamId)?.name || "選択中のチーム"}` : "個人（非公開）";
    const date = value => value ? new Date(value).toLocaleString("ja-JP", { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit", timeZone: "Asia/Tokyo" }) + " JST" : "";
    function message(text = "", error = false) { el("companion-message").textContent = text; el("companion-message").classList.toggle("error", error); }
    async function get(url, options) {
        const response = await TaskAuth.request(url, options);
        if (!response.ok) throw new Error(await getErrorMessage(response, "読み込み・保存に失敗しました。"));
        return response.json();
    }
    function rememberDraft() {
        const form = el("companion-form");
        if (form && form.dataset.dirty === "true") drafts.set(key(), Object.fromEntries(new FormData(form).entries()));
    }
    function allowDiscard() { rememberDraft(); return !drafts.size || confirm("変更を保存せずに閉じますか？"); }
    function close(force = false) {
        if (busy || (!force && !allowDiscard())) return false;
        seq++; drafts.clear(); loading = false; el("companion-reload").disabled = false; dialog.close(); record = null;
        if (changed) { changed = false; if (teamId === state.teamId) void loadTasks(); }
        if (returnTo === "inbox" && typeof TaskInbox !== "undefined") {
            void TaskInbox.refresh(true); el("work-inbox-title").focus({ preventScroll:true });
        } else if (returnFocus?.isConnected) returnFocus.focus();
        return true;
    }
    function resetScope() { if (dialog.open) close(true); seq++; drafts.clear(); record = null; }
    function setup(nextMode) {
        mode = nextMode; loading = false;
        el("companion-scope").textContent = scopeName();
        el("companion-back").hidden = !returnTo;
        el("companion-back").textContent = returnTo === "inbox" ? "← お助け依頼に戻る" : "← メモ一覧に戻る";
        if (!dialog.open) { returnFocus = document.activeElement; dialog.showModal(); }
        nav.replaceChildren(); content.replaceChildren();
        el("companion-title").textContent = mode === "task" ? "タスクの詳細" : mode === "search" ? "作業メモを検索" : "作業メモ";
    }
    async function openHub() {
        if (busy || isTaskBusy() || optionsState.busy) return;
        if (dialog.open && !allowDiscard()) return;
        drafts.clear(); originScope = state.teamId; teamId = state.teamId; returnTo = null; taskId = null; record = null; setup("hub"); await refresh();
    }
    async function openTask(id, nextTab = "notes", options = {}) {
        if (busy || isTaskBusy()) return;
        if (dialog.open && taskId !== id && !allowDiscard()) return;
        if (taskId !== id) drafts.clear();
        returnTo = options.fromInbox ? "inbox" : dialog.open && mode === "hub" ? "hub" : null;
        originScope = state.teamId; teamId = options.teamId ?? state.teamId; taskId = id; tab = nextTab; noteId = null; record = null; setup("task"); await refresh();
    }
    async function openSearch() {
        if (busy || isTaskBusy()) return;
        drafts.clear(); originScope = state.teamId; teamId = state.teamId; returnTo = null; taskId = null; record = null;
        searchQuery = titleInput.value.trim().slice(0, 200); searchTags = tagsInput.value;
        setup("search"); message(); renderSearch(); await searchNotes(searchQuery, searchTags, null, el("companion-search-results"));
    }
    async function refresh() {
        if (busy || !dialog.open) return;
        rememberDraft(); const version = ++seq, scope = teamId;
        loading = true; el("companion-reload").disabled = true; message("読み込み中…");
        try {
            if (mode === "search") { renderSearch(); await searchNotes(searchQuery, searchTags, null, el("companion-search-results")); message(); return; }
            const data = await get(mode === "task" ? `${apiRoot()}/tasks/${taskId}` : apiRoot());
            if (version !== seq || originScope !== state.teamId || !dialog.open) return;
            message(drafts.size ? "最新の内容を読み込みました。編集中の入力は保持しています。" : "");
            if (mode === "task") { record = data; el("companion-title").textContent = data.taskTitle; renderTask(); }
            else renderHub(data);
        } catch (error) {
            if (version === seq) message(getDisplayErrorMessage(error, "読み込めませんでした。「更新」で再試行してください。"), true);
        } finally { if (version === seq) { loading = false; el("companion-reload").disabled = false; } }
    }
    function caption(text) { content.append(node("p", text, "work-caption")); }
    function link(url, parent) {
        if (!url) return;
        try { const u = new URL(url); if (!["http:", "https:"].includes(u.protocol) || u.username || u.password) return; } catch { return; }
        const a = node("a", "資料・成果物を開く ↗"); a.href = url; a.target = "_blank"; a.rel = "noopener noreferrer"; a.referrerPolicy = "no-referrer"; parent.append(a);
    }
    function card(title, parent = content) { const n = node("article", null, "work-card"); n.append(node("h3", title)); parent.append(n); return n; }
    function actions(parent, items) { const row = node("div", null, "work-actions"); for (const [label, click, id] of items) row.append(button(label, click, id)); parent.append(row); }
    function field(form, name, label, value = "", max = 400, multiline = true, required = false) {
        const wrap = node("label", label), input = node(multiline ? "textarea" : "input");
        input.name = name; input.id = `work-${name}`; if (!multiline) input.type = name === "resourceUrl" ? "url" : "text";
        input.maxLength = max; input.value = value || ""; input.required = required;
        if (name === "resourceUrl") input.placeholder = "https://example.com/";
        wrap.append(input); form.append(wrap); return input;
    }
    function select(form, name, label, entries, value = "") {
        const wrap = node("label", label), input = node("select"); input.name = name; input.id = `work-${name}`;
        for (const [id, text] of Array.isArray(entries) ? entries : Object.entries(entries)) input.add(new Option(text, id));
        input.value = String(value); wrap.append(input); form.append(wrap); return input;
    }
    function makeForm(saveLabel, action, fill, extras = {}) {
        const form = node("form", null, "work-form"); form.id = "companion-form";
        fill(form);
        for (const [name, value] of Object.entries(extras)) { const input = node("input"); input.type = "hidden"; input.name = name; input.value = value; form.append(input); }
        const save = node("button", saveLabel); save.type = "submit"; save.id = "companion-save"; form.append(save);
        const draft = drafts.get(key());
        if (draft) { for (const [name, value] of Object.entries(draft)) { const input = form.elements.namedItem(name); if (input) input.value = value; } form.dataset.dirty = "true"; }
        for (const type of ["input", "change"]) form.addEventListener(type, () => { form.dataset.dirty = "true"; rememberDraft(); });
        form.addEventListener("submit", event => {
            event.preventDefault(); if (!form.reportValidity()) return;
            const values = Object.fromEntries(new FormData(form).entries());
            if (Object.hasOwn(values, "recipientId")) values.recipientId = values.recipientId ? Number(values.recipientId) : null;
            if (Object.hasOwn(values, "assignOnAccept")) values.assignOnAccept = values.assignOnAccept === "true";
            void perform(action, values, true);
        });
        content.append(form); return form;
    }
    async function perform(action, values = {}, savedForm = false) {
        if (busy || loading || !record || originScope !== state.teamId) return;
        rememberDraft(); const before = record, version = ++seq, scope = teamId;
        busy = true; state.isTaskMutation = true;
        const controls = [...dialog.querySelectorAll("button,input,textarea,select")];
        const priorDisabled = controls.map(control => control.disabled); controls.forEach(control => control.disabled = true);
        message("保存中…");
        try {
            const updated = await get(`${apiRoot()}/tasks/${taskId}`, {
                method: "POST", headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ ...values, action, version: before.version, expectedUpdatedAt: before.updatedAt })
            });
            if (version !== seq || originScope !== state.teamId || !dialog.open) return;
            // Do not bless parent form fields across an unseen edit by someone else.
            if (scope === state.teamId && state.editingTask?.id === updated.taskId && state.editingTask.version === before.version && state.editingTask.updatedAt === before.updatedAt)
                state.editingTask = { ...state.editingTask, version: updated.version, updatedAt: updated.updatedAt };
            record = updated; if (savedForm) drafts.delete(key()); changed = true; noteId = null; renderTask();
            if (typeof TaskWorkflow !== "undefined") void TaskWorkflow.refreshInbox();
            const feedback = ({ help_open:"依頼を送信しました。", help_offer:"依頼を引き受けました。", help_withdraw:"対応を辞退しました。",
                help_resolve:"解決済みにしました。", help_cancel:"依頼を取り下げました。", handoff_send:"依頼を送信しました。",
                handoff_accept:updated.handoff?.assignOnAccept ? "あなたを担当者に変更しました。" : "確認依頼を引き受けました。",
                handoff_question:"質問を送信しました。", handoff_cancel:"依頼を取り下げました。",
                savepoint_save:"自分用メモを保存しました。", savepoint_clear:"自分用メモを削除しました。",
                showcase_save:"作品を保存しました。", note_delete:"メモを削除しました。" })[action] || "保存しました。";
            message(feedback); el("companion-message").focus();
        } catch (error) {
            if (version === seq) { message(getDisplayErrorMessage(error, "保存結果を確認できませんでした。入力は保持しています。「更新」で状態を確認してください。"), true); el("companion-message").focus(); }
        } finally {
            busy = false; state.isTaskMutation = false;
            controls.forEach((control, i) => { if (control.isConnected) control.disabled = priorDisabled[i]; });
        }
    }
    function renderTask() {
        if (!record) return;
        nav.replaceChildren(); content.replaceChildren();
        for (const [id, label] of Object.entries(tabs)) {
            if (!teamId && ["help", "handoff"].includes(id)) continue;
            const b = button(label, () => { if (busy || loading) return; rememberDraft(); tab = id; noteId = null; renderTask(); el(`work-tab-${id}`).focus(); }, `work-tab-${id}`);
            if (tab === id) b.setAttribute("aria-current", "page"); nav.append(b);
        }
        // Secondary/legacy tools stay reachable without five competing main tabs.
        const more = node("details", null, "work-secondary-tools"); more.append(node("summary", record.showcase ? "自分用メモ・保存済みの作品" : "自分用メモ（非公開）"));
        more.open = tab === "resume" || tab === "showcase";
        more.append(button("自分用メモ（非公開）", () => { if (busy || loading) return; rememberDraft(); tab = "resume"; renderTask(); el("work-tab-resume").focus(); }, "work-tab-resume"));
        if (record.showcase) more.append(button("保存済みの作品を確認", () => { if (busy || loading) return; rememberDraft(); tab = "showcase"; renderTask(); el("work-tab-showcase").focus(); }, "work-tab-showcase"));
        ({ resume: renderResume, help: renderHelp, handoff: renderHandoff, notes: renderNotes, showcase: renderShowcase }[tab] || renderResume)();
        content.append(more);
    }
    function renderResume() {
        caption("このメモは自分だけが閲覧できます。");
        const s = record.savepoint;
        if (s) {
            caption(`次にすること：${s.nextStep}`);
            const c = card("保存したメモ");
            if (s.summary) c.append(node("p", `ここまで：${s.summary}`)); c.append(node("small", date(s.savedAt))); link(s.resourceUrl, c);
            actions(c, [["タスクを編集", () => resumeTask(record.taskId), "work-resume-task"], ["メモを削除", () => { if (confirm("この自分用メモを削除しますか？")) perform("savepoint_clear"); }, "work-clear-savepoint"]]);
        }
        makeForm("メモを保存", "savepoint_save", form => {
            field(form, "nextStep", "次にすること", s?.nextStep, 400, true, true);
            field(form, "summary", "進捗メモ（任意）", s?.summary);
            field(form, "resourceUrl", "関連リンク（任意）", s?.resourceUrl, 1000, false);
        });
    }
    function helpRecipientLabel(help, people = record?.people || {}, viewer = record?.viewerId) {
        if (help.recipientUnavailable) return "指定したメンバー（退出・退会済み）";
        if (help.recipientId == null) return "チーム全員";
        return `${people[help.recipientId] || "退出・退会したメンバー"}${help.recipientId === viewer ? "（あなた）" : ""}`;
    }
    function renderHelp() {
        caption("内容はチーム全員に共有されます。宛先を指定すると、そのメンバーだけが引き受けられます。");
        if (record.thanks?.length) {
            const history = node("details", null, "work-hub-section"); history.append(node("summary", `解決済みの依頼（${record.thanks.length}）`)); content.append(history);
            for (const thanks of [...record.thanks].reverse()) { const c = card(helpKinds[thanks.kind] || "相談", history); c.append(node("p", thanks.message), node("small", `${person(thanks.authorId)} → ${thanks.helperId ? person(thanks.helperId) : "チーム全員"}／${date(thanks.closedAt)}`)); }
        }
        const h = record.help;
        if (h) {
            const c = card(helpKinds[h.kind] || "相談"); c.append(node("p", h.message));
            c.append(node("p", `宛先：${helpRecipientLabel(h)}`, "work-help-recipient"));
            c.append(node("small", `${person(h.authorId)}から／${h.status === "resolved" ? "解決済み" : h.status === "cancelled" ? h.recipientUnavailable ? "宛先の退出により終了" : "取り下げ済み" : h.helperId ? `${person(h.helperId)}が対応中` : h.recipientId ? "返答待ち" : "対応するメンバーを募集中"}`));

            if (h.status === "open") {
                const items = [];
                if (!h.helperId && h.authorId !== record.viewerId && !h.recipientUnavailable && (h.recipientId == null || h.recipientId === record.viewerId))
                    items.push(["引き受ける", () => perform("help_offer", { entryId: h.id }), "work-help-offer"]);
                if (h.helperId === record.viewerId) items.push(["対応を辞退", () => perform("help_withdraw", { entryId: h.id }), "work-help-withdraw"]);
                if (canManage(h.authorId)) items.push(["解決済みにする", () => perform("help_resolve", { entryId: h.id }), "work-help-resolve"], ["依頼を取り下げる", () => perform("help_cancel", { entryId: h.id }), "work-help-cancel"]);
                actions(c, items);
                if (canManage(h.authorId)) c.append(node("p", "宛先の変更は、依頼を取り下げてから行えます。", "work-caption"));
                return;
            }
        }
        makeForm("依頼を送信", "help_open", form => {
            const recipients = [["", "チーム全員"],
                ...Object.entries(record.people).filter(([id]) => Number(id) !== record.viewerId)];
            select(form, "recipientId", "依頼先", recipients, "");
            select(form, "kind", "依頼の種類", helpKinds, "review");
            field(form, "message", "依頼内容", "", 400, true, true);
        });
    }
    function renderHandoff() {
        caption("内容はチーム全員に共有されます。担当の引き継ぎは、相手が引き受けた時点で反映されます。");
        const h = record.handoff;
        if (h) {
            const c = card(`${h.assignOnAccept ? "担当の引き継ぎ" : "確認依頼"}：${person(h.fromUserId)} → ${person(h.toUserId)}`);
            for (const [label, value] of [["お願い", h.request], ["完了の条件", h.criteria], ["返答", h.reply]]) if (value) c.append(node("p", `${label}：${value}`));
            c.append(node("small", ({ sent: "返答待ち", accepted: "引き受け済み", question: "質問あり", cancelled: "終了" })[h.status])); link(h.resourceUrl, c);
            if (["sent", "question"].includes(h.status)) {
                if (canManage(h.fromUserId)) actions(c, [["依頼を取り下げる", () => perform("handoff_cancel", { entryId: h.id }), "work-handoff-cancel"]]);
                if (h.toUserId === record.viewerId) {
                    actions(c, [[h.assignOnAccept ? "担当を引き継ぐ" : "確認を引き受ける", () => perform("handoff_accept", { entryId: h.id }), "work-handoff-accept"]]);
                    makeForm("質問を送信", "handoff_question", form => field(form, "message", "質問内容", "", 400, true, true), { entryId: h.id });
                    return;
                }
                if (h.status === "sent" || !canManage(h.fromUserId)) return;
            }
        }
        const people = Object.fromEntries(Object.entries(record.people).filter(([id]) => Number(id) !== record.viewerId));
        if (!Object.keys(people).length) { content.append(node("p", "依頼できるメンバーがいません。設定の「タグ・チーム」から招待できます。", "work-empty")); return; }
        makeForm(h?.status === "question" ? "修正して再送信" : "依頼を送信", "handoff_send", form => {
            select(form, "assignOnAccept", "依頼の目的", { false: "確認だけ（担当者は変更しない）", true: "担当を引き継ぐ（相手の承諾時に変更）" }, String(h?.assignOnAccept || false));
            select(form, "recipientId", "依頼先", { "": "メンバーを選択", ...people }, h?.status === "question" ? h.toUserId : "").required = true;
            field(form, "resourceUrl", "資料のリンク（任意）", h?.status === "question" ? h.resourceUrl : "", 1000, false);
            field(form, "message", "依頼内容", h?.status === "question" ? h.request : "", 400, true, true);
            field(form, "criteria", "完了の条件", h?.status === "question" ? h.criteria : "", 400, true, true);
        });
    }
    function noteCard(item, authorName, parent = content, match) {
        const c = card(item.learned, parent); if (match) c.append(node("small", match)); if (item.tried) c.append(node("p", `補足・試したこと：${item.tried}`));
        if (item.nextStep) c.append(node("p", `次にすること：${item.nextStep}`));
        c.append(node("small", `${authorName}／${date(item.updatedAt || item.createdAt)}`)); link(item.resourceUrl, c); return c;
    }
    function renderNotes() {
        caption(`${teamId ? "このチームに共有" : "自分だけに保存"}する作業メモです。1タスクにつき12件まで保存できます。`);
        for (const item of record.notes) {
            const c = noteCard(item, person(item.authorId));
            if (canManage(item.authorId)) actions(c, [["編集", () => {
                rememberDraft(); if (drafts.has(key()) && !confirm("編集中の内容を破棄して、このメモを編集しますか？")) return;
                drafts.delete(key()); noteId = item.id; renderTask(); el("work-tried").focus();
            }, `work-note-edit-${item.id}`], ["削除", () => { if (confirm("この作業メモを削除しますか？タスク本体は残ります。")) perform("note_delete", { entryId: item.id }); }, `work-note-delete-${item.id}`]]);
        }
        const editing = record.notes.find(n => n.id === (drafts.get(key())?.entryId || noteId));
        if (record.notes.length < 12 || editing) makeForm(editing ? "メモを更新" : "メモを保存", editing ? "note_edit" : "note_add", form => {
            field(form, "learned", "メモ本文（必須）", editing?.learned, 400, true, true);
            field(form, "tried", "補足・試したこと（任意）", editing?.tried, 400);
            field(form, "nextStep", "次にすること（任意）", editing?.nextStep);
            field(form, "resourceUrl", "参考リンク（任意）", editing?.resourceUrl, 1000, false);
        }, editing ? { entryId: editing.id } : {});
        const related = node("details", null, "work-hub-section"); related.id = "work-related-notes"; related.append(node("summary", "関連する作業メモを検索"));
        const results = node("div"); related.append(results); content.append(related);
        related.addEventListener("toggle", () => { if (related.open) searchNotes(record.taskTitle, record.tags || "", record.taskId, results); });
    }
    function renderShowcase() {
        caption(`${teamId ? "このチームだけ" : "自分だけ"}が見られる作品棚です。DONEの成果を1点飾れます。画像などの成果物はリンク先で開きます。外部画像の自動取得・公開はしません。`);
        const s = record.showcase;
        if (s) {
            const c = card(`${decorations[s.kind]}：${s.title}`); c.append(node("p", s.outcome), node("small", `記録：${person(s.authorId)}`)); link(s.resourceUrl, c);
            if (canManage(s.authorId)) actions(c, [["棚から外す", () => { if (confirm("作品棚から外しますか？タスクと獲得済みの経験値は残ります。")) perform("showcase_remove"); }, "work-showcase-remove"]]);
        }
        if (record.taskStatus !== "Done" && record.taskStatus !== 2) { content.append(node("p", "このタスクはまだDONEではありません。完了すると飾れます。DONEを取り消すと棚から一時的に隠れ、作品の記録は残ります。", "work-empty")); return; }
        if (s && !canManage(s.authorId)) return;
        makeForm(s ? "作品を更新" : "作品を保存", "showcase_save", form => {
            select(form, "kind", "表示スタイル", decorations, s?.kind || "frame");
            field(form, "title", "作品名", s?.title || record.taskTitle.slice(0, 120), 120, false, true);
            field(form, "summary", "成果・工夫した点", s?.outcome, 400, true, true);
            field(form, "resourceUrl", "成果物・画像・資料のリンク（任意）", s?.resourceUrl, 1000, false);
        });
    }
    function hubSection(title, count, expanded = false) { const d = node("details", null, "work-hub-section"); d.open = expanded; d.append(node("summary", `${title}（${count}）`)); content.append(d); return d; }
    function hubEmpty(parent, text) { parent.append(node("p", text, "work-empty")); }
    function renderHub(data) {
        nav.replaceChildren(); content.replaceChildren();

        caption("タスクカードの「メモ」から作成できます。");
        const resume = hubSection("自分用メモ", data.savepoints.length, true);
        if (!data.savepoints.length) hubEmpty(resume, "自分用メモはまだありません。");
        for (const item of data.savepoints) { const c = card(item.taskTitle, resume); c.append(node("p", `次にすること：${item.savepoint.nextStep}`)); actions(c, [["タスクを編集", () => resumeTask(item.taskId), `work-resume-${item.taskId}`], ["メモを見る", () => openTask(item.taskId, "resume"), `work-bookmark-${item.taskId}`]]); }
        if (teamId) {
            const help = hubSection("お助け依頼", data.help.length, data.help.length > 0);
            if (!data.help.length) hubEmpty(help, "お助け依頼はありません。");
            for (const item of data.help) { const c = card(item.taskTitle, help); c.append(node("p", `宛先：${helpRecipientLabel(item.help, item.people, item.viewerId)}`, "work-help-recipient"), node("p", `${helpKinds[item.help.kind]}：${item.help.message}`)); actions(c, [["依頼の詳細", () => openTask(item.taskId, "help"), `work-open-help-${item.taskId}`]]); }
            const handoffs = hubSection("確認・引き継ぎ", data.handoffs.length, data.handoffs.length > 0);
            if (!data.handoffs.length) hubEmpty(handoffs, "確認・引き継ぎの依頼はありません。");
            for (const item of data.handoffs) { const c = card(item.taskTitle, handoffs); c.append(node("p", `${item.people[item.handoff.fromUserId] || "退出したメンバー"} → ${item.people[item.handoff.toUserId] || "退出したメンバー"}／${item.handoff.status === "question" ? "質問あり" : "返答待ち"}`)); actions(c, [["依頼の詳細", () => openTask(item.taskId, "handoff"), `work-open-handoff-${item.taskId}`]]); }
        }
        const shelf = data.showcase.length ? hubSection("保存済みの作品（従来の記録）", data.showcase.length) : node("div");
        if (!data.showcase.length) { /* No empty showcase promotion in the primary workflow. */ }
        else {
            const room = node("div", null, "work-shelf"); shelf.append(room);
            for (const item of data.showcase) { const object = node("div", null, `work-object ${Object.hasOwn(decorations, item.showcase.kind) ? item.showcase.kind : "frame"}`); object.append(button(item.showcase.title, () => openTask(item.taskId, "showcase"), `work-artifact-${item.taskId}`), node("small", decorations[item.showcase.kind] || "作品")); room.append(object); }
            const pet = node("div", null, "work-shelf-pet pet-preview");
            const species = ["cat", "dog", "rabbit", "fox", "panda", "dragon"].includes(state.petProfile?.species) ? state.petProfile.species : "cat";
            pet.classList.add(`pet-preview-${species}`); pet.setAttribute("aria-hidden", "true"); shelf.append(pet);
        }
        const notes = hubSection("最近の作業メモ", data.recentNotes.length, true);
        notes.append(node("p", `今週の作業メモ ${data.notesThisWeek}件（${data.weekStart}〜）`, "work-caption"));
        if (!data.recentNotes.length) hubEmpty(notes, "作業メモはまだありません。");
        for (const item of data.recentNotes) { const c = noteCard(item.note, item.authorName, notes, item.taskTitle); actions(c, [["メモの詳細", () => openTask(item.taskId, "notes")]]); }
        if (teamId && data.recentThanks?.length) {
            const thanks = hubSection("解決済みの依頼", data.recentThanks.length);
            for (const item of data.recentThanks) { const c = card(item.taskTitle, thanks); c.append(node("p", `${item.authorName} → ${item.helperName}`)); actions(c, [["依頼の詳細", () => openTask(item.taskId, "help")]]); }
        }
    }
    function renderSearch() {
        nav.replaceChildren(); content.replaceChildren();
        caption("選択中のワークスペースから、タグやキーワードで検索します。");
        const form = node("form", null, "work-form");
        field(form, "searchQuery", "キーワード", searchQuery, 200, false);
        field(form, "searchTags", "タグ（カンマ区切り）", searchTags, 300, false);
        const b = node("button", "作業メモを検索"); b.type = "submit"; form.append(b); content.append(form);
        const results = node("div"); results.id = "companion-search-results"; results.setAttribute("aria-live", "polite"); content.append(results);
        form.addEventListener("submit", event => { event.preventDefault(); searchQuery = form.elements.searchQuery.value; searchTags = form.elements.searchTags.value; searchNotes(searchQuery, searchTags, null, results); });
    }
    let searchSeq = 0;
    async function searchNotes(query, tags, excludeTaskId, parent) {
        const version = ++searchSeq, scope = teamId;
        parent.replaceChildren(node("p", "検索中…", "work-caption"));
        const params = new URLSearchParams({ q: query || "", tags: tags || "" }); if (excludeTaskId) params.set("excludeTaskId", excludeTaskId);
        try {
            const result = await get(`${apiRoot()}/notes?${params}`);
            if (version !== searchSeq || originScope !== state.teamId || !parent.isConnected) return;
            parent.replaceChildren(); if (!result.length) hubEmpty(parent, "一致するメモがありません。検索条件を変更してください。");
            for (const item of result) noteCard(item.note, item.authorName, parent, `${item.taskTitle}／${item.matchReason}`);
        } catch (error) { if (version === searchSeq && parent.isConnected) parent.replaceChildren(node("p", getDisplayErrorMessage(error, "検索に失敗しました。もう一度開くか検索してください。"), "work-empty")); }
    }
    async function resumeTask(id) {
        if (busy || loading || !allowDiscard()) return;
        const version = ++seq, scope = teamId; message("読み込み中…");
        try {
            const task = await get(`${getTasksApiUrl(scope)}/${id}`);
            if (version !== seq || originScope !== state.teamId || !dialog.open) return;
            if (state.editingTask && state.editingTask.id !== id && !confirm("編集中のタスクの未保存の入力を破棄して、続きを開きますか？")) return;
            if (state.editingTask?.id === id) { close(true); el("edit-checklist-details").open = true; el("edit-checklist-details").querySelector("summary").focus(); return; }
            close(true); if (!settingsModal.classList.contains("hidden")) closeOptionsModal(settingsModal);
            if (scope !== state.teamId) { if (typeof TaskInbox !== "undefined") TaskInbox.close(); await switchWorkspace(scope); }
            startEditTask(task); el("edit-checklist-details").open = true; el("edit-checklist-details").querySelector("summary").focus();
        } catch (error) { if (version === seq) message(getDisplayErrorMessage(error, "タスクを開けませんでした。"), true); }
    }
    el("companion-close").addEventListener("click", () => close());
    dialog.addEventListener("cancel", event => { event.preventDefault(); close(); });
    el("companion-reload").addEventListener("click", refresh);
    el("companion-back").addEventListener("click", () => { if (returnTo === "inbox") close(); else void openHub(); });
    el("edit-companion-open").addEventListener("click", () => { if (state.editingTask) openTask(state.editingTask.id); });
    el("companion-hub-open").addEventListener("click", openHub);
    el("create-find-work-notes").addEventListener("click", openSearch);
    return { openTask, openHub, resetScope, get isSaving() { return busy; }, get isOpen() { return dialog.open; } };
})();
