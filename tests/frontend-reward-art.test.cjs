const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
function load() {
    const document = { createElement(tag) { return { tag, children:[], dataset:{}, style:{ setProperty(k,v){this[k]=String(v);}, getPropertyValue(k){return this[k];} }, setAttribute(){}, append(child){this.children.push(child);} }; } };
    const context=vm.createContext({document});
    for(const file of ['pet-reward-metrics.js','pet-reward-art.js'])vm.runInContext(fs.readFileSync(path.join(root,'frontend',file),'utf8'),context);
    return expression=>vm.runInContext(expression,context);
}
test('90 species-specific generated rewards exist with normalized alpha bounds',()=>{
    const run=load();
    assert.equal(run('Object.keys(PetRewardMetrics).length'),90);
    const paths=new Set();
    for(const species of ['dog','cat','rabbit','fox','panda','dragon'])for(let level=1;level<=5;level++)for(const kind of ['hat','bow','mat']){
        const art=run(`PetRewardArt.create('${species}',${level},'${kind}')`);
        assert.ok(art);assert.equal(art.children.length,1);
        const image=art.children[0];assert.match(image.src,new RegExp(`/${species}/lv-${level}-${kind}\\.png$`));
        assert.ok(fs.statSync(path.join(root,'frontend',image.src)).size>1000);
        paths.add(image.src);
        const bounds=run(`PetRewardMetrics['${species}_${level}_${kind}']`);
        for(const key of ['x','y','width','height'])assert.ok(bounds[key]>0&&bounds[key]<1);
        assert.ok(bounds.ratio>0);
        assert.ok(bounds.x+bounds.width<=1.000001&&bounds.y+bounds.height<=1.000001);
    }
    assert.equal(paths.size,90);
});
test('invalid species, paths, levels and slots never create image URLs',()=>{
    const run=load();
    for(const input of [['../cat',1,'hat'],['cat',6,'hat'],['cat',0,'hat'],['cat',1.5,'hat'],['cat',1,'__proto__'],['__proto__',1,'hat']])assert.equal(run(`PetRewardArt.create(...${JSON.stringify(input)})`),null);
});
test('every species/stage fit preserves art proportions and frame bounds',()=>{
    const run=load();
    for(const species of ['dog','cat','rabbit','fox','panda','dragon'])for(let row=0;row<4;row++)for(let column of [0,1,3,5])for(let level=1;level<=5;level++)for(const kind of ['hat','bow','mat']){
        const style=run(`(()=>{const e=PetRewardArt.create('${species}',${level},'${kind}');PetRewardArt.fit(e,'${species}',${row},${column},'${kind}',${species==='cat'&&row===0});return e.style})()`);
        const width=parseFloat(style.width),height=parseFloat(style.height),left=parseFloat(style.left),top=parseFloat(style.top);
        assert.ok(width>0&&height>0&&left>=0&&top>=0,`${species}/${row}/${level}/${kind}`);
        assert.ok(left+width<=100.001&&top+height<=100.001);
        assert.ok(Math.abs(width/height-Number(style['--art-ratio']))<.0001);
    }
});
