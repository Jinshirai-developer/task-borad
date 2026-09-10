const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../frontend/desktop-return.js'), 'utf8');
function run(pending, search = '') {
    const result = { destination: null, cleared: false };
    vm.runInNewContext(source, {
        URLSearchParams, Date,
        location: { search, replace: value => { result.destination = value; } },
        sessionStorage: { getItem: () => JSON.stringify(pending), removeItem: () => { result.cleared = true; } }
    });
    return result;
}
test('desktop login returns only to the fixed confirmation page', () => {
    const result = run({ code: 'ABCD2345', expires: Date.now() + 300000, returnUrl: 'https://attacker.example/' });
    assert.equal(result.destination, 'desktop.html#code=ABCD2345');
});
test('expired or malformed desktop requests cannot redirect the user', () => {
    for (const pending of [null, { code: 'ABCD2345', expires: Date.now() - 1 },
        { code: '//attacker.example/', expires: Date.now() + 5000 }, { code: 'ABCD2345', expires: Date.now() + 700000 }])
        assert.equal(run(pending).destination, null);
});
test('desktop return state never interrupts the offline demo', () => {
    assert.equal(run({ code: 'ABCD2345', expires: Date.now() + 300000 }, '?demo=1').destination, null);
});
