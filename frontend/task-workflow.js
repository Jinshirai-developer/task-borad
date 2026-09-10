// Everyday task controls. The API owns filtering, ordering, membership and request state.
const TaskWorkflow = (() => {
    const el = id => document.getElementById(id);
    const baselines = new Map();
    const formFor = prefix => prefix === "create" ? taskForm : editTaskForm;
    const modalFor = prefix => prefix === "create" ? createTaskModal : editModal;
    const snapshot = prefix => JSON.stringify([...formFor(prefix).querySelectorAll("input, textarea, select")]
        .map(input => [input.id, input.type === "checkbox" ? input.checked : input.value]));
    const rememberForm = prefix => baselines.set(prefix, snapshot(prefix));
    const hasChanges = prefix => !modalFor(prefix).classList.contains("hidden")
        && baselines.has(prefix) && baselines.get(prefix) !== snapshot(prefix);
    const allowClose = prefix => !hasChanges(prefix) || confirm("保存していないタスクの入力を破棄して閉じますか？");
    let assigneeSequence = 0;

    function readFilters() {
        state.assignee = state.teamId ? el("assignee-filter").value : "";
        state.due = el("due-filter").value;
    }
    function syncFilters() {
        const select = el("assignee-filter");
        if (state.assignee && ![...select.options].some(option => option.value === state.assignee))
            select.add(new Option("選択中の担当者", state.assignee));
        select.value = state.assignee || "";
        el("assignee-filter-field").hidden = !state.teamId;
        el("due-filter").value = state.due || "";
        el("quick-sort").value = state.sortOrder;
        el("quick-filter-mine").hidden = !state.teamId;
        for (const [name, selected] of [["all", !hasActiveTaskFilter()], ["mine", state.assignee === "me"],
            ["today", state.due === "through_today"], ["overdue", state.due === "overdue"]])
            el(`quick-filter-${name}`).setAttribute("aria-pressed", String(selected));
    }
    async function loadAssignees() {
        const sequence = ++assigneeSequence, teamId = state.teamId, select = el("assignee-filter");
        select.replaceChildren(new Option("すべての担当者", ""), new Option("自分の担当", "me"), new Option("未割り当て", "unassigned"));
        syncFilters(); el("assignee-filter-message").textContent = "";
        if (!teamId) return;
        const fallback = [...select.options];
        el("assignee-filter-message").textContent = "メンバーを確認中…";
        try {
            const response = await TaskAuth.request(`/api/teams/${teamId}`);
            if (!response.ok) throw new Error(await getErrorMessage(response, "メンバーを読み込めません。検索を開き直して再試行できます。"));
            const data = await response.json();
            if (sequence !== assigneeSequence || teamId !== state.teamId) return;
            const selected = select.value;
            select.replaceChildren(...fallback.slice(0, 3));
            for (const member of data.members) select.add(new Option(member.displayName, String(member.userProfileId)));
            if (selected && ![...select.options].some(option => option.value === selected)) {
                select.add(new Option("選択した担当者は退出しています（条件を変更してください）", selected));
            }
            select.value = selected;
            el("assignee-filter-message").textContent = "";
        } catch (error) {
            if (sequence === assigneeSequence && teamId === state.teamId)
                el("assignee-filter-message").textContent = getDisplayErrorMessage(error, "メンバーを読み込めません。検索を開き直してください。");
        }
    }
    async function quickFilter(kind) {
        if (isTaskBusy() || optionsState.busy) return;
        if (kind === "all") {
            state.search = ""; state.status = ""; state.priority = ""; state.tag = "";
            state.tagExact = ""; state.untagged = false; state.assignee = ""; state.due = ""; state.sortOrder = "desc";
            state.hideDone = false; hideDoneToggle.checked = false; state.pageSize = 100;
        } else {
            // Toggles compose: e.g. my tasks AND overdue, with existing tag/search filters.
            if (kind === "mine") state.assignee = state.assignee === "me" ? "" : "me";
            else { const due = kind === "today" ? "through_today" : "overdue"; state.due = state.due === due ? "" : due; }
            state.status = "";
        }
        state.page = 1; syncSearchFormFromState(); await loadTasks();
    }
    async function quickMove(task, nextStatus) {
        if (isTaskBusy() || optionsState.busy) return;
        const teamId = state.teamId;
        document.querySelectorAll(".task-quick-control").forEach(control => { control.disabled = true; });
        try { await moveTaskStatus(task, nextStatus); }
        finally {
            document.querySelectorAll(".task-quick-control").forEach(control => { control.disabled = false; });
            if (teamId === state.teamId) {
                const target = document.querySelector(`[data-quick-task-id="${task.id}"]`);
                const current = state.visibleTasks.find(item => item.id === task.id);
                if (target && current) target.value = String(getTaskStatus(current));
                (target || el("quick-create-task")).focus();
            }
        }
    }
    function addQuickControls(task, actions) {
        const group = document.createElement("div"); group.className = "task-quick-actions";
        const select = document.createElement("select"); select.className = "task-quick-control task-status-select";
        select.dataset.quickTaskId = String(task.id); select.setAttribute("aria-label", `${task.title}のステータス`);
        for (const [value, label] of [[0, "TODO"], [1, "DOING"], [2, "DONE"]]) select.add(new Option(label, String(value)));
        select.value = String(getTaskStatus(task));
        select.addEventListener("change", () => void quickMove(task, Number(select.value)));
        group.append(select);
        if (getTaskStatus(task) !== TASK_STATUS.Done) {
            const complete = document.createElement("button"); complete.type = "button";
            complete.className = "task-quick-control task-complete-button"; complete.textContent = "✓ 完了";
            complete.setAttribute("aria-label", `${task.title}を完了にする`);
            complete.addEventListener("click", () => void quickMove(task, TASK_STATUS.Done)); group.append(complete);
        }
        actions.append(group);
        if (state.teamId && !task.needsHelp) {
            const help = document.createElement("button"); help.type = "button"; help.className = "task-help-button";
            help.textContent = "お助けを依頼"; help.setAttribute("aria-label", `${task.title}のお助けを依頼`);
            help.addEventListener("click", () => { if (!isTaskBusy()) void TaskCompanion.openTask(task.id, "help"); }); actions.append(help);
        }
    }

    function initialize() { initialized = true; syncFilters(); TaskInbox.initialize(); }
    el("quick-create-task").addEventListener("click", () => { if (!isTaskBusy()) openCreateTaskModal(); });
    el("search-all-tasks").addEventListener("click", () => { if (!isTaskBusy()) openBoardSearchModal("all"); });
    for (const kind of ["all", "mine", "today", "overdue"]) el(`quick-filter-${kind}`).addEventListener("click", () => void quickFilter(kind));
    el("quick-sort").addEventListener("change", async () => {
        if (isTaskBusy() || optionsState.busy) { el("quick-sort").value = state.sortOrder; return; }
        state.sortOrder = el("quick-sort").value; state.page = 1; syncSearchFormFromState(); await loadTasks();
    });
    el("mobile-sidebar-toggle").addEventListener("click", () => {
        const expanded = document.querySelector(".sidebar").classList.toggle("is-expanded");
        el("mobile-sidebar-toggle").setAttribute("aria-expanded", String(expanded));
        el("mobile-sidebar-toggle").textContent = expanded ? "詳細を閉じる" : "相棒・タグの詳細";
    });
    window.addEventListener("beforeunload", event => { if (["create", "edit"].some(hasChanges)) { event.preventDefault(); event.returnValue = ""; } });
    return { initialize, rememberForm, hasChanges, allowClose, readFilters, syncFilters, loadAssignees, addQuickControls, refreshInbox: render => TaskInbox.refresh(render) };
})();
