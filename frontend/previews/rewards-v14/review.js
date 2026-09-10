const edges = { dog:[0,276,519,749,1024], cat:[0,267,502,737,1024], rabbit:[0,285,525,762,1024], fox:[0,276,517,764,1024], panda:[0,282,516,748,1024], dragon:[0,296,547,785,1024] };
function show() {
    const species=document.getElementById('species').value,row=Number(document.getElementById('stage').value),cat=species==='cat'&&row===0;
    const cards=document.getElementById('cards');cards.replaceChildren();
    for(let level=1;level<=5;level++){
        const card=document.createElement('section');card.className='card';
        const title=document.createElement('h2');title.textContent=`Lv.${level}`;card.append(title);
        const frame=document.createElement('div');frame.className='pet-atlas portrait';frame.dataset.art=cat?'cat-cutout':'atlas-cutout';
        const character=document.createElement('span');character.className='pet-character'+(cat?'':' pet-atlas-character');
        const body=document.createElement(cat?'img':'span');
        if(cat){body.className='pet-cat-image';body.src='assets/pet/portfolio-cat-idle-v3.png';body.alt='猫';}
        else {body.className='pet-atlas-body';body.style.backgroundImage=`url(assets/pet/portfolio-${species}-atlas-v2-alpha.png)`;const height=edges[species][row+1]-edges[species][row];body.style.backgroundSize=`600% ${1024/height*100}%`;body.style.backgroundPosition=`0% ${edges[species][row]/(1024-height)*100}%`;}
        character.append(body);frame.append(character);
        const arts=document.createElement('div');arts.className='arts';
        for(const kind of ['hat','bow','mat']){
            const art=PetRewardArt.create(species,level,kind);
            const thumb=document.createElement('div');thumb.className='pet-gift-art';
            if(art){const copy=art.cloneNode(true);thumb.append(copy);PetRewardArt.fit(art,species,row,0,kind,cat);(kind==='mat'?frame:character).append(art);}
            else thumb.textContent='制作中';
            arts.append(thumb);
        }
        card.append(frame,arts);cards.append(card);
    }
}
document.getElementById('species').onchange=show;document.getElementById('stage').onchange=show;show();
