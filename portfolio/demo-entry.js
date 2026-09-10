// Distribution-only adapter. The application's real server and Windows bundle are unchanged.
document.addEventListener('DOMContentLoaded', () => {
    const notice = document.querySelector('.demo-notice');
    if (notice) {
        const link = notice.querySelector('a');
        if (link) { link.href = '../'; link.textContent = '紹介ページに戻る'; }
    }
    const end = document.getElementById('user-logout-button');
    if (end) end.addEventListener('click', event => {
        event.stopImmediatePropagation(); location.assign('../');
    }, true);
});
