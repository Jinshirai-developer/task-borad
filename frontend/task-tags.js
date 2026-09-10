// Whole-workspace classifications. Task tags stay CSV-compatible; this is not an assignee field.
const TaskTags = (() => {
    let scopeTeamId = null;
    let requestVersion = 0;
    let catalog = null;
    let saving = false;
    const node = (id) => document.getElementById(id);
    const panel = node("task-classifications");
    const form = node("tag-create-form");
    const nameInput = node("tag-name-input");
    const choices = node("classification-chips");
    const sidebarFilter = node("sidebar-tag-filter");
    const pickerPrefixes = ["create", "edit"];
    const key = (name) => name.toLowerCase();
    const route = (teamId = scopeTeamId) => teamId ? `/api/teams/${encodeURIComponent(teamId)}/task-tags` : "/api/task-tags";
    const current = (teamId, version) => teamId === state.teamId && teamId === scopeTeamId && version === requestVersion;

    function parse(csv) {
        const seen = new Set();
        return String(csv || "").split(",").map((name) => name.trim()).filter((name) => {
            if (!name || seen.has(key(name))) return false;
            seen.add(key(name));
            return true;
        });
    }

    function validateName(name) {
        if (!name || name.length > 50) return "タグ名は1〜50文字で入力してください。";
        if (/[,\u0000-\u001f\u007f-\u009f]/.test(name)) return "タグ名にカンマや制御文字は使えません。";
        return "";
    }

    function message(text, error = false) {
        node("classification-message").textContent = text;
        node("classification-message").classList.toggle("error", error);
        node("sidebar-tag-message").textContent = error ? text : "";
    }

    function setFormOpen(open) {
        if (saving) return;
        form.hidden = !open;
        node("open-tag-create-button").setAttribute("aria-expanded", String(open));
        if (open) {
            nameInput.focus();
            // Keep the input and its Add/Close buttons together in view on small screens.
            form.scrollIntoView({ block: "nearest" });
        }
        else node("open-tag-create-button").focus();
    }

    function resetScope(teamId) {
        closePickers();
        scopeTeamId = teamId;
        requestVersion++;
        catalog = null;
        state.tagExact = "";
        state.untagged = false;
        nameInput.value = "";
        form.hidden = true;
        node("open-tag-create-button").setAttribute("aria-expanded", "false");
        choices.replaceChildren();
        // Clear not only chips but also draft input values; no private tag names cross scopes.
        tagsInput.value = "";
        editTagsInput.value = "";
        node("classification-summary").textContent = "分類タグを読み込み中です";
        node("tag-empty-hint").hidden = true;
        message("");
        renderSidebar();
        renderPickers();
        const manageForm = node("tag-manage-form");
        if (manageForm) { manageForm.reset(); node("tag-manage-details").open = false; renderManager(); }
    }

    async function refresh(teamId = state.teamId) {
        if (scopeTeamId !== teamId) resetScope(teamId);
        const version = ++requestVersion;
        panel.setAttribute("aria-busy", "true");
        try {
            const response = await TaskAuth.request(route(teamId), { headers: getRequestHeaders() });
            if (!current(teamId, version)) return false;
            if (!response.ok) throw new Error(await getErrorMessage(response, "分類タグを読み込めませんでした。"));
            const data = await response.json();
            if (!current(teamId, version)) return false;
            if (!Array.isArray(data.items)) throw new Error("分類タグの集計を読み込めませんでした。");
            catalog = data;
            node("tag-empty-hint").hidden = data.items.length !== 0;
            message("");
            render();
            return true;
        } catch (error) {
            if (!current(teamId, version)) return false;
            catalog = null;
            node("tag-empty-hint").hidden = true;
            renderFallback();
            renderPickers();
            node("classification-summary").textContent = state.tagExact ? `分類「${state.tagExact}」で絞り込み中（集計未取得）` : state.untagged ? "未分類で絞り込み中（集計未取得）" : "分類タグの集計未取得";
            message(`${getDisplayErrorMessage(error, "分類タグを読み込めませんでした。") } ワークスペースの「更新」で再試行できます。`, true);
            return false;
        } finally {
            if (current(teamId, version)) panel.setAttribute("aria-busy", "false");
        }
    }

    function chip(label, count, selected, action, className = "classification-chip") {
        const button = document.createElement("button");
        button.type = "button";
        button.className = className;
        button.dataset.tagName = label;
        button.setAttribute("aria-pressed", String(selected));
        const text = document.createElement("span");
        text.textContent = label;
        button.appendChild(text);
        if (count !== null) {
            const badge = document.createElement("span");
            badge.className = "classification-count";
            badge.textContent = String(count);
            button.appendChild(badge);
        }
        button.addEventListener("click", action);
        return button;
    }

    async function select(kind, name = "") {
        if (isTaskBusy() || state.draggingTask || optionsState.busy) {
            message("保存・更新が終わってから分類を切り替えてください。", true);
            return false;
        }
        if (!createTaskModal.classList.contains("hidden") || !editModal.classList.contains("hidden")) return false;
        state.tagExact = kind === "tag" ? name : "";
        state.untagged = kind === "untagged";
        state.page = 1;
        message("");
        render();
        await loadTasks();
        return true;
    }

    function render() {
        renderSidebar();
        renderManager();
        if (!catalog) { renderFallback(); return; }
        const focused = choices.contains(document.activeElement) ? document.activeElement.dataset.tagName : null;
        choices.replaceChildren(
            chip("すべて", catalog.totalTasks, !state.tagExact && !state.untagged, () => select("all")),
            chip("未分類", catalog.untaggedTasks, state.untagged, () => select("untagged"))
        );
        for (const item of catalog.items) {
            choices.appendChild(chip(item.name, item.total, !!state.tagExact && key(item.name) === key(state.tagExact), () => select("tag", item.name)));
        }
        if (focused !== null) {
            Array.from(choices.querySelectorAll("button")).find((button) => button.dataset.tagName === focused)?.focus();
        }
        const selected = catalog.items.find((item) => state.tagExact && key(item.name) === key(state.tagExact));
        node("classification-summary").textContent = state.tagExact
            ? `分類「${state.tagExact}」 全 ${selected?.total || 0}件　TODO ${selected?.todo || 0} / DOING ${selected?.doing || 0} / DONE ${selected?.done || 0}`
            : state.untagged ? `未分類 ${catalog.untaggedTasks}件（タグがないタスク）` : `すべて ${catalog.totalTasks}件　登録タグ ${catalog.registeredTags} / ${catalog.maxRegisteredTags || 50}`;
        renderPickers();
    }

    function renderFallback() {
        renderSidebar();
        renderManager();
        // Keep navigation available after a failed aggregate read, without inventing counts.
        choices.replaceChildren(
            chip("すべて", null, !state.tagExact && !state.untagged, () => select("all")),
            chip("未分類", null, state.untagged, () => select("untagged"))
        );
    }

    function renderSidebar() {
        const option = (value, name, count) => {
            const item = document.createElement("option");
            item.value = value;
            item.textContent = count === undefined ? name : `${name}（${count}件）`;
            return item;
        };
        sidebarFilter.replaceChildren(option("all", "すべてのタグ", catalog?.totalTasks), option("untagged", "未分類", catalog?.untaggedTasks));
        let selectedValue = state.untagged ? "untagged" : "all";
        for (const item of catalog?.items || []) {
            sidebarFilter.appendChild(option(`tag:${item.name}`, item.name, item.total));
            if (state.tagExact && key(item.name) === key(state.tagExact)) selectedValue = `tag:${item.name}`;
        }
        if (state.tagExact && selectedValue === "all") {
            selectedValue = `tag:${state.tagExact}`;
            sidebarFilter.appendChild(option(selectedValue, state.tagExact, catalog ? 0 : undefined));
        }
        sidebarFilter.appendChild(option("action:add-tag", "＋ 新しいタグを追加…"));
        sidebarFilter.value = selectedValue;
    }

    sidebarFilter.addEventListener("change", async () => {
        const value = sidebarFilter.value;
        if (value === "action:add-tag") {
            // This is navigation, not a filter. Restore the accepted value before opening settings.
            renderSidebar();
            if (!openTagSettings()) message("保存や開いている画面を閉じてから、タグを追加してください。", true);
            return;
        }
        await select(value.startsWith("tag:") ? "tag" : value === "untagged" ? "untagged" : "all", value.startsWith("tag:") ? value.slice(4) : "");
        // Restore the accepted selection if a save or another dialog blocked switching.
        renderSidebar();
    });

    function renderPickers() {
        for (const prefix of pickerPrefixes) {
            const input = prefix === "create" ? tagsInput : editTagsInput;
            const container = node(`${prefix}-tag-choices`);
            const messageId = `${prefix}-tag-message`;
            const selected = parse(input.value);
            node(`${prefix}-tags-selection`).textContent = selected.length ? selected.join("、") : "タグを選択（複数可）";
            if (input.value.length <= 300) node(messageId).textContent = "";
            const names = parse([...(catalog?.items || []).map((item) => item.name), ...selected].join(","));
            const focused = container.contains(document.activeElement) ? document.activeElement.dataset.tagName : null;
            container.replaceChildren();
            if (!names.length) {
                const hint = document.createElement("span");
                hint.className = "classification-hint";
                hint.textContent = catalog ? "候補はまだありません。「設定 → タグ・チーム」でタグを登録してください。" : "候補を取得できていません。「設定 → タグ・チーム」の「更新」で再試行できます。";
                container.appendChild(hint);
            }
            for (const name of names) {
                const checked = selected.some((item) => key(item) === key(name));
                const label = document.createElement("label");
                label.className = "tag-picker-option";
                const checkbox = document.createElement("input");
                checkbox.type = "checkbox";
                checkbox.checked = checked;
                checkbox.dataset.tagName = name;
                const text = document.createElement("span");
                text.textContent = name;
                label.append(checkbox, text);
                container.appendChild(label);
                checkbox.addEventListener("change", () => {
                    if (state.isSaving || state.isEditSaving || state.isTaskMutation) { checkbox.checked = checked; return; }
                    const values = parse(input.value);
                    const exists = values.some((item) => key(item) === key(name));
                    const next = (exists ? values.filter((item) => key(item) !== key(name)) : [...values, name]).join(", ");
                    if (next.length > 300) {
                        checkbox.checked = exists;
                        node(messageId).textContent = "タグは合計300文字までです。ほかのタグを外してから追加してください。";
                        return;
                    }
                    input.value = next;
                    node(messageId).textContent = "";
                    renderPickers();
                });
            }
            if (focused !== null) {
                Array.from(container.querySelectorAll("input")).find((checkbox) => checkbox.dataset.tagName === focused)?.focus();
            }
        }
    }

    function closePicker(prefix, restoreFocus = false) {
        const panel = node(`${prefix}-tag-panel`);
        if (panel.hidden) return false;
        panel.hidden = true;
        const toggle = node(`${prefix}-tags-toggle`);
        toggle.setAttribute("aria-expanded", "false");
        if (restoreFocus) toggle.focus();
        return true;
    }

    function closePickers(restoreFocus = false) {
        let closed = false;
        for (const prefix of pickerPrefixes) closed = closePicker(prefix, restoreFocus) || closed;
        return closed;
    }

    for (const prefix of pickerPrefixes) {
        const wrapper = node(`${prefix}-tag-picker`);
        const toggle = node(`${prefix}-tags-toggle`);
        toggle.addEventListener("click", () => {
            if (state.isSaving || state.isEditSaving || state.isTaskMutation) return;
            const panel = node(`${prefix}-tag-panel`);
            const open = panel.hidden;
            closePickers();
            if (open) {
                renderPickers();
                panel.hidden = false;
                toggle.setAttribute("aria-expanded", "true");
            }
        });
        wrapper.addEventListener("focusout", (event) => {
            if (event.relatedTarget && !wrapper.contains(event.relatedTarget)) closePicker(prefix);
        });
        document.addEventListener("click", (event) => {
            if (!wrapper.contains(event.target)) closePicker(prefix);
        });
    }

    async function register(event) {
        event.preventDefault();
        if (isTaskBusy() || state.draggingTask || optionsState.busy) return;
        const name = nameInput.value.trim();
        const error = validateName(name);
        if (error) { message(error, true); nameInput.focus(); return; }
        if (catalog && catalog.registeredTags >= (catalog.maxRegisteredTags || 50)) {
            message("登録タグは最大50個です。登録済みのタグを利用してください。", true);
            return;
        }
        const teamId = state.teamId;
        saving = true;
        form.setAttribute("aria-busy", "true");
        [nameInput, node("tag-create-submit"), node("tag-create-cancel"), node("open-tag-create-button")].forEach((button) => { button.disabled = true; });
        message("分類タグを登録しています");
        try {
            const response = await TaskAuth.request(route(teamId), {
                method: "POST", headers: getRequestHeaders({ "Content-Type": "application/json" }), body: JSON.stringify({ name })
            });
            if (teamId !== state.teamId || teamId !== scopeTeamId) return;
            if (!response.ok) throw new Error(await getErrorMessage(response, "分類タグを登録できませんでした。"));
            const loaded = await refresh(teamId);
            if (teamId !== state.teamId || teamId !== scopeTeamId) return;
            nameInput.value = "";
            if (loaded) message(`「${name}」を登録しました。タスクの作成・編集で選択できます。`);
        } catch (error) {
            if (teamId === state.teamId && teamId === scopeTeamId) message(getDisplayErrorMessage(error, "分類タグを登録できませんでした。"), true);
        } finally {
            saving = false;
            form.setAttribute("aria-busy", "false");
            [nameInput, node("tag-create-submit"), node("tag-create-cancel"), node("open-tag-create-button")].forEach((button) => { button.disabled = false; });
        }
    }

    node("open-tag-create-button").addEventListener("click", () => setFormOpen(form.hidden));
    node("tag-create-cancel").addEventListener("click", () => setFormOpen(false));
    form.addEventListener("submit", register);
    nameInput.addEventListener("keydown", (event) => { if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); setFormOpen(false); } });
    for (const input of [tagsInput, editTagsInput]) input.addEventListener("input", renderPickers);

    function renderManager() {
        if (!node("tag-manage-form")) return;
        for (const id of ["tag-manage-source", "tag-merge-target"]) {
            const select = node(id), value = select.value;
            const option = (label, name) => { const el = document.createElement("option"); el.textContent = label; el.value = name; return el; };
            select.replaceChildren(option("タグを選択", ""));
            for (const item of catalog?.items || []) select.appendChild(option(`${item.name}（${item.total}件）`, item.name));
            select.value = [...select.children].some(option => option.value === value) ? value : "";
        }
        updateManageAction();
    }
    function updateManageAction() {
        const action = node("tag-manage-action").value;
        node("tag-rename-field").hidden = action !== "rename";
        node("tag-rename-input").disabled = saving || action !== "rename";
        node("tag-rename-input").required = action === "rename";
        node("tag-merge-field").hidden = action !== "merge";
        node("tag-merge-target").disabled = saving || action !== "merge";
        node("tag-merge-target").required = action === "merge";
        node("tag-manage-submit").disabled = saving || !catalog?.items?.length;
    }
    node("tag-manage-action")?.addEventListener("change", updateManageAction);
    node("tag-manage-details")?.addEventListener("toggle", () => { if (node("tag-manage-details").open && !saving) refresh(); });
    node("tag-manage-form")?.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (isTaskBusy() || optionsState.busy || state.draggingTask) return;
        const name = node("tag-manage-source").value, action = node("tag-manage-action").value;
        const targetName = action === "merge" ? node("tag-merge-target").value : node("tag-rename-input").value.trim();
        if (!name) return message("整理するタグを選んでください。", true);
        if (action !== "delete") { const error = validateName(targetName); if (error) return message(error, true); }
        if (action === "merge" && key(name) === key(targetName)) return message("統合先には別のタグを選んでください。", true);
        const count = catalog?.items.find(item => key(item.name) === key(name))?.total || 0;
        const verb = action === "delete" ? "削除" : action === "merge" ? `「${targetName}」へ統合` : `「${targetName}」へ名前変更`;
        if (!confirm(`タグ「${name}」を${verb}します。\n現在の表示先の${count}件のタスクに付いているタグも変更されます。タスク自体は削除しません。続けますか？`)) return;
        const teamId = state.teamId;
        saving = true;
        const controls = [...node("tag-manage-form").querySelectorAll("input, select, button")];
        controls.forEach(control => { control.disabled = true; });
        message("タグを整理しています…");
        try {
            const response = await TaskAuth.request(`${route(teamId)}/manage`, { method: "POST", headers: getRequestHeaders({ "Content-Type": "application/json" }), body: JSON.stringify({ name, action, targetName: action === "delete" ? null : targetName }) });
            if (teamId !== state.teamId) return;
            if (!response.ok) throw new Error(await getErrorMessage(response, "タグを整理できませんでした。"));
            if (key(state.tagExact) === key(name)) state.tagExact = action === "delete" ? "" : targetName;
            node("tag-rename-input").value = "";
            const loaded = await loadTasks();
            message(loaded ? `タグを${verb}しました。` : "タグは更新しましたが一覧を読めませんでした。「更新」で再試行してください。", !loaded);
        } catch (error) { if (teamId === state.teamId) message(getDisplayErrorMessage(error, "タグを整理できませんでした。"), true); }
        finally { saving = false; controls.forEach(control => { control.disabled = false; }); updateManageAction(); }
    });
    return { refresh, resetScope, renderPickers, closePickers, select, parse, validateName, openCreateForm: () => setFormOpen(true), get isSaving() { return saving; } };
})();
