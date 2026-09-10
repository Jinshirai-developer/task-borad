// Shared with app.js: requests always use the same-origin authenticated client.
const optionsState = {
    preferences: { theme: "classic", layout: "board" },
    unlocks: null, progressionRequest: 0, progressionLoading: false,
    teams: [], teamsRequestVersion: 0, teamsError: "", teamDetail: null, busy: false, savingPreferences: false,
    teamDetailVersion: 0, progressVersion: 0, workspaceChanged: false
};
const UNLOCK_OPTIONS = {
    pets: [{ id: "dog", name: "いぬ", requiredLevel: 1 }, { id: "cat", name: "ねこ", requiredLevel: 1 }, { id: "rabbit", name: "うさぎ", requiredLevel: 1 }, { id: "fox", name: "きつね", requiredLevel: 1 }, { id: "panda", name: "パンダ", requiredLevel: 1 }, { id: "dragon", name: "ドラゴン", requiredLevel: 1 }],
    themes: [{ id: "classic", name: "クラシック", requiredLevel: 1 }, { id: "light", name: "ライト", requiredLevel: 1 }, { id: "dark", name: "ダーク", requiredLevel: 1 }, { id: "retro", name: "Windows風", requiredLevel: 1 }, { id: "forest", name: "フォレスト", requiredLevel: 1 }, { id: "sunset", name: "サンセット", requiredLevel: 1 }],
    layouts: [{ id: "board", name: "ボード", requiredLevel: 1 }, { id: "list", name: "リスト", requiredLevel: 1 }, { id: "compact", name: "コンパクト", requiredLevel: 1 }, { id: "gallery", name: "ギャラリー", requiredLevel: 1 }, { id: "focus", name: "集中", requiredLevel: 1 }]
};
const LAYOUT_DESCRIPTIONS = { board: "状態ごとに横並び", list: "縦に並べて一覧", compact: "小さな行でたくさん見渡す", gallery: "大きなカードを並べる", focus: "このページの最大3件に集中" };
const settingsModal = document.getElementById("settings-modal");
const teamModal = document.getElementById("team-modal");
const workspaceSelect = document.getElementById("workspace-select");
const workspaceTabs = document.getElementById("workspace-tabs");
const settingsTabs = Array.from(document.querySelectorAll("[data-settings-tab]"));
let returnToTaskSettings = false;
let teamSettingsOrigin = null;
let teamMode = "create";
const teamModes = ["create", "join", "manage"];

document.getElementById("open-settings-button").addEventListener("click", async () => {
    syncPreferencesForm();
    if (state.petProfile) renderPetProfile(state.petProfile);
    openOptionsModal(settingsModal, document.getElementById("settings-display-tab"));
    selectSettingsTab("display");
    await refreshProgression();
});
document.getElementById("settings-close-button").addEventListener("click", () => closeOptionsModal(settingsModal));
document.getElementById("team-close-button").addEventListener("click", () => closeOptionsModal(teamModal));
document.getElementById("settings-billing-button").addEventListener("click", () => openAccountBillingModal());
for (const modal of [settingsModal, teamModal]) {
    modal.addEventListener("click", (event) => { if (event.target === modal) closeOptionsModal(modal); });
}
settingsTabs.forEach((tab, index) => {
    tab.addEventListener("click", () => selectSettingsTab(tab.dataset.settingsTab));
    tab.addEventListener("keydown", (event) => {
        let next = index;
        if (event.key === "ArrowRight") next = (index + 1) % settingsTabs.length;
        else if (event.key === "ArrowLeft") next = (index + settingsTabs.length - 1) % settingsTabs.length;
        else if (event.key === "Home") next = 0;
        else if (event.key === "End") next = settingsTabs.length - 1;
        else return;
        event.preventDefault();
        selectSettingsTab(settingsTabs[next].dataset.settingsTab);
        settingsTabs[next].focus();
    });
});

document.getElementById("preferences-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    if (optionsState.savingPreferences || optionsState.progressionLoading) return;
    const requested = {
        theme: document.querySelector('input[name="theme"]:checked')?.value || "classic",
        layout: document.getElementById("layout-select").value
    };
    if (!isChoiceUnlocked("themes", requested.theme) || !isChoiceUnlocked("layouts", requested.layout)) {
        setOptionsMessage("preferences-message", "まだ解放されていない選択肢です。必要レベルを確認してください。", true);
        return;
    }
    const button = document.getElementById("preferences-save-button");
    optionsState.savingPreferences = true;
    button.disabled = true;
    setOptionsMessage("preferences-message", "保存中です");
    try {
        const preferences = await optionsRequest("/api/user/preferences", "PUT", requested);
        applyPreferences(preferences);
        setOptionsMessage("preferences-message", "表示設定を保存しました");
    } catch (error) {
        setOptionsMessage("preferences-message", getDisplayErrorMessage(error, "表示設定を保存できませんでした。"), true);
        if (error.status === 403) await refreshProgression();
    } finally {
        optionsState.savingPreferences = false;
        button.disabled = false;
    }
});

workspaceSelect.addEventListener("change", async () => {
    const requestedId = workspaceSelect.value ? Number(workspaceSelect.value) : null;
    if (!canChangeWorkspace()) {
        workspaceSelect.value = state.teamId ? String(state.teamId) : "";
        return;
    }
    await switchWorkspace(requestedId);
});
document.getElementById("refresh-workspace-button").addEventListener("click", async () => {
    if (isTaskBusy()) return;
    await Promise.all([loadTasks(), loadTeams(), refreshProgression()]);
});
document.getElementById("focus-show-all-button").addEventListener("click", async () => {
    if (optionsState.savingPreferences) return;
    optionsState.savingPreferences = true;
    const button = document.getElementById("focus-show-all-button");
    button.disabled = true;
    try {
        applyPreferences(await optionsRequest("/api/user/preferences", "PUT", { ...optionsState.preferences, layout: "board" }));
        setMessage("ボード表示に戻しました");
    } catch (error) {
        setMessage(getDisplayErrorMessage(error, "表示を変更できませんでした。"), true);
    } finally {
        optionsState.savingPreferences = false;
        button.disabled = false;
    }
});
for (const [id, mode] of [["settings-team-create-button", "create"], ["settings-team-join-button", "join"], ["open-team-button", "manage"]]) {
    document.getElementById(id).addEventListener("click", () => openTeamModal(mode, document.getElementById(id)));
}
for (const mode of teamModes) {
    const tab = document.getElementById(`team-${mode}-tab`);
    tab.addEventListener("click", () => showTeamMode(mode));
    tab.addEventListener("keydown", (event) => {
        if (optionsState.busy) return;
        const available = teamModes.filter((item) => item !== "manage" || state.teamId);
        let index = available.indexOf(mode);
        if (event.key === "ArrowRight") index = (index + 1) % available.length;
        else if (event.key === "ArrowLeft") index = (index + available.length - 1) % available.length;
        else if (event.key === "Home") index = 0;
        else if (event.key === "End") index = available.length - 1;
        else return;
        event.preventDefault();
        showTeamMode(available[index]);
        document.getElementById(`team-${available[index]}-tab`).focus();
    });
}
document.getElementById("team-open-board-button").addEventListener("click", () => closeOptionsModal(teamModal, true));
document.getElementById("team-copy-invite-button").addEventListener("click", async () => {
    const input = document.getElementById("team-invite-code");
    const code = input.value;
    if (!code || optionsState.busy) return;
    try {
        if (!globalThis.navigator?.clipboard?.writeText) throw new Error("Clipboard unavailable");
        await navigator.clipboard.writeText(code);
        if (input.value === code) setOptionsMessage("team-copy-message", "コピーしました。メンバーに送ってください。");
    } catch {
        if (input.value !== code) return;
        input.focus(); input.select();
        setOptionsMessage("team-copy-message", "自動コピーできませんでした。選択中のコードをコピーしてください。", true);
    }
});
document.getElementById("team-create-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const name = document.getElementById("team-name-input").value.trim();
    if (!name) return setOptionsMessage("team-message", "チーム名を入力してください。", true);
    await performTeamAction(async () => {
        const result = await optionsRequest("/api/teams", "POST", { name });
        document.getElementById("team-create-form").reset();
        await loadTeams();
        ensureWorkspaceOption(result.team);
        await switchWorkspace(result.team.id);
        renderTeamDetail(result.team);
        showTeamComplete(result.team, true);
        renderInvitation(result);
        return "チームを作成しました。招待コードをメンバーに伝えてください。";
    });
});
document.getElementById("team-join-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const inviteCode = document.getElementById("team-join-input").value.trim();
    if (!inviteCode) return setOptionsMessage("team-message", "招待コードを入力してください。", true);
    await performTeamAction(async () => {
        const result = await optionsRequest("/api/teams/join", "POST", { inviteCode });
        document.getElementById("team-join-form").reset();
        await loadTeams();
        ensureWorkspaceOption(result);
        await switchWorkspace(result.id);
        renderTeamDetail(result);
        clearInvitation();
        showTeamComplete(result, false);
        return "チームに参加しました。個人のタスクは共有されません。";
    });
});
document.getElementById("team-invite-button").addEventListener("click", async () => {
    if (!state.teamId) return;
    await performTeamAction(async () => {
        const invitation = await optionsRequest(`/api/teams/${state.teamId}/invites`, "POST");
        renderInvitation(invitation);
        return "招待コードを発行しました。以前のコードは無効になりました。";
    });
});
document.getElementById("team-leave-button").addEventListener("click", async () => {
    if (!state.teamId || !confirm("このチームから退出しますか？退出後はチームのタスクを閲覧・編集できなくなります。")) return;
    const teamId = state.teamId;
    await performTeamAction(async () => {
        await optionsRequest(`/api/teams/${teamId}/members/me`, "DELETE");
        forgetWorkspace(teamId);
        await switchWorkspace(null);
        await loadTeams();
        renderTeamDetail(null);
        clearInvitation();
        showTeamStart();
        return "チームから退出しました。";
    });
});
document.getElementById("team-delete-button").addEventListener("click", async () => {
    if (!state.teamId || !confirm(`「${optionsState.teamDetail?.name || "このチーム"}」と、チームの全タスク・参加情報を完全に削除します。元に戻せません。削除しますか？`)) return;
    const teamId = state.teamId;
    await performTeamAction(async () => {
        await optionsRequest(`/api/teams/${teamId}`, "DELETE");
        forgetWorkspace(teamId);
        await switchWorkspace(null);
        await loadTeams();
        renderTeamDetail(null);
        clearInvitation();
        showTeamStart();
        return "チームを削除しました。個人のタスクはそのままです。";
    });
});
document.getElementById("team-transfer-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const select = document.getElementById("team-owner-select");
    if (!state.teamId || !select.value) return;
    if (!confirm(`${select.options[select.selectedIndex].textContent} に所有権を譲ります。あなたは一般メンバーになり、招待発行やチーム削除はできなくなります。人数上限は新しい所有者のプランに従い、あなたのPro契約は譲渡されません。既存メンバーは残ります。続けますか？`)) return;
    await performTeamAction(async () => {
        await optionsRequest(`/api/teams/${state.teamId}/owner`, "PUT", { userProfileId: Number(select.value) });
        clearInvitation();
        await Promise.all([loadTeamDetail(state.teamId), loadTeams()]);
        return "チームの所有者を変更しました。";
    });
});

async function initializeOptions() {
    await Promise.all([refreshProgression(), loadTeams().then((loaded) => {
        if (loaded && !optionsState.workspaceChanged) restoreWorkspaceSelection();
    })]);
}

function workspaceSelectionKey() {
    // A tab remembers navigation only; credentials and task content stay out of storage.
    if (window.TaskDemo?.active || !Number.isSafeInteger(state.userProfileId) || state.userProfileId <= 0) return null;
    return `taskBoardWorkspace:${state.userProfileId}`;
}

function rememberWorkspaceSelection(teamId) {
    const key = workspaceSelectionKey();
    if (!key) return;
    try {
        if (teamId === null) window.sessionStorage.removeItem(key);
        else if (Number.isSafeInteger(teamId) && teamId > 0) window.sessionStorage.setItem(key, String(teamId));
    } catch { /* Storage can be unavailable; navigation must still work. */ }
}

function restoreWorkspaceSelection() {
    // A slow membership response must not redirect work the user has already begun.
    if (isTaskBusy() || state.taskLoadVersion > 0 || document.body.classList.contains("is-modal-open")) return;
    const key = workspaceSelectionKey();
    if (!key) return;
    let saved;
    try { saved = window.sessionStorage.getItem(key); } catch { return; }
    if (saved === null) return;
    const teamId = /^[1-9]\d*$/.test(saved) ? Number(saved) : NaN;
    if (!Number.isSafeInteger(teamId) || !optionsState.teams.some((team) => team.id === teamId)) {
        rememberWorkspaceSelection(null);
        return;
    }
    state.teamId = teamId;
    if (typeof TaskTags !== "undefined") TaskTags.resetScope(teamId);
    syncWorkspaceChoices();
    renderWorkspaceLabel();
}

async function loadPreferences() {
    try {
        applyPreferences(await optionsRequest("/api/user/preferences"));
    } catch (error) {
        setOptionsMessage("preferences-message", getDisplayErrorMessage(error, "表示設定を読み込めませんでした。"), true);
    }
}

function applyPreferences(preferences) {
    optionsState.preferences = {
        theme: isChoiceUnlocked("themes", preferences?.theme) ? preferences.theme : "classic",
        layout: isChoiceUnlocked("layouts", preferences?.layout) ? preferences.layout : "board"
    };
    document.body.dataset.theme = optionsState.preferences.theme;
    document.body.dataset.ui = optionsState.preferences.theme === "retro" ? "retro" : "modern";
    document.body.dataset.layout = optionsState.preferences.layout;
    syncPreferencesForm();
    renderFocusLayout();
}

function getUnlockOption(category, id) {
    const fallback = UNLOCK_OPTIONS[category]?.find((item) => item.id === id);
    if (!fallback) return null;
    const option = optionsState.unlocks?.[category]?.find((item) => item.id === id);
    return option || { ...fallback, unlocked: fallback.requiredLevel === 1, experienceRemaining: null };
}

function isChoiceUnlocked(category, id) {
    return getUnlockOption(category, id)?.unlocked === true;
}

function normalizeUnlockCatalog(catalog) {
    if (!Number.isInteger(catalog?.level) || catalog.level < 1 || !Number.isFinite(catalog?.totalExperience)
        || !["pets", "themes", "layouts"].every((category) => Array.isArray(catalog[category]))) {
        throw new Error("解放状況を確認できませんでした。更新して再度お試しください。");
    }
    const normalized = { level: catalog.level, totalExperience: Math.max(0, catalog.totalExperience) };
    for (const category of Object.keys(UNLOCK_OPTIONS)) {
        normalized[category] = UNLOCK_OPTIONS[category].map((fallback) => {
            const item = catalog[category].find((entry) => entry?.id === fallback.id);
            return {
                ...fallback,
                name: typeof item?.name === "string" ? item.name.slice(0, 100) : fallback.name,
                requiredLevel: Number.isInteger(item?.requiredLevel) && item.requiredLevel >= 1 ? item.requiredLevel : fallback.requiredLevel,
                unlocked: item ? item.unlocked === true : fallback.requiredLevel === 1,
                experienceRemaining: Number.isFinite(item?.experienceRemaining) ? Math.max(0, item.experienceRemaining) : null
            };
        });
    }
    return normalized;
}

async function refreshProgression() {
    const version = ++optionsState.progressionRequest;
    optionsState.progressionLoading = true;
    document.getElementById("preferences-save-button").disabled = true;
    petNameButton.disabled = true;
    const results = await Promise.allSettled([
        optionsRequest("/api/user/unlocks").then(normalizeUnlockCatalog),
        optionsRequest("/api/user/preferences"),
        optionsRequest("/api/pet")
    ]);
    if (version !== optionsState.progressionRequest) return false;
    optionsState.progressionLoading = false;
    document.getElementById("preferences-save-button").disabled = optionsState.savingPreferences;
    petNameButton.disabled = false;
    const [unlocks, preferences, pet] = results;
    optionsState.unlocks = unlocks.status === "fulfilled" ? unlocks.value : null;
    renderUnlockCatalog();
    if (preferences.status === "fulfilled") applyPreferences(preferences.value);
    else applyPreferences(optionsState.preferences);
    if (pet.status === "fulfilled") {
        state.petProfile = pet.value;
        renderPetProfile(pet.value);
    }
    const failure = results.find((result) => result.status === "rejected");
    setOptionsMessage("unlock-message", failure
        ? getDisplayErrorMessage(failure.reason, "育成・解放状況を更新できませんでした。更新して再度お試しください。")
        : "経験値と解放状況はアカウントに保存されています。", Boolean(failure));
    return !failure;
}

function renderUnlockCatalog() {
    for (const [category, inputName] of [["themes", "theme"], ["pets", "species"]]) {
        document.querySelectorAll(`input[name="${inputName}"]`).forEach((input) => {
            const item = getUnlockOption(category, input.value);
            input.disabled = !item?.unlocked;
            input.closest("label")?.classList.toggle("is-locked", !item?.unlocked);
            const label = document.querySelector(`[data-unlock-label="${category}:${input.value}"]`);
            if (label && item) label.textContent = item.unlocked ? `Lv.${item.requiredLevel} 解放済み` : `🔒 Lv.${item.requiredLevel}で解放`;
            input.title = item?.unlocked ? "選択できます" : `Lv.${item?.requiredLevel || "?"}で解放${Number.isFinite(item?.experienceRemaining) ? ` / あと ${item.experienceRemaining} EXP` : ""}`;
        });
    }
    for (const option of document.getElementById("layout-select").options) {
        const item = getUnlockOption("layouts", option.value);
        option.disabled = !item?.unlocked;
        if (item) option.textContent = item.unlocked
            ? `${item.name} — ${LAYOUT_DESCRIPTIONS[item.id]}`
            : `🔒 ${item.name} — Lv.${item.requiredLevel}で解放`;
    }
    const catalog = optionsState.unlocks;
    document.getElementById("unlock-level-label").textContent = `Lv.${catalog?.level || state.petProfile?.level || 1}`;
    const locked = Object.keys(UNLOCK_OPTIONS).flatMap((category) => UNLOCK_OPTIONS[category].map((item) => getUnlockOption(category, item.id)))
        .filter((item) => !item.unlocked).sort((a, b) => a.requiredLevel - b.requiredLevel);
    const next = locked[0];
    const nextNames = next ? locked.filter((item) => item.requiredLevel === next.requiredLevel).map((item) => item.name).join("・") : "";
    const nextText = !catalog ? "育成情報を読み込めていません。更新してください。"
        : next ? `次は Lv.${next.requiredLevel}：${nextNames}${Number.isFinite(next.experienceRemaining) ? ` / あと ${next.experienceRemaining} EXP` : ""}`
            : "表示方法・ペットの種類は、いつでも自由に選べます";
    document.getElementById("unlock-next-label").textContent = nextText;
    document.getElementById("pet-unlock-next").textContent = nextText;
    const requiredExperience = (catalog?.totalExperience || 0) + (next?.experienceRemaining || 0);
    const progress = !catalog ? 0 : !next ? 100 : requiredExperience > 0 ? Math.min(100, Math.round(catalog.totalExperience / requiredExperience * 100)) : 0;
    document.getElementById("unlock-progress").setAttribute("aria-valuenow", String(progress));
    document.getElementById("unlock-progress").setAttribute("aria-valuetext", nextText);
    document.getElementById("unlock-progress-fill").style.width = `${progress}%`;
    document.getElementById("layout-unlock-hint").textContent = "すべてLv.1から使えます。集中表示では一部のタスクが非表示になります。";
}

function getFocusSelection(tasks = state.visibleTasks || []) {
    const preferredStatus = state.status ? getTaskStatus({ status: state.status })
        : tasks.some((task) => getTaskStatus(task) === TASK_STATUS.Doing) ? TASK_STATUS.Doing
            : tasks.some((task) => getTaskStatus(task) === TASK_STATUS.Todo) ? TASK_STATUS.Todo : TASK_STATUS.Done;
    return { status: preferredStatus, tasks: tasks.filter((task) => getTaskStatus(task) === preferredStatus).slice(0, 3) };
}

function renderFocusLayout() {
    const enabled = document.body.dataset.layout === "focus";
    const notice = document.getElementById("focus-layout-notice");
    notice.classList.toggle("hidden", !enabled);
    const selection = getFocusSelection();
    const ids = new Set(selection.tasks.map((task) => String(task.id)));
    for (const list of [taskList, doingList, doneList]) {
        const listStatus = { todo: 0, doing: 1, done: 2 }[list.dataset.statusList];
        list.closest(".board-column")?.classList.toggle("is-focus-hidden", enabled && listStatus !== selection.status);
        for (const item of list.querySelectorAll("[data-task-id]")) {
            item.classList.toggle("is-focus-hidden", enabled && !ids.has(item.dataset.taskId));
        }
    }
    if (!enabled) return;
    const statusLabel = ["TODO", "DOING", "DONE"][selection.status];
    const hiddenCount = Math.max(0, state.totalCount - selection.tasks.length);
    document.getElementById("focus-layout-description").textContent = `検索対象の全 ${state.totalCount}件から、このページの ${statusLabel} ${selection.tasks.length}件を表示中。${hiddenCount}件は非表示です（他ページを含む）。状態は下の枠へのドラッグ、またはカードの編集から変更できます。`;
}

function syncPreferencesForm() {
    const extraThemes = document.getElementById("extra-themes");
    if (extraThemes && ["classic", "forest", "sunset"].includes(optionsState.preferences.theme)) extraThemes.open = true;
    document.querySelectorAll('input[name="theme"]').forEach((input) => { input.checked = input.value === optionsState.preferences.theme; });
    document.getElementById("layout-select").value = optionsState.preferences.layout;
}

function selectSettingsTab(name) {
    document.getElementById("unlock-overview").hidden = name !== "pet";
    for (const tab of settingsTabs) {
        const selected = tab.dataset.settingsTab === name;
        tab.setAttribute("aria-selected", String(selected));
        tab.tabIndex = selected ? 0 : -1;
        document.getElementById(tab.getAttribute("aria-controls")).hidden = !selected;
    }
    if (name === "pet" && typeof PetPlay !== "undefined") PetPlay.refresh();
}

function openOptionsModal(modal, focusTarget) {
    rememberModalFocus(modal);
    modal.classList.remove("hidden");
    updateModalOpenState();
    focusTarget.focus();
}

function openTagSettings() {
    if (isTaskBusy() || state.draggingTask || optionsState.busy || optionsState.savingPreferences || petNameButton.disabled
        || [createTaskModal, editModal, boardSearchModal, teamModal].some((modal) => !modal.classList.contains("hidden"))) return false;
    syncPreferencesForm();
    selectSettingsTab("tasks");
    openOptionsModal(settingsModal, document.getElementById("settings-tasks-tab"));
    modalFocusOrigins.set(settingsModal, document.getElementById("sidebar-tag-filter"));
    TaskTags.openCreateForm();
    // Keep the selected workspace/filter and preserve the sidebar as the return focus.
    void refreshProgression();
    return true;
}

function closeOptionsModal(modal, toBoard = false) {
    if (optionsState.busy || optionsState.savingPreferences || petNameButton.disabled || isTaskBusy()) return;
    // Completion is the end of create/join, not a nested settings page. X,
    // Escape, backdrop and OK all dismiss it to the selected board.
    if (modal === teamModal && teamMode === "complete") toBoard = true;
    modal.classList.add("hidden");
    if (modal === teamModal) { optionsState.teamDetailVersion++; clearInvitation(); if (typeof TeamBilling !== "undefined") TeamBilling.reset(); }
    if (modal === teamModal && toBoard) {
        returnToTaskSettings = false;
        settingsModal.classList.add("hidden");
        modalFocusOrigins.delete(teamModal);
        modalFocusOrigins.delete(settingsModal);
        updateModalOpenState();
        const activeTab = Array.from(workspaceTabs.querySelectorAll('[role="tab"]')).find((tab) => tab.getAttribute("aria-selected") === "true");
        (activeTab || document.getElementById("open-settings-button")).focus();
        return;
    }
    if (modal === teamModal && returnToTaskSettings) {
        returnToTaskSettings = false;
        settingsModal.classList.remove("hidden");
        selectSettingsTab(teamSettingsOrigin?.id === "settings-billing-button" ? "account" : "tasks");
        updateModalOpenState();
        modalFocusOrigins.delete(teamModal);
        (teamSettingsOrigin?.hidden ? document.getElementById("settings-team-create-button") : teamSettingsOrigin || document.getElementById("settings-team-create-button")).focus();
        return;
    }
    updateModalOpenState();
    restoreModalFocus(modal, document.getElementById("open-settings-button"));
}

async function openTeamModal(mode, origin) {
    if (isTaskBusy() || optionsState.busy || optionsState.savingPreferences || petNameButton.disabled) return;
    if ([createTaskModal, editModal, boardSearchModal].some((modal) => !modal.classList.contains("hidden"))) return;
    returnToTaskSettings = !settingsModal.classList.contains("hidden");
    teamSettingsOrigin = returnToTaskSettings ? origin : null;
    if (returnToTaskSettings) settingsModal.classList.add("hidden");
    openOptionsModal(teamModal, document.getElementById("team-close-button"));
    if (origin) modalFocusOrigins.set(teamModal, origin);
    const loading = showTeamMode(mode);
    document.getElementById(`team-${teamMode}-tab`).focus();
    await loading;
}

async function openAccountBillingModal() {
    if (isTaskBusy() || optionsState.busy || optionsState.savingPreferences || petNameButton.disabled) return;
    if ([createTaskModal, editModal, boardSearchModal].some((modal) => !modal.classList.contains("hidden"))) return;
    optionsState.teamDetailVersion++;
    teamMode = "account";
    returnToTaskSettings = !settingsModal.classList.contains("hidden");
    teamSettingsOrigin = document.getElementById("settings-billing-button");
    if (returnToTaskSettings) settingsModal.classList.add("hidden");
    openOptionsModal(teamModal, document.getElementById("team-close-button"));
    modalFocusOrigins.set(teamModal, returnToTaskSettings ? teamSettingsOrigin : document.getElementById("open-settings-button"));
    setOptionsMessage("team-message", "");
    await TeamBilling.showAccount();
}

function renderTeamMode(mode) {
    teamMode = mode;
    optionsState.teamDetailVersion++;
    document.getElementById("team-mode-tabs").hidden = mode === "complete";
    document.getElementById("team-complete-panel").hidden = mode !== "complete";
    document.getElementById("team-complete-actions").hidden = mode !== "complete";
    for (const value of teamModes) {
        const tab = document.getElementById(`team-${value}-tab`);
        tab.hidden = value === "manage" && !state.teamId;
        tab.setAttribute("aria-selected", String(value === mode));
        tab.tabIndex = value === mode ? 0 : -1;
        document.getElementById(`team-${value}-panel`).hidden = value !== mode;
    }
}

async function showTeamMode(mode) {
    if (optionsState.busy) return;
    const selected = mode === "manage" && state.teamId ? "manage" : mode === "join" ? "join" : "create";
    renderTeamMode(selected);
    clearInvitation();
    setOptionsMessage("team-message", "");
    if (selected === "manage") await loadTeamDetail(state.teamId);
    else renderTeamDetail(null);
}

function showTeamStart() {
    renderTeamMode("create");
    // Action buttons are still disabled until the request's finally block runs.
    document.getElementById("team-name-input").focus();
}

function ensureWorkspaceOption(team) {
    // A successful create/join is authoritative even if the follow-up list read failed.
    optionsState.teamsRequestVersion++;
    workspaceTabs.setAttribute("aria-busy", "false");
    if (!optionsState.teams.some((item) => item.id === team.id)) optionsState.teams.push(team);
    syncWorkspaceChoices();
    renderWorkspaceTabs();
}

function forgetWorkspace(teamId) {
    optionsState.teamsRequestVersion++;
    optionsState.teams = optionsState.teams.filter((team) => team.id !== teamId);
    workspaceTabs.setAttribute("aria-busy", "false");
}

function showTeamComplete(team, created) {
    renderTeamMode("complete");
    document.getElementById("team-complete-title").textContent = created ? `「${team.name}」を作成しました` : `「${team.name}」に参加しました`;
    document.getElementById("team-complete-description").textContent = created
        ? "次はメンバーを招待しましょう。下のコードをコピーして送ると、同じボードでタスクを共有できます。"
        : "共有ボードの準備ができました。メンバーとタスクを追加・編集できます。個人のタスクはそのまま非公開です。";
    document.getElementById("team-complete-panel").focus();
}

function closeOptionsModalOnEscape() {
    if (!settingsModal.classList.contains("hidden")) closeOptionsModal(settingsModal);
    else if (!teamModal.classList.contains("hidden")) closeOptionsModal(teamModal);
}

function setPetSettingsMessage(text, isError = false) {
    setOptionsMessage("pet-settings-message", text, isError);
}

function setOptionsMessage(id, text, isError = false) {
    const element = document.getElementById(id);
    element.textContent = text;
    element.classList.toggle("error", isError);
    element.classList.toggle("success", !isError && text.includes("しました"));
}

async function optionsRequest(url, method = "GET", body) {
    const response = await TaskAuth.request(url, {
        method,
        ...(body === undefined ? {} : { headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) })
    });
    if (!response.ok) {
        const error = new Error(await getErrorMessage(response, "処理できませんでした。時間をおいて再度お試しください。"));
        error.status = response.status;
        throw error;
    }
    if (response.status === 204) return null;
    return response.json();
}

async function loadTeams() {
    const version = ++optionsState.teamsRequestVersion;
    workspaceTabs.setAttribute("aria-busy", "true");
    try {
        const teams = await optionsRequest("/api/teams");
        if (version !== optionsState.teamsRequestVersion) return false;
        if (!Array.isArray(teams)) throw new Error("チーム一覧を読み込めませんでした。");
        optionsState.teams = teams;
        optionsState.teamsError = "";
        syncWorkspaceChoices();
        renderWorkspaceLabel();
        return true;
    } catch (error) {
        if (version !== optionsState.teamsRequestVersion) return false;
        optionsState.teamsError = "チーム一覧を更新できませんでした。「設定 → タグ・チーム」の「更新」で再試行できます。";
        renderWorkspaceTabs();
        setOptionsMessage("team-message", getDisplayErrorMessage(error, "チーム一覧を読み込めませんでした。"), true);
        document.getElementById("workspace-description").textContent = "チーム一覧を読み込めませんでした。「更新」で再試行できます。";
        return false;
    } finally {
        if (version === optionsState.teamsRequestVersion) workspaceTabs.setAttribute("aria-busy", "false");
    }
}

function syncWorkspaceChoices() {
    workspaceSelect.replaceChildren(new Option("自分のタスク（非公開）", ""));
    optionsState.teams.forEach((team) => workspaceSelect.add(new Option(`${team.name} (${team.memberCount}人)`, String(team.id))));
    if (state.teamId && !optionsState.teams.some((team) => team.id === state.teamId)) {
        // Never relabel a missing team's content as personal tasks.
        workspaceSelect.add(new Option("参加状況を確認できないチーム", String(state.teamId)));
    }
    workspaceSelect.value = state.teamId ? String(state.teamId) : "";
}

function renderWorkspaceTabs() {
    const activeKey = state.teamId ? String(state.teamId) : "";
    const focusedKey = workspaceTabs.contains(document.activeElement) ? document.activeElement.dataset.workspaceId : null;
    const entries = [{ id: null, name: "個人" }, ...optionsState.teams];
    if (state.teamId && !optionsState.teams.some((team) => team.id === state.teamId)) entries.push({ id: state.teamId, name: "参加状況を確認できないチーム" });
    const focusKey = focusedKey !== null && entries.some((entry) => String(entry.id || "") === focusedKey) ? focusedKey : activeKey;
    const scrollLeft = workspaceTabs.scrollLeft;
    workspaceTabs.replaceChildren();
    for (const entry of entries) {
        const key = entry.id ? String(entry.id) : "";
        const tab = document.createElement("button");
        tab.id = entry.id ? `workspace-tab-team-${entry.id}` : "workspace-tab-personal";
        tab.type = "button";
        tab.className = "workspace-tab";
        tab.dataset.workspaceId = key;
        tab.setAttribute("role", "tab");
        tab.setAttribute("aria-controls", "workspace-task-panel");
        tab.setAttribute("aria-selected", String(key === activeKey));
        tab.tabIndex = key === focusKey ? 0 : -1;
        tab.title = entry.id ? `${entry.name}（共有チーム）` : "個人のタスク（非公開）";
        const name = document.createElement("span");
        name.className = "workspace-tab-name";
        name.textContent = entry.name;
        const kind = document.createElement("span");
        kind.className = "workspace-tab-kind";
        kind.textContent = entry.id ? "共有" : "非公開";
        tab.append(name, kind);
        tab.addEventListener("click", () => activateWorkspaceTab(entry.id));
        tab.addEventListener("keydown", handleWorkspaceTabKey);
        workspaceTabs.appendChild(tab);
        if (key === activeKey) document.getElementById("workspace-task-panel").setAttribute("aria-labelledby", tab.id);
    }
    workspaceTabs.scrollLeft = scrollLeft;
    if (focusedKey !== null) {
        const target = Array.from(workspaceTabs.querySelectorAll('[role="tab"]')).find((tab) => tab.dataset.workspaceId === focusKey);
        target?.focus();
        target?.scrollIntoView({ block: "nearest", inline: "nearest" });
    }
    const message = document.getElementById("workspace-tabs-message");
    message.textContent = optionsState.teamsError;
    message.hidden = !optionsState.teamsError;
}

function handleWorkspaceTabKey(event) {
    const tabs = Array.from(workspaceTabs.querySelectorAll('[role="tab"]'));
    let index = tabs.indexOf(event.currentTarget);
    if (event.key === "ArrowRight") index = (index + 1) % tabs.length;
    else if (event.key === "ArrowLeft") index = (index + tabs.length - 1) % tabs.length;
    else if (event.key === "Home") index = 0;
    else if (event.key === "End") index = tabs.length - 1;
    else return; // Enter / Space use the button's native click: arrows do not fetch data.
    event.preventDefault();
    tabs.forEach((tab, i) => { tab.tabIndex = i === index ? 0 : -1; });
    tabs[index].focus();
    tabs[index].scrollIntoView({ block: "nearest", inline: "nearest" });
}

async function activateWorkspaceTab(teamId) {
    if (teamId === state.teamId) {
        optionsState.workspaceChanged = true;
        rememberWorkspaceSelection(teamId);
        return true;
    }
    if (teamId && !optionsState.teams.some((team) => team.id === teamId)) return false;
    if (!settingsModal.classList.contains("hidden") || !teamModal.classList.contains("hidden")) return false;
    if (!canChangeWorkspace()) return false;
    await switchWorkspace(teamId);
    return true;
}

function isTaskBusy() {
    return state.isSaving || state.isEditSaving || state.isTaskMutation || state.isDeletingAccount
        || (typeof TaskTags !== "undefined" && TaskTags.isSaving)
        || (typeof PetPlay !== "undefined" && PetPlay.isSaving);
}

function canChangeWorkspace() {
    if (typeof TaskCompanion !== "undefined" && TaskCompanion.isOpen) return false;
    if (isTaskBusy() || optionsState.busy || state.draggingTask) {
        setMessage("保存・更新が終わってからワークスペースを切り替えてください。", true);
        return false;
    }
    if ((!createTaskModal.classList.contains("hidden") || !editModal.classList.contains("hidden"))
        && !confirm("入力中の内容を破棄してワークスペースを切り替えますか？")) return false;
    return true;
}

async function switchWorkspace(teamId) {
    if (typeof TeamBilling !== "undefined") TeamBilling.reset();
    if (typeof TaskCompanion !== "undefined") TaskCompanion.resetScope();
    if (typeof TaskDetails !== "undefined") TaskDetails.clearUndo();
    state.teamId = teamId;
    if (typeof TaskTags !== "undefined") TaskTags.resetScope(teamId);
    optionsState.workspaceChanged = true;
    rememberWorkspaceSelection(teamId);
    state.taskLoadVersion++;
    state.draggingTask = null;
    state.page = 1;
    state.totalCount = 0;
    state.statusCounts = null;
    state.totalPages = 0;
    state.search = "";
    state.assignee = "";
    state.due = "";
    state.status = "";
    state.priority = "";
    state.tag = "";
    state.hideDone = false;
    hideDoneToggle.checked = false;
    syncSearchFormFromState();
    if (!createTaskModal.classList.contains("hidden")) closeCreateTaskModal(true);
    if (!editModal.classList.contains("hidden")) closeEditModal(true);
    if (!boardSearchModal.classList.contains("hidden")) closeBoardSearchModal();
    syncWorkspaceChoices();
    document.getElementById("workspace-progress").classList.add("hidden");
    clearInvitation();
    renderTasks([]);
    renderPagination({ page: 1, totalPages: 0 });
    renderWorkspaceLabel();
    await loadTasks();
}

function renderWorkspaceLabel() {
    document.getElementById("companion-hub-open").textContent = "作業メモ";
    const team = optionsState.teams.find((item) => item.id === state.teamId);
    document.getElementById("tag-scope-label").textContent = state.teamId
        ? `追加先：${team?.name || "参加状況を確認できないチーム"}（チームで共有）`
        : "追加先：個人（非公開）";
    document.getElementById("workspace-description").textContent = state.teamId
        ? `${team?.name || "チーム"} のメンバー全員と共有中。個人のタスクは含まれません。15秒ごとに自動更新します（編集中は一時停止）。`
        : "このタスクは、あなただけに表示されます。";
    document.getElementById("open-team-button").hidden = !state.teamId;
    document.getElementById("create-modal-title").textContent = state.teamId ? "チームのタスクを追加" : "個人のタスクを追加";
    renderFilterSummary();
    renderWorkspaceTabs();
}

async function loadWorkspaceProgress(teamId = state.teamId) {
    if (!teamId) return;
    const requestVersion = ++optionsState.progressVersion;
    try {
        const progress = await optionsRequest(`/api/teams/${teamId}/summary`);
        if (state.teamId !== teamId || requestVersion !== optionsState.progressVersion) return;
        const percent = progress.total ? Math.round(progress.done / progress.total * 100) : 0;
        document.getElementById("workspace-progress").classList.remove("hidden");
        document.getElementById("workspace-progress-title").textContent = "チーム全体の進捗（検索条件に関係なく全件）";
        document.getElementById("workspace-progress-label").textContent = `${percent}% 完了`;
        document.getElementById("workspace-progress-bar").setAttribute("aria-valuenow", String(percent));
        document.getElementById("workspace-progress-fill").style.width = `${percent}%`;
        document.getElementById("workspace-progress-counts").textContent = `全 ${progress.total}件　TODO ${progress.todo} / DOING ${progress.doing} / DONE ${progress.done}　期限超過 ${progress.overdue || 0}件（JST基準）`;
        document.getElementById("workspace-progress-counts").title = "期限は日本時間（JST）の日付で判定します。当日期限は期限超過に含みません。";
    } catch (error) {
        if (state.teamId !== teamId || requestVersion !== optionsState.progressVersion) return;
        document.getElementById("workspace-progress").classList.add("hidden");
        setMessage(`タスクは読み込めましたが、チーム全体の集計を取得できませんでした。${getDisplayErrorMessage(error, "更新してください。")}`, true);
    }
}

async function loadTeamDetail(teamId) {
    const requestVersion = ++optionsState.teamDetailVersion;
    renderTeamDetail(null);
    setOptionsMessage("team-message", "チーム情報を読み込み中です");
    try {
        const detail = await optionsRequest(`/api/teams/${teamId}`);
        if (state.teamId !== teamId || requestVersion !== optionsState.teamDetailVersion) return;
        renderTeamDetail(detail);
        setOptionsMessage("team-message", "");
    } catch (error) {
        if (state.teamId !== teamId || requestVersion !== optionsState.teamDetailVersion) return;
        setOptionsMessage("team-message", getDisplayErrorMessage(error, "チーム情報を読み込めませんでした。"), true);
    }
}

function renderTeamDetail(detail) {
    optionsState.teamDetail = detail;
    if (typeof TeamBilling !== "undefined") void TeamBilling.show(detail);
    document.getElementById("current-team-panel").hidden = !detail;
    if (!detail) return;
    const owner = detail.role === "owner";
    document.getElementById("current-team-title").textContent = detail.name;
    document.getElementById("team-member-count").textContent = `${detail.memberCount}人が参加中 / あなたは${owner ? "所有者" : "メンバー"}です`;
    document.getElementById("team-owner-actions").hidden = !owner;
    document.getElementById("team-leave-button").hidden = owner;
    const members = document.getElementById("team-members");
    members.replaceChildren();
    const ownerSelect = document.getElementById("team-owner-select");
    ownerSelect.replaceChildren();
    for (const member of detail.members || []) {
        const item = document.createElement("li");
        const name = document.createElement("span");
        name.textContent = member.displayName;
        const role = document.createElement("span");
        role.className = "team-member-role";
        role.textContent = member.role === "owner" ? "所有者" : "メンバー";
        item.append(name, role);
        members.appendChild(item);
        if (member.role !== "owner") ownerSelect.add(new Option(member.displayName, String(member.userProfileId)));
    }
    document.getElementById("team-transfer-form").hidden = ownerSelect.options.length === 0;
}

function renderInvitation(invitation) {
    if (!invitation.inviteCode) return;
    document.getElementById("team-invite-result").hidden = false;
    document.getElementById("team-invite-code").value = invitation.inviteCode;
    document.getElementById("team-invite-expiry").textContent = invitation.expiresAt ? `有効期限: ${new Date(invitation.expiresAt).toLocaleString("ja-JP")}` : "";
}

function clearInvitation() {
    document.getElementById("team-invite-result").hidden = true;
    document.getElementById("team-invite-code").value = "";
    document.getElementById("team-invite-expiry").textContent = "";
    setOptionsMessage("team-copy-message", "");
}

async function performTeamAction(action) {
    if (optionsState.busy || isTaskBusy()) return;
    optionsState.busy = true;
    teamModal.setAttribute("aria-busy", "true");
    const buttons = Array.from(teamModal.querySelectorAll("button"));
    buttons.forEach((button) => { button.disabled = true; });
    setOptionsMessage("team-message", "処理中です");
    try {
        const resultMessage = await action();
        setOptionsMessage("team-message", resultMessage);
    } catch (error) {
        setOptionsMessage("team-message", getDisplayErrorMessage(error, "チーム操作に失敗しました。"), true);
    } finally {
        optionsState.busy = false;
        teamModal.setAttribute("aria-busy", "false");
        buttons.forEach((button) => { button.disabled = false; });
        if (typeof TeamBilling !== "undefined") TeamBilling.restoreDisabled();
    }
}
