// フロントエンドとAPIは同一オリジン。HttpOnly CookieとCSRF tokenはTaskAuthが扱う。
const API_BASE_URL = "";
const API_URL = `${API_BASE_URL}/api/tasks`;
const PET_API_URL = `${API_BASE_URL}/api/pet`;
const USER_API_URL = `${API_BASE_URL}/api/user`;

// HTMLの要素を取得
const taskForm = document.getElementById("task-form");
const titleInput = document.getElementById("title");
const descriptionInput = document.getElementById("description");
const createStatusSelect = document.getElementById("create-status");
const dueDateInput = document.getElementById("due-date");
const prioritySelect = document.getElementById("priority");
const tagsInput = document.getElementById("tags");
const submitButton = document.getElementById("submit-button");
const currentUserName = document.getElementById("current-user-name");
const currentUserKey = document.getElementById("current-user-key");
const userPanel = document.querySelector(".user-panel");
const userLogoutButton = document.getElementById("user-logout-button");
const userDeleteButton = document.getElementById("user-delete-button");
const userMessage = document.getElementById("user-message");
const profileMessage = document.getElementById("profile-message");
const openCreateTaskButton = document.getElementById("open-create-task-button");
const createTaskModal = document.getElementById("create-task-modal");
const createModalCloseButton = document.getElementById("create-modal-close-button");
const createCancelButton = document.getElementById("create-cancel-button");
const createMessage = document.getElementById("create-message");
const taskList = document.getElementById("task-list");
const doingList = document.getElementById("doing-list");
const doneList = document.getElementById("done-list");
const doneColumn = document.getElementById("done-column");
const todoCount = document.getElementById("todo-count");
const doingCount = document.getElementById("doing-count");
const doneCount = document.getElementById("done-count");
const message = document.getElementById("message");
const filterSummary = document.getElementById("filter-summary");
const hideDoneToggle = document.getElementById("hide-done-toggle");
const summary = document.getElementById("summary");
const petSprite = document.getElementById("pet-sprite");
const petStatus = document.getElementById("pet-status");
const petNameForm = document.getElementById("pet-name-form");
const petNameInput = document.getElementById("pet-name-input");
const petNameButton = document.getElementById("pet-name-button");
const petLevel = document.getElementById("pet-level");
const petTitle = document.getElementById("pet-title");
const petExperience = document.getElementById("pet-experience");
const petExperienceBar = document.getElementById("pet-experience-bar");
const petEnergy = document.getElementById("pet-energy");
const petEnergyBar = document.getElementById("pet-energy-bar");
const petCompletedCount = document.getElementById("pet-completed-count");
const petStreak = document.getElementById("pet-streak");
const petAchievements = document.getElementById("pet-achievements");
const petEvent = document.getElementById("pet-event");
const filterForm = document.getElementById("filter-form");
const searchInput = document.getElementById("search");
const statusFilter = document.getElementById("status-filter");
const boardSearchStatusLabel = document.getElementById("board-search-status-label");
const sortOrderSelect = document.getElementById("sort-order");
const pageSizeSelect = document.getElementById("page-size");
const priorityFilter = document.getElementById("priority-filter");
const tagFilter = document.getElementById("tag-filter");
const prevPageButton = document.getElementById("prev-page");
const nextPageButton = document.getElementById("next-page");
const pageInfo = document.getElementById("page-info");
const editModal = document.getElementById("edit-modal");
const editTaskForm = document.getElementById("edit-task-form");
const editModalCloseButton = document.getElementById("edit-modal-close-button");
const editTitleInput = document.getElementById("edit-title");
const editDescriptionInput = document.getElementById("edit-description");
const editStatusSelect = document.getElementById("edit-status");
const editDueDateInput = document.getElementById("edit-due-date");
const editPrioritySelect = document.getElementById("edit-priority");
const editTagsInput = document.getElementById("edit-tags");
const editMessage = document.getElementById("edit-message");
const editDeleteButton = document.getElementById("edit-delete-button");
const editCancelButton = document.getElementById("edit-cancel-button");
const editSaveButton = document.getElementById("edit-save-button");
const boardSearchButtons = document.querySelectorAll("[data-board-search]");
const boardSearchModal = document.getElementById("board-search-modal");
const boardSearchTitle = document.getElementById("board-search-title");
const boardSearchCloseButton = document.getElementById("board-search-close-button");
const boardSearchClearButton = document.getElementById("board-search-clear-button");
const boardSearchCancelButton = document.getElementById("board-search-cancel-button");
const modalFocusOrigins = new Map();

const TASK_STATUS = {
    Todo: 0,
    Doing: 1,
    Done: 2
};

const BOARD_LABELS = {
    todo: "TODO",
    doing: "DOING",
    done: "DONE"
};

const BOARD_STATUS_VALUES = {
    todo: "Todo",
    doing: "Doing",
    done: "Done"
};

const TASK_PRIORITY = {
    Low: 0,
    Medium: 1,
    High: 2
};

const PRIORITY_LABELS = {
    [TASK_PRIORITY.Low]: "低",
    [TASK_PRIORITY.Medium]: "中",
    [TASK_PRIORITY.High]: "高"
};

const PET_STATES = {
    Idle: "idle",
    Happy: "happy",
    Working: "working",
    Proud: "proud",
    Sleepy: "sleepy",
    Sad: "sad"
};

const PET_STATE_LABELS = {
    [PET_STATES.Idle]: "待機中",
    [PET_STATES.Happy]: "よくできました",
    [PET_STATES.Working]: "集中中",
    [PET_STATES.Proud]: "誇らしげ",
    [PET_STATES.Sleepy]: "うとうと",
    [PET_STATES.Sad]: "しょんぼり"
};

const PET_STATE_CLASSES = Object.values(PET_STATES).map((petState) => `is-${petState}`);

const state = {
    statusCounts: null,
    teamId: null,
    taskLoadVersion: 0,
    isTaskMutation: false,
    page: 1,
    pageSize: 100,
    search: "",
    userKey: "guest",
    userProfileId: null,
    status: "",
    assignee: "",
    due: "",
    priority: "",
    tag: "",
    tagExact: "",
    untagged: false,
    hideDone: false,
    sortOrder: "desc",
    totalPages: 1,
    editingTask: null,
    isSaving: false,
    isEditSaving: false,
    draggingTask: null,
    petMoodTimer: null,
    petEventTimer: null,
    petProfile: null,
    boardCounts: {
        todo: 0,
        doing: 0,
        done: 0
    },
    visibleCount: 0,
    visibleTasks: [],
    totalCount: 0,
    isDeletingAccount: false
};

document.addEventListener("DOMContentLoaded", async () => {
    setupDropZone(taskList, TASK_STATUS.Todo);
    setupDropZone(doingList, TASK_STATUS.Doing);
    setupDropZone(doneList, TASK_STATUS.Done);
    document.querySelectorAll("[data-focus-drop]").forEach((zone) => setupDropZone(zone, Number(zone.dataset.focusDrop)));

    const userLoaded = await loadUserProfile();

    if (!userLoaded) {
        return;
    }

    // Resolve the remembered workspace before requesting any task list.
    await initializeOptions();
    await loadTasks();
    if (typeof TaskWorkflow !== "undefined") TaskWorkflow.initialize();
    if (typeof TeamBilling !== "undefined") await TeamBilling.handleReturn();
});

userLogoutButton.addEventListener("click", async () => {
    if (state.isDeletingAccount) {
        return;
    }

    await logoutUserProfile();
});

userDeleteButton.addEventListener("click", async () => {
    await deleteCurrentUser();
});

filterForm.addEventListener("submit", async (event) => {
    event.preventDefault();

    state.page = 1;
    state.search = searchInput.value.trim();
    state.status = statusFilter.value;
    state.priority = priorityFilter.value;
    state.tag = tagFilter.value.trim();
    state.sortOrder = sortOrderSelect.value;
    state.pageSize = Number(pageSizeSelect.value);
    if (typeof TaskWorkflow !== "undefined") TaskWorkflow.readFilters();

    await loadTasks();
    closeBoardSearchModal();
});

prevPageButton.addEventListener("click", async () => {
    if (state.page <= 1) {
        return;
    }

    state.page--;
    await loadTasks();
});

nextPageButton.addEventListener("click", async () => {
    if (state.page >= state.totalPages) {
        return;
    }

    state.page++;
    await loadTasks();
});

hideDoneToggle.addEventListener("change", async () => {
    state.hideDone = hideDoneToggle.checked;
    await loadTasks();
});

openCreateTaskButton.addEventListener("click", () => {
    openCreateTaskModal();
});

createModalCloseButton.addEventListener("click", () => {
    closeCreateTaskModal();
});

createCancelButton.addEventListener("click", () => {
    closeCreateTaskModal();
});

createTaskModal.addEventListener("click", (event) => {
    if (event.target === createTaskModal) {
        closeCreateTaskModal();
    }
});

petNameForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    await updatePetName();
});

editTaskForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    await saveTaskFromModal();
});

editModalCloseButton.addEventListener("click", () => {
    closeEditModal();
});

editCancelButton.addEventListener("click", () => {
    closeEditModal();
});

editDeleteButton.addEventListener("click", async () => {
    if (!state.editingTask || state.isEditSaving) {
        return;
    }

    const task = state.editingTask;
    const confirmed = confirm(`「${task.title}」を削除しますか？`);

    if (!confirmed) {
        return;
    }

    setEditSavingState(true);
    setEditMessage("削除中です");
    const result = await deleteTask(task);
    setEditSavingState(false);

    if (result.deleted) {
        closeEditModal(true);
        setMessage(
            result.tasksLoaded ? "タスクを削除しました" : "タスクは削除されましたが、表示を更新できませんでした。再読み込みしてください。",
            !result.tasksLoaded
        );
        return;
    }

    setEditMessage(result.errorMessage, true);
});

editModal.addEventListener("click", (event) => {
    if (event.target === editModal) {
        closeEditModal();
    }
});

boardSearchButtons.forEach((button) => {
    button.addEventListener("click", () => {
        openBoardSearchModal(button.dataset.boardSearch);
    });
});

boardSearchCloseButton.addEventListener("click", () => {
    closeBoardSearchModal();
});

boardSearchCancelButton.addEventListener("click", () => {
    closeBoardSearchModal();
});

boardSearchClearButton.addEventListener("click", async () => {
    await clearBoardSearch();
});

boardSearchModal.addEventListener("click", (event) => {
    if (event.target === boardSearchModal) {
        closeBoardSearchModal();
    }
});

document.addEventListener("keydown", (event) => {
    if (document.getElementById("companion-dialog")?.open) return;
    if (event.key === "Tab" && trapFocusInOpenModal(event)) {
        return;
    }

    if (event.key !== "Escape") {
        return;
    }

    if (typeof TaskTags !== "undefined" && TaskTags.closePickers(true)) {
        event.preventDefault();
        return;
    }

    if (!createTaskModal.classList.contains("hidden")) {
        closeCreateTaskModal();
        return;
    }

    if (!boardSearchModal.classList.contains("hidden")) {
        closeBoardSearchModal();
        return;
    }

    if (!editModal.classList.contains("hidden")) {
        closeEditModal();
        return;
    }

    closeOptionsModalOnEscape();
});

taskForm.addEventListener("submit", async (event) => {
    event.preventDefault();

    if (state.isSaving || (typeof PetPlay !== "undefined" && PetPlay.isSaving)) {
        return;
    }

    const requestStatus = Number(createStatusSelect.value);
    const previousPetProfile = getPetRewardSnapshot();
    const request = {
        title: titleInput.value.trim(),
        description: descriptionInput.value.trim(),
        isCompleted: requestStatus === TASK_STATUS.Done,
        status: requestStatus,
        dueDate: toApiDate(dueDateInput.value),
        priority: Number(prioritySelect.value),
        tags: tagsInput.value.trim()
    };
    if (typeof TaskDetails !== "undefined") Object.assign(request, TaskDetails.read("create"));

    const validationMessage = validateTaskRequest(request);

    if (validationMessage !== "") {
        setCreateMessage(validationMessage, true);
        titleInput.focus();
        return;
    }

    let saved = false;

    try {
        setSavingState(true);
        clearPetFeedback();
        setCreateMessage("保存中です");

        const response = await TaskAuth.request(getTasksApiUrl(), {
            method: "POST",
            headers: getRequestHeaders({
                "Content-Type": "application/json"
            }),
            body: JSON.stringify(request)
        });

        if (!response.ok) {
            throw new Error(await getErrorMessage(response, "タスクの作成に失敗しました。"));
        }

        state.page = 1;
        const tasksLoaded = await loadTasks();

        const progressionLoaded = await refreshProgression();
        if (progressionLoaded && requestStatus === TASK_STATUS.Done) showPetReward(previousPetProfile, 1);

        saved = true;
        setMessage(
            tasksLoaded ? "タスクを追加しました" : "タスクは保存されましたが、表示を更新できませんでした。再読み込みしてください。",
            !tasksLoaded
        );
    } catch (error) {
        setCreateMessage(getDisplayErrorMessage(error, "タスクの作成に失敗しました。"), true);
    } finally {
        setSavingState(false);

        if (saved) {
            closeCreateTaskModal(true);
        }
    }
});

function taskQueryParameters() {
    const params = new URLSearchParams();
    params.set("page", state.page);
    params.set("pageSize", state.pageSize);
    params.set("sortOrder", state.sortOrder);
    for (const name of ["search", "status", "priority", "tag", "assignee", "due"]) if (state[name]) params.set(name, state[name]);
    if (state.tagExact !== "") params.set("tagExact", state.tagExact);
    else if (state.untagged) params.set("untagged", "true");
    return params;
}

async function loadTasks({ background = false } = {}) {
    const loadVersion = ++state.taskLoadVersion;
    const scopeTeamId = state.teamId;
    document.getElementById("workspace-task-panel").setAttribute("aria-busy", "true");
    // Tag counts are whole-workspace data, never counts from the current search/page.
    const tagLoad = !background && typeof TaskTags !== "undefined" ? TaskTags.refresh(scopeTeamId) : Promise.resolve();
    try {
        if (!background) setMessage("読み込み中です");
        const params = taskQueryParameters();

        const response = await TaskAuth.request(`${getTasksApiUrl(scopeTeamId)}?${params.toString()}`, {
            headers: getRequestHeaders()
        });

        if (!response.ok) {
            throw new Error(await getErrorMessage(response, "タスクの取得に失敗しました。"));
        }

        const data = await response.json();

        if (loadVersion !== state.taskLoadVersion || scopeTeamId !== state.teamId) {
            return false;
        }

        if (background && (isTaskBusy() || state.draggingTask || document.hidden || document.querySelector('.modal-backdrop:not(.hidden)') || document.querySelector('.task-item:focus-within'))) return false;

        state.page = data.page;
        state.pageSize = data.pageSize;
        state.totalPages = data.totalPages;
        state.totalCount = data.totalCount;
        state.statusCounts = data.statusCounts || null;

        renderTasks(data.items);
        renderPagination(data);
        renderFilterSummary(data);
        summary.textContent = `${data.totalCount} tasks`;
        setMessage("");
        await Promise.all([loadWorkspaceProgress(scopeTeamId), background && typeof TaskTags !== "undefined" ? TaskTags.refresh(scopeTeamId) : tagLoad]);
        return true;
    } catch (error) {
        if (loadVersion !== state.taskLoadVersion || scopeTeamId !== state.teamId) {
            return false;
        }
        if (background) return false;
        setMessage(getDisplayErrorMessage(error, "タスクを読み込めませんでした。"), true);
        state.totalCount = 0;
        state.statusCounts = null;
        state.totalPages = 0;
        renderTasks([]);
        renderPagination({ page: 1, totalPages: 0 });
        document.getElementById("workspace-progress").classList.add("hidden");
        renderFilterSummary();
        return false;
    } finally {
        if (loadVersion === state.taskLoadVersion && scopeTeamId === state.teamId) {
            document.getElementById("workspace-task-panel").setAttribute("aria-busy", "false");
        }
    }
}

async function loadPetProfile() {
    try {
        const response = await TaskAuth.request(PET_API_URL, {
            headers: getRequestHeaders()
        });

        if (!response.ok) {
            throw new Error(await getErrorMessage(response, "ペット情報の取得に失敗しました。"));
        }

        const profile = await response.json();
        state.petProfile = profile;
        renderPetProfile(profile);
    } catch (error) {
        setPetState(PET_STATES.Sad, "育成データを読めません");
        showPetEvent(getDisplayErrorMessage(error, "育成データを読めません。"), true);
    }
}

async function loadUserProfile() {
    try {
        const response = await TaskAuth.request(USER_API_URL, {
            headers: getRequestHeaders()
        });

        if (!response.ok) {
            const errorMessage = await getErrorMessage(response, "ユーザー情報の取得に失敗しました。");

            if (response.status === 401) {
                return false;
            }

            throw new Error(errorMessage);
        }

        const profile = await response.json();

        if (!profile.userKey || profile.userKey === "guest") {
            redirectToLogin("ログイン期限が切れました。もう一度ログインしてください。");
            return false;
        }

        state.userKey = profile.userKey;
        state.userProfileId = profile.id;
        renderCurrentUser(profile);
        setUserMessage("");
        profileMessage.textContent = "";
        return true;
    } catch (error) {
        profileMessage.textContent = getDisplayErrorMessage(error, "ユーザー情報を読めません。再読み込みしてください。");
        profileMessage.classList.add("error");
        return false;
    }
}

async function logoutUserProfile() {
    if (typeof PetPlay !== "undefined" && PetPlay.isSaving) return;
    userLogoutButton.disabled = true;
    try {
        const response = await TaskAuth.request(`${API_BASE_URL}/api/auth/logout`, { method: "POST" });
        if (!response.ok && response.status !== 401) {
            throw new Error(await getErrorMessage(response, "ログアウトできませんでした。もう一度お試しください。"));
        }
        redirectToLogin("ログアウトしました。");
    } catch (error) {
        setUserMessage(getDisplayErrorMessage(error, "ログアウトできませんでした。"), true);
    } finally {
        userLogoutButton.disabled = false;
    }
}

async function deleteCurrentUser() {
    if (state.isDeletingAccount || (typeof PetPlay !== "undefined" && PetPlay.isSaving)) {
        return;
    }

    const confirmed = confirm("アカウント、個人のタスク、育成データを削除し、参加しているチームから退出します。共有タスクはチームに残ります。所有チームがある場合は先に所有権の変更かチーム削除が必要です。この操作は元に戻せません。続けますか？");

    if (!confirmed) {
        return;
    }

    try {
        setAccountDeleteState(true);
        setUserMessage("アカウントを削除しています");

        const response = await TaskAuth.request(USER_API_URL, {
            method: "DELETE",
            headers: getRequestHeaders()
        });

        if (!response.ok) {
            throw new Error(await getErrorMessage(response, "アカウントを削除できませんでした。"));
        }

        redirectToLogin("アカウントを削除しました。");
    } catch (error) {
        setUserMessage(getDisplayErrorMessage(error, "アカウントを削除できませんでした。"), true);
    } finally {
        setAccountDeleteState(false);
    }
}

function setAccountDeleteState(isDeleting) {
    state.isDeletingAccount = isDeleting;
    userPanel.setAttribute("aria-busy", String(isDeleting));
    userLogoutButton.disabled = isDeleting;
    userDeleteButton.disabled = isDeleting;
    userDeleteButton.textContent = isDeleting ? "削除中" : "アカウント削除";
}

function redirectToLogin(messageText) {
    const params = new URLSearchParams();

    if (messageText) {
        params.set("message", messageText);
    }

    window.location.href = `login.html${params.toString() ? `?${params.toString()}` : ""}`;
}

function renderCurrentUser(profile) {
    currentUserName.textContent = profile.displayName || profile.userKey;
    currentUserKey.textContent = `@${profile.userKey}`;
    document.getElementById("settings-current-user").textContent = `${profile.displayName || profile.userKey} (@${profile.userKey})`;
}

function getRequestHeaders(headers = {}) {
    return { ...headers };
}

function setUserMessage(text, isError = false) {
    userMessage.textContent = text;
    userMessage.classList.toggle("error", isError);
    userMessage.classList.toggle("success", text.endsWith("ました") && !isError);
}

async function updatePetName() {
    if (petNameButton.disabled || (typeof PetPlay !== "undefined" && PetPlay.isSaving)) return;
    const name = petNameInput.value.trim();
    const species = document.querySelector('input[name="species"]:checked')?.value || "dog";
    if (!isChoiceUnlocked("pets", species)) {
        setPetSettingsMessage("まだ解放されていない相棒です。必要レベルを確認してください。", true);
        return;
    }

    if (name === "") {
        setPetSettingsMessage("名前を入力してください", true);
        petNameInput.focus();
        return;
    }

    try {
        petNameButton.disabled = true;
        petNameButton.textContent = "保存中";
        petNameForm.setAttribute("aria-busy", "true");

        const response = await TaskAuth.request(PET_API_URL, {
            method: "PUT",
            headers: getRequestHeaders({
                "Content-Type": "application/json"
            }),
            body: JSON.stringify({ name, species })
        });

        if (!response.ok) {
            const errorMessage = await getErrorMessage(response, "ペット設定の更新に失敗しました。");
            if (response.status === 403) await refreshProgression();
            throw new Error(errorMessage);
        }

        const profile = await response.json();
        state.petProfile = profile;
        renderPetProfile(profile);
        setPetSettingsMessage("ペット設定を保存しました");
    } catch (error) {
        setPetSettingsMessage(getDisplayErrorMessage(error, "ペット設定の更新に失敗しました。"), true);
    } finally {
        petNameButton.disabled = false;
        petNameButton.textContent = "ペット設定を保存";
        petNameForm.setAttribute("aria-busy", "false");
    }
}

function renderTasks(tasks) {
    taskList.innerHTML = "";
    doingList.innerHTML = "";
    doneList.innerHTML = "";

    const todoTasks = tasks.filter((task) => getTaskStatus(task) === TASK_STATUS.Todo);
    const doingTasks = tasks.filter((task) => getTaskStatus(task) === TASK_STATUS.Doing);
    const doneTasks = tasks.filter((task) => getTaskStatus(task) === TASK_STATUS.Done);
    const visibleTodoTasks = isBoardVisible("todo") ? todoTasks : [];
    const visibleDoingTasks = isBoardVisible("doing") ? doingTasks : [];
    const visibleDoneTasks = isBoardVisible("done") ? doneTasks : [];
    const shouldHideDoneColumn = state.hideDone && state.status !== BOARD_STATUS_VALUES.done;

    for (const [key, counter, shown] of [["todo", todoCount, visibleTodoTasks.length], ["doing", doingCount, visibleDoingTasks.length], ["done", doneCount, visibleDoneTasks.length]]) {
        const total = state.statusCounts?.[key];
        counter.textContent = Number.isInteger(total) && total !== shown ? `${shown} / ${total}` : String(shown);
        counter.title = Number.isInteger(total) ? `このページ ${shown}件 / 検索対象全体 ${total}件` : `このページ ${shown}件`;
    }
    doneColumn.classList.toggle("hidden", shouldHideDoneColumn);
    state.boardCounts = {
        todo: visibleTodoTasks.length,
        doing: visibleDoingTasks.length,
        done: visibleDoneTasks.length
    };
    state.visibleCount = visibleTodoTasks.length + visibleDoingTasks.length + visibleDoneTasks.length;
    state.visibleTasks = [...visibleTodoTasks, ...visibleDoingTasks, ...visibleDoneTasks];

    renderTaskColumn(taskList, visibleTodoTasks, getEmptyMessage("todo", "TODOのタスクはありません"));
    renderTaskColumn(doingList, visibleDoingTasks, getEmptyMessage("doing", "DOINGのタスクはありません"));
    renderTaskColumn(doneList, visibleDoneTasks, getEmptyMessage("done", "DONEのタスクはありません"));
    renderBoardSearchButtonStates();
    renderFocusLayout();
    if (typeof TaskWorkflow !== "undefined") TaskWorkflow.syncFilters();

    if (!state.petMoodTimer) {
        updatePetStateFromBoard();
    }
}

function isBoardVisible(boardKey) {
    if (boardKey === "done" && state.hideDone && state.status !== BOARD_STATUS_VALUES.done) {
        return false;
    }

    return state.status === "" || state.status === BOARD_STATUS_VALUES[boardKey];
}

function renderTaskColumn(listElement, tasks, emptyMessage) {
    if (tasks.length === 0) {
        const emptyItem = document.createElement("li");
        emptyItem.className = "empty";
        emptyItem.textContent = emptyMessage;
        listElement.appendChild(emptyItem);
        return;
    }

    tasks.forEach((task) => {
        const item = createTaskItem(task);
        listElement.appendChild(item);
    });
}

function getEmptyMessage(boardKey, defaultMessage) {
    if (state.status !== "" && state.status !== BOARD_STATUS_VALUES[boardKey]) {
        return "検索対象外です";
    }

    if (state.statusCounts?.[boardKey] > 0) {
        return `このページにはありません（検索対象全体に${state.statusCounts[boardKey]}件）。列の検索ボタンから表示できます。`;
    }
    if (!state.statusCounts && state.totalPages > 1) return "このページにはありません。他のページも確認してください。";

    if (hasActiveTaskFilter()) {
        return "検索に一致するタスクはありません";
    }

    return defaultMessage;
}

function renderBoardSearchButtonStates() {
    const hasActiveFilter = hasActiveTaskFilter();

    boardSearchButtons.forEach((button) => {
        const boardKey = button.dataset.boardSearch;
        const isTargetBoard = state.status === BOARD_STATUS_VALUES[boardKey];

        button.classList.toggle("is-active", hasActiveFilter && isTargetBoard);
    });
}

function hasActiveTaskFilter() {
    return state.search !== ""
        || state.status !== ""
        || state.priority !== ""
        || state.tag !== ""
        || state.tagExact !== ""
        || state.untagged
        || !!state.assignee
        || !!state.due
        || state.hideDone
        || state.sortOrder !== "desc"
        || state.pageSize !== 100;
}

function renderFilterSummary(data = null) {
    if (!filterSummary) {
        return;
    }

    const team = typeof optionsState !== "undefined" ? optionsState.teams?.find((item) => item.id === state.teamId) : null;
    const parts = [state.teamId ? `${team?.name || "チーム"}（共有）` : "個人（非公開）"];
    const resultCount = data?.totalCount ?? state.totalCount;

    if (state.status !== "") {
        parts.push(`${getStatusLabel(state.status)}のみ`);
    } else {
        parts.push("すべて");
    }

    if (state.search !== "") {
        parts.push(`キーワード: ${state.search}`);
    }

    if (state.priority !== "") {
        parts.push(`優先度: ${getPriorityLabel(state.priority)}`);
    }

    if (state.tag !== "") {
        parts.push(`タグ: ${state.tag}`);
    }
    if (state.tagExact !== "") parts.push(`分類: ${state.tagExact}`);
    else if (state.untagged) parts.push("未分類");

    if (state.hideDone && state.status !== BOARD_STATUS_VALUES.done) {
        parts.push("DONE非表示");
    }

    if (state.assignee) parts.push(state.assignee === "me" ? "自分の担当" : state.assignee === "unassigned" ? "未割り当て" : `担当: ${document.getElementById("assignee-filter")?.selectedOptions?.[0]?.textContent || "指定メンバー"}`);
    if (state.due) parts.push(({ today: "今日が期限（未完了）", through_today: "今日まで（未完了）", overdue: "期限超過（未完了）", none: "期限なし" })[state.due]);
    parts.push(({ asc: "古い順", desc: "新しい順", due: "期限が近い順" })[state.sortOrder] || "新しい順");
    parts.push(state.totalPages > 1 ? `このページ ${state.visibleCount}件 / 全 ${resultCount}件` : `${resultCount}件`);

    filterSummary.textContent = parts.join(" / ");
    filterSummary.classList.toggle("is-active", hasActiveTaskFilter());
}

function getStatusLabel(statusValue) {
    if (statusValue === "Todo") {
        return "TODO";
    }

    if (statusValue === "Doing") {
        return "DOING";
    }

    if (statusValue === "Done") {
        return "DONE";
    }

    return "すべて";
}

function getPriorityLabel(priorityValue) {
    const priority = Number(priorityValue);

    return PRIORITY_LABELS[priority] || "中";
}

function createTaskItem(task) {
    const status = getTaskStatus(task);
    const priority = getTaskPriority(task);
    const item = document.createElement("li");
    item.dataset.taskId = String(task.id);
    item.className = status === TASK_STATUS.Done
        ? `task-item priority-${priority} is-completed`
        : `task-item priority-${priority}`;
    item.draggable = true;

    item.addEventListener("dragstart", () => {
        state.draggingTask = task;
        item.classList.add("is-dragging");
    });

    item.addEventListener("dragend", () => {
        state.draggingTask = null;
        item.classList.remove("is-dragging");
    });

    const content = document.createElement("div");
    content.className = "task-content";

    const title = document.createElement("p");
    title.className = "task-title";
    title.textContent = task.title;

    const description = document.createElement("p");
    description.className = "task-description";
    description.textContent = task.description || "説明なし";

    const meta = document.createElement("div");
    meta.className = "task-meta";

    const priorityBadge = document.createElement("span");
    priorityBadge.className = `priority-badge priority-${priority}`;
    priorityBadge.textContent = `優先度 ${getPriorityLabel(priority)}`;
    meta.appendChild(priorityBadge);

    if (task.dueDate) {
        const dueDate = document.createElement("span");
        dueDate.className = isTaskOverdue(task) ? "due-date is-overdue" : "due-date";
        dueDate.textContent = `期限 ${formatDueDate(task.dueDate)}`;
        dueDate.title = "期限は日本時間（JST）の日付で判定します。当日期限は期限超過に含みません。";
        meta.appendChild(dueDate);
    }

    const tagNames = getTaskTags(task);

    if (tagNames.length > 0) {
        const tags = document.createElement("div");
        tags.className = "task-tags";

        tagNames.slice(0, 3).forEach((tag) => {
            const tagElement = document.createElement("span");
            tagElement.className = "task-tag";
            tagElement.textContent = tag;
            tags.appendChild(tagElement);
        });

        meta.appendChild(tags);
    }

    const editButton = document.createElement("button");
    editButton.type = "button";
    editButton.className = "edit-button";
    editButton.textContent = "編集";
    editButton.dataset.editTaskId = String(task.id);
    editButton.setAttribute("aria-label", `${task.title}を編集`);
    editButton.addEventListener("click", () => startEditTask(task));

    const actions = document.createElement("div");
    actions.className = "task-actions";
    if (typeof TaskWorkflow !== "undefined") TaskWorkflow.addQuickControls(task, actions);
    actions.appendChild(editButton);
    const memoButton = document.createElement("button"); memoButton.type = "button"; memoButton.className = "task-memo-button";
    memoButton.textContent = "メモ"; memoButton.setAttribute("aria-label", `${task.title}の作業メモを開く`);
    memoButton.addEventListener("click", () => { if (typeof TaskCompanion !== "undefined") void TaskCompanion.openTask(task.id); });
    actions.appendChild(memoButton);

    content.appendChild(title);
    content.appendChild(description);
    content.appendChild(meta);
    if (typeof TaskDetails !== "undefined") TaskDetails.badges(task, meta);

    if (state.teamId && task.createdByDisplayName) {
        const creator = document.createElement("span");
        creator.className = "task-assignee";
        creator.textContent = `作成: ${task.createdByDisplayName}`;
        content.appendChild(creator);
    }

    item.appendChild(content);
    item.appendChild(actions);

    return item;
}

function getTaskStatus(task) {
    if (task.status === "Doing") {
        return TASK_STATUS.Doing;
    }

    if (task.status === "Done") {
        return TASK_STATUS.Done;
    }

    if (task.status === "Todo") {
        return TASK_STATUS.Todo;
    }

    if (typeof task.status === "number") {
        return task.status;
    }

    return task.isCompleted ? TASK_STATUS.Done : TASK_STATUS.Todo;
}

function getTaskTags(task) {
    if (!task.tags) {
        return [];
    }

    return task.tags
        .split(",")
        .map((tag) => tag.trim())
        .filter((tag) => tag !== "");
}

function isTaskOverdue(task, now = new Date()) {
    if (!task.dueDate || getTaskStatus(task) === TASK_STATUS.Done) {
        return false;
    }

    const dueDate = new Date(task.dueDate);
    if (Number.isNaN(dueDate.getTime())) return false;

    // Due dates are date-only values encoded at UTC midnight, not local instants.
    // Match the API's calendar boundary even when the viewer is outside Japan.
    return dueDate.toISOString().slice(0, 10) < getTokyoDateKey(now);
}

function getTokyoDateKey(date) {
    const parts = new Intl.DateTimeFormat("en-US", {
        timeZone: "Asia/Tokyo", year: "numeric", month: "2-digit", day: "2-digit"
    }).formatToParts(date);
    const value = (type) => parts.find((part) => part.type === type).value;
    return `${value("year")}-${value("month")}-${value("day")}`;
}

function formatDueDate(value) {
    const date = new Date(value);

    if (Number.isNaN(date.getTime())) {
        return "";
    }

    return date.toISOString().slice(0, 10).replaceAll("-", "/");
}

function toDateInputValue(value) {
    if (!value) {
        return "";
    }

    const date = new Date(value);

    if (Number.isNaN(date.getTime())) {
        return "";
    }

    return date.toISOString().slice(0, 10);
}

function toApiDate(value) {
    if (!value) {
        return null;
    }

    return `${value}T00:00:00.000Z`;
}

function getTaskPriority(task) {
    if (task.priority === "High") {
        return TASK_PRIORITY.High;
    }

    if (task.priority === "Low") {
        return TASK_PRIORITY.Low;
    }

    if (task.priority === "Medium") {
        return TASK_PRIORITY.Medium;
    }

    if (typeof task.priority === "number") {
        return task.priority;
    }

    return TASK_PRIORITY.Medium;
}

function setupDropZone(listElement, nextStatus) {
    if (!listElement) {
        return;
    }

    listElement.addEventListener("dragover", (event) => {
        event.preventDefault();
        listElement.classList.add("is-drag-over");
    });

    listElement.addEventListener("dragleave", () => {
        listElement.classList.remove("is-drag-over");
    });

    listElement.addEventListener("drop", async (event) => {
        event.preventDefault();
        listElement.classList.remove("is-drag-over");

        if (!state.draggingTask) {
            return;
        }

        const currentStatus = getTaskStatus(state.draggingTask);

        if (currentStatus === nextStatus) {
            return;
        }

        await moveTaskStatus(state.draggingTask, nextStatus);
    });
}

function renderPetProfile(profile) {
    if (!profile) {
        return;
    }

    const experienceToNextLevel = Math.max(1, profile.experienceToNextLevel);
    const rawProgress = Number.isInteger(profile.experienceProgress)
        ? profile.experienceProgress
        : Math.min(100, Math.round((profile.experience / experienceToNextLevel) * 100));
    const progress = Math.min(100, Math.max(0, rawProgress));
    const rawEnergy = Number(profile.energy);
    const energy = Number.isFinite(rawEnergy) ? Math.min(100, Math.max(0, rawEnergy)) : 0;

    if (petNameInput && document.activeElement !== petNameInput) {
        petNameInput.value = profile.name;
    }
    const species = isChoiceUnlocked("pets", profile.species) ? profile.species : "dog";
    document.getElementById("pet-name-display").textContent = profile.name;
    document.getElementById("pet-species-display").textContent = getUnlockOption("pets", species)?.name || "いぬ";
    petSprite.dataset.species = species;
    if (typeof PetPlay !== "undefined") PetPlay.onProfile(profile);
    if (!document.getElementById("settings-pet").contains(document.activeElement)
        || document.querySelector('input[name="species"]:checked')?.disabled) {
        document.querySelectorAll('input[name="species"]').forEach((input) => { input.checked = input.value === species; });
    }

    if (petLevel) {
        petLevel.textContent = `Lv.${profile.level}`;
    }

    if (petTitle) {
        petTitle.textContent = profile.title || "見習い相棒";
    }

    if (petExperience) {
        petExperience.textContent = `${profile.experience} / ${experienceToNextLevel} EXP`;
    }

    if (petExperienceBar) {
        petExperienceBar.style.width = `${progress}%`;
        petExperienceBar.setAttribute("aria-valuenow", String(progress));
        petExperienceBar.setAttribute("aria-valuetext", `${profile.experience} / ${experienceToNextLevel} EXP`);
    }

    if (petEnergy) {
        petEnergy.textContent = `元気 ${energy}`;
        petEnergy.title = profile.energyLabel || "";
    }

    if (petEnergyBar) {
        petEnergyBar.style.width = `${energy}%`;
        petEnergyBar.setAttribute("aria-valuenow", String(energy));
        petEnergyBar.setAttribute("aria-valuetext", `${energy}%${profile.energyLabel ? ` (${profile.energyLabel})` : ""}`);
    }

    if (petCompletedCount) {
        petCompletedCount.textContent = `完了 ${profile.completedTaskCount}件`;
    }

    if (petStreak) {
        petStreak.textContent = `連続 ${profile.streakDays}日`;
    }

    renderPetAchievements(profile.achievements || []);

    if (!state.petMoodTimer && state.boardCounts.doing > 0) {
        setPetState(PET_STATES.Working);
        return;
    }

    if (!state.petMoodTimer) {
        // Steady mood text must not replay a previous completion as a new XP award.
        setPetState(getPetStateFromProfile(profile));
    }
}

function renderPetAchievements(achievements) {
    if (!petAchievements) {
        return;
    }

    petAchievements.innerHTML = "";

    achievements.slice(0, 4).forEach((achievement) => {
        const badge = document.createElement("span");
        badge.className = "pet-achievement";
        badge.textContent = achievement;
        petAchievements.appendChild(badge);
    });
}

function getPetStateFromProfile(profile) {
    const mood = String(profile.mood || "").toLowerCase();

    if (Object.values(PET_STATES).includes(mood)) {
        return mood;
    }

    return PET_STATES.Idle;
}

function setPetState(nextState, labelText = PET_STATE_LABELS[nextState]) {
    if (!petSprite || !petStatus) {
        return;
    }

    const resolvedState = Object.values(PET_STATES).includes(nextState) ? nextState : PET_STATES.Idle;
    const resolvedLabel = labelText || PET_STATE_LABELS[resolvedState];

    petSprite.classList.remove(...PET_STATE_CLASSES);
    petSprite.classList.add(`is-${resolvedState}`);
    petStatus.textContent = resolvedLabel;
    petSprite.setAttribute("aria-label", `育成キャラクター: ${resolvedLabel}`);
    if (typeof PetPlay !== "undefined") PetPlay.onMood(resolvedState);
}

function updatePetStateFromBoard() {
    if (state.boardCounts.doing > 0) {
        setPetState(PET_STATES.Working);
        return;
    }

    if (state.petProfile) {
        renderPetProfile(state.petProfile);
        return;
    }

    if (state.boardCounts.todo >= 5 && state.boardCounts.done === 0) {
        setPetState(PET_STATES.Sleepy);
        return;
    }

    setPetState(PET_STATES.Idle);
}

function showTemporaryPetState(nextState, labelText = PET_STATE_LABELS[nextState]) {
    clearTimeout(state.petMoodTimer);
    setPetState(nextState, labelText);

    state.petMoodTimer = setTimeout(() => {
        state.petMoodTimer = null;
        updatePetStateFromBoard();
    }, 2400);
}

function getPetRewardSnapshot() {
    const totalExperience = state.petProfile?.totalExperience;

    return {
        level: Number(state.petProfile?.level) || 1,
        totalExperience: Number.isFinite(totalExperience) ? totalExperience : null,
        species: state.petProfile?.species,
        theme: optionsState.preferences.theme,
        layout: optionsState.preferences.layout
    };
}

function clearPetFeedback() {
    clearTimeout(state.petMoodTimer);
    clearTimeout(state.petEventTimer);
    state.petMoodTimer = null;
    state.petEventTimer = null;
    petEvent.classList.add("hidden");
    petEvent.textContent = "";
    petSprite.closest(".pet-panel")?.classList.remove("is-level-up");
    updatePetStateFromBoard();
}

function showPetReward(previousProfile, expectedDirection = 1) {
    const previousLevel = Number(previousProfile?.level) || 1;
    const previousTotalExperience = previousProfile?.totalExperience;
    const currentLevel = Number(state.petProfile?.level) || previousLevel;
    const currentTotalExperience = state.petProfile?.totalExperience;

    if (!Number.isFinite(previousTotalExperience)
        || !Number.isFinite(currentTotalExperience)
        || currentTotalExperience === previousTotalExperience) {
        return false;
    }

    const experienceGained = currentTotalExperience - previousTotalExperience;
    if (Math.sign(experienceGained) !== Math.sign(expectedDirection)) return false;

    if (experienceGained < 0) {
        const hasReset = (previousProfile.species && previousProfile.species !== state.petProfile?.species)
            || (previousProfile.theme && previousProfile.theme !== optionsState.preferences.theme)
            || (previousProfile.layout && previousProfile.layout !== optionsState.preferences.layout);
        const levelMessage = currentLevel < previousLevel ? ` / Lv.${previousLevel} → Lv.${currentLevel}` : "";
        const resetMessage = hasReset ? " / 表示設定を最新の保存内容に同期しました" : "";
        showTemporaryPetState(PET_STATES.Idle, "完了を取り消しました");
        showPetEvent(`完了取り消し：${experienceGained} EXP${levelMessage}${resetMessage}`);
        return true;
    }

    if (currentLevel > previousLevel) {
        showTemporaryPetState(PET_STATES.Proud, `レベルアップ！ Lv.${currentLevel}`);
        showPetEvent(`レベルアップ！ Lv.${previousLevel} → Lv.${currentLevel} / +${experienceGained} EXP${currentLevel <= 5 ? " / 相棒を押してごほうびを選ぼう" : ""}`);
        petSprite.closest(".pet-panel")?.classList.add("is-level-up");

        setTimeout(() => {
            petSprite.closest(".pet-panel")?.classList.remove("is-level-up");
        }, 1800);

        return true;
    }

    showTemporaryPetState(PET_STATES.Happy, "経験値を獲得しました");
    showPetEvent(`+${experienceGained} EXP / 次まで ${state.petProfile?.experienceRemaining || 0} EXP`);
    return true;
}

function showPetEvent(text, isError = false) {
    if (!petEvent) {
        return;
    }

    clearTimeout(state.petEventTimer);

    petEvent.textContent = text;
    petEvent.classList.remove("hidden");
    petEvent.classList.toggle("error", isError);

    state.petEventTimer = setTimeout(() => {
        petEvent.classList.add("hidden");
    }, 2600);
}

function renderPagination(data) {
    const totalPages = data.totalPages === 0 ? 1 : data.totalPages;

    pageInfo.textContent = `${data.page} / ${totalPages}`;
    prevPageButton.disabled = data.page <= 1;
    nextPageButton.disabled = data.page >= data.totalPages || data.totalPages === 0;
}

function openCreateTaskModal() {
    if (typeof TaskTags !== "undefined") TaskTags.closePickers();
    rememberModalFocus(createTaskModal);
    resetTaskForm();
    tagsInput.value = state.tagExact;
    if (typeof TaskDetails !== "undefined") TaskDetails.open("create");
    if (typeof TaskTags !== "undefined") TaskTags.renderPickers();
    setCreateMessage("");

    if (typeof TaskWorkflow !== "undefined") TaskWorkflow.rememberForm("create");
    createTaskModal.classList.remove("hidden");
    updateModalOpenState();
    titleInput.focus();
}

function closeCreateTaskModal(force = false) {
    if (state.isSaving && !force) {
        return;
    }

    if (!force && typeof TaskWorkflow !== "undefined" && !TaskWorkflow.allowClose("create")) return false;

    resetTaskForm();
    if (typeof TaskTags !== "undefined") TaskTags.closePickers();
    setCreateMessage("");

    createTaskModal.classList.add("hidden");
    updateModalOpenState();
    restoreModalFocus(createTaskModal, openCreateTaskButton);
}

function startEditTask(task) {
    if (typeof TaskTags !== "undefined") TaskTags.closePickers();
    rememberModalFocus(editModal);
    state.editingTask = task;
    if (typeof TaskDetails !== "undefined") TaskDetails.open("edit", task);

    editTitleInput.value = task.title;
    editDescriptionInput.value = task.description || "";
    editStatusSelect.value = String(getTaskStatus(task));
    editDueDateInput.value = toDateInputValue(task.dueDate);
    editPrioritySelect.value = String(getTaskPriority(task));
    editTagsInput.value = task.tags || "";
    if (typeof TaskTags !== "undefined") TaskTags.renderPickers();
    setEditMessage("");

    if (typeof TaskWorkflow !== "undefined") TaskWorkflow.rememberForm("edit");
    editModal.classList.remove("hidden");
    updateModalOpenState();
    editTitleInput.focus();
}

function resetTaskForm() {
    taskForm.reset();
    tagsInput.value = "";
    submitButton.textContent = "追加";
}

function closeEditModal(force = false) {
    if (state.isEditSaving && !force) {
        return;
    }

    if (!force && typeof TaskWorkflow !== "undefined" && !TaskWorkflow.allowClose("edit")) return false;

    const editingTaskId = state.editingTask?.id;
    state.editingTask = null;
    editTaskForm.reset();
    editTagsInput.value = "";
    if (typeof TaskTags !== "undefined") TaskTags.closePickers();
    setEditMessage("");

    editModal.classList.add("hidden");
    updateModalOpenState();

    const replacementEditButton = Array.from(document.querySelectorAll("[data-edit-task-id]"))
        .find((button) => button.dataset.editTaskId === String(editingTaskId));
    restoreModalFocus(editModal, replacementEditButton || openCreateTaskButton);
}

function openBoardSearchModal(boardKey) {
    if (boardKey !== "all" && (!BOARD_LABELS[boardKey] || !BOARD_STATUS_VALUES[boardKey])) {
        return;
    }

    rememberModalFocus(boardSearchModal);

    if (boardKey === "done" && state.hideDone) {
        state.hideDone = false;
        hideDoneToggle.checked = false;
    }

    boardSearchTitle.textContent = "タスクの検索・絞り込み";
    syncSearchFormFromState();
    statusFilter.value = boardKey === "all" ? "" : BOARD_STATUS_VALUES[boardKey];
    boardSearchStatusLabel.textContent = BOARD_LABELS[boardKey] || "すべて";
    if (typeof TaskWorkflow !== "undefined") void TaskWorkflow.loadAssignees();

    boardSearchModal.classList.remove("hidden");
    updateModalOpenState();
    searchInput.focus();
    searchInput.select();
}

function closeBoardSearchModal() {
    boardSearchModal.classList.add("hidden");
    updateModalOpenState();
    restoreModalFocus(boardSearchModal);
}

async function clearBoardSearch() {
    state.page = 1;
    state.search = "";
    state.status = "";
    state.priority = "";
    state.tag = "";
    state.hideDone = false;
    state.sortOrder = "desc";
    state.pageSize = 100;
    state.assignee = "";
    state.due = "";
    hideDoneToggle.checked = false;

    syncSearchFormFromState();
    await loadTasks();
    closeBoardSearchModal();
}

function syncSearchFormFromState() {
    if (typeof TaskWorkflow !== "undefined") TaskWorkflow.syncFilters();
    searchInput.value = state.search;
    statusFilter.value = state.status;
    priorityFilter.value = state.priority;
    tagFilter.value = state.tag;
    sortOrderSelect.value = state.sortOrder;
    pageSizeSelect.value = String(state.pageSize);
}

function updateModalOpenState() {
    const hasOpenModal = !createTaskModal.classList.contains("hidden")
        || !editModal.classList.contains("hidden")
        || !boardSearchModal.classList.contains("hidden")
        || !document.getElementById("settings-modal").classList.contains("hidden")
        || !document.getElementById("team-modal").classList.contains("hidden");

    document.body.classList.toggle("is-modal-open", hasOpenModal);
}

function trapFocusInOpenModal(event) {
    const openModal = [createTaskModal, editModal, boardSearchModal, document.getElementById("settings-modal"), document.getElementById("team-modal")]
        .find((modal) => !modal.classList.contains("hidden"));

    if (!openModal) {
        return false;
    }

    const focusableElements = Array.from(openModal.querySelectorAll(
        'button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [href], [tabindex]:not([tabindex="-1"])'
    )).filter((element) => element.getClientRects().length > 0);

    if (focusableElements.length === 0) {
        event.preventDefault();
        return true;
    }

    const firstElement = focusableElements[0];
    const lastElement = focusableElements[focusableElements.length - 1];

    if (event.shiftKey && document.activeElement === firstElement) {
        event.preventDefault();
        lastElement.focus();
        return true;
    }

    if (!event.shiftKey && document.activeElement === lastElement) {
        event.preventDefault();
        firstElement.focus();
        return true;
    }

    if (!openModal.contains(document.activeElement)) {
        event.preventDefault();
        firstElement.focus();
        return true;
    }

    return false;
}

function rememberModalFocus(modal) {
    const activeElement = document.activeElement;
    modalFocusOrigins.set(modal, activeElement && activeElement !== document.body ? activeElement : null);
}

function restoreModalFocus(modal, fallbackElement = null) {
    const previousElement = modalFocusOrigins.get(modal);
    modalFocusOrigins.delete(modal);

    const focusTarget = previousElement?.isConnected && !previousElement.disabled
        ? previousElement
        : fallbackElement;

    if (!focusTarget?.isConnected || focusTarget.disabled || typeof focusTarget.focus !== "function") {
        return;
    }

    // The modal is already hidden. Restore now so a later frame cannot steal
    // focus from a newly opened dialog or defer keyboard navigation indefinitely.
    focusTarget.focus();
}

async function saveTaskFromModal() {
    if (!state.editingTask || state.isEditSaving || (typeof PetPlay !== "undefined" && PetPlay.isSaving)) {
        return;
    }

    const editingTask = state.editingTask;
    const previousStatus = getTaskStatus(editingTask);
    const nextStatus = Number(editStatusSelect.value);
    const previousPetProfile = getPetRewardSnapshot();
    const request = {
        title: editTitleInput.value.trim(),
        description: editDescriptionInput.value.trim(),
        isCompleted: nextStatus === TASK_STATUS.Done,
        status: nextStatus,
        dueDate: toApiDate(editDueDateInput.value),
        priority: Number(editPrioritySelect.value),
        tags: editTagsInput.value.trim()
    };
    if (typeof TaskDetails !== "undefined") Object.assign(request, TaskDetails.read("edit"));
    if (editingTask.version !== undefined) request.version = editingTask.version;

    const validationMessage = validateTaskRequest(request);

    if (validationMessage !== "") {
        setEditMessage(validationMessage, true);
        editTitleInput.focus();
        return;
    }

    let saved = false;

    try {
        setEditSavingState(true);
        clearPetFeedback();
        setEditMessage("更新中です");

        const response = await TaskAuth.request(`${getTasksApiUrl()}/${editingTask.id}`, {
            method: "PUT",
            headers: getRequestHeaders({
                "Content-Type": "application/json"
            }),
            body: JSON.stringify(request)
        });

        if (response.status === 404) {
            throw new Error("更新対象のタスクが見つかりません。");
        }

        if (!response.ok) {
            throw new Error(await getErrorMessage(response, "タスクの更新に失敗しました。"));
        }

        const updatedTask = await response.json();
        if (typeof TaskDetails !== "undefined") TaskDetails.offerUndo(updatedTask.undo, "ステータスを変更しました");

        const tasksLoaded = await loadTasks();

        const progressionLoaded = await refreshProgression();
        if (progressionLoaded && previousStatus !== TASK_STATUS.Done && nextStatus === TASK_STATUS.Done) showPetReward(previousPetProfile, 1);
        if (progressionLoaded && previousStatus === TASK_STATUS.Done && nextStatus !== TASK_STATUS.Done) showPetReward(previousPetProfile, -1);

        setMessage(
            tasksLoaded ? "タスクを更新しました" : "タスクは更新されましたが、表示を更新できませんでした。再読み込みしてください。",
            !tasksLoaded
        );
        saved = true;
    } catch (error) {
        setEditMessage(getDisplayErrorMessage(error, "タスクの更新に失敗しました。"), true);
    } finally {
        setEditSavingState(false);

        if (saved) {
            closeEditModal(true);
        }
    }
}

function setEditMessage(text, isError = false) {
    editMessage.textContent = text;
    editMessage.classList.toggle("error", isError);
    editMessage.classList.toggle("success", text.endsWith("しました") && !isError);
}

function setCreateMessage(text, isError = false) {
    createMessage.textContent = text;
    createMessage.classList.toggle("error", isError);
    createMessage.classList.toggle("success", text.endsWith("しました") && !isError);
}

function setEditSavingState(isSaving) {
    state.isEditSaving = isSaving;
    editTaskForm.setAttribute("aria-busy", String(isSaving));

    editSaveButton.disabled = isSaving;
    editCancelButton.disabled = isSaving;
    editDeleteButton.disabled = isSaving;
    editModalCloseButton.disabled = isSaving;
    editSaveButton.textContent = isSaving ? "保存中" : "保存";
}

function validateTaskRequest(request) {
    if (typeof TaskDetails !== "undefined") {
        const detailError = TaskDetails.validate(request);
        if (detailError) return detailError;
    }
    if (request.title === "") {
        return "タイトルを入力してください。";
    }

    if (request.title.length > 200) {
        return "タイトルは200文字以内で入力してください。";
    }

    if (request.description && request.description.length > 2000) {
        return "説明は2000文字以内で入力してください。";
    }

    if (request.tags && request.tags.length > 300) {
        return "タグは300文字以内で入力してください。";
    }

    return "";
}

async function getErrorMessage(response, fallbackMessage) {
    if (response.status === 401) {
        const message = "ログイン期限が切れました。もう一度ログインしてください。";
        redirectToLogin(message);
        return message;
    }

    try {
        const errorData = await response.json();

        if (response.status === 403 && errorData.code === "account_setup_required") {
            window.location.assign("auth.html");
            return "利用を続けるため、アカウントの手続きを完了してください。";
        }

        if (errorData.message) {
            return errorData.message;
        }

        if (errorData.errors) {
            const messages = Object.values(errorData.errors).flat();
            return messages[0] || fallbackMessage;
        }

        if (errorData.detail || errorData.title) {
            return errorData.detail || errorData.title;
        }

        return fallbackMessage;
    } catch {
        return fallbackMessage;
    }
}

function getDisplayErrorMessage(error, fallbackMessage) {
    const message = error?.message || fallbackMessage;

    if (message === "Failed to fetch" || message.includes("NetworkError")) {
        const isLocalHost = ["localhost", "127.0.0.1", "::1", "[::1]"].includes(window.location.hostname);

        return isLocalHost
            ? "APIに接続できません。TaskApiを起動してから再読み込みしてください。"
            : "現在サービスに接続できません。時間をおいて再度お試しください。";
    }

    return message;
}

async function deleteTask(task) {
    if (typeof PetPlay !== "undefined" && PetPlay.isSaving) return;
    state.isTaskMutation = true;
    clearPetFeedback();
    try {
        const versionQuery = task.version !== undefined ? `?version=${encodeURIComponent(task.version)}` : "";
        const response = await TaskAuth.request(`${getTasksApiUrl()}/${task.id}${versionQuery}`, {
            method: "DELETE",
            headers: getRequestHeaders()
        });

        if (response.status === 404) {
            throw new Error("タスクが見つかりませんでした。");
        }

        if (!response.ok) {
            throw new Error(await getErrorMessage(response, "タスクの削除に失敗しました。"));
        }

        if (typeof TaskDetails !== "undefined") TaskDetails.offerUndo({ token: response.headers.get("X-Task-Undo"), expiresAt: response.headers.get("X-Task-Undo-Expires") }, "タスクを削除しました");

        const tasksLoaded = await loadTasks();
        await refreshProgression();
        return { deleted: true, tasksLoaded, errorMessage: "" };
    } catch (error) {
        return {
            deleted: false,
            tasksLoaded: false,
            errorMessage: getDisplayErrorMessage(error, "タスクの削除に失敗しました。")
        };
    } finally {
        state.isTaskMutation = false;
    }
}

async function moveTaskStatus(task, nextStatus) {
    if (state.isTaskMutation || (typeof PetPlay !== "undefined" && PetPlay.isSaving)) return;
    const currentStatus = getTaskStatus(task);

    if (currentStatus === nextStatus) {
        return;
    }

    const previousPetProfile = getPetRewardSnapshot();
    const request = {
        title: task.title,
        description: task.description,
        isCompleted: nextStatus === TASK_STATUS.Done,
        status: nextStatus,
        dueDate: task.dueDate || null,
        priority: getTaskPriority(task),
        tags: task.tags || null
    };
    request.assigneeUserProfileId = task.assigneeUserProfileId ?? null;
    request.checklist = task.checklist || [];
    if (task.version !== undefined) request.version = task.version;

    try {
        state.isTaskMutation = true;
        clearPetFeedback();
        setMessage("更新中です");

        const response = await TaskAuth.request(`${getTasksApiUrl()}/${task.id}`, {
            method: "PUT",
            headers: getRequestHeaders({
                "Content-Type": "application/json"
            }),
            body: JSON.stringify(request)
        });

        if (response.status === 404) {
            throw new Error("タスクが見つかりませんでした。");
        }

        if (!response.ok) {
            throw new Error(await getErrorMessage(response, "タスクの更新に失敗しました。"));
        }

        const updatedTask = await response.json();
        if (typeof TaskDetails !== "undefined") TaskDetails.offerUndo(updatedTask.undo, "ステータスを変更しました");
        const tasksLoaded = await loadTasks();
        const progressionLoaded = await refreshProgression();
        if (progressionLoaded && currentStatus !== TASK_STATUS.Done && nextStatus === TASK_STATUS.Done) showPetReward(previousPetProfile, 1);
        if (progressionLoaded && currentStatus === TASK_STATUS.Done && nextStatus !== TASK_STATUS.Done) showPetReward(previousPetProfile, -1);
        setMessage(
            tasksLoaded ? "タスクを更新しました" : "タスクは更新されましたが、表示を更新できませんでした。再読み込みしてください。",
            !tasksLoaded
        );
    } catch (error) {
        setMessage(getDisplayErrorMessage(error, "タスクの更新に失敗しました。"), true);
    } finally {
        state.isTaskMutation = false;
    }
}

function getTasksApiUrl(teamId = state.teamId) {
    return teamId ? `${API_BASE_URL}/api/teams/${encodeURIComponent(teamId)}/tasks` : API_URL;
}

function setMessage(text, isError = false) {
    const isSuccess = text.endsWith("しました");

    message.textContent = text;
    message.classList.toggle("error", isError);
    message.classList.toggle("success", isSuccess && !isError);
}

function setSavingState(isSaving) {
    state.isSaving = isSaving;
    taskForm.setAttribute("aria-busy", String(isSaving));

    submitButton.disabled = isSaving;
    createCancelButton.disabled = isSaving;
    createModalCloseButton.disabled = isSaving;

    if (isSaving) {
        submitButton.textContent = "保存中";
        return;
    }

    submitButton.textContent = "追加";
}
