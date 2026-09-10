// One generated asset per species / level / slot. No recoloring or stretch-to-fit.
const PetRewardArt = (() => {
    const fits = {
        dog: { x:64, hat:[34,29,27,21], bow:[56,48,46,39], feet:[88,86,91,74], matX:60, width:34, bowWidth:26 },
        cat: { x:52, hat:[32,27,24,22], bow:[54,52,49,40], feet:[91,91,91,74], matX:54, width:29, bowWidth:25 },
        rabbit: { x:56, hat:[48,35,34,31], bow:[69,62,61,52], feet:[93,92,91,82], matX:56, width:24, bowWidth:24 },
        fox: { x:50, hat:[32,27,25,21], bow:[61,62,58,51], feet:[94,94,96,87], matX:55, width:28, bowWidth:26 },
        panda: { x:52, hat:[32,26,21,17], bow:[66,59,56,44], feet:[92,95,94,78], matX:52, width:31, bowWidth:30 },
        dragon: { x:43, hat:[34,30,26,17], bow:[61,57,53,46], feet:[94,94,97,90], matX:53, width:18, bowWidth:24 }
    };
    function create(species, level, kind) {
        if (!Object.hasOwn(fits, species) || !Number.isInteger(level) || level < 1 || level > 5 || !['hat','bow','mat'].includes(kind)) return null;
        const key = `${species}_${level}_${kind}`;
        const box = typeof PetRewardMetrics === 'undefined' ? null : PetRewardMetrics[key];
        if (!box) return null;
        const el = document.createElement('span');
        el.className = `pet-reward-sprite reward-${kind}`;
        el.dataset.reward = key;
        el.setAttribute('aria-hidden', 'true');
        el.style.setProperty('--art-ratio', box.ratio);
        const img = document.createElement('img');
        img.src = `assets/pet/rewards-v2/${species}/lv-${level}-${kind}.png`;
        img.alt = ''; img.draggable = false;
        img.style.width = `${100 / box.width}%`;
        img.style.height = `${100 / box.height}%`;
        img.style.left = `${-100 * box.x / box.width}%`;
        img.style.top = `${-100 * box.y / box.height}%`;
        el.append(img);
        return el;
    }
    function fit(el, species, row, column, kind, catCutout = false) {
        const base = fits[species] || fits.dog;
        const sadX = { dog:54, cat:52, rabbit:49, fox:51, panda:52, dragon:47 };
        const x = catCutout ? 43 : species === 'dragon' && kind === 'hat' ? 50 : column === 5 ? sadX[species] : base.x;
        const ratio = Number(el.style.getPropertyValue('--art-ratio'));
        const bottom = catCutout ? 24 : base.hat[row] - (column === 3 ? 3 : 0) + (column === 5 && species !== 'rabbit' ? 5 : 0);
        const availableHeight = kind === 'hat' ? Math.max(8, bottom - 2) : kind === 'mat' ? 37 : 19;
        const desiredWidth = kind === 'hat' ? (catCutout ? 28 : base.width) : kind === 'bow' ? (catCutout ? 22 : base.bowWidth) : 78;
        const width = Math.min(desiredWidth, availableHeight * ratio), height = width / ratio;
        const feet = catCutout ? 91 : 5 + .9 * base.feet[row];
        el.style.width = `${width}%`; el.style.height = `${height}%`;
        el.style.left = `${(kind === 'mat' ? base.matX : x) - width / 2}%`;
        el.style.top = `${kind === 'hat' ? bottom - height : kind === 'bow' ? (catCutout ? 50 : base.bow[row] + (column === 5 ? 4 : 0)) : feet - height * .86}%`;
    }
    return { create, fit };
})();
