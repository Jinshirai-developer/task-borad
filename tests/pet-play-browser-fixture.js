
window.uiErrors=[];addEventListener('error',e=>uiErrors.push(e.message));addEventListener('unhandledrejection',e=>uiErrors.push(String(e.reason)));
const originalFetch=fetch;
const defs={private:['プログラマー','アーティスト','プランナー'],team:['チーム分類']};
const tasks={private:Array.from({length:9},(_,i)=>({id:i+1,title:['認証APIを作る','画面の動きを整える','キャラクターを描く','分類機能を実装する','チームで仕様をレビューする','APIのテストを書く','公開前に確認する','完了タスクを振り返る','ポートフォリオの説明を書く'][i],description:'自由なタグで役割や目的を分け、複数のタグを組み合わせます。',status:i<3?0:i<7?1:2,priority:1,tags:i===0?'プログラマー,api':i===1?'プログラマー,アーティスト':[3,4,5].includes(i)?'プログラマー':i===2?'アーティスト':i===6?'API-v2':null,version:1})),team:[{id:42,title:'共有タスク',status:0,priority:1,tags:'チーム分類',version:1}]};
const required={pets:{dog:1,cat:1,rabbit:1,fox:1,panda:1,dragon:1},themes:{classic:1,light:1,dark:1,retro:1,forest:1,sunset:1},layouts:{board:1,list:1,compact:1,gallery:1,focus:1}};
const catalog={level:7,totalExperience:1350,...Object.fromEntries(Object.entries(required).map(([kind,entries])=>[kind,Object.entries(entries).map(([id,requiredLevel])=>({id,name:id,requiredLevel,unlocked:true,experienceRemaining:0}))]))};
const pet={name:'モカ',species:'dog',level:7,totalExperience:1350,experience:0,experienceToNextLevel:400,experienceProgress:0,energy:90,title:'頼れる相棒',mood:'Working',completedTaskCount:54,streakDays:3,achievements:[]};
let preferences={theme:'classic',layout:'board'};
const tokens=t=>String(t.tags||'').split(',').map(v=>v.trim().toLowerCase()).filter(Boolean);
window.fixture={tasks,defs,requests:[],holdPrivateTags:false};
const tagCatalog=scope=>{const names=[...defs[scope]];for(const t of tasks[scope])for(const n of String(t.tags||'').split(',').map(v=>v.trim()).filter(Boolean))if(!names.some(v=>v.toLowerCase()===n.toLowerCase()))names.push(n);return {items:names.map(name=>{const matches=tasks[scope].filter(t=>tokens(t).includes(name.toLowerCase()));return {name,total:matches.length,todo:matches.filter(t=>t.status===0).length,doing:matches.filter(t=>t.status===1).length,done:matches.filter(t=>t.status===2).length}}),totalTasks:tasks[scope].length,untaggedTasks:tasks[scope].filter(t=>!tokens(t).length).length,registeredTags:defs[scope].length,maxRegisteredTags:50}};
window.fetch=async(url,options={})=>{const address=new URL(url,location.href),path=address.pathname,p=address.searchParams,method=options.method||'GET';const ok=(data,status=200)=>new Response(JSON.stringify(data),{status,headers:{'Content-Type':'application/json'}});if(path.startsWith('/api/'))fixture.requests.push({path,query:address.search,method,body:options.body});
if(path==='/api/auth/csrf')return ok({token:'mock-tags-only'});
if(path==='/api/user')return ok({userKey:'tags_demo',displayName:'タグ分類デモ'});
if(path==='/api/user/unlocks')return ok(catalog);
if(path==='/api/user/preferences'){if(method==='PUT')preferences=JSON.parse(options.body);return ok(preferences);}
if(path==='/api/pet')return ok(pet);
if(path==='/api/teams')return ok([{id:42,name:'制作チーム',role:'owner',memberCount:1}]);
if(path==='/api/teams/42/summary')return ok({total:tasks.team.length,todo:tasks.team.filter(t=>t.status===0).length,doing:0,done:0,overdue:0});
if(path.endsWith('/task-tags')){const scope=path.includes('/teams/')?'team':'private';if(method==='POST'){const {name}=JSON.parse(options.body);if(defs[scope].includes(name))return ok({message:'同じタグが登録済みです。'},409);defs[scope].push(name);return ok({name},201);}const result=tagCatalog(scope);if(scope==='private'&&fixture.holdPrivateTags){fixture.holdPrivateTags=false;return new Promise(resolve=>{window.resolvePrivateTags=()=>resolve(ok(result));});}return ok(result);}
if(path==='/api/tasks'||path==='/api/teams/42/tasks'){const scope=path.includes('/teams/')?'team':'private';if(method==='POST'){const task={...JSON.parse(options.body),id:Math.max(...tasks[scope].map(t=>t.id))+1,version:1};tasks[scope].push(task);return ok(task,201);}let items=tasks[scope].filter(t=>(!p.has('tagExact')||tokens(t).includes(p.get('tagExact').toLowerCase()))&&(!p.has('untagged')||!tokens(t).length)&&(!p.has('search')||t.title.includes(p.get('search'))));const totalCount=items.length;const page=Number(p.get('page')||1),pageSize=Number(p.get('pageSize')||100);return ok({items:items.slice((page-1)*pageSize,page*pageSize),totalCount,page,pageSize,totalPages:Math.ceil(totalCount/pageSize)});}
if(/^\/api\/(?:teams\/42\/)?tasks\/\d+$/.test(path)){const scope=path.includes('/teams/')?'team':'private',id=Number(path.split('/').pop()),i=tasks[scope].findIndex(t=>t.id===id);if(method==='PUT'){tasks[scope][i]={...tasks[scope][i],...JSON.parse(options.body),version:tasks[scope][i].version+1};return ok(tasks[scope][i]);}if(method==='DELETE'){tasks[scope].splice(i,1);return new Response(null,{status:204});}}
if(path.startsWith('/api/'))return ok({},404);
return originalFetch(url,options);
};

// Isolated, deterministic collection API. Never touches the running preview.
const collectionFetch = window.fetch;
const displayNames={dog:'いぬ',cat:'ねこ',rabbit:'うさぎ',fox:'きつね',panda:'パンダ',dragon:'ドラゴン',classic:'クラシック',light:'ライト',dark:'ダーク',retro:'Windows風',forest:'フォレスト',sunset:'サンセット',board:'ボード',list:'リスト',compact:'コンパクト',gallery:'ギャラリー',focus:'集中'};
for(const category of Object.keys(required))for(const item of catalog[category])item.name=displayNames[item.id];
const choices = new Map(), memories = new Map();
let appearance = { stage: 'base', hatLevel: null, bowLevel: null, matLevel: null };
window.petFixture = { requests: [], failRead: false, failWrite: false, holdRead: false, holdWrite: false, confirm: true };
const stageDefs = [['base','いつもの相棒',1],['explorer','小さな冒険家',5],['grown','頼れる相棒',10],['festival','星のお祝い姿',20]];
const memoryDefs = [['first','はじめの一歩','初めて完了',0,1],['tasks10','10個の達成','タスクを10個完了',0,3],['tasks50','50個の達成','タスクを50個完了',1,3],['tasks100','100個の達成','タスクを100個完了',2,3],['level5','小さな冒険','Lv.5に到達',1,0],['level10','頼れる相棒','Lv.10に到達',2,0],['level20','星のお祝い','Lv.20に到達',3,3],['pet','なかよしの時間','初めてなでる',0,1],['treat','おやつの時間','初めておやつをあげる',0,2],['rest','おやすみ','初めて休憩する',0,4]];
const collect = () => {
    for(const level of [5,10,20])if(pet.level>=level)memories.set('level'+level,'2026-09-08T01:00:00Z');
    return {level:pet.level,totalExperience:pet.totalExperience,species:pet.species,appearance:{...appearance},maxRewardLevel:5,
        rewards:[1,2,3,4,5,...[...choices.keys()].filter(level=>level>5)].map(level=>({level,isLegacy:level>5,available:level<=pet.level||choices.has(level),claimedChoice:choices.get(level)||null,claimedAt:choices.has(level)?'2026-09-08T01:00:00Z':null,options:[['hat','帽子'],['bow','リボン'],['mat','クッション']].map(([id,name])=>({id,name:`${displayNames[pet.species]} Lv.${level} ${name}`,shape:0,hue:0,image:level<=5?`assets/pet/rewards-v2/${pet.species}/lv-${level}-${id}.png`:null}))})),
        stages:stageDefs.map(([id,name,requiredLevel],row)=>({id,name,requiredLevel,row,available:requiredLevel<=pet.level||memories.has('level'+requiredLevel)})),
        memories:memoryDefs.map(([key,name,description,row,column])=>({key,name,description,row,column,unlockedAt:memories.get(key)||null}))};
};
petFixture.setLevel = level => {
    pet.level=level;pet.totalExperience=(level-1)*(level+2)*25;
    pet.completedTaskCount=pet.totalExperience/25;pet.experienceToNextLevel=(level+1)*50;
    catalog.level=level;catalog.totalExperience=pet.totalExperience;
    for(const key of Object.keys(required))for(const item of catalog[key])item.unlocked=item.requiredLevel<=level;
    collect();
};
window.confirm = () => petFixture.confirm;
window.fetch = async (url,options={}) => {
    const path=new URL(url,location.href).pathname,method=options.method||'GET';
    const ok=(data,status=200)=>new Response(JSON.stringify(data),{status,headers:{'Content-Type':'application/json'}});
    if(path==='/api/pet'&&method==='PUT'){Object.assign(pet,JSON.parse(options.body));return ok(pet);}
    if(!['/api/pet/collection','/api/pet/rewards','/api/pet/appearance','/api/pet/interactions'].includes(path))return collectionFetch(url,options);
    const body=options.body?JSON.parse(options.body):{};
    petFixture.requests.push({path,method,body});
    if(method==='GET'){
        if(petFixture.failRead){petFixture.failRead=false;return ok({message:'読み込み失敗のテスト'},503);}
        const result=collect();
        if(petFixture.holdRead){petFixture.holdRead=false;return new Promise(resolve=>petFixture.releaseRead=()=>resolve(ok(result)));}
        return ok(result);
    }
    if(petFixture.holdWrite){petFixture.holdWrite=false;await new Promise(resolve=>petFixture.releaseWrite=resolve);}
    if(petFixture.failWrite){petFixture.failWrite=false;return ok({message:'保存失敗のテスト'},503);}
    if(path==='/api/pet/rewards'){
        if(body.level>5&&!choices.has(body.level))return ok({message:'ごほうびはLv.5までです'},400);
        if(body.level>pet.level)return ok({message:'必要レベル未満です'},403);
        if(choices.has(body.level)&&choices.get(body.level)!==body.choice)return ok({message:'受け取り済みです'},409);
        choices.set(body.level,body.choice);
    }
    if(path==='/api/pet/appearance')appearance={...body};
    if(path==='/api/pet/interactions')memories.set(body.action,'2026-09-08T01:00:00Z');
    return ok(collect());
};
