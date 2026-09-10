// Real PostgreSQL and local SMTP. Never uses Stripe or a user's application host.
import assert from 'node:assert/strict';
const app=new URL('http://task-v17-qa-web:8080'),mail=new URL('http://task-v17-qa-mail:8025');
const publicApp=new URL(process.env.PUBLIC_APP_URL || 'http://localhost:5117');
assert.ok(['http://localhost:5117','http://localhost:5100'].includes(publicApp.origin),'Disposable browser origin only');
let checks=0,requests=0;
const status=(r,n,label)=>{assert.equal(r.status,n,label);checks++;return r;};
class Actor {
    constructor(label){this.key=`v17_${label}_${crypto.randomUUID().slice(0,8)}`;this.email=this.key+'@taskboard.test';this.password='V17-test-'+crypto.randomUUID();this.cookies=new Map();this.csrf=null;}
    async request(path,method='GET',body,retry=0){
        if(method!=='GET'&&!this.csrf)this.csrf=(await(await this.request('/api/auth/csrf')).json()).token;
        const headers={Accept:'application/json',Cookie:[...this.cookies].map(([k,v])=>`${k}=${v}`).join('; ')};
        if(this.csrf)headers['X-CSRF-TOKEN']=this.csrf;if(body!==undefined)headers['Content-Type']='application/json';requests++;
        const r=await fetch(new URL(path,app),{method,headers,body:body===undefined?undefined:JSON.stringify(body),redirect:'error',signal:AbortSignal.timeout(15000)});
        for(const cookie of r.headers.getSetCookie()){const p=cookie.split(';')[0],i=p.indexOf('=');if(i>0){if(p.slice(i+1))this.cookies.set(p.slice(0,i),p.slice(i+1));else this.cookies.delete(p.slice(0,i));}}
        if(r.status===429&&retry<2){console.log('QA rate limit reached; waiting for the next window');await new Promise(resolve=>setTimeout(resolve,60000));return this.request(path,method,body,retry+1);}return r;
    }
    async json(path){return(status(await this.request(path),200,'GET')).json();}
    async register(config){
        this.user=(await(status(await this.request('/api/auth/register','POST',{userKey:this.key,displayName:this.key,email:this.email,password:this.password,acceptTerms:true,termsVersion:config.termsVersion,privacyVersion:config.privacyVersion}),200,'register')).json()).user;this.csrf=null;
        let secret;for(let i=0;i<80&&!secret;i++){
            const list=await(await fetch(new URL('/api/v1/search?query='+encodeURIComponent('to:'+this.email),mail))).json();
            for(const entry of list.messages||[]){const m=await(await fetch(new URL('/api/v1/message/'+entry.ID,mail))).json();for(const candidate of m.Text.match(/https?:\/\/[^\s<>"']+/g)||[]){const link=new URL(candidate);assert.equal(link.origin,publicApp.origin);if(link.searchParams.get('mode')==='confirm'){const p=new URLSearchParams(link.hash.slice(1));secret={userId:Number(p.get('userId')),token:p.get('token')};}}}
            if(!secret)await new Promise(resolve=>setTimeout(resolve,500));
        }assert.ok(secret);status(await this.request('/api/auth/confirm-email','POST',secret),200,'confirm');this.csrf=null;
        status(await this.request('/api/auth/login','POST',{userKey:this.key,password:this.password}),200,'login');this.csrf=null;
    }
}
const actors=['owner','a','b','c'].map(name=>new Actor(name)),[owner,a,b,c]=actors;let teamId=null;
try{
    const config=await owner.json('/api/auth/config');for(const actor of actors)await actor.register(config);
    const team=await(status(await owner.request('/api/teams','POST',{name:'Billing cap QA'}),201,'create')).json();teamId=team.team.id;
    status(await a.request('/api/teams/join','POST',{inviteCode:team.inviteCode}),200,'second seat');
    const joins=await Promise.all([b,c].map(actor=>actor.request('/api/teams/join','POST',{inviteCode:team.inviteCode})));
    assert.deepEqual(joins.map(r=>r.status).sort(),[200,409]);checks++;
    const accepted=joins[0].ok?b:c,rejected=joins[0].ok?c:b,path=`/api/teams/${teamId}/billing`;
    const plan=await owner.json(path);assert.equal(plan.memberCount,3);assert.equal(plan.memberLimit,3);assert.equal(plan.canJoin,false);assert.equal(plan.checkoutAvailable,false);checks+=4;
    status(await accepted.request('/api/teams/join','POST',{inviteCode:team.inviteCode}),200,'idempotent join');
    status(await rejected.request(path),404,'outsider privacy');
    status(await a.request(path+'/checkout','POST'),403,'owner required');
    status(await owner.request(path+'/checkout','POST'),503,'unconfigured must not fake success');
    status(await owner.request(`/api/teams/${teamId}/tasks`,'POST',{title:'Shared work survives cap',status:'Todo'}),201,'task');
    const tasks=await a.json(`/api/teams/${teamId}/tasks`);assert.equal(tasks.totalCount,1);checks++;
    status(await accepted.request(`/api/teams/${teamId}/members/me`,'DELETE'),204,'leave');
    status(await rejected.request('/api/teams/join','POST',{inviteCode:team.inviteCode}),200,'released seat reusable');
    assert.equal((await owner.json(path)).memberCount,3);assert.equal((await owner.json('/api/pet')).totalExperience,0);checks+=2;
    console.log(JSON.stringify({passed:checks,apiRequests:requests,realPostgres:true,stripeConnected:false}));
}finally{
    if(teamId)status(await owner.request(`/api/teams/${teamId}`,'DELETE'),204,'cleanup team');
    for(const actor of actors)if(actor.user)status(await actor.request('/api/user','DELETE'),204,'cleanup account');
}
