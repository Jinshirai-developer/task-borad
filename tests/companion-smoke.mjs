// Real PostgreSQL/SMTP exercise. Hard hostname guard excludes every user environment.
import assert from "node:assert/strict";
const app = new URL(process.env.APP_URL || "http://task-v16-qa-web:8080");
const mail = new URL(process.env.MAILPIT_URL || "http://task-v16-qa-mail:8025");
assert.equal(app.hostname,"task-v16-qa-web"); assert.equal(mail.hostname,"task-v16-qa-mail");
let checks=0, requests=0;
function status(r,expected,label) { assert.equal(r.status,expected,`${label}: ${r.status}`); checks++; return r; }
class Actor {
    constructor(label) { this.key=`v16_${label}_${crypto.randomUUID().slice(0,8)}`; this.email=`${this.key}@taskboard.test`; this.password=`V16-test-${crypto.randomUUID()}`; this.cookies=new Map(); this.csrf=null; }
    async request(path,method="GET",body,skipCsrf=false,retry=0) {
        if(method!=="GET"&&!skipCsrf&&!this.csrf) this.csrf=(await(await this.request("/api/auth/csrf")).json()).token;
        const headers={Accept:"application/json",Cookie:[...this.cookies].map(([k,v])=>`${k}=${v}`).join("; ")};
        if(this.csrf&&!skipCsrf) headers["X-CSRF-TOKEN"]=this.csrf;
        if(body!==undefined) headers["Content-Type"]="application/json";
        requests++;
        const r=await fetch(new URL(path,app),{method,headers,body:body===undefined?undefined:JSON.stringify(body),redirect:"error",signal:AbortSignal.timeout(15000)});
        for(const cookie of r.headers.getSetCookie()) { const pair=cookie.split(";")[0],equal=pair.indexOf("="); if(equal>0){const key=pair.slice(0,equal),value=pair.slice(equal+1); if(value)this.cookies.set(key,value);else this.cookies.delete(key);} }
        // A 429 is rejected by middleware before any command executes. Never retry
        // an ambiguous timeout or server error automatically.
        if(r.status===429&&retry<2) { console.log("QA rate limit reached; waiting for the next test window"); await new Promise(resolve=>setTimeout(resolve,Math.min(60,Number(r.headers.get("Retry-After"))||60)*1000)); return this.request(path,method,body,skipCsrf,retry+1); }
        return r;
    }
    async json(path){return(status(await this.request(path),200,"GET")).json();}
    async register(config) {
        const r=status(await this.request("/api/auth/register","POST",{userKey:this.key,displayName:this.key,email:this.email,password:this.password,acceptTerms:true,termsVersion:config.termsVersion,privacyVersion:config.privacyVersion}),200,"register");
        this.user=(await r.json()).user;this.csrf=null;let secret;
        for(let i=0;i<80&&!secret;i++) {
            const list=await(await fetch(new URL(`/api/v1/search?query=${encodeURIComponent(`to:${this.email}`)}`,mail))).json();
            for(const entry of list.messages||[]) {
                const message=await(await fetch(new URL(`/api/v1/message/${entry.ID}`,mail))).json();
                for(const candidate of message.Text.match(/https?:\/\/[^\s<>"']+/g)||[]) {
                    const link=new URL(candidate);assert.equal(link.origin,"http://localhost:5100");
                    if(link.searchParams.get("mode")==="confirm") {const p=new URLSearchParams(link.hash.slice(1));secret={userId:Number(p.get("userId")),token:p.get("token")};}
                }
            }
            if(!secret)await new Promise(resolve=>setTimeout(resolve,500));
        }
        assert.ok(secret,"local SMTP delivered");status(await this.request("/api/auth/confirm-email","POST",secret),200,"confirm");this.csrf=null;
        status(await this.request("/api/auth/login","POST",{userKey:this.key,password:this.password}),200,"login");this.csrf=null;
    }
}
const owner=new Actor("owner"),member=new Actor("member"),helper=new Actor("helper"),outsider=new Actor("outside");
let teamId=null;
try {
    for (let i=0;i<40;i++) {
        try { if ((await fetch(new URL("/health/ready",app),{signal:AbortSignal.timeout(1000)})).ok) break; } catch {}
        await new Promise(resolve=>setTimeout(resolve,250));
    }
    status(await fetch(new URL("/health/ready",app)),200,"ready");
    const config=await owner.json("/api/auth/config");
    for(const actor of [owner,member,helper,outsider])await actor.register(config);
    const team=await(status(await owner.request("/api/teams","POST",{name:"Companion QA"}),201,"team")).json();teamId=team.team.id;
    for(const actor of [member,helper])status(await actor.request("/api/teams/join","POST",{inviteCode:team.inviteCode}),200,"join");
    const tasks=`/api/teams/${teamId}/tasks`,root=`/api/teams/${teamId}/companion`;
    const task=await(status(await owner.request(tasks,"POST",{title:"Companion workflow",status:"Doing",tags:"art",assigneeUserProfileId:member.user.id,checklist:[{text:"Review",isCompleted:false}]}),201,"task")).json();
    const path=`${root}/tasks/${task.id}`;
    const change=async(actor,action,values={},expected=200)=>{const before=await actor.json(path);const r=status(await actor.request(path,"POST",{action,version:before.version,expectedUpdatedAt:before.updatedAt,...values}),expected,action);return r.ok?r.json():null;};
    status(await outsider.request(root),404,"outsider dashboard");status(await outsider.request(`${root}/notes?q=art`),404,"outsider notes");status(await outsider.request(path),404,"outsider task");
    status(await owner.request(`/api/companion/tasks/${task.id}`),404,"wrong scope");
    status(await owner.request(path,"POST",{},true),400,"CSRF enforced");
    let current=await change(owner,"savepoint_save",{nextStep:"owner secret",summary:"up to here",resourceUrl:"https://example.test/ref"});
    // Use the POST timestamp directly, without GET: catches database precision bugs.
    current=await(status(await owner.request(path,"POST",{action:"savepoint_save",nextStep:"owner private updated",version:current.version,expectedUpdatedAt:current.updatedAt}),200,"immediate timestamp roundtrip")).json();
    await change(member,"savepoint_save",{nextStep:"member private"});
    assert.equal((await owner.json(path)).savepoint.nextStep,"owner private updated");
    assert.ok(!JSON.stringify(await member.json(root)).includes("owner private"));
    assert.ok(!JSON.stringify(await owner.json(`${tasks}/${task.id}`)).includes("private"));checks+=2;
    await change(owner,"savepoint_save",{nextStep:"bad",resourceUrl:"javascript:alert(1)"},400);
    let h=(await change(member,"help_open",{kind:"review",message:"Please review"})).help;
    current=await helper.json(path);
    const offers=await Promise.all([owner,helper].map(actor=>actor.request(path,"POST",{action:"help_offer",entryId:h.id,version:current.version,expectedUpdatedAt:current.updatedAt})));
    assert.deepEqual(offers.map(r=>r.status).sort(),[200,409]);checks++;
    await change(member,"help_resolve",{entryId:h.id});
    await change(member,"help_open",{kind:"decision",message:"Second question"});
    assert.equal((await owner.json(root)).recentThanks.length,1);
    h=(await owner.json(path)).help;await change(member,"help_cancel",{entryId:h.id});
    let handoff=(await change(owner,"handoff_send",{recipientId:member.user.id,message:"Make three faces",criteria:"3 PNGs",resourceUrl:"https://example.test/design"})).handoff;
    await change(helper,"handoff_accept",{entryId:handoff.id},403);
    await change(member,"handoff_question",{entryId:handoff.id,message:"What size?"});
    handoff=(await change(owner,"handoff_send",{recipientId:member.user.id,message:"32 px",criteria:"3 PNGs"})).handoff;
    await change(member,"handoff_accept",{entryId:handoff.id});
    const n=(await change(member,"note_add",{tried:"Flood fill",learned:"Keep white fur",nextStep:"Use edge mask"})).notes[0];
    assert.equal((await owner.json(`${root}/notes?tags=ART`)).length,1);
    assert.equal((await owner.json(`${root}/notes?tags=artist`)).length,0);
    assert.equal((await owner.json("/api/companion/notes?q=fur")).length,0);
    await change(helper,"note_delete",{entryId:n.id},403);
    await change(member,"note_edit",{entryId:n.id,tried:"Mask",learned:"Transparent edge works"});
    await change(owner,"showcase_save",{kind:"frame",title:"Art",summary:"Ready"},409);
    current=await owner.json(`${tasks}/${task.id}`);
    assert.equal(current.status,"Doing");assert.equal(current.assigneeUserProfileId,member.user.id);assert.equal((await owner.json("/api/pet")).totalExperience,0);
    current=await(status(await owner.request(`${tasks}/${task.id}`,"PUT",{...current,status:"Done",isCompleted:true}),200,"complete")).json();
    await change(member,"showcase_save",{kind:"monitor",title:"First release",summary:"Works",resourceUrl:"https://example.test/demo"});
    assert.equal((await owner.json(root)).showcase.length,1);assert.equal((await member.json("/api/pet")).totalExperience,25);
    current=await owner.json(`${tasks}/${task.id}`);
    const removed=status(await owner.request(`${tasks}/${task.id}?version=${current.version}`,"DELETE"),204,"delete");
    const restored=await(status(await owner.request(`${tasks}/undo/${removed.headers.get("X-Task-Undo")}`,"POST"),200,"Undo")).json();
    assert.equal(restored.id,task.id);assert.equal((await member.json(path)).savepoint.nextStep,"member private");assert.equal((await owner.json(root)).showcase.length,1);assert.equal((await member.json("/api/pet")).totalExperience,25);
    current=await owner.json(`${tasks}/${task.id}`);
    const reopened=await(status(await member.request(`${tasks}/${task.id}`,"PUT",{...current,status:"Todo",isCompleted:false}),200,"reopen")).json();
    assert.equal((await owner.json(root)).showcase.length,0);
    status(await member.request(`${tasks}/undo/${reopened.undo.token}`,"POST"),200,"restore done");
    assert.equal((await owner.json(root)).showcase.length,1);assert.equal((await member.json("/api/pet")).totalExperience,25);
    await change(owner,"handoff_send",{recipientId:member.user.id,message:"Follow up",criteria:"review"});
    status(await member.request(`/api/teams/${teamId}/members/me`,"DELETE"),204,"leave");
    assert.equal((await owner.json(path)).handoff.status,"cancelled");status(await member.request(path),404,"former member cannot read");
    status(await member.request("/api/user","DELETE"),204,"delete member");member.user=null;
    const anonymized=await owner.json(path);assert.equal(anonymized.notes[0].authorId,null);assert.equal(anonymized.showcase.authorId,null);
    console.log(JSON.stringify({passed:checks,apiRequests:requests,realPostgres:true,features:5}));
} finally {
    if(teamId)status(await owner.request(`/api/teams/${teamId}`,"DELETE"),204,"delete test team");
    for(const actor of [owner,member,helper,outsider])if(actor.user)status(await actor.request("/api/user","DELETE"),204,"delete test account");
}
