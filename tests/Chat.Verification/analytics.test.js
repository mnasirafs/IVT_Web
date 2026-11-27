'use strict';
const assert = require('node:assert/strict');
const analytics = require('../../AFSDataGateway/Scripts/ivt-chat-analytics.js');

// Minimal DOM for deterministic rendering/event assertions. No browser, network or database required.
class Element {
    constructor(tag) { this.tagName = tag; this.children = []; this.events = {}; this.style = {}; this.attributes = {}; this._text = ''; this._value = undefined; }
    set textContent(value) { this._text = String(value); this.children = []; }
    get textContent() { return this._text + this.children.map(c => c.textContent).join(''); }
    set value(value) { this._value = value; }
    get value() { return this._value === undefined && this.tagName === 'select' ? this.children[0].value : this._value; }
    setAttribute(key, value) { this.attributes[key] = value; }
    appendChild(child) { this.children.push(child); return child; }
    addEventListener(name, fn) { this.events[name] = fn; }
    click() { if (this.events.click) return this.events.click(); }
    remove() { this.removed = true; }
}
global.document = { createElement: tag => new Element(tag), body: new Element('body') };
function find(root, predicate) { return [root, ...root.children.flatMap(c => find(c, () => true))].filter(predicate); }
const source = { Data: { source: 'IVT dataset currentTrading', scope: 'Account Manager: AMONE', reportingWeek: '2026-11-23', result: {
    dataset: 'currentTrading', rowMeaning: 'One row per consignment line.',
    appliedFilters: [{ Field: 'customerOrGroup', Op: 'equals', Values: ['CUSTOMER'] }],
    result: { groupBy: ['carrier'], metrics: ['sum(buyTotalNett)', 'countDistinct(connote)'], truncated: true,
        truncationNote: 'More groups exist beyond those shown.', groups: [
            { carrier: 'Carrier A', 'sum(buyTotalNett)': 2400, 'countDistinct(connote)': 20 },
            { carrier: '<img src=x onerror=alert(1)>', 'sum(buyTotalNett)': -1200, 'countDistinct(connote)': 10 },
            { carrier: 'Unknown', 'sum(buyTotalNett)': null, 'countDistinct(connote)': 0 }
        ] }
} } };
const data = analytics.series([source])[0];
assert.deepEqual(data.metrics, ['sum(buyTotalNett)', 'countDistinct(connote)']);
assert.deepEqual(data.dimensions, ['carrier']);
assert.equal(data.filters[0].Values[0], 'CUSTOMER');
assert.equal(analytics.series([{ Data: { result: { status: 'not_your_account', groups: source.Data.result.result.groups } } }]).length, 0);
const root = new Element('div');
analytics.renderEvidence(root, [source]);
assert.equal(find(root, e => e.tagName === 'table').length, 1);
assert(root.textContent.includes('More groups exist'));
assert(root.textContent.includes('CUSTOMER'));
assert.equal(find(root, e => e.tagName === 'img').length, 0, 'Database HTML stays inert text');
let bars = find(root, e => (e.className || '').split(' ').includes('ivt-chat-bar'));
assert.equal(bars[0].style.width, '100%');
assert.equal(bars[1].style.width, '50%');
assert(bars[1].className.includes('negative'));
assert.equal(bars[2].style.width, '0%', 'Missing metrics are not drawn as numbers');
const select = find(root, e => e.tagName === 'select')[0];
select.value = 'countDistinct(connote)'; select.events.change();
bars = find(root, e => (e.className || '').split(' ').includes('ivt-chat-bar'));
assert(!bars[1].className.includes('negative'), 'Measure changes redraw the plot');

const answer = new Element('div'); let chosen = null;
analytics.renderAnswer(answer, 'Observed results\n| Carrier | Cost |\n| --- | ---: |\n| A | 100 |\n| B | 80 |\nAsk next: Compare those carriers on the same route\n<script>alert(1)</script>', q => { chosen = q; });
assert.equal(find(answer, e => e.tagName === 'table').length, 1);
assert.equal(find(answer, e => e.tagName === 'script').length, 0, 'Model HTML stays inert text');
assert.equal(chosen, null, 'Suggestions never execute automatically');
find(answer, e => e.className === 'ivt-chat-followup')[0].events.click();
assert.equal(chosen, 'Compare those carriers on the same route');
const empty = new Element('div'); analytics.renderEvidence(empty, []); assert.equal(empty.children.length, 0);
console.log('PASS: nested dataset evidence, chart measure changes, missing/negative values, filters, truncation, safe tables and optional follow-ups.');

const rich = new Element('div');
analytics.renderAnswer(rich, '**Please confirm**\n- Customer: **CH2**\n- Period: Last 13 weeks\n\nWhat I will do once you confirm:\n1. Compare **carrier mix**\n2. Review ancillary fees\n\nAsk next: **Compare routes**', () => {});
assert.equal(find(rich, e => e.tagName === 'ul').length, 1);
assert.equal(find(rich, e => e.tagName === 'ol').length, 1);
assert.equal(find(rich, e => e.tagName === 'li').length, 4);
assert(find(rich, e => e.tagName === 'strong').some(e => e.textContent === 'Customer:'));
assert(find(rich, e => e.tagName === 'strong').some(e => e.textContent === 'carrier mix'));
assert.equal(find(rich, e => e.tagName === 'h4')[0].textContent, 'What I will do once you confirm:');
assert.equal(find(rich, e => e.className === 'ivt-chat-followup')[0].textContent, 'Compare routes');
const csv = analytics.csv([{ name: '=1+1', amount: -12.34, quote: 'a,"b"\nnext' }, { name: '\t@SUM(1)', amount: 0, quote: null }], ['name', 'amount', 'quote']);
assert(csv.startsWith('\ufeff'), 'CSV includes Excel UTF-8 BOM');
assert(csv.includes('"\'=1+1"'));
assert(csv.includes('"\'\t@SUM(1)"'));
assert(csv.includes('"-12.34"'), 'Numeric negatives remain usable numbers');
assert(csv.includes('"a,""b""\nnext"'), 'CSV quotes commas, quotes and newlines');
const svg = analytics.chartSvg(data, 'sum(buyTotalNett)');
assert(svg.includes('Total buy cost (nett)'));
assert(svg.includes('PARTIAL RESULTS'));
assert(svg.includes('CUSTOMER'));
assert(svg.includes('&lt;img src=x onerror=alert(1)&gt;'));
assert(!svg.includes('<img'), 'Exported SVG cannot inject source markup');
assert(analytics.chartSvg(data, 'countDistinct(connote)').includes('Distinct connote'), 'Chart export uses selected measure');
const originalURL = global.URL, originalTimeout = global.setTimeout; let downloaded;
try {
    global.URL = { createObjectURL(blob) { downloaded = blob; return 'blob:test'; }, revokeObjectURL() {} };
    global.setTimeout = () => 0;
    find(answer, e => e.className === 'ivt-chat-download')[0].click();
    assert.equal(downloaded.type, 'text/csv;charset=utf-8');
    assert(document.body.children[0].download.endsWith('.csv'));
    const svgButton = find(root, e => e.className === 'ivt-chat-download' && e.textContent.includes('(SVG)'))[0];
    svgButton.click();
    assert.equal(downloaded.type, 'image/svg+xml;charset=utf-8');
    assert(document.body.children[1].download.endsWith('.svg'));
} finally { global.URL = originalURL; global.setTimeout = originalTimeout; }
console.log('PASS: bold headings and lists, suggestions, CSV formula protection/escaping, scoped SVG exports and download actions.');
