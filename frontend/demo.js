// Local, ephemeral simulation only. No network, cookies, storage or real accounts.
// The authenticated server remains authoritative outside ?demo=1.
window.TaskDemo = (() => {
    const active = new URL(window.location.href).searchParams.get("demo") === "1";
    if (!active) return Object.freeze({ active: false });
    const clone = value => JSON.parse(JSON.stringify(value));
    const stamp = () => new Date().toISOString();
    const statusValue = value => typeof value === "number" ? ["Todo", "Doing", "Done"][value] : value || "Todo";
    const tokens = value => [...new Map(String(value || "").split(",").map(x => x.trim()).filter(Boolean).map(x => [x.toLowerCase(), x])).values()];
    const same = (a, b) => String(a).toLowerCase() === String(b).toLowerCase();
    const response = (data, status = 200, extra = {}) => new Response(status === 204 ? null : JSON.stringify(data), { status, headers: { "Content-Type": "application/json", ...extra } });
    const fail = (message, status = 400) => response({ message }, status);
    const user = { id: 1, userKey: "demo-visitor", displayName: "お試しユーザー" };
    let preferences = { theme: "light", layout: "board" }, petIdentity = { name: "こむぎ", species: "cat" };
    let nextId = 10, nextTeamId = 2;
    const teams = [{ id: 1, name: "サンプル制作チーム", ownerUserProfileId: 1, role: "owner", members: [
        { userProfileId: 1, displayName: "お試しユーザー", role: "owner" }, { userProfileId: 2, displayName: "デザイナー（見本）", role: "member" }
    ] }];
    const tasks = [];
    const demoBilling = new Map();
    function billingFor(team) {
        const saved = demoBilling.get(team.ownerUserProfileId) || { status: "free", paidThrough: null, cancelAtPeriodEnd: false, hasContract: false };
        const pro = saved.status === "active" && saved.paidThrough > stamp();
        return { ...saved, scope: "account", billingUserProfileId: team.ownerUserProfileId, billingDisplayName: user.displayName, ownedTeamCount: teams.filter(t => t.ownerUserProfileId === team.ownerUserProfileId).length, simulation: true, testOnly: true, teamId: team.id, plan: pro ? "pro" : "free", memberCount: team.members.length,
            memberLimit: pro ? null : 3, canJoin: pro || team.members.length < 3, isOwner: team.ownerUserProfileId === user.id,
            checkoutAvailable: true, monthlyYen: 500, lastSyncedAt: stamp() };
    }
    const registered = new Map([["personal", ["プログラマー", "準備"]], [1, ["プログラマー", "アーティスト", "プランナー"]]]);
    const receipts = new Map(), undos = new Map(), choices = new Map(), memories = new Map();
    let appearance = { stage: "base", hatLevel: null, bowLevel: null, matLevel: null };
    const stages = [["base", "いつもの相棒", 1], ["explorer", "小さな冒険家", 5], ["grown", "頼れる相棒", 10], ["festival", "星のお祝い姿", 20]];
    const memoryCatalog = [
        ["first", "はじめの一歩", "初めての完了", 0, 1], ["tasks10", "10個の達成", "10個完了", 0, 3],
        ["tasks50", "50個の達成", "50個完了", 1, 3], ["tasks100", "100個の達成", "100個完了", 2, 3],
        ["level5", "小さな冒険", "Lv.5に到達", 1, 0], ["level10", "頼れる相棒", "Lv.10に到達", 2, 0],
        ["level20", "星のお祝い", "Lv.20に到達", 3, 3], ["pet", "なかよしの時間", "初めてなでる", 0, 1],
        ["treat", "おやつの時間", "初めておやつ", 0, 2], ["rest", "おやすみ", "初めて休憩", 0, 4]
    ];
    function addSample(id, teamId, title, status, tags, checklist = [], assignee = null) {
        tasks.push({ id, teamId, title, status, isCompleted: status === "Done", description: "お試し用のサンプルです。自由に編集できます。",
            dueDate: new Date(Date.now() + 86400000).toISOString().slice(0, 10) + "T00:00:00Z", priority: 1,
            tags, checklist, assigneeUserProfileId: assignee, createdByUserProfileId: 1, createdByDisplayName: user.displayName,
            version: 1, createdAt: new Date(Date.now() - id * 60000).toISOString(), updatedAt: stamp(), undo: null });
        if (status === "Done") receipts.set(id, { date: stamp(), active: true });
    }
    addSample(1, null, "カードをDONEに移動してみよう", "Todo", "準備", [{ text: "ドラッグか編集でDONEにする", isCompleted: false }]);
    addSample(2, null, "ログイン画面の動作を確認", "Doing", "プログラマー", [{ text: "画面を作る", isCompleted: true }, { text: "入力チェックを確認", isCompleted: false }]);
    addSample(3, null, "今日の予定を整理", "Done", "準備");
    addSample(4, 1, "APIの仕様をまとめる", "Todo", "プログラマー, プランナー", [], 1);
    addSample(5, 1, "ペットの表情をデザイン", "Doing", "アーティスト", [{ text: "通常の表情", isCompleted: true }, { text: "うれしい表情", isCompleted: false }], 2);
    addSample(6, 1, "チームの目標を決める", "Done", "プランナー", [], 1);
    const companionDemo = window.CompanionDemo?.create({ tasks, user, teams, response, fail, clone, stamp, tokens });
    receipts.set(-1, { date: new Date(Date.now() - 7 * 86400000).toISOString(), active: true });
    function pet() {
        const completed = 54 + [...receipts.values()].filter(item => item.active && (item.recipientId ?? user.id) === user.id).length;
        const totalExperience = completed * 25;
        let level = 1; const threshold = n => 25 * (n - 1) * (n + 2);
        while (threshold(level + 1) <= totalExperience) level++;
        const experience = totalExperience - threshold(level), experienceToNextLevel = threshold(level + 1) - threshold(level);
        for (const [key, minimum] of [["first", 1], ["tasks10", 10], ["tasks50", 50], ["tasks100", 100]]) if (completed >= minimum && !memories.has(key)) memories.set(key, stamp());
        for (const n of [5, 10, 20]) if (level >= n && !memories.has(`level${n}`)) memories.set(`level${n}`, stamp());
        if (!stages.some(stage => stage[0] === appearance.stage)) appearance.stage = "base";
        return { id: 1, ...petIdentity, level, totalExperience, experience, experienceToNextLevel,
            experienceProgress: Math.round(experience / experienceToNextLevel * 100), experienceRemaining: experienceToNextLevel - experience,
            completedTaskCount: completed, title: "お試しの相棒", streakDays: 2, energy: 88, mood: "Idle", moodLabel: "待機中", energyLabel: "元気いっぱい",
            message: "一つ終わったら、一緒に喜ぼう。", achievements: ["初完了", "10個の達成"], lastCompletedAt: stamp(), updatedAt: stamp() };
    }
    function collection() {
        const p = pet();
        return { level: p.level, totalExperience: p.totalExperience, species: p.species, appearance: clone(appearance), maxRewardLevel: 5,
            stages: stages.map(([id, name, requiredLevel], row) => ({ id, name, requiredLevel, row, available: p.level >= requiredLevel || memories.has(`level${requiredLevel}`) })),
            rewards: Array.from({ length: 5 }, (_, i) => ({ level: i + 1, available: i < p.level || choices.has(i + 1), claimedChoice: choices.get(i + 1) || null, claimedAt: choices.has(i + 1) ? stamp() : null,
                options: [["hat", "帽子"], ["bow", "リボン"], ["mat", "クッション"]].map(([id, name]) => ({ id, name: `Lv.${i + 1}の${name}`, shape: 0, hue: 0, image: `assets/pet/rewards-v2/${p.species}/lv-${i + 1}-${id}.png` })) })),
            memories: memoryCatalog.map(([key, name, description, row, column]) => ({ key, name, description, row, column, unlockedAt: memories.get(key) || null })) };
    }
    function scoped(teamId) { return tasks.filter(task => task.teamId === teamId); }
    function describe(task) {
        const { _companion, ...visible } = clone(task);
        return { ...visible, needsHelp: Boolean(task.teamId && _companion?.help?.status === "open"), handoffPending: Boolean(task.teamId && ["sent", "question"].includes(_companion?.handoff?.status)), handoffRecipientId: task.teamId && ["sent", "question"].includes(_companion?.handoff?.status) ? _companion.handoff.toUserId : null, noteCount: _companion?.notes?.length || 0, assigneeDisplayName: teams.find(team => team.id === task.teamId)?.members.find(member => member.userProfileId === task.assigneeUserProfileId)?.displayName || null };
    }
    function summary(teamId) {
        const list = scoped(teamId), count = status => list.filter(task => task.status === status).length;
        return { total: list.length, todo: count("Todo"), doing: count("Doing"), done: count("Done"), overdue: 0 };
    }
    function describeTeam(team) {
        const counts = summary(team.id);
        return { ...clone(team), memberCount: team.members.length, totalTasks: counts.total, todoCount: counts.todo, doingCount: counts.doing, doneCount: counts.done };
    }
    function tags(teamId) {
        const list = scoped(teamId), map = new Map();
        for (const name of registered.get(teamId ?? "personal") || []) map.set(name.toLowerCase(), { name, total: 0, todo: 0, doing: 0, done: 0 });
        for (const task of list) for (const name of tokens(task.tags)) {
            if (!map.has(name.toLowerCase())) map.set(name.toLowerCase(), { name, total: 0, todo: 0, doing: 0, done: 0 });
            const item = map.get(name.toLowerCase()); item.total++; item[task.status.toLowerCase()]++;
        }
        return { items: [...map.values()].sort((a, b) => a.name.localeCompare(b.name)), totalTasks: list.length,
            untaggedTasks: list.filter(task => !tokens(task.tags).length).length, registeredTags: (registered.get(teamId ?? "personal") || []).length, maxRegisteredTags: 50 };
    }
    function recordUndo(task, deleted, before, priorReceipt) {
        const token = crypto.randomUUID(), expiresAt = new Date(Date.now() + 30000).toISOString();
        for (const [key, entry] of undos) if (entry.expiresAt <= stamp()) undos.delete(key);
        undos.set(token, { task: clone(task), before: clone(before), priorReceipt: priorReceipt ? clone(priorReceipt) : null, deleted, expiresAt });
        return { token, expiresAt };
    }
    function validateTask(body, teamId) {
        if (!body.title?.trim() || body.title.length > 200 || body.description?.length > 2000 || body.tags?.length > 300) return "タイトル・説明・タグの長さを確認してください。";
        if (body.assigneeUserProfileId != null && !teams.find(team => team.id === teamId)?.members.some(member => member.userProfileId === body.assigneeUserProfileId)) return "チームのメンバーを選んでください。";
        if (body.checklist && (body.checklist.length > 20 || body.checklist.some(item => !item.text?.trim() || item.text.length > 120))) return "チェック項目の内容と件数を確認してください。";
        return "";
    }
    function weekly() {
        const today = new Date(Date.now() + 9 * 3600000), monday = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate()));
        monday.setUTCDate(monday.getUTCDate() - (monday.getUTCDay() + 6) % 7);
        const start = monday.getTime() - 9 * 3600000;
        const awards = [...receipts.values()].filter(item => item.active && (item.recipientId ?? user.id) === user.id).map(item => Date.parse(item.date));
        const thisWeek = awards.filter(date => date >= start).length, lastWeek = awards.filter(date => date >= start - 7 * 86400000 && date < start).length;
        return { weekStart: monday.toISOString().slice(0, 10), weekEnd: new Date(monday.getTime() + 6 * 86400000).toISOString().slice(0, 10), thisWeek, lastWeek, difference: thisWeek - lastWeek,
            days: Array.from({ length: 7 }, (_, i) => ({ date: new Date(monday.getTime() + i * 86400000).toISOString().slice(0, 10), completed: awards.filter(date => date >= start + i * 86400000 && date < start + (i + 1) * 86400000).length })),
            message: `今週は${thisWeek}件できたね！ひとつずつ、自分のペースで。（お試しデータ）` };
    }
    async function request(url, options = {}) {
        if (url.origin !== window.location.origin) return fail("お試しでは外部APIへ接続しません。", 403);
        const method = (options.method || "GET").toUpperCase(), path = url.pathname;
        let body = {}; try { if (options.body) body = JSON.parse(options.body); } catch { return fail("入力を確認してください。"); }
        if (path === "/api/user") {
            if (method === "DELETE") return response(null, 204);
            if (method === "PUT") user.displayName = String(body.displayName || "お試しユーザー").slice(0, 100);
            return response(user);
        }
        if (path === "/api/auth/logout") return response(null, 204);
        if (path === "/api/user/preferences") { if (method === "PUT") preferences = { theme: body.theme, layout: body.layout }; return response(preferences); }
        if (path === "/api/user/unlocks") {
            const p = pet(), result = { level: p.level, totalExperience: p.totalExperience };
            for (const [category, values] of Object.entries(UNLOCK_OPTIONS)) result[category] = values.map(item => ({ ...item, unlocked: p.level >= item.requiredLevel, remainingExperience: Math.max(0, 25 * (item.requiredLevel - 1) * (item.requiredLevel + 2) - p.totalExperience) }));
            return response(result);
        }
        if (path === "/api/pet") {
            if (method === "PUT") { if (!body.name?.trim() || body.name.length > 100 || !["cat", "dog", "rabbit", "fox", "panda", "dragon"].includes(body.species)) return fail("名前と種類を確認してください。"); petIdentity = { name: body.name.trim(), species: body.species }; }
            return response(pet());
        }
        if (path === "/api/pet/weekly-review") return response(weekly());
        if (path === "/api/pet/collection") return response(collection());
        if (path === "/api/pet/interactions") { if (!["pet", "treat", "rest"].includes(body.action)) return fail("操作を選んでください。"); if (!memories.has(body.action)) memories.set(body.action, stamp()); return response(collection()); }
        if (path === "/api/pet/rewards") {
            if (!Number.isInteger(body.level) || body.level < 1 || body.level > 5 || (body.level > pet().level && !choices.has(body.level)) || !["hat", "bow", "mat"].includes(body.choice)) return fail("ごほうびを選んでください。");
            if (choices.has(body.level) && choices.get(body.level) !== body.choice) return fail("このレベルのごほうびは受け取り済みです。", 409);
            choices.set(body.level, body.choice); return response(collection());
        }
        if (path === "/api/pet/appearance") {
            if (!collection().stages.some(stage => stage.id === body.stage && stage.available)) return fail("この姿はまだ解放されていません。", 403);
            for (const kind of ["hat", "bow", "mat"]) if (body[`${kind}Level`] != null && choices.get(body[`${kind}Level`]) !== kind) return fail("受け取ったごほうびを選んでください。", 403);
            appearance = clone(body); return response(collection());
        }
        if (path === "/api/work-inbox" && method === "GET") return companionDemo?.inbox(url.searchParams.get("view") || "incoming") || fail("依頼一覧を読み込めません。再読み込みしてください。", 503);
        if (path === "/api/teams") {
            if (method === "POST") {
                if (!body.name?.trim() || body.name.length > 100) return fail("チーム名を確認してください。");
                if (teams.length >= 10) return fail("お試しチームは10件までです。", 409);
                const team = { id: nextTeamId++, name: body.name.trim(), ownerUserProfileId: 1, role: "owner", members: [{ userProfileId: 1, displayName: user.displayName, role: "owner" }] }; teams.push(team); registered.set(team.id, []);
                return response({ team: describeTeam(team), inviteCode: `DEMO-LOCAL-ONLY-${team.id}`, expiresAt: stamp() }, 201);
            }
            return response(teams.map(describeTeam));
        }
        if (path === "/api/teams/join") return fail("お試しのチームはこの画面内だけの見本です。実際の参加・招待はログイン後に利用できます。");
        const teamRoute = path.match(/^\/api\/teams\/(\d+)(?:\/(.*))?$/);
        const accountRoute = path.match(/^\/api\/user\/billing(?:\/(.*))?$/);
        const teamId = teamRoute ? Number(teamRoute[1]) : null, team = accountRoute ? { id:0, ownerUserProfileId:user.id, members:[] } : teams.find(item => item.id === teamId);
        if (teamRoute && !team) return fail("チームが見つかりません。", 404);
        const tail = teamRoute ? teamRoute[2] || "" : path.replace(/^\/api\//, "");
        if (accountRoute || (teamRoute && (tail === "billing" || tail.startsWith("billing/")))) {
            if (method === "GET" && (tail === "billing" || (accountRoute && !accountRoute[1]))) return response(billingFor(team));
            if (method !== "POST" || team.ownerUserProfileId !== user.id) return fail("プラン操作は所有者だけが行えます。", 403);
            const operation = accountRoute ? accountRoute[1] : tail.slice(8), current = billingFor(team);
            if (operation === "checkout") demoBilling.set(team.ownerUserProfileId, { status: "active", paidThrough: new Date(Date.now()+30*86400000).toISOString(), cancelAtPeriodEnd:false, hasContract:true });
            else if (operation === "cancel" || operation === "resume") { if (!current.hasContract) return fail("契約がありません。",409); demoBilling.set(team.ownerUserProfileId,{...current,cancelAtPeriodEnd:operation === "cancel"}); }
            else if (operation === "end_now" || operation === "abandon") demoBilling.set(team.ownerUserProfileId,{status:"canceled",paidThrough:null,cancelAtPeriodEnd:false,hasContract:false});
            else if (operation === "demo-fail") demoBilling.set(team.ownerUserProfileId,{status:"past_due",paidThrough:new Date(Date.now()-1000).toISOString(),cancelAtPeriodEnd:false,hasContract:true});
            else if (operation === "demo-member") {
                if (accountRoute) return fail("先にチームを選んでください。",400);
                if (!current.canJoin) return fail("無料チームは所有者を含め3人までです。既存メンバーは引き続き利用できます。",409);
                team.members.push({userProfileId:100+team.members.length,displayName:`見本メンバー${team.members.length}`,role:"member"});
            } else if (operation !== "sync") return fail("対応しない操作です。");
            return response({ url:null,billing:billingFor(team) });
        }
        if (tail === "companion" || tail.startsWith("companion/")) return companionDemo
            ? companionDemo.request(tail, teamId, method, body, url.searchParams) : fail("お試しの相棒機能を読み込めませんでした。再読み込みしてください。", 503);
        if (teamRoute && tail === "summary") return response(summary(teamId));
        if (teamRoute && tail === "invites") return fail("お試しでは実際の招待コードは発行しません。");
        if (teamRoute && tail === "owner") return fail("管理者の引き継ぎはログイン後のチームで利用できます。");
        if (teamRoute && (tail === "members/me" || !tail)) {
            if (method === "DELETE") { for (let i = tasks.length - 1; i >= 0; i--) if (tasks[i].teamId === teamId) tasks.splice(i, 1); teams.splice(teams.indexOf(team), 1); registered.delete(teamId); return response(null, 204); }
            return response(describeTeam(team));
        }
        if (tail === "task-tags") {
            if (method === "POST") {
                const name = String(body.name || "").trim(), list = registered.get(teamId ?? "personal") || [];
                if (!name || name.length > 50 || /[,\u0000-\u001f]/.test(name)) return fail("タグ名を確認してください。");
                if (list.some(item => same(item, name))) return fail("このタグは登録済みです。", 409);
                if (list.length >= 50) return fail("タグは50個までです。", 409);
                registered.set(teamId ?? "personal", [...list, name]); return response({ name }, 201);
            }
            return response(tags(teamId));
        }
        if (tail === "task-tags/manage") {
            const { name, action, targetName } = body, catalog = tags(teamId);
            if (!catalog.items.some(item => same(item.name, name))) return fail("タグは変更されています。", 409);
            if (!["rename", "delete", "merge"].includes(action)) return fail("操作を選んでください。");
            if (action !== "delete" && (!targetName?.trim() || targetName.length > 50 || /[,\u0000-\u001f]/.test(targetName))) return fail("タグ名を確認してください。");
            const targetExists = catalog.items.some(item => same(item.name, targetName));
            if (action === "rename" && targetExists && !same(name, targetName)) return fail("同名のタグがあります。「統合」を使ってください。", 409);
            if (action === "merge" && (!targetExists || same(name, targetName))) return fail("別の既存タグを選んでください。");
            const mapNames = values => tokens(values.map(item => same(item, name) ? action === "delete" ? "" : targetName : item).filter(Boolean).join(", "));
            const updates = scoped(teamId).filter(task => tokens(task.tags).some(item => same(item, name))).map(task => [task, mapNames(tokens(task.tags)).join(", ")]);
            if (updates.some(([, csv]) => csv.length > 300)) return fail("タグが300文字を超えるタスクがあります。");
            registered.set(teamId ?? "personal", mapNames(registered.get(teamId ?? "personal") || []));
            for (const [task, csv] of updates) { task.tags = csv; task.version++; task.updatedAt = stamp(); }
            return response(tags(teamId));
        }
        const undoRoute = tail.match(/^tasks\/undo\/(.+)$/);
        if (undoRoute && method === "POST") {
            const entry = undos.get(undoRoute[1]);
            if (!entry || entry.expiresAt <= stamp() || entry.task.teamId !== teamId) return fail("元に戻せる時間を過ぎています。", 409);
            const current = tasks.find(item => item.id === entry.task.id);
            if (entry.deleted) {
                if (current) return fail("すでに復元されています。", 409);
                if (scoped(teamId).length >= 500) return fail("タスク数の上限です。", 409);
                tasks.push({ ...entry.before, version: entry.before.version + 1 });
            } else {
                if (!current || current.version !== entry.task.version) return fail("別の操作で更新されています。", 409);
                current.status = entry.before.status; current.isCompleted = entry.before.isCompleted; current.version++; current.updatedAt = stamp();
                if (entry.priorReceipt) receipts.set(current.id, entry.priorReceipt); else receipts.delete(current.id);
            }
            undos.delete(undoRoute[1]); return response(describe(tasks.find(item => item.id === entry.task.id)));
        }
        const taskRoute = tail.match(/^tasks(?:\/(\d+))?$/);
        if (taskRoute) {
            const id = taskRoute[1] ? Number(taskRoute[1]) : null;
            const task = scoped(teamId).find(item => item.id === id);
            if (id != null && !task) return fail("タスクが見つかりません。", 404);
            if (method === "DELETE") {
                if (Number(url.searchParams.get("version")) !== task.version) return fail("別の操作で更新されています。", 409);
                const undo = recordUndo(task, true, task, receipts.get(task.id)); tasks.splice(tasks.indexOf(task), 1);
                return response(null, 204, { "X-Task-Undo": undo.token, "X-Task-Undo-Expires": undo.expiresAt });
            }
            if (method === "POST" || method === "PUT") {
                const error = validateTask(body, teamId); if (error) return fail(error);
                if (method === "POST" && scoped(teamId).length >= 500) return fail("タスクは500件までです。", 409);
                if (task && body.version !== task.version) return fail("別の操作で更新されています。", 409);
                const before = task ? clone(task) : null, priorReceipt = task ? clone(receipts.get(task.id) || null) : null;
                const next = { ...(task || { id: nextId++, teamId, createdAt: stamp(), createdByUserProfileId: 1, createdByDisplayName: user.displayName }),
                    ...clone(body), status: body.isCompleted ? "Done" : statusValue(body.status), version: (task?.version || 0) + 1, updatedAt: stamp(), undo: null };
                next.checklist = body.checklist || task?.checklist || []; next.isCompleted = next.status === "Done";
                if (next.status === "Done" && task?.status !== "Done") receipts.set(next.id, { date: stamp(), active: true, recipientId: next.assigneeUserProfileId ?? user.id });
                else if (task?.status === "Done" && next.status !== "Done" && receipts.has(next.id)) receipts.get(next.id).active = false;
                if (task) tasks[tasks.indexOf(task)] = next; else tasks.push(next);
                pet(); // Persist earned milestones even if the next action immediately undoes completion.
                const result = describe(next);
                if (before && before.status !== next.status) result.undo = recordUndo(next, false, before, priorReceipt);
                return response(result, task ? 200 : 201);
            }
            if (id) return response(describe(task));
            const q = url.searchParams, page = Number(q.get("page") || 1), pageSize = Number(q.get("pageSize") || 100);
            const assignee = q.get("assignee"), due = q.get("due");
            if (assignee && !["me", "unassigned"].includes(assignee)
                && !teams.find(team => team.id === teamId)?.members.some(member => String(member.userProfileId) === assignee))
                return fail("担当者は選択中のチームのメンバーから選んでください。");
            if (due && !["today", "through_today", "overdue", "none"].includes(due)) return fail("期限の検索条件が不正です。");
            const today = new Date(Date.now() + 9 * 3600000).toISOString().slice(0, 10);
            let list = scoped(teamId).filter(task => (!q.get("search") || task.title.includes(q.get("search")) || (task.description || "").includes(q.get("search")))
                && (!q.get("status") || task.status === statusValue(q.get("status"))) && (!q.get("priority") || task.priority === Number(q.get("priority")))
                && (!q.get("tag") || String(task.tags || "").includes(q.get("tag"))) && (!q.get("tagExact") || tokens(task.tags).some(item => same(item, q.get("tagExact"))))
                && (q.get("untagged") !== "true" || !tokens(task.tags).length));
            if (assignee) list = list.filter(task => assignee === "me" ? !teamId || task.assigneeUserProfileId === user.id
                : assignee === "unassigned" ? task.assigneeUserProfileId == null : String(task.assigneeUserProfileId) === assignee);
            if (due) list = list.filter(task => due === "none" ? !task.dueDate : task.status !== "Done" && task.dueDate
                && (due === "today" ? task.dueDate.slice(0,10) === today : due === "overdue" ? task.dueDate.slice(0,10) < today : task.dueDate.slice(0,10) <= today));
            const priority = task => typeof task.priority === "number" ? task.priority : ["Low", "Medium", "High"].indexOf(task.priority);
            list.sort(q.get("sortOrder") === "due"
                ? (a, b) => (a.dueDate || "9999").localeCompare(b.dueDate || "9999") || priority(b) - priority(a) || b.id - a.id
                : (a, b) => (a.createdAt.localeCompare(b.createdAt) || a.id - b.id) * (q.get("sortOrder") === "asc" ? 1 : -1));
            return response({ items: list.slice((page - 1) * pageSize, page * pageSize).map(describe), statusCounts: Object.fromEntries(["Todo", "Doing", "Done"].map(status => [status.toLowerCase(), list.filter(task => task.status === status).length])), totalCount: list.length, totalPages: Math.ceil(list.length / pageSize), page, pageSize });
        }
        return fail("この操作はお試しモードでは利用できません。ログイン後に利用できます。", 404);
    }
    document.addEventListener("DOMContentLoaded", () => {
        document.body.dataset.demo = "true";
        const notice = document.createElement("aside"); notice.className = "demo-notice"; notice.setAttribute("aria-label", "お試しモード");
        const text = document.createElement("span"); text.textContent = "お試し中：サンプル専用・再読み込みでリセット・実際の共有や招待は行いません";
        const reset = document.createElement("button"); reset.type = "button"; reset.textContent = "最初から試す";
        reset.addEventListener("click", () => { if (confirm("お試しの変更をリセットしますか？実データには影響しません。")) window.location.reload(); });
        const exit = document.createElement("a"); exit.href = "login.html"; exit.textContent = "ログイン画面へ";
        notice.append(text, reset, exit); document.body.prepend(notice);
        document.querySelector(".user-session-label").textContent = "お試しモード";
        document.getElementById("user-delete-button").hidden = true;
        document.getElementById("user-logout-button").textContent = "お試しを終了";
    });
    return Object.freeze({ active: true, request });
})();
