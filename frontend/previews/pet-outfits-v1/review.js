(() => {
  const species = document.getElementById('species');
  const board = document.getElementById('cards');
  const names = { hat: '帽子', bow: 'リボン', mat: 'クッション' };
  const kinds = ['hat', 'bow', 'mat'];
  let revision = 0;
  function show() {
    const current = ++revision;
    const selected = OutfitCatalog.filter(item => item.species === species.value)
      .sort((a, b) => kinds.indexOf(a.kind) - kinds.indexOf(b.kind) || a.level - b.level);
    board.replaceChildren();
    const loads = selected.map(item => {
      const card = document.createElement('article'); card.className = 'card'; card.dataset.key = item.key;
      const link = document.createElement('a'); link.href = item.src; link.target = '_blank'; link.rel = 'noopener';
      const img = new Image(); img.alt = `${species.selectedOptions[0].textContent}・Lv.${item.level} ${item.name}`;
      img.draggable = false; link.append(img);
      const caption = document.createElement('div'); caption.className = 'caption';
      const label = document.createElement('small'); label.textContent = `${names[item.kind]} · Lv.${item.level}`;
      const title = document.createElement('h2'); title.textContent = item.name;
      caption.append(label, title); card.append(link, caption); board.append(card);
      return new Promise(resolve => {
        img.onload = () => resolve(true);
        img.onerror = () => { card.classList.add('missing'); resolve(false); };
        img.src = item.src;
      });
    });
    document.getElementById('status').textContent = '画像を確認中…';
    Promise.all(loads).then(result => {
      if (current !== revision) return;
      document.getElementById('status').textContent = `${result.filter(Boolean).length} / ${selected.length} 枚を表示`;
    });
  }
  species.addEventListener('change', show);
  document.getElementById('background').addEventListener('change', event => { document.body.dataset.background = event.target.value; });
  show();
})();
