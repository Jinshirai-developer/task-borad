// Disposable workflow PostgreSQL/Mailpit environment only. Never production or the user preview.
import assert from "node:assert/strict";
const app = new URL(process.env.APP_URL || "http://task-v15-qa-web:8080");
const mail = new URL(process.env.MAILPIT_URL || "http://task-v15-qa-mail:8025");
assert.equal(app.hostname, "task-v15-qa-web"); assert.equal(mail.hostname, "task-v15-qa-mail");
let checks = 0, requests = 0;
function status(response, expected, label) { assert.equal(response.status, expected, `${label}: ${response.status}`); checks++; return response; }
class Actor {
    constructor(label) {
        this.key = `v15_${label}_${crypto.randomUUID().slice(0, 8)}`; this.email = `${this.key}@taskboard.test`;
        this.password = `V15-test-${crypto.randomUUID()}`; this.cookies = new Map(); this.csrf = null;
    }
    async request(path, method = "GET", body, skipCsrf = false) {
        if (method !== "GET" && !skipCsrf && !this.csrf) this.csrf = (await (await this.request("/api/auth/csrf")).json()).token;
        const headers = { Accept: "application/json", Cookie: [...this.cookies].map(([key, value]) => `${key}=${value}`).join("; ") };
        if (this.csrf && !skipCsrf) headers["X-CSRF-TOKEN"] = this.csrf;
        if (body !== undefined) headers["Content-Type"] = "application/json";
        requests++;
        const response = await fetch(new URL(path, app), { method, headers, body: body === undefined ? undefined : JSON.stringify(body), redirect: "error", signal: AbortSignal.timeout(15000) });
        for (const cookie of response.headers.getSetCookie()) {
            const pair = cookie.split(";")[0], equal = pair.indexOf("=");
            if (equal > 0) { const key = pair.slice(0, equal), value = pair.slice(equal + 1); if (value) this.cookies.set(key, value); else this.cookies.delete(key); }
        }
        return response;
    }
    async register(config) {
        const registration = status(await this.request("/api/auth/register", "POST", { userKey: this.key, displayName: this.key, email: this.email, password: this.password, acceptTerms: true, termsVersion: config.termsVersion, privacyVersion: config.privacyVersion }), 200, "register");
        this.user = (await registration.json()).user; this.csrf = null;
        let secret;
        for (let i = 0; i < 80 && !secret; i++) {
            const list = await (await fetch(new URL(`/api/v1/search?query=${encodeURIComponent(`to:${this.email}`)}`, mail))).json();
            for (const summary of list.messages || []) {
                const message = await (await fetch(new URL(`/api/v1/message/${summary.ID}`, mail))).json();
                for (const candidate of message.Text.match(/https?:\/\/[^\s<>"']+/g) || []) {
                    const link = new URL(candidate); assert.equal(link.origin, "http://localhost:5100");
                    if (link.searchParams.get("mode") === "confirm") { const params = new URLSearchParams(link.hash.slice(1)); secret = { userId: Number(params.get("userId")), token: params.get("token") }; }
                }
            }
            if (!secret) await new Promise(resolve => setTimeout(resolve, 500));
        }
        assert.ok(secret, "local SMTP confirmation delivered");
        status(await this.request("/api/auth/confirm-email", "POST", secret), 200, "confirm"); this.csrf = null;
        status(await this.request("/api/auth/login", "POST", { userKey: this.key, password: this.password }), 200, "login"); this.csrf = null;
    }
    async json(path) { return (status(await this.request(path), 200, "get")).json(); }
}
const owner = new Actor("workflow_owner"), member = new Actor("workflow_member"), outsider = new Actor("workflow_outside");
const config = await owner.json("/api/auth/config");
for (const actor of [owner, member, outsider]) await actor.register(config);
const created = await (status(await owner.request("/api/teams", "POST", {name:"Workflow QA"}),201,"team")).json();
const team = created.team.id, root = `/api/teams/${team}/tasks`;
status(await member.request("/api/teams/join","POST",{inviteCode:created.inviteCode}),200,"join");
const today = new Date(Date.now()+9*3600000).toISOString().slice(0,10)+"T00:00:00Z";
const yesterday = new Date(Date.parse(today)-86400000).toISOString();
const create = async body => (status(await owner.request(root,"POST",{title:"Workflow",description:"needle",tags:"work",...body}),201,"create")).json();
const late = await create({dueDate:yesterday,assigneeUserProfileId:member.user.id});
const due = await create({dueDate:today,assigneeUserProfileId:member.user.id,status:"Doing"});
const undated = await create({assigneeUserProfileId:member.user.id});
await create({dueDate:yesterday,assigneeUserProfileId:member.user.id,status:"Done"});
await create({dueDate:yesterday,assigneeUserProfileId:owner.user.id});
for (const suffix of ["", "&tagExact=work"]) {
 const q="?search=needle&assignee=me&due=through_today&sortOrder=due&pageSize=1"+suffix;
 const page=await member.json(root+q);
 assert.equal(page.totalCount,2); assert.equal(page.items[0].id,late.id); assert.equal(page.totalPages,2);
 assert.deepEqual(page.statusCounts,{todo:1,doing:1,done:0}); checks+=4;
 assert.equal((await member.json(root+q+"&page=2")).items[0].id,due.id); checks++;
}
assert.equal((await member.json(root+"?assignee=me&sortOrder=due")).items.at(-1).id,undated.id); checks++;
assert.equal((await member.json(root+"?assignee=me&due=overdue")).totalCount,1); checks++;
assert.equal((await member.json(root+"?assignee=me&due=today")).totalCount,1); checks++;
status(await owner.request(root+"?assignee="+outsider.user.id),400,"nonmember filter denied");
status(await outsider.request(root+"?assignee=me"),404,"outside workspace denied");
status(await owner.request(root+"?due=invalid"),400,"bad due denied");
const change=async(actor,id,action,fields={})=>{
 const path=`/api/teams/${team}/companion/tasks/${id}`, before=await actor.json(path);
 return (status(await actor.request(path,"POST",{action,version:before.version,expectedUpdatedAt:before.updatedAt,...fields}),200,action)).json();
};
const help=await change(owner,late.id,"help_open",{kind:"review",message:"Directed request",recipientId:member.user.id});
await change(owner,late.id,"savepoint_save",{nextStep:"PRIVATE-DO-NOT-LEAK"});
let inbox=await member.json("/api/work-inbox");
assert.equal(inbox.directCount,1); assert.equal(inbox.items[0].taskId,late.id); checks+=2;
assert.ok(!JSON.stringify(inbox).includes("PRIVATE-DO-NOT-LEAK")); checks++;
assert.deepEqual(await member.json("/api/work-inbox"),inbox); checks++;
assert.equal((await outsider.json("/api/work-inbox")).items.length,0); checks++;
await change(member,late.id,"help_offer",{entryId:help.help.id});
assert.equal((await member.json("/api/work-inbox")).directCount,0); checks++;
await change(member,late.id,"help_withdraw",{entryId:help.help.id});
assert.equal((await member.json("/api/work-inbox")).directCount,1); checks++;
const all=await change(owner,due.id,"help_open",{kind:"review",message:"For team"});
assert.equal((await member.json("/api/work-inbox")).teamCount,1); checks++;
const handoff=await change(owner,undated.id,"handoff_send",{recipientId:member.user.id,message:"Please check",criteria:"Review done"});
assert.equal((await member.json("/api/work-inbox")).directCount,2); checks++;
await change(member,undated.id,"handoff_accept",{entryId:handoff.handoff.id});
assert.equal((await member.json("/api/work-inbox")).directCount,1); checks++;
const team2=await (status(await owner.request("/api/teams","POST",{name:"Second team"}),201,"team2")).json();
let secondJoin = await member.request("/api/teams/join","POST",{inviteCode:team2.inviteCode});
if (secondJoin.status === 429) {
 console.log("Respecting auth rate limit before the second team join.");
 await new Promise(resolve => setTimeout(resolve, Math.min(61, Number(secondJoin.headers.get("retry-after")) || 61)*1000+250));
 secondJoin = await member.request("/api/teams/join","POST",{inviteCode:team2.inviteCode});
}
status(secondJoin,200,"join team2");
const second=await (status(await owner.request(`/api/teams/${team2.team.id}/tasks`,"POST",{title:"Other team"}),201,"second task")).json();
const p=`/api/teams/${team2.team.id}/companion/tasks/${second.id}`, record=await owner.json(p);
status(await owner.request(p,"POST",{action:"help_open",version:record.version,expectedUpdatedAt:record.updatedAt,kind:"review",message:"Other team request",recipientId:member.user.id}),200,"second help");
inbox=await member.json("/api/work-inbox"); assert.equal(new Set(inbox.items.map(item=>item.teamId)).size,2); checks++;
status(await member.request(`/api/teams/${team}/members/me`,"DELETE"),204,"leave");
inbox=await member.json("/api/work-inbox"); assert.equal(inbox.items.length,1); assert.equal(inbox.items[0].teamId,team2.team.id); checks+=2;
console.log(JSON.stringify({passed:checks,apiRequests:requests,realPostgres:true,features:["filters","due sorting","cross-team inbox","membership","private notes"]}));
