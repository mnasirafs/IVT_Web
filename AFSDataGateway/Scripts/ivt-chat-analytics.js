(function (host) {
    'use strict';
    function el(tag, text, cls) {
        var node = document.createElement(tag);
        if (text != null) node.textContent = String(text);
        if (cls) node.className = cls;
        return node;
    }
    function label(key) {
        var names = { buyTotalNett: 'buy cost (nett)', sellTotalNett: 'revenue (nett)', carrier: 'carrier', carrierGroup: 'carrier group',
            customerOrGroup: 'customer / group', reportingWeek: 'reporting week', connote: 'connote', count: 'line count' };
        var metric = key.match(/^(sum|avg|min|max|countDistinct)\((.+)\)$/);
        if (metric) return { sum: 'Total ', avg: 'Average ', min: 'Minimum ', max: 'Maximum ', countDistinct: 'Distinct ' }[metric[1]] + label(metric[2]);
        return names[key] || key.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/_/g, ' ');
    }
    function filterText(filters) {
        if (!Array.isArray(filters)) return '';
        return filters.map(function (filter) {
            var op = { equals: 'is', contains: 'contains', between: 'between', gte: 'from', lte: 'to', notBlank: 'is present', blank: 'is blank', startsWith: 'starts with' };
            return label(filter.Field || filter.field || '') + ' ' + (op[filter.Op || filter.op] || filter.Op || filter.op || '') + ' ' +
                (filter.Values || filter.values || []).join(' / ');
        }).join('; ');
    }
    function scalar(value) { return value == null || ['string', 'number', 'boolean'].indexOf(typeof value) >= 0; }
    function numeric(value) { return typeof value === 'number' && Number.isFinite(value); }
    function format(value) { return numeric(value) ? value.toLocaleString(undefined, { maximumFractionDigits: 2 }) : value == null ? '\u2014' : String(value); }
    function inline(parent, text) {
        var pattern = /(\*\*|__)(.+?)\1/g, match, at = 0;
        while ((match = pattern.exec(text))) {
            if (match.index > at) parent.appendChild(el('span', text.slice(at, match.index)));
            parent.appendChild(el('strong', match[2])); at = pattern.lastIndex;
        }
        if (at < text.length) parent.appendChild(el('span', text.slice(at)));
    }
    function plain(text) { return String(text).replace(/(\*\*|__)(.+?)\1/g, '$2'); }
    function csv(rows, keys, headers) {
        function cell(value) {
            var text = value == null ? '' : plain(value);
            // Guard spreadsheet formulas, including prefixes hidden behind whitespace/control characters.
            if (/^[\s\u0000-\u001f]*[=+\-@]/.test(text) && !/^-?\d+(\.\d+)?$/.test(text)) text = "'" + text;
            return '"' + text.replace(/"/g, '""') + '"';
        }
        return '\ufeff' + [keys.map(function (key, i) { return cell(headers ? headers[i] : label(key)); }).join(',')]
            .concat(rows.map(function (row) { return keys.map(function (key) { return cell(row[key]); }).join(','); })).join('\r\n');
    }
    function filename(title, extension) { return 'Vera-' + String(title).replace(/[^a-z0-9-]+/gi, '-').slice(0, 64) + '.' + extension; }
    function download(blob, name) {
        var url = URL.createObjectURL(blob), link = el('a');
        link.href = url; link.download = name; document.body.appendChild(link); link.click(); link.remove();
        setTimeout(function () { URL.revokeObjectURL(url); }, 60000);
    }
    function csvButton(parent, rows, keys, headers, title) {
        var button = el('button', 'Download table (CSV)', 'ivt-chat-download'); button.type = 'button';
        button.addEventListener('click', function () { download(new Blob([csv(rows, keys, headers)], { type: 'text/csv;charset=utf-8' }), filename(title || 'table', 'csv')); });
        parent.appendChild(button);
    }
    function grid(parent, rows, keys, headers) {
        var wrap = el('div', null, 'ivt-chat-table-wrap'), table = el('table');
        var head = el('thead'), tr = el('tr');
        keys.forEach(function (key, i) { var th = el('th'); inline(th, headers ? headers[i] : label(key)); th.scope = 'col'; tr.appendChild(th); });
        head.appendChild(tr); table.appendChild(head);
        var body = el('tbody');
        rows.forEach(function (row) {
            var line = el('tr');
            keys.forEach(function (key) { var td = el('td'); inline(td, format(row[key])); line.appendChild(td); });
            body.appendChild(line);
        });
        table.appendChild(body); wrap.appendChild(table); parent.appendChild(wrap);
    }
    // Deliberately small Markdown subset. Model/database text is never interpreted as HTML, URLs or code.
    function renderAnswer(parent, text, choose) {
        var lines = String(text || '').split(/\r?\n/), prose = [], followups = 0, list = null, followupBox;
        function flush() { if (prose.length) { var p = el('p'); inline(p, prose.join('\n')); parent.appendChild(p); } prose = []; list = null; }
        function cells(line) { return line.trim().replace(/^\||\|$/g, '').split('|').map(function (v) { return v.trim(); }); }
        for (var i = 0; i < lines.length; i++) {
            var next = lines[i].match(/^Ask next:\s*(.{1,500})$/i);
            if (next && followups < 3) {
                followups++;
                flush();
                if (!followupBox) { followupBox = el('div', null, 'ivt-chat-followups'); followupBox.appendChild(el('strong', 'Suggested next questions')); parent.appendChild(followupBox); }
                var question = plain(next[1]);
                var button = el('button', question, 'ivt-chat-followup'); button.type = 'button';
                button.addEventListener('click', (function (value) { return function () { choose(value); }; })(question));
                followupBox.appendChild(button);
            } else if (i + 1 < lines.length && lines[i].indexOf('|') >= 0 && /^\s*\|?\s*:?-{3,}/.test(lines[i + 1]) &&
                cells(lines[i + 1]).every(function (v) { return /^:?-{3,}:?$/.test(v); })) {
                flush();
                var headers = cells(lines[i]), rows = []; i += 2;
                while (i < lines.length && lines[i].indexOf('|') >= 0 && rows.length < 100) {
                    var values = cells(lines[i]);
                    if (values.length !== headers.length) break;
                    rows.push(values); i++;
                }
                i--; var keys = headers.map(function (_, n) { return n; });
                grid(parent, rows, keys, headers); csvButton(parent, rows, keys, headers, 'answer-table');
            } else if (!lines[i].trim()) { flush(); }
            else if (/^\s*#{1,6}\s+/.test(lines[i]) || (lines[i].trim().length <= 100 && /:\s*$/.test(lines[i]))) {
                flush(); var heading = el('h4', null, 'ivt-chat-subtitle'); inline(heading, lines[i].replace(/^\s*#{1,6}\s+/, '').trim()); parent.appendChild(heading);
            } else if (/^\s*(?:[-*]\s+|\d+[.)]\s+)/.test(lines[i])) {
                var ordered = /^\s*\d/.test(lines[i]);
                if (!list || list.tagName.toLowerCase() !== (ordered ? 'ol' : 'ul')) { flush(); list = el(ordered ? 'ol' : 'ul', null, 'ivt-chat-answer-list'); parent.appendChild(list); }
                var content = lines[i].replace(/^\s*(?:[-*]\s+|\d+[.)]\s+)/, ''), li = el('li');
                var prefix = content.match(/^([^:*]{1,65}:)(\s+.*)$/);
                if (prefix) { li.appendChild(el('strong', prefix[1])); inline(li, prefix[2]); } else inline(li, content);
                list.appendChild(li);
            } else { if (list) flush(); prose.push(lines[i]); }
        }
        flush();
    }
    // Only server-returned, permission-filtered evidence supplies chart values; never parse figures out of model prose.
    function series(sources) {
        var output = [];
        (sources || []).forEach(function (source) {
            var data = source.Data || {}, envelope = data.result || {}, result = envelope.result || envelope;
            if (data.error || envelope.error || envelope.status === 'not_your_account' || result.error || result.status === 'not_your_account') return;
            ['groups', 'rules', 'byRule', 'customers', 'exceptions', 'records', 'definitions'].forEach(function (name) {
                var rows = result[name];
                if (!Array.isArray(rows) || !rows.length || !rows.every(function (r) { return r && typeof r === 'object' && !Array.isArray(r); })) return;
                var keys = [];
                rows.forEach(function (row) { Object.keys(row).forEach(function (key) {
                    if (keys.indexOf(key) < 0 && rows.every(function (r) { return scalar(r[key]); })) keys.push(key);
                }); });
                var metrics = keys.filter(function (key) { return rows.some(function (r) { return numeric(r[key]); }) && rows.every(function (r) { return r[key] == null || numeric(r[key]); }); });
                // Identifiers are labels even if SQL serialises a rule or job code as a number.
                metrics = metrics.filter(function (key) { return !/^(id|code|ruleCode|jobNumber|connote|postcode)$/i.test(key); });
                var dimensions = Array.isArray(result.groupBy) ? result.groupBy.filter(function (key) { return keys.indexOf(key) >= 0; }) :
                    keys.filter(function (key) { return metrics.indexOf(key) < 0; }).slice(0, 2);
                if (keys.length) output.push({ title: data.source || name, scope: data.scope, period: data.reportingWeek,
                    filters: envelope.appliedFilters, note: envelope.rowMeaning || data.periodNote, truncated: result.truncated, truncationNote: result.truncationNote,
                    rows: rows, keys: keys, metrics: metrics.filter(function (key) { return dimensions.indexOf(key) < 0; }), dimensions: dimensions });
            });
        });
        return output;
    }
    function chartSvg(data, metric) {
        function xml(value) { return String(value).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;' }[c]; }).replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f]/g, ''); }
        function text(x, y, value, size, weight) { return '<text x="' + x + '" y="' + y + '" font-size="' + (size || 15) + '" font-weight="' + (weight || 400) + '">' + xml(value) + '</text>'; }
        function short(value, max) { value = String(value); return value.length > max ? value.slice(0, max - 1) + '\u2026' : value; }
        var rows = data.rows.slice(0, 12), max = Math.max.apply(null, rows.map(function (row) { return numeric(row[metric]) ? Math.abs(row[metric]) : 0; }).concat([0]));
        var notes = [data.scope || '', 'Loaded reporting week: ' + (data.period ? String(data.period).slice(0, 10) : 'not available'),
            'Filters: ' + (filterText(data.filters) || 'No additional filters'),
            'First ' + rows.length + ' returned groups in query order. Bar length is magnitude; red indicates a negative value.'];
        if (data.truncated) notes.push('PARTIAL RESULTS: ' + (data.truncationNote || 'More groups exist beyond those returned.'));
        var height = 180 + notes.length * 23 + rows.length * 54;
        var svg = '<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="' + height + '" viewBox="0 0 1200 ' + height + '"><title>' + xml(data.title + ' - ' + label(metric)) + '</title><desc>' + xml(notes.join('\n')) + '</desc><rect width="100%" height="100%" fill="white"/><g font-family="Arial, sans-serif" fill="#17384b">';
        svg += text(30, 40, short('Vera | ' + data.title, 110), 22, 700) + text(30, 72, label(metric), 18, 700);
        notes.forEach(function (note, i) { svg += text(30, 104 + i * 23, short(note, 145), 13); });
        var start = 125 + notes.length * 23;
        rows.forEach(function (row, i) {
            var y = start + i * 54, name = data.dimensions.map(function (key) { return format(row[key]); }).join(' / '), value = row[metric];
            svg += text(30, y + 18, short(name, 38), 14);
            svg += '<rect x="345" y="' + y + '" width="640" height="24" rx="3" fill="#edf3f6"/>';
            svg += '<rect x="345" y="' + y + '" width="' + (numeric(value) && max > 0 ? Math.abs(value) / max * 640 : 0) + '" height="24" rx="3" fill="' + (value < 0 ? '#b93939' : '#157c91') + '"/>';
            svg += text(1000, y + 18, format(value), 14);
        });
        return svg + '</g></svg>';
    }
    function chartDownloads(parent, data, select) {
        var bar = el('div', null, 'ivt-chat-downloads');
        var status = el('span', null, 'ivt-chat-export-status'); status.setAttribute('role', 'status');
        ['PNG', 'SVG'].forEach(function (type) {
            var button = el('button', 'Download chart (' + type + ')', 'ivt-chat-download'); button.type = 'button';
            button.addEventListener('click', async function () {
                var metric = select.value, svg = new Blob([chartSvg(data, metric)], { type: 'image/svg+xml;charset=utf-8' });
                var name = data.title + '-' + label(metric);
                if (type === 'SVG') { download(svg, filename(name, 'svg')); return; }
                button.disabled = true; status.textContent = 'Preparing chart\u2026';
                var url = URL.createObjectURL(svg);
                try {
                    var image = new Image();
                    await new Promise(function (resolve, reject) { image.onload = resolve; image.onerror = reject; image.src = url; });
                    var canvas = document.createElement('canvas'); canvas.width = image.naturalWidth; canvas.height = image.naturalHeight;
                    canvas.getContext('2d').drawImage(image, 0, 0);
                    var png = await new Promise(function (resolve) { canvas.toBlob(resolve, 'image/png'); });
                    if (!png) throw new Error('PNG export unavailable');
                    download(png, filename(name, 'png')); status.textContent = '';
                } catch (error) { status.textContent = 'Could not create PNG. Please download SVG instead.'; }
                finally { URL.revokeObjectURL(url); button.disabled = false; }
            });
            bar.appendChild(button);
        });
        bar.appendChild(status); parent.appendChild(bar);
    }
    function renderEvidence(parent, sources) {
        var datasets = series(sources);
        datasets.slice(0, 6).forEach(function (data) {
            var card = el('section', null, 'ivt-chat-analytics');
            card.appendChild(el('strong', data.title));
            card.appendChild(el('p', (data.scope || '') + ' \u00b7 Loaded reporting week: ' + (data.period ? String(data.period).slice(0, 10) : 'not available')));
            if (data.filters && data.filters.length) card.appendChild(el('p', 'Filters: ' + filterText(data.filters)));
            if (data.note) card.appendChild(el('p', data.note));
            if (data.metrics.length && data.dimensions.length && data.rows.length > 1) {
                var control = el('label', 'Chart measure: '), select = el('select');
                select.setAttribute('aria-label', 'Chart measure');
                data.metrics.forEach(function (metric) { var option = el('option', label(metric)); option.value = metric; select.appendChild(option); });
                control.appendChild(select); card.appendChild(control);
                var plot = el('div', null, 'ivt-chat-plot'); card.appendChild(plot);
                function draw() {
                    plot.textContent = '';
                    var metric = select.value, shown = data.rows.slice(0, 12);
                    var max = Math.max.apply(null, shown.map(function (row) { return numeric(row[metric]) ? Math.abs(row[metric]) : 0; }).concat([0]));
                    plot.setAttribute('role', 'img');
                    plot.setAttribute('aria-label', label(metric) + ' comparison. Exact values follow in the data table.');
                    shown.forEach(function (row) {
                        var title = data.dimensions.map(function (key) { return format(row[key]); }).join(' / ');
                        var line = el('div', null, 'ivt-chat-bar-row');
                        line.appendChild(el('span', title, 'ivt-chat-bar-label'));
                        var track = el('span', null, 'ivt-chat-bar-track'), value = row[metric];
                        var bar = el('span', null, 'ivt-chat-bar' + (value < 0 ? ' negative' : ''));
                        bar.style.width = (numeric(value) && max > 0 ? Math.abs(value) / max * 100 : 0) + '%';
                        track.appendChild(bar); line.appendChild(track); line.appendChild(el('span', format(value)));
                        line.title = title + ': ' + format(value); plot.appendChild(line);
                    });
                }
                select.addEventListener('change', draw); draw();
                chartDownloads(card, data, select);
                card.appendChild(el('p', 'Bar length shows magnitude; negative values are marked in red. First ' + Math.min(12, data.rows.length) + ' returned groups shown, in query order.'));
            }
            var details = el('details'); details.open = data.rows.length <= 10;
            details.appendChild(el('summary', 'Data table (' + Math.min(data.rows.length, 100) + ' returned rows)'));
            grid(details, data.rows.slice(0, 100), data.keys.slice(0, 20)); card.appendChild(details);
            csvButton(card, data.rows, data.keys, null, data.title);
            card.appendChild(el('p', 'CSV contains the returned rows only; it does not fetch additional records. Chart downloads use the selected measure and displayed groups.'));
            if (data.truncated || data.rows.length > 100 || data.keys.length > 20)
                card.appendChild(el('p', data.truncationNote || 'Partial results shown. Narrow the question before drawing conclusions.'));
            parent.appendChild(card);
        });
        if (datasets.length > 6) parent.appendChild(el('p', 'Showing six source tables. Ask a narrower question to explore the remaining results.'));
    }
    var api = { renderAnswer: renderAnswer, renderEvidence: renderEvidence, series: series, csv: csv, chartSvg: chartSvg, csvButton: csvButton };
    if (typeof module === 'object' && module.exports) module.exports = api;
    else host.IvtChatAnalytics = api;
})(typeof window !== 'undefined' ? window : this);
