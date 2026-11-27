/* IVT Administration pages. Server endpoints enforce permissions, validation and confirmation;
   this script only renders data and collects explicit operator input. ASCII only. */
(function () {
    'use strict';
    var root = document.querySelector('[data-ivt-page]');
    if (!root) return;
    var tokenInput = document.querySelector('#ivt-af input[name="__RequestVerificationToken"]');
    var token = tokenInput ? tokenInput.value : '';

    // ------------------------------------------------------------ helpers
    function el(tag, attrs, text) {
        var e = document.createElement(tag);
        if (attrs) Object.keys(attrs).forEach(function (k) {
            if (k === 'className') e.className = attrs[k];
            else if (k === 'html') e.innerHTML = attrs[k];
            else if (attrs[k] !== null && attrs[k] !== undefined && attrs[k] !== false) e.setAttribute(k, attrs[k] === true ? '' : attrs[k]);
        });
        if (text !== undefined && text !== null) e.textContent = String(text);
        return e;
    }
    function $(sel, ctx) { return (ctx || root).querySelector(sel); }
    function $$(sel, ctx) { return Array.prototype.slice.call((ctx || root).querySelectorAll(sel)); }
    function clear(node) { while (node && node.firstChild) node.removeChild(node.firstChild); }
    function qs(params) {
        var p = new URLSearchParams();
        Object.keys(params || {}).forEach(function (k) { if (params[k] !== undefined && params[k] !== null) p.set(k, params[k]); });
        return p.toString();
    }
    async function call(url, options) {
        var response;
        try { response = await fetch(url, options); }
        catch (e) { throw new Error('The server could not be reached. The request may not have been processed; refresh before retrying.'); }
        var type = response.headers.get('Content-Type') || '';
        if (type.indexOf('application/json') < 0) throw new Error('Unexpected response (HTTP ' + response.status + '). Your session may have expired; refresh the page.');
        var data = await response.json();
        // Operation DTOs use PascalCase; query envelopes use camelCase. Keep row/column names intact.
        if (data && Object.prototype.hasOwnProperty.call(data, 'Outcome')) {
            data = {
                ok: data.Ok, outcome: data.Outcome, message: data.Message, warning: data.Warning,
                correlationId: data.CorrelationId, auditId: data.AuditId, data: data.Data,
                steps: (data.Steps || []).map(function (s) { return { name: s.Name, status: s.Status, message: s.Message }; })
            };
        }
        if (!response.ok && data && data.ok === false && !data.outcome) throw new Error(data.message || ('HTTP ' + response.status));
        if (!response.ok && data && data.outcome === 'ERROR') throw new Error(data.message || ('HTTP ' + response.status));
        return data;
    }
    function get(url, params) {
        return call(url + (url.indexOf('?') < 0 ? '?' : '&') + qs(params), { credentials: 'same-origin', cache: 'no-store', headers: { 'Accept': 'application/json' } });
    }
    function post(url, data, form) {
        var body = form || new URLSearchParams();
        Object.keys(data || {}).forEach(function (k) { if (data[k] !== undefined && data[k] !== null) body.append(k, data[k]); });
        body.append('__RequestVerificationToken', token);
        return call(url, { method: 'POST', credentials: 'same-origin', cache: 'no-store', headers: { 'Accept': 'application/json' }, body: body });
    }
    function fmt(value, type) {
        if (value === null || value === undefined) return '';
        if (typeof value === 'boolean') return value ? 'Yes' : 'No';
        var s = String(value);
        if (type === 'date' && s.length >= 10) return s.substring(0, 10);
        if (s.length >= 19 && /^\d{4}-\d\d-\d\d[T ]\d\d:\d\d/.test(s)) return s.replace('T', ' ').substring(0, 19).replace(' 00:00:00', '');
        return s;
    }
    function busy(button, on, label) {
        if (!button) return;
        if (on) { button.dataset.label = button.textContent; button.disabled = true; button.textContent = label || 'Working...'; }
        else { button.disabled = false; if (button.dataset.label) button.textContent = button.dataset.label; }
    }
    function alertBox(container, kind, text) {
        clear(container);
        container.appendChild(el('div', { className: 'alert alert-' + kind, role: kind === 'danger' ? 'alert' : 'status' }, text));
        revealMessage(container);
    }
    function revealMessage(container) {
        container.setAttribute('tabindex', '-1');
        container.focus({ preventScroll: true });
        container.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
    // Native <dialog> provides keyboard focus containment and Escape without browser confirm boxes.
    var confirmationQueue = Promise.resolve();
    function confirmAction(message) {
        var pending = confirmationQueue.then(function () {
            return new Promise(function (resolve) {
                var previousFocus = document.activeElement;
                var dialog = el('dialog', { className: 'ivt-confirm', 'aria-labelledby': 'ivt-confirm-title', 'aria-describedby': 'ivt-confirm-message' });
                dialog.appendChild(el('div', { className: 'ivt-confirm-eyebrow' }, 'IVT Administration' + (root.dataset.envLabel ? ' / ' + root.dataset.envLabel : '')));
                dialog.appendChild(el('h2', { id: 'ivt-confirm-title' }, 'Confirm action'));
                dialog.appendChild(el('p', { id: 'ivt-confirm-message' }, message));
                var footer = el('div', { className: 'ivt-confirm-actions' });
                var cancel = el('button', { type: 'button', className: 'btn btn-light', autofocus: true }, 'Cancel');
                var proceed = el('button', { type: 'button', className: 'btn btn-primary' }, 'Confirm');
                footer.appendChild(cancel); footer.appendChild(proceed); dialog.appendChild(footer);
                cancel.addEventListener('click', function () { dialog.close('cancel'); });
                proceed.addEventListener('click', function () { dialog.close('confirm'); });
                dialog.addEventListener('close', function () {
                    var confirmed = dialog.returnValue === 'confirm';
                    dialog.remove();
                    if (previousFocus && document.contains(previousFocus)) previousFocus.focus({ preventScroll: true });
                    resolve(confirmed);
                }, { once: true });
                document.body.appendChild(dialog);
                dialog.showModal();
            });
        });
        confirmationQueue = pending.catch(function () {});
        return pending;
    }
    // Renders an operation result exactly as the server reported it (never "all succeeded" on partial/failed outcomes).
    function showResult(container, r) {
        clear(container);
        var kind = r.ok ? (r.outcome === 'SUBMITTED' ? 'info' : 'success') : (r.outcome === 'PARTIAL' || r.outcome === 'AMBIGUOUS' || r.outcome === 'SAVED_NOTIFY_FAILED' ? 'warning' : 'danger');
        var box = el('div', { className: 'alert alert-' + kind, role: 'status' });
        var titles = { SUCCEEDED: 'Completed successfully', SUBMITTED: 'Request submitted', PARTIAL: 'Some changes need attention',
            REJECTED: 'Request not applied', FAILED: 'Action failed', AMBIGUOUS: 'Check the result before retrying',
            SAVED_NOTIFY_FAILED: 'Saved; notification needs attention', ERROR: 'Unable to complete the request' };
        box.appendChild(el('strong', null, (titles[r.outcome] || (r.ok ? 'Action completed' : 'Action needs attention')) + ': '));
        box.appendChild(document.createTextNode(r.message || ''));
        if (r.warning) box.appendChild(el('div', { className: 'mt-1' }, r.warning));
        if (r.steps && r.steps.length) {
            var ul = el('ul', { className: 'ivt-steps' });
            r.steps.forEach(function (s) {
                var li = el('li', { className: 'st-' + String(s.status || '').toLowerCase().replace(/\s+/g, '-') });
                li.appendChild(el('b', null, s.name + ': ' + s.status));
                if (s.message) li.appendChild(document.createTextNode(' - ' + s.message));
                ul.appendChild(li);
            });
            box.appendChild(ul);
        }
        if (Array.isArray(r.data) && r.data.length && typeof r.data[0] === 'object') {
            var issues = r.data.filter(function (d) { var st = d.status || d.Status; return st === 'Failed' || st === 'Not attempted'; });
            var list = issues.length ? issues : r.data;
            var details = el('details', issues.length ? { open: true } : null);
            details.appendChild(el('summary', null, issues.length ? issues.length + ' row(s) not applied' : r.data.length + ' row result(s)'));
            var ul2 = el('ul');
            list.slice(0, 300).forEach(function (d) {
                ul2.appendChild(el('li', null, [d.connote || d.Id || d.id || '', d.status || d.Status, d.message || d.Message || ''].filter(Boolean).join(' - ')));
            });
            details.appendChild(ul2);
            box.appendChild(details);
        }
        if (r.correlationId) box.appendChild(el('div', { className: 'ivt-ref' }, 'Reference ' + r.correlationId + (r.auditId ? ' / audit #' + r.auditId : '')));
        container.appendChild(box);
        revealMessage(container);
    }
    function failure(container, error) { alertBox(container, 'danger', error.message || String(error)); }

    // Generic read-only table. options: { select: 'radio'|'checkbox', key: 'Id', actions: [{label, onClick(row)}] }
    function renderTable(container, columns, rows, options) {
        options = options || {};
        clear(container);
        if (!rows.length) { container.appendChild(el('p', { className: 'ivt-empty' }, options.empty || 'No records match.')); return null; }
        var wrap = el('div', { className: 'ivt-table-wrap' });
        var table = el('table', { className: 'ivt-grid' });
        var head = el('tr');
        if (options.select) head.appendChild(el('th', null, ''));
        columns.forEach(function (c) { head.appendChild(el('th', { scope: 'col' }, c.label || c.name)); });
        if (options.actions) head.appendChild(el('th', null, 'Actions'));
        var thead = el('thead'); thead.appendChild(head); table.appendChild(thead);
        var tbody = el('tbody');
        var keyIndex = options.key ? columns.map(function (c) { return c.name.toLowerCase(); }).indexOf(options.key.toLowerCase()) : -1;
        rows.forEach(function (row, i) {
            var tr = el('tr');
            if (options.select) {
                var td0 = el('td');
                td0.appendChild(el('input', { type: options.select, name: 'ivt-select', value: keyIndex >= 0 ? row[keyIndex] : i, 'aria-label': 'Select row' }));
                tr.appendChild(td0);
            }
            columns.forEach(function (c, ci) {
                var v = row[ci];
                tr.appendChild(el('td', v === null || v === undefined ? { className: 'null' } : null, fmt(v, c.type)));
            });
            if (options.actions) {
                var td = el('td', { className: 'ivt-actions' });
                options.actions.forEach(function (a) {
                    var b = el('button', { type: 'button', className: 'btn btn-sm btn-outline-secondary' }, a.label);
                    b.addEventListener('click', function () { a.onClick(row, b); });
                    td.appendChild(b);
                });
                tr.appendChild(td);
            }
            tbody.appendChild(tr);
        });
        table.appendChild(tbody); wrap.appendChild(table); container.appendChild(wrap);
        return table;
    }

    function isFriday(value) { var d = new Date(value + 'T00:00:00'); return !isNaN(d) && d.getDay() === 5; }

    var pages = {};

    // ------------------------------------------------------------ overview / health
    pages.overview = function () {
        var button = $('#ivt-health-run'), out = $('#ivt-health');
        if (!button) return;
        button.addEventListener('click', async function () {
            busy(button, true, 'Checking (read-only)...');
            try {
                var r = await get(root.dataset.healthUrl);
                var cols = [{ name: 'Area' }, { name: 'Environment' }, { name: 'Name' }, { name: 'Status' }, { name: 'Detail' }];
                var rows = r.items.map(function (i) { return [i.Area, i.Environment, i.Name, i.Status, i.Detail]; });
                var t = renderTable(out, cols, rows);
                if (t) $$('tbody tr', t).forEach(function (tr, i) { tr.className = 'health-' + String(r.items[i].Status).toLowerCase(); });
            } catch (e) { failure(out, e); }
            finally { busy(button, false); }
        });
    };

    // ------------------------------------------------------------ run control
    pages.run = function () {
        var env = root.dataset.env, msg = $('#ivt-run-result'), week = $('#ivt-week'), lz = $('#ivt-lz'), weekHint = $('#ivt-week-hint');
        var timer = null;
        function updateHint() {
            weekHint.textContent = week.value && !isFriday(week.value) ? 'Note: this date is not a Friday.' : '';
        }
        week.addEventListener('change', updateHint); updateHint();
        async function refresh() {
            try {
                var s = await get(root.dataset.statusUrl, { env: env });
                var jobs = $('#ivt-jobs'); clear(jobs);
                var anyRunning = false;
                s.jobs.forEach(function (j) {
                    anyRunning = anyRunning || j.IsRunning;
                    var card = el('div', { className: 'ivt-job ' + (!j.Exists ? 'job-missing' : j.IsRunning ? 'job-running' : j.LastOutcome === 'Failed' ? 'job-failed' : j.LastOutcome === 'Succeeded' ? 'job-ok' : '') });
                    card.appendChild(el('strong', null, j.Name));
                    card.appendChild(el('div', null, j.Summary));
                    if (j.RunningSince) card.appendChild(el('div', { className: 'muted' }, 'Running since ' + fmt(j.RunningSince)));
                    if (!j.IsRunning && j.LastRunStart) card.appendChild(el('div', { className: 'muted' }, 'Last run ' + fmt(j.LastRunStart) + ', duration ' + j.LastRunDuration));
                    if (j.LastOutcome === 'Failed' && j.LastMessage) card.appendChild(el('details', null)).appendChild(el('pre', null, j.LastMessage));
                    if (j.Steps && j.Steps.length) {
                        var d = el('details'); d.appendChild(el('summary', null, 'Steps'));
                        var holder = el('div'); d.appendChild(holder); card.appendChild(d);
                        renderTable(holder, [{ name: 'Step' }, { name: 'Name' }, { name: 'Last outcome' }, { name: 'Last run' }, { name: 'Duration' }],
                            j.Steps.map(function (st) { return [st.StepId, st.Name, st.LastOutcome, st.LastRun, st.LastDuration]; }));
                    }
                    jobs.appendChild(card);
                });
                var hist = $('#ivt-history');
                if (s.history) renderTable(hist, s.history.columns.map(function (c) { return { name: c }; }), s.history.rows, { empty: 'No run history.' });
                else alertBox(hist, 'warning', s.historyError || 'History unavailable.');
                renderTable($('#ivt-recent'), [{ name: 'Audit' }, { name: 'When' }, { name: 'Operator' }, { name: 'Operation' }, { name: 'Invoice week', type: 'date' }, { name: 'Outcome' }, { name: 'Job / run' }],
                    s.recent.map(function (r) { return [r.Id, r.when, r.Operator, r.Operation, r.InvoiceWeek, r.Outcome, r.ExternalId]; }), { empty: 'No runs recorded by this module yet.' });
                $('#ivt-refreshed').textContent = 'Refreshed ' + new Date().toLocaleTimeString();
                clearTimeout(timer);
                if (anyRunning) timer = setTimeout(refresh, 30000);
            } catch (e) { failure($('#ivt-jobs'), e); }
        }
        $('#ivt-refresh').addEventListener('click', refresh);
        refresh();
        $$('[data-run]').forEach(function (button) {
            button.addEventListener('click', async function () {
                if (!lz.checked) { alertBox(msg, 'warning', 'Tick the landing-zone confirmation first.'); return; }
                if (!week.value) { alertBox(msg, 'warning', 'Choose an invoice week.'); return; }
                var plan = button.dataset.plan || '';
                if (!await confirmAction('Start ' + button.dataset.label + ' on ' + root.dataset.envLabel + ' for invoice week ' + week.value + '?\n\nSteps:\n' + plan.split('|').join('\n') + '\n\nThe job is only submitted; use Refresh to follow it.')) return;
                busy(button, true, 'Submitting...');
                try {
                    var r = await post(root.dataset.startUrl, { env: env, kind: button.dataset.run, invoiceWeek: week.value, landingConfirmed: lz.checked });
                    showResult(msg, r);
                    refresh();
                } catch (e) { failure(msg, e); }
                finally { busy(button, false); }
            });
        });
        var azure = $('#ivt-azure-run');
        if (azure) azure.addEventListener('click', async function () {
            if (!lz.checked || !week.value) { alertBox(msg, 'warning', 'Tick the landing-zone confirmation and choose the invoice week.'); return; }
            if (!await confirmAction('Run the AZURE sequence for ' + week.value + '?\nPrepares IVT, IVT_Test and Azure IVT, uploads Buy/Sell files (overwriting), and starts the Data Factory pipeline. These steps are not one transaction.')) return;
            busy(azure, true, 'Submitting...');
            try { var r = await post(root.dataset.azureUrl, { invoiceWeek: week.value, landingConfirmed: lz.checked }); showResult(msg, r); if (r.data) $('#ivt-azure-runid').value = r.data; }
            catch (e) { failure(msg, e); } finally { busy(azure, false); }
        });
        var azureStatus = $('#ivt-azure-status');
        if (azureStatus) azureStatus.addEventListener('click', async function () {
            busy(azureStatus, true, 'Checking...');
            try { var r = await post(root.dataset.azureStatusUrl, { runId: $('#ivt-azure-runid').value }); alertBox(msg, 'info', 'Azure pipeline run: ' + r.message); }
            catch (e) { failure(msg, e); } finally { busy(azureStatus, false); }
        });
        var rollback = $('#ivt-rollback');
        rollback.addEventListener('click', async function () {
            var phrase = $('#ivt-rollback-confirm').value;
            if (!await confirmAction('ROLL BACK the last invoice-week load on ' + root.dataset.envLabel + '? This is destructive.')) return;
            busy(rollback, true, 'Rolling back...');
            try { showResult(msg, await post(root.dataset.rollbackUrl, { env: env, confirmation: phrase })); refresh(); }
            catch (e) { failure(msg, e); } finally { busy(rollback, false); $('#ivt-rollback-confirm').value = ''; }
        });
    };

    // ------------------------------------------------------------ reference settings
    pages.settings = function () {
        var def = JSON.parse(root.dataset.def);
        var env = root.dataset.env, msg = $('#ivt-msg'), grid = $('#ivt-grid'), searchBox = $('#ivt-search'), count = $('#ivt-count');
        var editable = def.fields.filter(function (f) { return f.hasUpdate && !f.readOnly; });
        var state = { search: '', columns: [], rows: [] };
        function colIndex(name) { return state.columns.map(function (c) { return c.name.toLowerCase(); }).indexOf(name.toLowerCase()); }
        function input(field, value) {
            var i;
            if (field.kind === 'Boolean') {
                i = el('input', { type: 'checkbox', 'data-field': field.column, 'aria-label': field.label });
                i.checked = value === true || value === 1 || value === '1' || value === 'true';
            } else {
                i = el('input', { type: 'text', className: 'form-control form-control-sm', 'data-field': field.column, 'aria-label': field.label, maxlength: field.maxLength || null, title: field.help || null });
                i.value = value === null || value === undefined ? '' : (field.kind === 'Text' ? String(value) : String(value));
                if (value === null || value === undefined) i.placeholder = 'NULL';
                if (field.kind !== 'Text') i.inputMode = 'decimal';
            }
            return i;
        }
        function rowValues(tr) {
            var o = { Id: tr.dataset.id };
            $$('[data-field]', tr).forEach(function (i) { o[i.dataset.field] = i.type === 'checkbox' ? i.checked : i.value; });
            return o;
        }
        function render() {
            clear(grid);
            count.textContent = state.rows.length + ' record(s) for ' + (state.search ? '"' + state.search + '"' : 'all') ;
            $('#ivt-save-all').textContent = 'Save all displayed (' + state.rows.length + ')';
            $('#ivt-save-all').disabled = state.rows.length === 0;
            if (!state.rows.length) { grid.appendChild(el('p', { className: 'ivt-empty' }, 'No records match.')); return; }
            var wrap = el('div', { className: 'ivt-table-wrap' }), table = el('table', { className: 'ivt-grid ivt-edit' });
            var head = el('tr');
            def.fields.forEach(function (f) { if (colIndex(f.column) >= 0) head.appendChild(el('th', { scope: 'col', title: f.help || null }, f.label)); });
            head.appendChild(el('th', null, 'Actions'));
            var thead = el('thead'); thead.appendChild(head); table.appendChild(thead);
            var tbody = el('tbody');
            state.rows.forEach(function (row) {
                var id = row[colIndex('Id')];
                var tr = el('tr', { 'data-id': id });
                var deleted = def.fields.some(function (f) { return f.column === 'IsDeleted'; }) ? row[colIndex('IsDeleted')] : null;
                if (deleted === true || deleted === 1) tr.className = 'is-deleted';
                def.fields.forEach(function (f) {
                    var ci = colIndex(f.column); if (ci < 0) return;
                    var td = el('td');
                    if (f.readOnly || !f.hasUpdate) td.textContent = fmt(row[ci]);
                    else td.appendChild(input(f, row[ci]));
                    tr.appendChild(td);
                });
                var actions = el('td', { className: 'ivt-actions' });
                var save = el('button', { type: 'button', className: 'btn btn-sm btn-primary' }, 'Save');
                save.addEventListener('click', async function () {
                    busy(save, true, 'Saving...');
                    try { var r = await post(root.dataset.updateUrl, { key: def.key, env: env, search: state.search, row: JSON.stringify(rowValues(tr)) }); showResult(msg, r); if (r.ok) await load(); }
                    catch (e) { failure(msg, e); } finally { busy(save, false); }
                });
                actions.appendChild(save);
                def.fields.filter(function (f) { return f.toggle; }).forEach(function (f) {
                    var ci = colIndex(f.column); if (ci < 0) return;
                    var on = row[ci] === true || row[ci] === 1;
                    var label = f.column === 'IsDeleted' ? (on ? 'Reactivate' : (f.label === 'Disabled' ? 'Disable' : 'Delete')) : (on ? 'Turn off ' : 'Turn on ') + f.label;
                    var b = el('button', { type: 'button', className: 'btn btn-sm ' + (f.column === 'IsDeleted' && !on ? 'btn-outline-danger' : 'btn-outline-secondary') }, label);
                    b.addEventListener('click', async function () {
                        if (!await confirmAction(label + ' record ' + id + ' on ' + root.dataset.envLabel + '?')) return;
                        busy(b, true, '...');
                        try { var r = await post(root.dataset.toggleUrl, { key: def.key, env: env, search: state.search, id: id, field: f.column, value: !on }); showResult(msg, r); await load(); }
                        catch (e) { failure(msg, e); } finally { busy(b, false); }
                    });
                    actions.appendChild(b);
                });
                tr.appendChild(actions);
                tbody.appendChild(tr);
            });
            table.appendChild(tbody); wrap.appendChild(table); grid.appendChild(wrap);
        }
        async function load() {
            var button = $('#ivt-search-go');
            busy(button, true, 'Searching...');
            try {
                var r = await get(root.dataset.searchUrl, { key: def.key, env: env, search: searchBox.value.trim() });
                state = { search: r.extra.search, columns: r.columns, rows: r.rows };
                render();
            } catch (e) { failure(msg, e); clear(grid); state.rows = []; count.textContent = ''; }
            finally { busy(button, false); }
        }
        $('#ivt-search-form').addEventListener('submit', function (e) { e.preventDefault(); clear(msg); load(); });
        $('#ivt-save-all').addEventListener('click', async function () {
            var b = this;
            if (!await confirmAction('Save ALL ' + state.rows.length + ' displayed record(s) for search "' + (state.search || 'all') + '" on ' + root.dataset.envLabel + '?\nOnly changed rows are written; records outside this result are never touched.')) return;
            var rows = $$('tbody tr', grid).map(rowValues);
            busy(b, true, 'Saving...');
            try { var r = await post(root.dataset.updateAllUrl, { key: def.key, env: env, search: state.search, expectedCount: state.rows.length, rows: JSON.stringify(rows) }); showResult(msg, r); if (r.ok || r.outcome === 'PARTIAL') await load(); }
            catch (e) { failure(msg, e); } finally { busy(b, false); }
        });
        var createForm = $('#ivt-create');
        def.fields.filter(function (f) { return f.hasCreate && !f.fixedOnCreate; }).forEach(function (f) {
            var holder = $('[data-section="' + f.section + '"]', createForm) || $('[data-section="General"]', createForm);
            var group = el('div', { className: 'ivt-field' });
            var id = 'new-' + f.column;
            group.appendChild(el('label', { 'for': id }, f.label + (f.required ? ' *' : '')));
            var i = input(f, f.kind === 'Boolean' ? false : null);
            i.id = id; i.placeholder = '';
            group.appendChild(i);
            if (f.help) group.appendChild(el('small', { className: 'muted' }, f.help));
            holder.appendChild(group);
        });
        createForm.addEventListener('submit', async function (e) {
            e.preventDefault();
            var values = {};
            $$('[data-field]', createForm).forEach(function (i) { values[i.dataset.field] = i.type === 'checkbox' ? i.checked : i.value; });
            if (!await confirmAction('Create this record on ' + root.dataset.envLabel + '?')) return;
            var b = $('button[type=submit]', createForm);
            busy(b, true, 'Creating...');
            try { var r = await post(root.dataset.createUrl, { key: def.key, env: env, values: JSON.stringify(values) }); showResult(msg, r); if (r.ok) { createForm.reset(); await load(); } }
            catch (err) { failure(msg, err); } finally { busy(b, false); }
        });
        load();
    };

    pages.contacts = function () {
        var msg = $('#ivt-msg');
        $('#ivt-contacts').addEventListener('submit', async function (e) {
            e.preventDefault();
            if (!await confirmAction('Save the shared contacts on ' + root.dataset.envLabel + '?')) return;
            var b = $('button[type=submit]', this);
            busy(b, true, 'Saving...');
            try { showResult(msg, await post(root.dataset.saveUrl, { env: root.dataset.env, cc: $('#cc').value, exec: $('#exec').value, admin: $('#admin').value })); }
            catch (err) { failure(msg, err); } finally { busy(b, false); }
        });
    };

    // ------------------------------------------------------------ reports
    pages.report = function () {
        var msg = $('#ivt-msg'), out = $('#ivt-grid'), count = $('#ivt-count'), form = $('#ivt-report-form'), exportLink = $('#ivt-export');
        function params() {
            var p = { key: root.dataset.key };
            $$('[data-param]', form).forEach(function (i) { p[i.dataset.param] = i.value.trim(); });
            return p;
        }
        var amSelect = $('[data-param="am"]', form), actSelect = $('[data-param="activity"]', form);
        if (amSelect || actSelect) get(root.dataset.listsUrl).then(function (l) {
            if (actSelect) l.activities.forEach(function (a) { actSelect.appendChild(el('option', { value: a }, a)); });
            if (amSelect) l.managers.forEach(function (m) { amSelect.appendChild(el('option', { value: m.login }, (m.name || m.login) + ' (' + m.login + ')')); });
        }).catch(function (e) { failure(msg, e); });
        async function run() {
            var b = $('button[type=submit]', form);
            busy(b, true, 'Loading...'); clear(msg);
            try {
                var p = params();
                var r = await get(root.dataset.dataUrl, p);
                renderTable(out, r.columns, r.rows, { empty: 'No records match these filters.' });
                count.textContent = r.extra.total + ' row(s)' + (r.extra.truncated ? ' - showing the first ' + r.count + '; Export CSV contains all rows' : '') + '. ' + r.extra.filters.join(' | ');
                exportLink.href = root.dataset.exportUrl + '?' + qs(p);
                exportLink.classList.remove('disabled');
            } catch (e) { failure(msg, e); clear(out); count.textContent = ''; exportLink.classList.add('disabled'); }
            finally { busy(b, false); }
        }
        form.addEventListener('submit', function (e) { e.preventDefault(); run(); });
        if (!form.querySelector('[data-param]')) run();
    };

    // ------------------------------------------------------------ manual hold / release
    pages.holdrelease = function () {
        var msg = $('#ivt-msg'), out = $('#ivt-grid'), count = $('#ivt-count');
        var state = { mode: 'hold', search: '', ids: [] };
        function setMode(mode) {
            state.mode = mode;
            $$('[data-mode]').forEach(function (b) { b.classList.toggle('active', b.dataset.mode === mode); });
            $('#ivt-text-label').textContent = mode === 'hold' ? 'Hold comment (required for hold-all; optional for one connote)' : 'Release justification (required)';
            $('#ivt-one').textContent = mode === 'hold' ? 'Hold selected connote' : 'Release selected connote';
            clear(out); count.textContent = ''; state.ids = []; updateAll();
        }
        function updateAll() {
            var b = $('#ivt-all');
            b.textContent = (state.mode === 'hold' ? 'Hold' : 'Release') + ' ALL displayed (' + state.ids.length + ')';
            b.disabled = state.ids.length === 0;
        }
        $$('[data-mode]').forEach(function (b) { b.addEventListener('click', function () { setMode(b.dataset.mode); }); });
        async function load() {
            var b = $('#ivt-search-go');
            busy(b, true, 'Searching...');
            try {
                var search = $('#ivt-search').value.trim();
                var r = await get(root.dataset.candidatesUrl, { mode: state.mode, search: search });
                state.search = search;
                var idIndex = r.columns.map(function (c) { return c.name.toLowerCase(); }).indexOf('id');
                state.ids = r.rows.map(function (row) { return row[idIndex]; });
                renderTable(out, r.columns, r.rows, { select: 'radio', key: 'Id', empty: state.mode === 'hold' ? 'No IVT results match.' : 'No held connotes match.' });
                count.textContent = r.count + ' connote row(s)' + (r.extra.truncated ? ' - too many to act on all; narrow the search' : '');
                updateAll();
            } catch (e) { failure(msg, e); clear(out); state.ids = []; updateAll(); }
            finally { busy(b, false); }
        }
        $('#ivt-search-form').addEventListener('submit', function (e) { e.preventDefault(); clear(msg); load(); });
        async function act(all, button) {
            var text = $('#ivt-text').value.trim();
            var selected = $$('input[name="ivt-select"]:checked', out).map(function (i) { return i.value; });
            if (!all && selected.length !== 1) { alertBox(msg, 'warning', 'Select one connote first.'); return; }
            var verb = state.mode === 'hold' ? 'HOLD' : 'RELEASE';
            var n = all ? state.ids.length : 1;
            if (!await confirmAction(verb + ' ' + n + ' connote row(s) in PRODUCTION (IVT)' + (all ? ' - every row displayed for search "' + (state.search || 'all') + '"' : '') + '?')) return;
            busy(button, true, 'Working...');
            try {
                var r = await post(state.mode === 'hold' ? root.dataset.holdUrl : root.dataset.releaseUrl,
                    { search: state.search, all: all, ids: (all ? state.ids : selected).join(','), expectedCount: all ? state.ids.length : 1, text: text });
                showResult(msg, r);
                if (r.ok || r.outcome === 'PARTIAL') { $('#ivt-text').value = ''; await load(); }
            } catch (e) { failure(msg, e); } finally { busy(button, false); }
        }
        $('#ivt-one').addEventListener('click', function () { act(false, this); });
        $('#ivt-all').addEventListener('click', function () { act(true, this); });
        setMode('hold');
    };

    // ------------------------------------------------------------ comments
    pages.comments = function () {
        var msg = $('#ivt-msg'), out = $('#ivt-grid'), job = $('#ivt-job'), pending = $('#ivt-pending');
        async function load() {
            if (!job.value.trim()) return;
            try {
                var r = await get(root.dataset.logUrl, { job: job.value.trim() });
                renderTable(out, r.columns, r.rows, { empty: 'No log entries for this job yet.' });
                clear(pending);
                (r.extra.pendingNotifications || []).forEach(function (p) {
                    var b = el('button', { type: 'button', className: 'btn btn-sm btn-warning' }, 'Retry notification for comment by ' + p.Operator + ' at ' + fmt(p.when));
                    b.addEventListener('click', async function () {
                        busy(b, true, 'Sending...');
                        try { showResult(msg, await post(root.dataset.retryUrl, { auditId: p.Id })); await load(); }
                        catch (e) { failure(msg, e); } finally { busy(b, false); }
                    });
                    pending.appendChild(b);
                });
            } catch (e) { failure(msg, e); clear(out); }
        }
        $('#ivt-job-form').addEventListener('submit', function (e) { e.preventDefault(); clear(msg); load(); });
        $('#ivt-comment-form').addEventListener('submit', async function (e) {
            e.preventDefault();
            var text = $('#ivt-comment').value.trim();
            if (!job.value.trim() || !text) { alertBox(msg, 'warning', 'Enter the job number and a comment.'); return; }
            var b = $('button[type=submit]', this);
            busy(b, true, 'Saving...');
            try { var r = await post(root.dataset.addUrl, { job: job.value.trim(), text: text }); showResult(msg, r); if (r.ok) $('#ivt-comment').value = ''; await load(); }
            catch (err) { failure(msg, err); } finally { busy(b, false); }
        });
        load();
    };

    // ------------------------------------------------------------ file loads
    pages.files = function () {
        var msg = $('#ivt-msg');
        async function refresh() {
            try {
                var s = await get(root.dataset.statusUrl);
                s.folders.forEach(function (f) {
                    var holder = $('[data-files="' + f.kind + '"]'); if (!holder) return;
                    clear(holder);
                    if (f.error) { holder.appendChild(el('div', { className: 'alert alert-danger' }, f.error)); return; }
                    if (!f.files.length) { holder.appendChild(el('p', { className: 'ivt-empty' }, 'No file waiting.')); return; }
                    var ul = el('ul', { className: 'ivt-files' });
                    f.files.forEach(function (file) {
                        var li = el('li');
                        var a = el('a', { href: root.dataset.fileUrl + '?' + qs({ kind: f.kind, name: file.Name }) }, file.Name);
                        li.appendChild(a);
                        li.appendChild(document.createTextNode(' - ' + Math.round(file.Size / 1024 * 10) / 10 + ' KB, ' + fmt(file.Modified)));
                        ul.appendChild(li);
                    });
                    holder.appendChild(ul);
                });
                $$('[data-job="admin"]').forEach(function (n) { n.textContent = s.adminJob.Name + ': ' + s.adminJob.Summary; });
                $$('[data-job="ref"]').forEach(function (n) { n.textContent = s.refJob.Name + ': ' + s.refJob.Summary; });
                var errs = $('#ivt-loader-errors'); clear(errs);
                if (s.loaderErrors && s.loaderErrors.length) s.loaderErrors.forEach(function (e) {
                    var d = el('details', { open: true }); d.appendChild(el('summary', null, e.name)); d.appendChild(el('pre', null, e.text)); errs.appendChild(d);
                });
                else errs.appendChild(el('p', { className: 'ivt-empty' }, 'No loader error files.'));
                renderTable($('#ivt-recent'), [{ name: 'Audit' }, { name: 'When' }, { name: 'Operator' }, { name: 'Operation' }, { name: 'File / job' }, { name: 'Outcome' }],
                    s.recent.map(function (r) { return [r.Id, r.when, r.Operator, r.Operation, r.Target, r.Outcome]; }), { empty: 'Nothing recorded yet.' });
            } catch (e) { failure(msg, e); }
        }
        $('#ivt-refresh').addEventListener('click', refresh);
        $$('form[data-upload]').forEach(function (form) {
            form.addEventListener('submit', async function (e) {
                e.preventDefault();
                var file = $('input[type=file]', form).files[0];
                if (!file) { alertBox(msg, 'warning', 'Choose a CSV file.'); return; }
                var b = $('button[type=submit]', form);
                busy(b, true, 'Uploading...');
                var fd = new FormData(); fd.append('file', file); fd.append('kind', form.dataset.upload);
                try { showResult(msg, await post(root.dataset.uploadUrl, {}, fd)); form.reset(); refresh(); }
                catch (err) { failure(msg, err); } finally { busy(b, false); }
            });
        });
        $$('[data-start]').forEach(function (button) {
            button.addEventListener('click', async function () {
                var kind = button.dataset.start;
                var confirmBox = $('[data-confirm="' + kind + '"]');
                if (!confirmBox.checked) { alertBox(msg, 'warning', 'Tick the confirmation that the correct file is in the folder.'); return; }
                if (!await confirmAction(button.dataset.prompt)) return;
                busy(button, true, 'Submitting...');
                try { showResult(msg, await post(root.dataset.startUrl, { kind: kind, confirmed: confirmBox.checked })); confirmBox.checked = false; refresh(); }
                catch (err) { failure(msg, err); } finally { busy(button, false); }
            });
        });
        refresh();
    };

    // ------------------------------------------------------------ notifications
    pages.notifications = function () {
        var msg = $('#ivt-msg');
        $$('[data-notify]').forEach(function (button) {
            button.addEventListener('click', async function () {
                if (!await confirmAction(button.dataset.prompt)) return;
                busy(button, true, 'Sending...');
                try {
                    var r = await post(root.dataset.notifyUrl, { operation: button.dataset.notify, sendAgain: false });
                    if (r.data === 'confirm-repeat' && await confirmAction(r.message + '\n\nSend again?')) r = await post(root.dataset.notifyUrl, { operation: button.dataset.notify, sendAgain: true });
                    showResult(msg, r);
                } catch (e) { failure(msg, e); } finally { busy(button, false); }
            });
        });
        var test = $('#ivt-notify-test');
        if (test) test.addEventListener('click', async function () {
            if (!await confirmAction('Run the TEST notification process now?')) return;
            busy(test, true, 'Running (this can take minutes)...');
            try {
                var r = await post(root.dataset.testUrl, {});
                showResult(msg, r);
                if (r.data) { var pre = el('pre', { className: 'ivt-output' }, 'Exit code: ' + r.data.exitCode + (r.data.timedOut ? ' (timed out)' : '') + '\n--- output ---\n' + r.data.output + '\n--- errors ---\n' + r.data.error); msg.appendChild(pre); }
            } catch (e) { failure(msg, e); } finally { busy(test, false); }
        });
    };

    var page = pages[root.dataset.ivtPage];
    if (page) page();
})();
