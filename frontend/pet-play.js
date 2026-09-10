// Server-owned collection. No local XP, unlocks, or random claims; failed writes are never retried automatically.
const PetPlay = (() => {
    const root = document.getElementById("pet-play-root");
    const opener = document.getElementById("pet-interact-button");
    const badge = document.getElementById("pet-gift-badge");
    const speciesIds = ["dog", "cat", "rabbit", "fox", "panda", "dragon"];
    const moodColumns = { idle: 0, happy: 1, working: 2, proud: 3, sleepy: 4, sad: 5 };
    // The generated sheets have slightly irregular row spacing. Explicit cut boundaries
    // keep ears/feet intact without rewriting any generated bitmap pixels.
    const rowEdges = { dog: [0, 276, 519, 749, 1024], cat: [0, 267, 502, 737, 1024], rabbit: [0, 285, 525, 762, 1024], fox: [0, 276, 517, 764, 1024], panda: [0, 282, 516, 748, 1024], dragon: [0, 296, 547, 785, 1024] };
    const anchors = {
        dog: { x: 64, hat: [25, 20, 18, 12], bow: [60, 51, 49, 42] },
        cat: { x: 52, hat: [25, 20, 17, 15], bow: [59, 56, 53, 44] },
        rabbit: { x: 56, hat: [43, 29, 28, 25], bow: [71, 64, 63, 54] },
        fox: { x: 50, hat: [25, 20, 18, 14], bow: [63, 64, 60, 53] },
        panda: { x: 52, hat: [24, 18, 13, 9], bow: [68, 61, 58, 46] },
        dragon: { x: 43, hat: [27, 23, 19, 10], bow: [63, 59, 55, 48] }
    };
    const tabs = [["touch", "触れ合い"], ["gifts", "ごほうび"], ["dress", "着せ替え"], ["album", "アルバム"]];
    const kinds = [["hat", "帽子"], ["bow", "リボン"], ["mat", "クッション"]];
    let data = null, profile = null, profileKey = "", requestVersion = 0, pending = null;
    let busy = false, ready = false, tab = "touch", giftLevel = null, mood = "idle", ambientMood = "idle", poseTimer = null;
    let interaction = null, message = "育成データを読み込み中です", error = false;
    let draft = null;

    function node(tag, text, className) {
        const el = document.createElement(tag);
        if (text != null) el.textContent = text;
        if (className) el.className = className;
        return el;
    }
    function button(text, action, className) {
        const el = node("button", text, className);
        el.type = "button";
        el.addEventListener("click", action);
        return el;
    }
    function hint(text) { return node("p", text, "field-hint"); }
    function giftOption(level, kind) { return data?.rewards.find(item => item.level === level)?.options.find(item => item.id === kind); }
    function giftIcon(level, kind) {
        const option = giftOption(level, kind);
        const sprite = option?.image ? PetRewardArt.create(profile?.species || "dog", level, kind) : null;
        if (sprite) return sprite;
        const el = node("span", null, `pet-accessory accessory-${kind}`);
        el.dataset.shape = String(option?.shape || 0);
        el.style.setProperty("--gift-hue", `${option?.hue || 0}deg`);
        el.setAttribute("aria-hidden", "true");
        return el;
    }
    function safeAppearance(value) {
        const stage = data?.stages.find(item => item.id === value?.stage && item.available);
        const result = { stage: stage?.id || "base" };
        for (const [kind] of kinds) {
            const selected = value?.[`${kind}Level`];
            result[`${kind}Level`] = data?.rewards.some(item => item.level === selected && item.claimedChoice === kind) ? selected : null;
        }
        return result;
    }
    function catImage(variant, className = "") {
        const image = node("img", null, `pet-cat-image ${className}`);
        image.src = `assets/pet/portfolio-cat-${variant}-v3.png?v=20260908-2`;
        image.alt = "";
        image.draggable = false;
        image.setAttribute("aria-hidden", "true");
        return image;
    }
    function rewardArt(level, kind) {
        const art = node("div", null, "pet-gift-art");
        art.append(giftIcon(level, kind));
        return art;
    }
    function paintCat(el, safe, column) {
        const character = node("span", null, "pet-character");
        character.setAttribute("aria-hidden", "true");
        const happy = column === 1 || column === 3;
        character.append(catImage(happy ? "pet" : "idle"));
        equip(el, character, safe, "cat", 0, column, true);
        el.append(character);
    }
    function equip(frame, character, safe, species, row, column, catCutout = false) {
        for (const [kind] of kinds) {
            const level = safe[`${kind}Level`];
            if (!level || (kind !== "mat" && !catCutout && [2, 4].includes(column))) continue;
            const art = giftIcon(level, kind);
            if (art.classList.contains("pet-reward-sprite")) PetRewardArt.fit(art, species, row, column, kind, catCutout);
            // Floor items are siblings of the moving body, never attached to it.
            (kind === "mat" ? frame : character).append(art);
        }
    }
    function paint(el, appearance, column = moodColumns[mood] ?? 0, rowOverride = null) {
        const safe = safeAppearance(appearance);
        const species = speciesIds.includes(profile?.species) ? profile.species : "dog";
        const row = rowOverride ?? data?.stages.find(item => item.id === safe.stage)?.row ?? 0;
        el.classList.add("pet-atlas");
        el.dataset.species = species;
        el.dataset.pose = String(column);
        el.dataset.stage = String(row);
        el.dataset.interaction = interaction || "";
        el.dataset.art = species === "cat" && row === 0 ? "cat-cutout" : "atlas-cutout";
        el.replaceChildren();
        if (el.dataset.art === "cat-cutout") {
            el.style.setProperty("--hat-left", "28%");
            el.style.setProperty("--hat-top", "14%");
            el.style.setProperty("--bow-left", "33%");
            el.style.setProperty("--bow-top", "50%");
            el.style.backgroundImage = "none";
            el.style.backgroundSize = "auto";
            el.style.backgroundPosition = "center";
            paintCat(el, safe, column);
            return;
        }
        el.style.backgroundImage = "none";
        const character = node("span", null, "pet-character pet-atlas-character");
        character.setAttribute("aria-hidden", "true");
        const body = node("span", null, "pet-atlas-body");
        body.style.backgroundImage = `url("assets/pet/portfolio-${species}-atlas-v2-alpha.png")`;
        const edges = rowEdges[species], height = edges[row + 1] - edges[row];
        body.style.backgroundSize = `600% ${1024 / height * 100}%`;
        body.style.backgroundPosition = `${column * 20}% ${edges[row] / (1024 - height) * 100}%`;
        el.style.setProperty("--hat-left", `${anchors[species].x - 15}%`);
        el.style.setProperty("--hat-top", `${anchors[species].hat[row] - (column === 3 ? 3 : 0)}%`);
        el.style.setProperty("--bow-left", `${anchors[species].x - 10}%`);
        el.style.setProperty("--bow-top", `${anchors[species].bow[row]}%`);
        character.append(body);
        equip(el, character, safe, species, row, column);
        el.append(character);
    }
    function updatePortraits() {
        const main = document.getElementById("pet-sprite");
        const preview = document.getElementById("pet-play-preview");
        if (main) paint(main, data?.appearance);
        if (preview) paint(preview, tab === "dress" ? draft : data?.appearance, tab === "dress" ? 0 : moodColumns[mood] ?? 0);
        const count = ready ? data.rewards.filter(item => item.available && !item.claimedChoice).length : 0;
        badge.hidden = !count;
        badge.textContent = `🎁 ${count}`;
        opener.setAttribute("aria-label", `${profile?.name || "相棒"}と触れ合う${count ? `・未受取のごほうび${count}個` : "・コレクションを見る"}`);
        opener.title = count ? `ごほうびを${count}個受け取れます` : "クリックして相棒と触れ合う";
        if (ready) {
            const next = data.stages.find(item => !item.available);
            document.getElementById("pet-unlock-next").textContent = count ? `相棒を押して、ごほうびを${count}個選ぼう` : next ? `次の成長した姿：Lv.${next.requiredLevel} ${next.name}` : "成長した姿をすべて解放！アルバムも見てみよう";
        }
    }
    function setMessage(text, isError = false) {
        message = text; error = isError;
        const el = document.getElementById("pet-play-message");
        if (el) { el.textContent = text; el.classList.toggle("error", isError); }
    }
    function focus(id) { document.getElementById(id)?.focus(); }

    async function refresh(force = false) {
        if (busy || (!force && pending)) return pending;
        const version = ++requestVersion;
        ready = false;
        render();
        pending = (async () => {
            try {
                const result = await optionsRequest("/api/pet/collection");
                if (version !== requestVersion) return;
                data = result;
                // A different browser may have changed progression. This response owns eligibility.
                profile = { ...profile, level: result.level, totalExperience: result.totalExperience, species: result.species };
                draft = { ...result.appearance };
                ready = true;
                if (!giftLevel) giftLevel = result.rewards.find(item => item.available && !item.claimedChoice)?.level || 1;
                setMessage("");
            } catch (failure) {
                if (version !== requestVersion) return;
                setMessage(getDisplayErrorMessage(failure, "コレクションを読み込めませんでした。"), true);
            } finally {
                if (version === requestVersion) { pending = null; render(); }
            }
        })();
        return pending;
    }
    function onProfile(value) {
        profile = value;
        const key = `${value.id}:${value.level}:${value.totalExperience}:${value.species}:${value.name}`;
        updatePortraits();
        if (key !== profileKey) { profileKey = key; refresh(true); }
    }
    function onMood(value) {
        ambientMood = value;
        if (!interaction) mood = value;
        updatePortraits();
    }
    function selectTab(value, moveFocus = false) {
        tab = value;
        draft = data ? { ...data.appearance } : null;
        render();
        if (moveFocus) focus(`pet-tab-${value}`);
    }
    async function mutate(path, method, body, success, focusId) {
        if (!ready || busy || isTaskBusy() || optionsState.busy || optionsState.savingPreferences || petNameButton.disabled) return false;
        busy = true;
        ++requestVersion; pending = null;
        setMessage("保存中です…"); render();
        try {
            data = await optionsRequest(path, method, body);
            profile = { ...profile, level: data.level, totalExperience: data.totalExperience, species: data.species };
            draft = { ...data.appearance };
            setMessage(success);
            return true;
        } catch (failure) {
            ready = false; // Unknown write outcome: a deliberate reload reconciles safely.
            setMessage(getDisplayErrorMessage(failure, "保存できませんでした。更新して状況を確認してください。"), true);
            return false;
        } finally {
            busy = false; render(); focus(ready ? focusId : "pet-play-retry");
        }
    }
    async function interact(action) {
        if (interaction) return;
        const labels = { pet: "なでると、うれしそう！", treat: "おやつをどうぞ。もぐもぐ…", rest: "少しだけ一緒にひと休み。" };
        if (!await mutate("/api/pet/interactions", "POST", { action }, labels[action], `pet-action-${action}`)) return;
        interaction = action;
        mood = action === "rest" ? "sleepy" : action === "treat" ? "working" : "happy";
        updatePortraits();
        clearTimeout(poseTimer);
        poseTimer = setTimeout(() => { interaction = null; mood = ambientMood; updatePortraits(); }, 4200);
    }
    function renderTouch(panel) {
        panel.append(node("h3", "相棒とひと息"), hint("触れ合うと表情やしぐさが変わります。経験値は増えないので、好きなときにどうぞ。"));
        const actions = node("div", null, "pet-touch-actions");
        for (const [id, title] of [["pet", "♡ なでる"], ["treat", "◉ おやつ"], ["rest", "☾ 休憩する"]]) {
            const el = button(title, () => interact(id)); el.id = `pet-action-${id}`; el.disabled = busy || !ready; actions.append(el);
        }
        panel.append(actions, hint("最初の触れ合いはアルバムに記録されます。成長した姿やごほうびは隣のタブから。"));
    }
    function renderGifts(panel) {
        panel.append(node("h3", "Lv.1〜5、相棒に似合うごほうび"), hint("各レベルで帽子・リボン・クッションから1点。ペットごとに専用デザインを用意しました。受け取った後は選び直せません。"));
        if (!data) return;
        const label = node("label", "ごほうびのレベル", "modal-field"); label.htmlFor = "pet-gift-level";
        const select = node("select"); select.id = "pet-gift-level";
        for (const item of data.rewards.filter(item => !item.isLegacy)) {
            const option = node("option", `Lv.${item.level} — ${item.claimedChoice ? "受取済み" : item.available ? "選べます" : "未解放"}`);
            option.value = String(item.level); option.selected = item.level === giftLevel; select.append(option);
        }
        select.disabled = busy || !ready;
        select.addEventListener("change", () => { giftLevel = Number(select.value); render(); focus("pet-gift-level"); }); label.append(select); panel.append(label);
        const reward = data.rewards.find(item => item.level === giftLevel);
        if (!reward) return;
        const grid = node("div", null, "pet-gift-grid");
        for (const option of reward.options) {
            const card = node("div", null, "pet-gift-card");
            const art = rewardArt(reward.level, option.id);
            card.append(art, node("strong", option.name));
            const chosen = reward.claimedChoice === option.id;
            const el = button(chosen ? "✓ 受取済み" : reward.claimedChoice ? "選択できません" : reward.available ? "これを受け取る" : `Lv.${reward.level}で解放`, async () => {
                if (!ready || busy || reward.claimedChoice || !reward.available) return;
                if (!confirm(`Lv.${reward.level}のごほうびに「${option.name}」を選びますか？このレベルでは他の品に選び直せません。`)) return;
                await mutate("/api/pet/rewards", "POST", { level: reward.level, choice: option.id }, `「${option.name}」を受け取りました。「着せ替え」で使えます。`, "pet-gift-level");
            });
            el.disabled = busy || !ready || !!reward.claimedChoice || !reward.available;
            card.classList.toggle("is-owned", chosen); card.append(el); grid.append(card);
        }
        panel.append(grid, hint("受取枠はペットの種類を変えても共通です。一度受け取った品・解放した成長姿は、完了を取り消しても使えます。新しいごほうびはLv.5までです。"));
    }
    function renderDress(panel) {
        panel.append(node("h3", "好きな姿で、一緒に"), hint("成長しても姿は自動で変わりません。いつもの小さな相棒にも戻せます。"));
        if (!data) return;
        const form = node("form", null, "pet-dress-form");
        const addSelect = (key, title, choices) => {
            const label = node("label", title, "modal-field"); label.htmlFor = `pet-dress-${key}`;
            const select = node("select"); select.id = `pet-dress-${key}`;
            for (const choice of choices) {
                const option = node("option", choice.name); option.value = choice.value; option.disabled = choice.locked;
                option.selected = String(draft?.[key] ?? "") === choice.value; select.append(option);
            }
            select.disabled = busy || !ready;
            select.addEventListener("change", () => { draft[key] = key === "stage" ? select.value : Number(select.value) || null; updatePortraits(); });
            label.append(select); form.append(label);
        };
        addSelect("stage", "成長した姿", data.stages.map(item => ({ value: item.id, name: `${item.name} — Lv.${item.requiredLevel}${item.available ? "" : "で解放"}`, locked: !item.available })));
        for (const [kind, title] of kinds) addSelect(`${kind}Level`, title, [{ value: "", name: "つけない", locked: false }, ...data.rewards.filter(item => item.claimedChoice === kind).map(item => ({ value: String(item.level), name: `${item.isLegacy ? "旧ごほうび · " : ""}${giftOption(item.level, kind).name}`, locked: false }))]);
        const save = node("button", "この着せ替えを保存"); save.id = "pet-dress-save"; save.type = "submit"; save.disabled = busy || !ready; form.append(save);
        form.addEventListener("submit", async event => { event.preventDefault(); await mutate("/api/pet/appearance", "PUT", { ...draft }, "着せ替えを保存しました。", "pet-dress-save"); });
        panel.append(form, hint("上の絵で試着できます。帽子・リボンは立ち姿で表示されます。取得済みの品・成長した姿はレベルが下がっても使えます。"));
        if (data.rewards.some(item => item.isLegacy)) panel.append(hint("以前に受け取ったLv.6以上の品も、旧ごほうびとして引き続き使えます。"));
    }
    function renderAlbum(panel) {
        panel.append(node("h3", "ふたりのアルバム"), hint("一度出会った思い出は、完了を取り消しても残ります。日付はアプリに記録された日です。"));
        if (!data) return;
        const unlocked = data.memories.filter(item => item.unlockedAt);
        panel.append(node("p", `思い出 ${unlocked.length} / ${data.memories.length}`, "pet-collection-count"));
        const grid = node("div", null, "pet-memory-grid");
        for (const memory of data.memories) {
            const card = node("article", null, `pet-memory${memory.unlockedAt ? "" : " is-locked"}`);
            if (memory.unlockedAt) { const art = node("div", null, "pet-memory-art"); paint(art, {}, memory.column, memory.row); art.setAttribute("aria-hidden", "true"); card.append(art); }
            else card.append(node("div", "？", "pet-memory-placeholder"));
            card.append(node("strong", memory.unlockedAt ? memory.name : "まだ見ぬ思い出"), hint(memory.description));
            if (memory.unlockedAt) { const time = node("time", new Date(memory.unlockedAt).toLocaleDateString("ja-JP")); time.dateTime = memory.unlockedAt; card.append(time); }
            grid.append(card);
        }
        panel.append(grid, node("h3", "集めたごほうび"));
        const owned = data.rewards.filter(item => item.claimedChoice);
        const legacyCount = owned.filter(item => item.isLegacy).length;
        panel.append(hint(`${owned.length - legacyCount} / ${data.maxRewardLevel || 5} 点（各レベルから1点）${legacyCount ? ` · 旧ごほうび ${legacyCount} 点を保管中` : ""}`));
        const items = node("div", null, "pet-owned-grid");
        for (const reward of owned) {
            const card = node("div", null, "pet-owned-item"); const art = rewardArt(reward.level, reward.claimedChoice);
            card.append(art, node("span", `${giftOption(reward.level, reward.claimedChoice).name}`)); items.append(card);
        }
        panel.append(items);
        if (!owned.length) panel.append(hint("「ごほうび」タブで最初の1点を選べます。"));
    }
    function render() {
        const active = root.contains(document.activeElement) ? document.activeElement.id : null;
        root.replaceChildren(); root.setAttribute("aria-busy", String(busy));
        const hero = node("div", null, "pet-play-hero");
        const preview = node("div", null, "pet-play-preview"); preview.id = "pet-play-preview"; preview.setAttribute("role", "img"); preview.setAttribute("aria-label", `${profile?.name || "相棒"}の姿`);
        const heading = node("div"); heading.append(node("h3", profile?.name || "あなたの相棒"), hint(`Lv.${profile?.level || 1} · ゆっくり、一緒に育とう`)); hero.append(preview, heading); root.append(hero);
        const nav = node("div", null, "pet-play-tabs"); nav.setAttribute("role", "tablist"); nav.setAttribute("aria-label", "相棒との過ごし方");
        for (const [index, [id, title]] of tabs.entries()) {
            const el = button(title, () => selectTab(id, true)); el.id = `pet-tab-${id}`; el.setAttribute("role", "tab"); el.setAttribute("aria-selected", String(id === tab)); el.setAttribute("aria-controls", `pet-panel-${id}`); el.tabIndex = id === tab ? 0 : -1; el.disabled = busy;
            el.addEventListener("keydown", event => {
                const next = event.key === "ArrowRight" ? (index + 1) % tabs.length : event.key === "ArrowLeft" ? (index + tabs.length - 1) % tabs.length : event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 : null;
                if (next !== null) { event.preventDefault(); selectTab(tabs[next][0], true); }
            }); nav.append(el);
        }
        root.append(nav);
        const status = node("p", message, `edit-message${error ? " error" : ""}`); status.id = "pet-play-message"; status.setAttribute("role", "status"); status.setAttribute("aria-live", "polite"); root.append(status);
        if (!ready && !busy) { const retry = button(pending ? "読み込み中…" : "コレクションを更新", () => refresh()); retry.id = "pet-play-retry"; retry.disabled = !!pending; root.append(retry); }
        for (const [id] of tabs) {
            const panel = node("section", null, "pet-play-panel"); panel.id = `pet-panel-${id}`; panel.setAttribute("role", "tabpanel"); panel.setAttribute("aria-labelledby", `pet-tab-${id}`); panel.hidden = id !== tab;
            if (id === tab) ({ touch: renderTouch, gifts: renderGifts, dress: renderDress, album: renderAlbum })[id](panel);
            root.append(panel);
        }
        updatePortraits();
        if (active) focus(active);
    }
    opener.addEventListener("click", () => {
        if (isTaskBusy() || optionsState.busy || document.body.classList.contains("modal-open")) return;
        const hasGifts = ready && data.rewards.some(item => item.available && !item.claimedChoice);
        openOptionsModal(settingsModal, document.getElementById("settings-pet-tab"));
        selectSettingsTab("pet");
        selectTab(hasGifts ? "gifts" : "touch");
    });
    render();
    return { refresh, onProfile, onMood, get isSaving() { return busy; } };
})();
