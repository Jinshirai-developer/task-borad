// Review only: no auth, task APIs, localStorage, or writes to application data.
const background = document.getElementById("background");
const motion = document.getElementById("motion");
const demo = document.getElementById("demo-cat");
let endInteraction;
background.addEventListener("change", () => { document.body.dataset.background = background.value; });
motion.addEventListener("change", () => { document.body.dataset.motion = motion.checked ? "on" : "off"; });
document.body.dataset.motion = "on";
document.querySelectorAll("[data-action]").forEach(button => button.addEventListener("click", () => {
    clearTimeout(endInteraction);
    const action = button.dataset.action;
    demo.src = document.getElementById(action === "pet" ? "sample-pet" : "sample-idle").src;
    demo.dataset.action = "";
    // Restart only the transparent character's animation. The parent never transforms.
    void demo.offsetWidth;
    demo.dataset.action = action;
    document.getElementById("demo-message").textContent = {
        pet: "猫だけが、うれしそうに少し反応します。",
        treat: "おやつの仮モーション：猫だけが小さく上下します。",
        rest: "休憩の仮モーション：猫だけがゆっくり呼吸します。"
    }[action];
    endInteraction = setTimeout(() => { demo.dataset.action = ""; demo.src = "cat-idle.png"; }, action === "rest" ? 5000 : 3600);
}));

// Read-only alpha audit. Nothing is composited into a saved or replacement asset.
window.alphaAudit = Promise.all(Array.from(document.querySelectorAll(".sample img")).map(async img => {
    await img.decode();
    const canvas = document.createElement("canvas");
    canvas.width = img.naturalWidth; canvas.height = img.naturalHeight;
    const context = canvas.getContext("2d", { willReadFrequently: true });
    context.drawImage(img, 0, 0);
    const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
    let transparent = 0, opaque = 0, visible = 0, border = 0, borderTransparent = 0;
    for (let i = 3; i < pixels.length; i += 4) {
        if (pixels[i] === 0) transparent++;
        if (pixels[i] === 255) opaque++;
        if (pixels[i] >= 128) visible++;
        const n = (i - 3) / 4, x = n % canvas.width, y = Math.floor(n / canvas.width);
        if (x === 0 || y === 0 || x === canvas.width - 1 || y === canvas.height - 1) { border++; if (pixels[i] === 0) borderTransparent++; }
    }
    const total = canvas.width * canvas.height;
    return { id: img.id, width: canvas.width, height: canvas.height, transparent, opaque, visible, total, borderTransparent, border,
        valid: transparent / total > .25 && visible > total * .1 && borderTransparent / border > .99 };
})).then(results => {
    const valid = results.every(result => result.valid);
    for (const result of results) {
        const img = document.getElementById(result.id);
        img.dataset.transparent = String(result.valid);
        img.closest(".sample").classList.toggle("needs-transparency", !result.valid);
    }
    document.querySelector('[data-action="pet"]').disabled = !results.find(result => result.id === "sample-pet")?.valid;
    const report = document.getElementById("alpha-report");
    report.textContent = valid ? "透過データ確認済み：4枚とも背景に実際の透明ピクセルがあります。背景を切り替えて輪郭を確認できます。" : "透過調整中の素材があります。該当画像の動きは停止しています。この状態では完成素材として採用しません。";
    report.classList.toggle("is-error", !valid);
    return results;
}).catch(error => {
    const report = document.getElementById("alpha-report");
    report.textContent = "画像を読み込めませんでした。生成中、またはプレビューファイルを確認してください。";
    report.classList.add("is-error");
    return [{ valid: false, error: String(error) }];
});
