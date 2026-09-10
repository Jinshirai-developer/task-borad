import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createWorker, parseRange } from './worker.mjs';
const base = '/p/review-link/';
const parts = [{key:'part1',size:3},{key:'part2',size:4},{key:'part3',size:3}];
const config = { base, readyKey:'ready', release:{ name:'windows.zip',size:10,sha256:'abc',parts } };
const files = { 'part1':'012','part2':'3456','part3':'789', 'ready':'ready', 'picture':'PNG' };
function fixture(ready = true) {
    const writes = [];
    const env = { FILES: {
        async head(key) { if (key === 'ready' && !ready) return null; return Object.hasOwn(files,key) ? {size:files[key].length} : null; },
        async get(key, options) { if (!Object.hasOwn(files,key)) return null; const range=options?.range; let body=files[key]; if(range)body=body.slice(range.offset,range.offset+range.length); return {body:new Response(body).body}; },
        async put(...args) { writes.push(args); }
    } };
    const worker = createWorker(config, {'index.html':{content:'Landing',type:'text/html'},'demo/index.html':{content:'Demo',type:'text/html'}}, {'board.png':{key:'picture',sha256:'a'.repeat(64),size:3,type:'image/png'},'_windows/0':{key:'part1',sha256:'b'.repeat(64),size:3,type:'application/octet-stream'}});
    const call=(path,options={}) => worker.fetch(new Request('https://site.test'+path,options),env);
    return {call,env,writes};
}
test('root and guessed links reveal neither content nor file objects', async()=>{
    const {call}=fixture();
    for(const url of ['/','/index.html','/p/wrong/','/api/user',base+'_windows/0',base+'objects/picture',base+'../index.html']) assert.equal((await call(url)).status,404,url);
    const good=await call(base);assert.equal(await good.text(),'Landing');assert.match(good.headers.get('X-Robots-Tag'),/noindex/);assert.equal(good.headers.get('Referrer-Policy'),'no-referrer');
});
test('demo always enters sample mode and account routes return to landing',async()=>{
    const {call}=fixture();
    for(const page of ['demo/','demo/index.html']) { const r=await call(base+page+'?demo=0'); assert.equal(r.status,302);assert.equal(r.headers.get('Location'),base+'demo/index.html?demo=1'); }
    assert.equal(await (await call(base+'demo/index.html?demo=1')).text(),'Demo');
    for(const page of ['login','google','auth','desktop'])assert.equal((await call(base+`demo/${page}.html`)).headers.get('Location'),base);
    for(const page of ['terms','privacy'])assert.equal((await call(base+`demo/${page}.html`)).headers.get('Location'),base+'about#about-demo');
    assert.equal((await call('/api/tasks',{method:'POST',body:'private data'})).status,405);
});
test('download streams byte-identical data across all part boundaries',async()=>{
    const {call}=fixture();
    const r=await call(base+'download/windows');assert.equal(r.status,200);assert.equal(r.headers.get('Content-Length'),'10');assert.equal(r.headers.get('Content-Disposition'),'attachment; filename="windows.zip"');assert.equal(await r.text(),'0123456789');
    for(const [range,expected] of [['bytes=2-7','234567'],['bytes=0-0','0'],['bytes=9-','9'],['bytes=-3','789'],['bytes=0-999','0123456789']]){const r=await call(base+'download/windows',{headers:{Range:range}});assert.equal(r.status,206);assert.equal(await r.text(),expected);assert.equal(r.headers.get('Content-Length'),String(expected.length));}
    const head=await call(base+'download/windows',{method:'HEAD'});assert.equal(head.status,200);assert.equal(await head.text(),'');assert.equal(head.headers.get('Content-Length'),'10');
});
test('invalid and multipart ranges cannot trigger accidental large downloads',async()=>{
    for(const header of ['bytes=10-','bytes=4-2','bytes=-0','bytes=','bytes=0-1,4-5','items=1-2','bytes=999999999999999999999999-'])assert.equal(parseRange(header,10),null,header);
    const {call}=fixture();const r=await call(base+'download/windows',{headers:{Range:'bytes=10-'}});assert.equal(r.status,416);assert.equal(r.headers.get('Content-Range'),'bytes */10');
});
test('download does not advertise an incomplete release as ready',async()=>{
    const {call}=fixture(false);const r=await call(base+'download/windows');assert.equal(r.status,503);assert.equal(r.headers.get('Retry-After'),'60');
});
test('upload requires a configured, unexpired secret and a predeclared object hash',async()=>{
    const {call,env,writes}=fixture();const pathname='/_release-assets/'+'a'.repeat(64);
    assert.equal((await call(pathname,{method:'PUT',body:'PNG'})).status,404);
    env.RELEASE_UPLOAD_TOKEN='release-test-secret';env.RELEASE_UPLOAD_EXPIRES=String(Date.now()+60000);
    assert.equal((await call(pathname,{method:'PUT',headers:{Authorization:'Bearer wrong'},body:'PNG'})).status,404);
    const headers={Authorization:'Bearer release-test-secret','Content-Length':'3'};
    assert.equal((await call('/_release-assets/unknown',{method:'PUT',headers,body:'PNG'})).status,404);
    assert.equal((await call(pathname,{method:'PUT',headers:{...headers,'Content-Length':'9'},body:'PNG'})).status,400);
    assert.equal((await call(pathname,{method:'PUT',headers,body:'PNG'})).status,200);assert.equal(writes.length,1);assert.equal(writes[0][0],'picture');assert.equal(writes[0][2].sha256,'a'.repeat(64));
    env.RELEASE_UPLOAD_EXPIRES=String(Date.now()-1);assert.equal((await call(pathname,{method:'PUT',headers,body:'PNG'})).status,404);assert.equal(writes.length,1);
});
test('Mac entry grants link access and anonymous root APIs remain hidden',async()=>{
    const {call,env}=fixture();
    env.MAC_SERVER_ORIGIN='https://upstream.example.test';env.MAC_SERVER_PROXY_KEY='server-secret';
    for(const path of ['/','/login.html','/api/auth/config','/signin-google'])assert.equal((await call(path)).status,404);
    const entry=await call(base);assert.equal(entry.status,302);assert.equal(entry.headers.get('Location'),'/login.html?mode=register');
    assert.match(entry.headers.get('Set-Cookie'),/Secure; HttpOnly; SameSite=Lax/);
    const mail=await call(base+'auth.html?mode=confirm');assert.equal(mail.headers.get('Location'),'/auth.html?mode=confirm');
    assert.equal(await (await call(base+'about')).text(),'Landing');
    assert.equal(await (await call(base+'download/windows')).text(),'0123456789');
});
test('Mac proxy forwards only app cookies and trusted gateway headers, preserving CSRF',async()=>{
    const {call,env}=fixture();env.MAC_SERVER_ORIGIN='https://upstream.example.test';env.MAC_SERVER_PROXY_KEY='server-secret';
    const access=(await call(base)).headers.get('Set-Cookie').split(';')[0];
    const original=globalThis.fetch;let seen;
    globalThis.fetch=async(url,options)=>{
        seen={url,options};
        const headers=new Headers({'Content-Type':'application/json','Content-Security-Policy':"connect-src 'self'"});
        headers.append('Set-Cookie','__Host-TaskBoard.Auth=chunks-2; Path=/; Secure; HttpOnly');
        headers.append('Set-Cookie','__Host-TaskBoard.AuthC1=first; Path=/; Secure; HttpOnly');
        headers.append('Set-Cookie','__Host-TaskBoard.AuthC2=second; Path=/; Secure; HttpOnly');
        headers.append('Set-Cookie','unrelated_platform_session=private; Secure');
        return new Response('{"ok":true}',{headers});
    };
    try{
        const r=await call('/api/auth/login',{method:'POST',headers:{
            Cookie:access+'; __Host-TaskBoard.Csrf=csrf; __Host-TaskBoard.ExternalC1=external; site_session=private',
            'Content-Type':'application/json','X-CSRF-TOKEN':'csrf-token','Origin':'https://site.test',
            'X-TaskBoard-Proxy-Key':'forged','X-TaskBoard-Client-IP':'forged','CF-Connecting-IP':'203.0.113.5'
        },body:'{"userKey":"reviewer"}'});
        assert.equal(r.status,200);assert.equal(seen.url,'https://upstream.example.test/api/auth/login');
        assert.equal(seen.options.headers.get('Cookie'),'__Host-TaskBoard.Csrf=csrf; __Host-TaskBoard.ExternalC1=external');
        assert.equal(seen.options.headers.get('X-CSRF-TOKEN'),'csrf-token');
        assert.equal(seen.options.headers.get('X-TaskBoard-Proxy-Key'),'server-secret');
        assert.equal(seen.options.headers.get('X-TaskBoard-Client-IP'),'203.0.113.5');
        assert.equal(seen.options.redirect,'manual');
        assert.equal(await new Response(seen.options.body).text(),'{"userKey":"reviewer"}');
        assert.equal(r.headers.getSetCookie().length,3);assert.match(r.headers.getSetCookie()[0],/^__Host-TaskBoard.Auth=/);
        assert.match(r.headers.getSetCookie()[1],/^__Host-TaskBoard.AuthC1=/);assert.match(r.headers.getSetCookie()[2],/^__Host-TaskBoard.AuthC2=/);
        assert.equal(r.headers.get('Content-Security-Policy'),"connect-src 'self'");
        assert.equal(r.headers.get('Cache-Control'),'no-store');
        assert.equal((await call('/api/auth/login',{method:'POST',headers:{Cookie:access,Origin:'https://other.test'}})).status,403);
    }finally{globalThis.fetch=original;}
});
test('Mac outage leaves the independent sample and Windows download available',async()=>{
    const {call,env}=fixture();env.MAC_SERVER_ORIGIN='https://upstream.example.test';env.MAC_SERVER_PROXY_KEY='server-secret';
    const access=(await call(base)).headers.get('Set-Cookie').split(';')[0];
    const original=globalThis.fetch;globalThis.fetch=async()=>{throw new Error('offline');};
    try{
        assert.equal((await call('/api/auth/session',{headers:{Cookie:access}})).status,503);
        assert.equal((await call('/login.html',{headers:{Cookie:access}})).status,503);
        assert.equal(await (await call(base+'demo/index.html?demo=1')).text(),'Demo');
        assert.equal(await (await call(base+'download/windows')).text(),'0123456789');
    }finally{globalThis.fetch=original;}
});
test('canonical migration moves old app and email links but preserves distribution',async()=>{
    const {call,env}=fixture();env.MAC_SERVER_ORIGIN='https://upstream.example.test';env.MAC_SERVER_PROXY_KEY='server-secret';
    const access=(await call(base)).headers.get('Set-Cookie').split(';')[0];
    env.CANONICAL_APP_ORIGIN='https://taskboard.example.test';
    assert.equal((await call(base)).headers.get('Location'),'https://taskboard.example.test/');
    assert.equal((await call(base+'auth.html?mode=confirm')).headers.get('Location'),'https://taskboard.example.test/auth.html?mode=confirm');
    assert.equal((await call('/index.html',{headers:{Cookie:access}})).headers.get('Location'),'https://taskboard.example.test/index.html');
    assert.equal((await call('/api/tasks',{method:'POST',headers:{Cookie:access},body:'private data'})).status,409);
    assert.equal((await call('/api/tasks')).status,404);
    assert.equal(await (await call(base+'about')).text(),'Landing');
    assert.equal(await (await call(base+'demo/index.html?demo=1')).text(),'Demo');
    assert.equal(await (await call(base+'download/windows')).text(),'0123456789');
    env.CANONICAL_APP_ORIGIN='http://invalid.test';
    assert.equal((await call(base)).status,503);
});
