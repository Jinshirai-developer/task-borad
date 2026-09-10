// Extra controls live in existing dialogs. Background reads never overwrite drafts.
const TaskDetails = (() => {
    const el = (id) => document.getElementById(id);
    const forms = { create: taskForm, edit: editTaskForm };
    const versions = { create: 0, edit: 0 };
    let undo = null, undoTimer = null, pollPending = false, nextPollAt = 0, syncFailed = false;
    const copy = (items) => (items || []).map(item => ({ text: item.text, isCompleted: !!item.isCompleted }));

    function updateCount(prefix) {
        const rows = [...el(`${prefix}-checklist`).children];
        el(`${prefix}-checklist-count`).textContent = `${rows.filter(row => row.querySelector('input[type="checkbox"]').checked).length}/${rows.length}`;
        el(`${prefix}-checklist-add`).disabled = rows.length >= 20 || isTaskBusy();
    }
    function addRow(prefix, item = { text: "", isCompleted: false }, focus = false) {
        const list = el(`${prefix}-checklist`);
        if (list.children.length >= 20) return;
        const row = document.createElement("div"); row.className = "checklist-row";
        const check = document.createElement("input"); check.type = "checkbox"; check.checked = item.isCompleted;
        const input = document.createElement("input"); input.type = "text"; input.maxLength = 120; input.required = true; input.value = item.text;
        input.placeholder = "例：動作テストをする"; input.setAttribute("aria-label", "チェック項目の内容");
        const remove = document.createElement("button"); remove.type = "button"; remove.textContent = "×"; remove.setAttribute("aria-label", "このチェック項目を削除");
        const label = () => check.setAttribute("aria-label", `${input.value || "この項目"}の完了`);
        input.addEventListener("input", label); label();
        check.addEventListener("change", () => updateCount(prefix));
        remove.addEventListener("click", () => { if (isTaskBusy()) return; const next = row.nextElementSibling || row.previousElementSibling; row.remove(); updateCount(prefix); (next?.querySelector('input[type="text"]') || el(`${prefix}-checklist-add`)).focus(); });
        row.append(check, input, remove); list.append(row); updateCount(prefix);
        if (focus) input.focus();
    }
    async function open(prefix, task = null) {
        const version = ++versions[prefix], teamId = state.teamId;
        el(`${prefix}-checklist`).replaceChildren();
        for (const item of copy(task?.checklist)) addRow(prefix, item);
        updateCount(prefix);
        el(`${prefix}-checklist-details`).open = !!task?.checklist?.length;
        const select = el(`${prefix}-assignee`), note = el(`${prefix}-assignee-message`);
        el(`${prefix}-assignee-field`).hidden = !teamId;
        select.replaceChildren(new Option("未割り当て", ""));
        if (task?.assigneeUserProfileId) select.add(new Option(task.assigneeDisplayName || "現在の担当者", String(task.assigneeUserProfileId)));
        select.value = task?.assigneeUserProfileId ? String(task.assigneeUserProfileId) : "";
        select.disabled = !!teamId; note.textContent = "";
        if (!teamId) return;
        note.textContent = "メンバーを確認中です…";
        try {
            const response = await TaskAuth.request(`/api/teams/${teamId}`);
            if (version !== versions[prefix] || teamId !== state.teamId) return;
            if (!response.ok) throw new Error(await getErrorMessage(response, "メンバーを読み込めません。閉じて開き直すと再試行できます。"));
            const data = await response.json();
            if (version !== versions[prefix] || teamId !== state.teamId) return;
            select.replaceChildren(new Option("未割り当て", ""));
            for (const member of data.members || []) select.add(new Option(member.displayName, String(member.userProfileId)));
            select.value = [...select.options].some(option => option.value === String(task?.assigneeUserProfileId)) ? String(task.assigneeUserProfileId) : "";
            select.disabled = false; note.textContent = "チーム内の担当者を1人選べます。";
        } catch (error) {
            if (version === versions[prefix] && teamId === state.teamId) note.textContent = getDisplayErrorMessage(error, "メンバーを読み込めませんでした。");
        }
    }
    function read(prefix) {
        return {
            assigneeUserProfileId: state.teamId && el(`${prefix}-assignee`).value ? Number(el(`${prefix}-assignee`).value) : null,
            checklist: [...el(`${prefix}-checklist`).children].map(row => ({ text: row.querySelector('input[type="text"]').value.trim(), isCompleted: row.querySelector('input[type="checkbox"]').checked }))
        };
    }
    function validate(request) {
        const items = request.checklist;
        return items && (items.length > 20 || items.some(item => !item.text || item.text.length > 120 || /[\u0000-\u001f\u007f-\u009f]/.test(item.text)))
            ? "チェックリストは20項目まで、各項目は1〜120文字で入力してください。" : "";
    }
    function badges(task, container) {
        const values = [];
        if (state.teamId) values.push(`担当：${task.assigneeDisplayName || "未割り当て"}`);
        if (task.checklist?.length) values.push(`チェック ${task.checklist.filter(item => item.isCompleted).length}/${task.checklist.length}`);
        for (const value of values) { const badge = document.createElement("span"); badge.className = "task-detail-badge"; badge.textContent = value; container.append(badge); }
        for (const [show, label, tab] of [[task.needsHelp, "お助け依頼あり", "help"], [task.handoffPending, "確認・引き継ぎ待ち", "handoff"], [task.noteCount > 0, `メモ ${task.noteCount}件`, "notes"]]) {
            if (!show) continue;
            const link = document.createElement("button"); link.type = "button"; link.className = "task-work-badge"; link.textContent = label;
            link.setAttribute("aria-label", `${task.title}の${label}を開く`);
            link.addEventListener("click", () => { if (typeof TaskCompanion !== "undefined") void TaskCompanion.openTask(task.id, tab); });
            container.append(link);
        }
    }
    function clearUndo() {
        clearTimeout(undoTimer); undo = null;
        const hadFocus = el("task-undo-toast").contains(document.activeElement);
        el("task-undo-toast").hidden = true;
        if (hadFocus) openCreateTaskButton.focus();
    }
    function offerUndo(receipt, text, teamId = state.teamId) {
        clearUndo();
        if (!receipt?.token || teamId !== state.teamId) return;
        undo = { ...receipt, teamId };
        el("task-undo-message").textContent = `${text}（15秒以内に取り消せます）`;
        el("task-undo-button").disabled = false;
        el("task-undo-toast").hidden = false;
        undoTimer = setTimeout(clearUndo, 15000);
    }
    async function applyUndo() {
        if (!undo || isTaskBusy() || optionsState.busy || state.draggingTask) return;
        const current = undo;
        if (current.teamId !== state.teamId) return clearUndo();
        state.isTaskMutation = true; clearTimeout(undoTimer); el("task-undo-button").disabled = true;
        try {
            const response = await TaskAuth.request(`${getTasksApiUrl(current.teamId)}/undo/${encodeURIComponent(current.token)}`, { method: "POST" });
            if (!response.ok) throw new Error(await getErrorMessage(response, "元に戻せませんでした。"));
            clearUndo();
            const loaded = await loadTasks(); await refreshProgression();
            setMessage(loaded ? "操作を元に戻しました" : "操作は戻しました。表示を再読み込みしてください。", !loaded);
        } catch (error) {
            clearUndo();
            await loadTasks(); await refreshProgression();
            setMessage(getDisplayErrorMessage(error, "元に戻せませんでした。"), true);
        } finally { state.isTaskMutation = false; }
    }
    function canPoll() {
        return !!state.teamId && !document.hidden && !isTaskBusy() && !optionsState.busy && !optionsState.savingPreferences
            && !state.draggingTask && !document.querySelector('.modal-backdrop:not(.hidden)') && !document.getElementById("companion-dialog")?.open
            && !document.querySelector('.task-item:focus-within')
            && el("workspace-task-panel").getAttribute("aria-busy") !== "true";
    }
    async function poll() {
        if (pollPending || Date.now() < nextPollAt || !canPoll()) return;
        pollPending = true;
        const teamId = state.teamId, loadVersion = state.taskLoadVersion;
        const query = taskQueryParameters().toString();
        try {
            const response = await TaskAuth.request(`${getTasksApiUrl(teamId)}?${query}`);
            if (teamId !== state.teamId || loadVersion !== state.taskLoadVersion || query !== taskQueryParameters().toString() || !canPoll()) return;
            if (response.status === 429) nextPollAt = Date.now() + 60000;
            if (!response.ok) {
                if (response.status === 404) { renderTasks([]); await loadTeams(); }
                throw new Error(await getErrorMessage(response, "自動更新を一時停止しています。「更新」で再試行できます。"));
            }
            const data = await response.json();
            if (teamId !== state.teamId || loadVersion !== state.taskLoadVersion || query !== taskQueryParameters().toString() || !canPoll()) return;
            if (JSON.stringify(data.items) !== JSON.stringify(state.visibleTasks) || data.totalCount !== state.totalCount) {
                // Foreground load owns paging, all-workspace aggregates and stale-response guards.
                // There is no await between this final guard and beginning that load.
                await loadTasks({ background: true });
            }
            el("workspace-sync-status").textContent = "自動更新中（15秒間隔）。編集中は一時停止します。";
            if (syncFailed) { syncFailed = false; setMessage("自動更新が再開しました"); }
        } catch (error) {
            if (teamId === state.teamId && loadVersion === state.taskLoadVersion) {
                el("workspace-sync-status").textContent = getDisplayErrorMessage(error, "自動更新できませんでした。「更新」で再試行できます。");
                if (!syncFailed) setMessage("自動更新に接続できません。表示中の内容は最新でない場合があります。設定の「更新」で再試行できます。", true);
                syncFailed = true;
            }
        } finally { pollPending = false; }
    }
    for (const prefix of Object.keys(forms)) el(`${prefix}-checklist-add`).addEventListener("click", () => { if (!isTaskBusy()) addRow(prefix, undefined, true); });
    el("task-undo-button").addEventListener("click", applyUndo);
    el("task-undo-close").addEventListener("click", clearUndo);
    setInterval(poll, 15000);
    document.addEventListener("visibilitychange", () => { if (!document.hidden) poll(); });
    window.addEventListener("online", poll);

    let reviewVersion = 0;
    async function weeklyReview() {
        const version = ++reviewVersion, button = el("weekly-review-refresh");
        button.disabled = true; el("weekly-review-message").textContent = "今週の記録を確認しています…";
        try {
            const response = await TaskAuth.request("/api/pet/weekly-review");
            if (!response.ok) throw new Error(await getErrorMessage(response, "振り返りを読み込めませんでした。"));
            const data = await response.json();
            if (version !== reviewVersion) return;
            const box = el("weekly-review-content"); box.replaceChildren();
            el("weekly-review-message").textContent = `${state.petProfile?.name || "相棒"}：${data.message}`;
            const period = document.createElement("p"); period.textContent = `${data.weekStart} 〜 ${data.weekEnd}（JST）`;
            const counts = document.createElement("div"); counts.className = "weekly-counts";
            for (const text of [`今週 ${data.thisWeek}件`, `先週 ${data.lastWeek}件`, `差 ${data.difference > 0 ? "+" : ""}${data.difference}件`]) { const span = document.createElement("span"); span.textContent = text; counts.append(span); }
            const days = document.createElement("ol"); days.className = "weekly-days";
            data.days.forEach((day, index) => { const item = document.createElement("li"), label = document.createElement("span"), count = document.createElement("strong"); label.textContent = ["月", "火", "水", "木", "金", "土", "日"][index]; count.textContent = `${day.completed}件`; item.title = day.date; item.append(label, count); days.append(item); });
            box.append(period, counts, days);
        } catch (error) {
            if (version === reviewVersion) { el("weekly-review-content").replaceChildren(); el("weekly-review-message").textContent = getDisplayErrorMessage(error, "振り返りを読み込めませんでした。"); }
        } finally { if (version === reviewVersion) button.disabled = false; }
    }
    el("weekly-review-refresh").addEventListener("click", weeklyReview);
    el("weekly-review-details").addEventListener("toggle", () => { if (el("weekly-review-details").open) weeklyReview(); });
    return { open, read, validate, badges, offerUndo, clearUndo, poll, canPoll };
})();
