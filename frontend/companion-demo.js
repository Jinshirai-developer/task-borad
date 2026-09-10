// Factory receives only the isolated demo's memory. It never fetches or stores data.
window.CompanionDemo = (() => {
    function create({ tasks, user, teams, response, fail, clone, stamp, tokens }) {
        const empty = () => ({ savepoints: [], help: null, thanks: [], handoff: null, notes: [], showcase: null });
        const state = task => clone(task._companion || empty());
        const people = teamId => Object.fromEntries((teams.find(t => t.id === teamId)?.members || [{ userProfileId: user.id, displayName: user.displayName }]).map(m => [m.userProfileId, m.displayName]));
        const describe = task => {
            const w = state(task);
            return { taskId: task.id, taskTitle: task.title, teamId: task.teamId, taskStatus: task.status, tags: task.tags,
                version: task.version, updatedAt: task.updatedAt, viewerId: user.id,
                isTeamOwner: teams.find(t => t.id === task.teamId)?.ownerUserProfileId === user.id,
                people: people(task.teamId), savepoint: w.savepoints.find(s => s.userId === user.id) || null,
                help: w.help, thanks: w.thanks || [], handoff: w.handoff, notes: w.notes, showcase: w.showcase };
        };
        const note = (tried, learned, authorId = 1) => ({ id: crypto.randomUUID(), authorId, tried, learned, nextStep: "次のタスクでも確認する", resourceUrl: null, createdAt: stamp(), updatedAt: stamp() });
        tasks.find(t => t.id === 2)._companion = { ...empty(), savepoints: [{ userId: 1, summary: "画面はできました", nextStep: "エラー表示を確認する", resourceUrl: null, savedAt: stamp() }], notes: [note("空の入力で送信", "必須項目の案内を先に出すと分かりやすい")] };
        tasks.find(t => t.id === 5)._companion = { ...empty(), help: { id: crypto.randomUUID(), authorId: 2, helperId: null, kind: "review", message: "完了時の表情が小さい画面でも分かるか、確認してください。", status: "open", createdAt: stamp(), closedAt: null }, notes: [note("白い背景をまとめて削除", "白い体毛を残すには外側の背景だけを選ぶ", 2)] };
        tasks.find(t => t.id === 4)._companion = { ...empty(), handoff: { id: crypto.randomUUID(), fromUserId: 2, toUserId: 1, request: "API仕様書のエラー応答と必須項目を確認してください", criteria: "不明点や修正が必要な箇所を作業メモにまとめる", resourceUrl: null, status: "sent", reply: null, createdAt: stamp(), respondedAt: null } };
        tasks.find(t => t.id === 6)._companion = { ...empty(), showcase: { authorId: 1, kind: "book", title: "チームのはじめの一冊", outcome: "作りたいものと役割をチームで決めました", resourceUrl: null, createdAt: stamp(), updatedAt: stamp() } };
        function matches(teamId, q = "", tags = "", exclude = null) {
            const words = q.trim().split(/\s+/).filter(Boolean).slice(0, 8), selected = tokens(tags).map(t => t.toLowerCase()), found = [];
            for (const task of tasks.filter(t => t.teamId === teamId && t.id !== exclude)) {
                const tagMatch = tokens(task.tags).some(t => selected.includes(t.toLowerCase()));
                for (const n of state(task).notes) {
                    const text = `${task.title}\n${n.tried}\n${n.learned}\n${n.nextStep}`.toLowerCase();
                    const count = words.filter(w => text.includes(w.toLowerCase())).length;
                    if ((words.length || selected.length) && !tagMatch && !count) continue;
                    found.push({ taskId: task.id, taskTitle: task.title, note: n, authorName: people(teamId)[n.authorId] || "退出したメンバー", matchReason: tagMatch ? "同じタグの記録" : count ? "キーワードが一致" : "最近の記録", score: count + (tagMatch ? 10 : 0) });
                }
            }
            return found.sort((a, b) => b.score - a.score || b.note.updatedAt.localeCompare(a.note.updatedAt)).slice(0, 10);
        }
        function dashboard(teamId) {
            const rows = tasks.filter(t => t.teamId === teamId).map(describe);
            const monday = new Date(Date.now() + 9 * 3600000); monday.setUTCHours(0, 0, 0, 0); monday.setUTCDate(monday.getUTCDate() - (monday.getUTCDay() + 6) % 7);
            const since = monday.getTime() - 9 * 3600000;
            return { weekStart: monday.toISOString().slice(0, 10), notesThisWeek: rows.flatMap(r => r.notes).filter(n => Date.parse(n.createdAt) >= since && Date.parse(n.createdAt) <= Date.now()).length,
                savepoints: rows.filter(r => r.savepoint && r.taskStatus !== "Done").sort((a, b) => b.savepoint.savedAt.localeCompare(a.savepoint.savedAt)).slice(0, 20),
                help: rows.filter(r => r.help?.status === "open").slice(0, 20), handoffs: rows.filter(r => ["sent", "question"].includes(r.handoff?.status)).slice(0, 20),
                showcase: rows.filter(r => r.showcase && r.taskStatus === "Done").slice(0, 50), recentNotes: matches(teamId),
                recentThanks: rows.flatMap(r => r.thanks.map(t => ({ taskId: r.taskId, taskTitle: r.taskTitle, authorName: r.people[t.authorId] || "退出したメンバー", helperName: r.people[t.helperId] || "チームのみんな", closedAt: t.closedAt }))).sort((a,b)=>b.closedAt.localeCompare(a.closedAt)).slice(0,10) };
        }
        function change(task, body) {
            if (body.version !== task.version || body.expectedUpdatedAt !== task.updatedAt) return fail("タスクが更新されています。入力は保持しています。「更新」で最新の内容を確認してください。", 409);
            const w = state(task), now = stamp(), id = user.id, owner = teams.find(t => t.id === task.teamId)?.ownerUserProfileId === id;
            const error = (message, status = 400) => { throw { message, status }; };
            const text = (value, required = false, max = 400) => { const v = String(value || "").trim(); if ((required && !v) || v.length > max || /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/.test(v)) error(`入力は${required ? 1 : 0}〜${max}文字にしてください。`); return v; };
            const url = value => { if (!value?.trim()) return null; try { const u = new URL(value); if (value.length > 1000 || !["http:", "https:"].includes(u.protocol) || u.username || u.password || /[\u0000-\u001f]/.test(value)) throw 0; return value.trim(); } catch { error("リンクは認証情報を含まないhttp/https URLにしてください。"); } };
            const team = () => { if (!task.teamId) error("依頼はチームのタスクで利用できます。"); };
            const manage = author => { if (author !== id && !owner) error("作成者またはチーム管理者だけが変更できます。", 403); };
            const help = () => { team(); if (w.help?.id !== body.entryId || w.help?.status !== "open") error("依頼は変更されたか終了しています。", 409); };
            const handoff = () => { team(); if (w.handoff?.id !== body.entryId || !["sent", "question"].includes(w.handoff?.status)) error("依頼は変更されたか終了しています。", 409); };
            try {
                switch (body.action) {
                    case "savepoint_save": w.savepoints = w.savepoints.filter(s => s.userId !== id); w.savepoints.push({ userId: id, nextStep: text(body.nextStep, true), summary: text(body.summary), resourceUrl: url(body.resourceUrl), savedAt: now }); break;
                    case "savepoint_clear": w.savepoints = w.savepoints.filter(s => s.userId !== id); break;
                    case "help_open":
                        team(); if (w.help?.status === "open") error("未解決の依頼があります。", 409);
                        if (!["decision", "review", "material", "together"].includes(body.kind)) error("依頼の種類を選んでください。");
                        if (body.recipientId != null && (!Number.isInteger(body.recipientId) || body.recipientId === id || !Object.hasOwn(people(task.teamId), body.recipientId)))
                            error("自分以外のチームメンバーを選んでください。");
                        w.help = { id: crypto.randomUUID(), authorId: id, recipientId: body.recipientId ?? null, recipientUnavailable: false, helperId: null, kind: body.kind, message: text(body.message, true), status: "open", createdAt: now, closedAt: null }; break;
                    case "help_offer":
                        help(); if (w.help.authorId === id) error("自分が送った依頼は引き受けられません。");
                        if (w.help.recipientUnavailable || (w.help.recipientId != null && w.help.recipientId !== id)) error("この依頼を引き受けられるのは、宛先のメンバーだけです。", 403);
                        if (w.help.helperId && w.help.helperId !== id) error("ほかのメンバーが対応中です。", 409); w.help.helperId = id; break;
                    case "help_withdraw": help(); if (w.help.helperId !== id) error("対応を辞退できるのは、引き受けた本人だけです。", 403); w.help.helperId = null; break;
                    case "help_resolve": case "help_cancel": help(); manage(w.help.authorId); w.help.status = body.action === "help_resolve" ? "resolved" : "cancelled"; w.help.closedAt = now;
                        if (body.action === "help_resolve") w.thanks = [...(w.thanks || []), clone(w.help)].slice(-20); break;
                    case "handoff_send":
                        team(); if (w.handoff?.status === "sent") error("受け渡し中です。", 409); if (w.handoff?.status === "question") manage(w.handoff.fromUserId);
                        if (body.recipientId === id || !Object.hasOwn(people(task.teamId), body.recipientId)) error("自分以外のメンバーを選んでください。");
                        w.handoff = { id: crypto.randomUUID(), fromUserId: id, toUserId: body.recipientId, assignOnAccept: body.assignOnAccept === true, request: text(body.message, true), criteria: text(body.criteria, true), resourceUrl: url(body.resourceUrl), status: "sent", reply: null, createdAt: now, respondedAt: null }; break;
                    case "handoff_accept": case "handoff_question": handoff(); if (w.handoff.toUserId !== id) error("宛先だけが返答できます。", 403); w.handoff.reply = text(body.message, body.action === "handoff_question"); w.handoff.status = body.action === "handoff_accept" ? "accepted" : "question"; w.handoff.respondedAt = now; if (body.action === "handoff_accept" && w.handoff.assignOnAccept) task.assigneeUserProfileId = id; break;
                    case "handoff_cancel": handoff(); manage(w.handoff.fromUserId); w.handoff.status = "cancelled"; w.handoff.respondedAt = now; break;
                    case "note_add": case "note_edit": {
                        const n = body.action === "note_edit" ? w.notes.find(n => n.id === body.entryId) : { id: crypto.randomUUID(), authorId: id, createdAt: now };
                        if (!n) error("メモが見つかりません。", 404); if (body.action === "note_edit") manage(n.authorId); else if (w.notes.length >= 12) error("1タスク12件までです。", 409);
                        Object.assign(n, { tried: text(body.tried), learned: text(body.learned, true), nextStep: text(body.nextStep), resourceUrl: url(body.resourceUrl), updatedAt: now }); if (body.action === "note_add") w.notes.push(n); break;
                    }
                    case "note_delete": { const n = w.notes.find(n => n.id === body.entryId); if (!n) error("メモが見つかりません。", 404); manage(n.authorId); w.notes = w.notes.filter(n => n.id !== body.entryId); break; }
                    case "showcase_save":
                        if (task.status !== "Done") error("DONEのタスクを飾れます。", 409); if (w.showcase) manage(w.showcase.authorId);
                        if (!["frame", "monitor", "book"].includes(body.kind)) error("飾り方を選んでください。");
                        w.showcase = { authorId: w.showcase?.authorId ?? id, title: text(body.title, true, 120), outcome: text(body.summary, true), kind: body.kind, resourceUrl: url(body.resourceUrl), createdAt: w.showcase?.createdAt || now, updatedAt: now }; break;
                    case "showcase_remove": if (w.showcase) manage(w.showcase.authorId); w.showcase = null; break;
                    default: error("操作が不正です。");
                }
                task._companion = w; task.version++; task.updatedAt = now; return response(describe(task));
            } catch (e) { return fail(e.message || "入力を確認してください。", e.status || 400); }
        }
        function request(tail, teamId, method, body, params) {
            if (tail === "companion" && method === "GET") return response(dashboard(teamId));
            if (tail === "companion/notes" && method === "GET") {
                if ((params.get("q") || "").length > 200 || (params.get("tags") || "").length > 300) return fail("検索文字数を確認してください。");
                return response(matches(teamId, params.get("q") || "", params.get("tags") || "", Number(params.get("excludeTaskId")) || null));
            }
            const match = tail.match(/^companion\/tasks\/(\d+)$/);
            if (!match) return fail("操作が見つかりません。", 404);
            const task = tasks.find(t => t.id === Number(match[1]) && t.teamId === teamId);
            if (!task) return fail("タスクが見つかりません。", 404);
            return method === "POST" ? change(task, body) : method === "GET" ? response(describe(task)) : fail("操作が不正です。", 405);
        }
        function inbox(view = "incoming") {
            if (!["incoming", "helping", "sent"].includes(view)) return fail("依頼の表示条件が正しくありません。", 400);
            const incoming = [], helping = [], sent = [];
            for (const task of tasks.filter(task => teams.some(team => team.id === task.teamId
                && team.members.some(member => member.userProfileId === user.id)))) {
                const work = state(task), teamName = teams.find(team => team.id === task.teamId).name;
                const names = people(task.teamId), name = id => names[id] || "退出したメンバー";
                const base = request => ({ id:request.id, teamId:task.teamId, teamName, taskId:task.id, taskTitle:task.title,
                    version:task.version, updatedAt:task.updatedAt, actions:[], assignOnAccept:false, helperName:null, requestKind:"", criteria:null });
                const h = work.help, handoff = work.handoff;
                if (h?.status === "open" && !h.recipientUnavailable) {
                    const item = { ...base(h), kind:"help", audience:h.recipientId == null ? "team" : "direct", message:h.message,
                        createdAt:h.createdAt, authorName:name(h.authorId), recipientName:h.recipientId == null ? "チーム全員" : name(h.recipientId),
                        helperName:h.helperId ? name(h.helperId) : null, requestKind:h.kind, status:h.helperId ? "helping" : "waiting" };
                    if (!h.helperId && h.authorId !== user.id && (h.recipientId == null || h.recipientId === user.id)) incoming.push({...item, actions:["help_offer"]});
                    if (h.helperId === user.id) helping.push({...item, actions:["help_withdraw"]});
                    if (h.authorId === user.id) sent.push({...item, actions:["help_resolve", "help_cancel"]});
                }
                if (handoff && ["sent", "question"].includes(handoff.status)) {
                    const item = { ...base(handoff), kind:"handoff", audience:"direct", status:handoff.status, criteria:handoff.criteria,
                        message:handoff.status === "question" ? handoff.reply || handoff.request : handoff.request,
                        createdAt:handoff.respondedAt || handoff.createdAt, authorName:name(handoff.fromUserId), recipientName:name(handoff.toUserId), assignOnAccept:!!handoff.assignOnAccept };
                    if (handoff.status === "sent" && handoff.toUserId === user.id) incoming.push({...item, actions:["handoff_accept"]});
                    if (handoff.fromUserId === user.id) { sent.push({...item, actions:["handoff_cancel"]}); if (handoff.status === "question") incoming.push(item); }
                }
            }
            const items = ({ incoming, helping, sent })[view];
            items.sort((a, b) => b.createdAt.localeCompare(a.createdAt) || a.id.localeCompare(b.id));
            return response({ items, directCount:incoming.filter(item => item.audience === "direct").length,
                teamCount:incoming.filter(item => item.audience === "team").length, helpingCount:helping.length, sentCount:sent.length });
        }
        return { request, inbox };
    }
    return Object.freeze({ create });
})();
