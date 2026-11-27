(function () {
    'use strict';
    var root = document.getElementById('ivt-chat');
    if (!root) return;
    var panel = root.querySelector('.ivt-chat-panel');
    var launcher = root.querySelector('.ivt-chat-launch');
    var form = root.querySelector('form');
    var input = root.querySelector('textarea');
    var messages = root.querySelector('.ivt-chat-messages');
    var status = root.querySelector('.ivt-chat-status');
    var scopeLabel = root.querySelector('.ivt-chat-scope');
    var suggestions = root.querySelector('.ivt-chat-suggestions');
    var token = null;
    var busy = false;
    var restoredToken = null;  // conversation whose history is already on screen
    // Per-browser UI preferences only (open state, size); the conversation itself is kept on the server.
    function pref(key, value) {
        try {
            if (value === undefined) return window.localStorage.getItem('ivtChat.' + key);
            if (value === null) window.localStorage.removeItem('ivtChat.' + key); else window.localStorage.setItem('ivtChat.' + key, value);
        } catch (e) { return null; }
        return null;
    }

    function element(tag, text, className) {
        var item = document.createElement(tag);
        if (text !== undefined && text !== null) item.textContent = String(text);
        if (className) item.className = className;
        return item;
    }
    function setBusy(value) {
        busy = value;
        form.querySelectorAll('button').forEach(function (button) { button.disabled = value; });
        suggestions.querySelectorAll('button').forEach(function (button) { button.disabled = value; });
        input.disabled = value;
        panel.setAttribute('aria-busy', value ? 'true' : 'false');
    }
    function clear() {
        messages.textContent = '';
        token = null;
        restoredToken = null;
        suggestions.hidden = false;
    }
    async function request(url, values) {
        var options = { credentials: 'same-origin', cache: 'no-store', headers: { 'Accept': 'application/json' } };
        if (values) {
            var body = new URLSearchParams();
            Object.keys(values).forEach(function (key) { body.set(key, values[key]); });
            body.set('__RequestVerificationToken', form.querySelector('[name="__RequestVerificationToken"]').value);
            options.method = 'POST';
            options.body = body;
        }
        var abort = new AbortController();
        options.signal = abort.signal;
        var timer = setTimeout(function () { abort.abort(); }, 95000);
        try {
            var response = await fetch(url, options);
            if (response.redirected || !(response.headers.get('Content-Type') || '').includes('application/json')) {
                clear();
                throw new Error('Your session may have expired. Refresh the page and sign in again.');
            }
            var result = await response.json();
            if (!response.ok) {
                if ([401, 403, 409].indexOf(response.status) !== -1) clear();
                throw new Error(result.error || 'The assistant is unavailable. Please try again.');
            }
            return result;
        } finally { clearTimeout(timer); }
    }
    async function context() {
        var result = await request(root.dataset.contextUrl);
        if (token && token !== result.token) {
            clear();
            status.textContent = 'Your account scope or conversation changed. A new conversation has started.';
        }
        token = result.token;
        scopeLabel.textContent = result.scope;
        // Restore the conversation after moving to another page (source tables are not kept; ask again for them).
        if (restoredToken !== token) {
            restoredToken = token;
            if (!messages.children.length && result.history && result.history.length) {
                result.history.forEach(function (turn) {
                    message('user', turn.question);
                    feedback(message('assistant', turn.answer), turn.id, turn.rating);
                });
                if (result.pendingAction) pendingCard(messages.lastElementChild, result.pendingAction);
                messages.scrollTop = messages.scrollHeight;
            }
        }
    }
    // Thumbs up/down with an optional comment; stored for IVT admins to improve the assistant.
    function feedback(parent, turnId, rating) {
        if (!turnId || !root.dataset.feedbackUrl) return;
        var bar = element('div', null, 'ivt-chat-feedback');
        bar.appendChild(element('span', 'Was this helpful?'));
        var up = element('button', '\ud83d\udc4d'), down = element('button', '\ud83d\udc4e');
        up.type = down.type = 'button';
        up.title = 'Helpful'; down.title = 'Not helpful - tell us what was wrong';
        up.setAttribute('aria-label', 'Helpful'); down.setAttribute('aria-label', 'Not helpful');
        bar.appendChild(up); bar.appendChild(down);
        function mark(value) {
            up.classList.toggle('chosen', value === 1);
            down.classList.toggle('chosen', value === -1);
        }
        if (rating) mark(rating);
        function ask(value) {
            var existing = bar.querySelector('.ivt-chat-feedback-form');
            if (existing) existing.remove();
            var box = element('div', null, 'ivt-chat-feedback-form');
            var comment = element('textarea');
            comment.rows = 2; comment.maxLength = 2000;
            comment.placeholder = value === 1 ? 'Optional comment' : 'What was wrong or what did you expect? e.g. "trading value means revenue", "BlueStar is BSTARGL"';
            var submit = element('button', 'Send feedback'); submit.type = 'button';
            box.appendChild(comment); box.appendChild(submit); bar.appendChild(box);
            comment.focus();
            submit.addEventListener('click', async function () {
                submit.disabled = true;
                try {
                    var result = await request(root.dataset.feedbackUrl, { token: token, turnId: turnId, rating: value, comment: comment.value.trim() });
                    mark(value);
                    box.replaceWith(element('span', result.message));
                } catch (error) { submit.disabled = false; status.textContent = error.message; }
            });
        }
        up.addEventListener('click', function () { ask(1); });
        down.addEventListener('click', function () { ask(-1); });
        parent.appendChild(bar);
    }
    function message(role, text) {
        var item = element('div', null, 'ivt-chat-message ivt-chat-message-' + role);
        item.appendChild(element('strong', role === 'user' ? 'You' : 'Vera'));
        if (role === 'assistant' && window.IvtChatAnalytics) {
            window.IvtChatAnalytics.renderAnswer(item, text, function (question) {
                if (busy) return;
                input.value = question; input.focus();
            });
        } else item.appendChild(element('p', text));
        messages.appendChild(item);
        suggestions.hidden = true;
        messages.scrollTop = messages.scrollHeight;
        return item;
    }
    function table(parent, rows, columns) {
        if (!rows || !rows.length) return;
        var wrap = element('div', null, 'ivt-chat-table-wrap');
        var grid = element('table');
        var head = element('tr');
        columns.forEach(function (column) { var th = element('th', column[1]); th.scope = 'col'; head.appendChild(th); });
        var thead = element('thead'); thead.appendChild(head); grid.appendChild(thead);
        var tbody = element('tbody');
        rows.forEach(function (row) {
            var tr = element('tr');
            columns.forEach(function (column) { tr.appendChild(element('td', row[column[0]] == null ? '\u2014' : row[column[0]])); });
            tbody.appendChild(tr);
        });
        grid.appendChild(tbody); wrap.appendChild(grid); parent.appendChild(wrap);
        if (window.IvtChatAnalytics) window.IvtChatAnalytics.csvButton(parent, rows, columns.map(function (column) { return column[0]; }), columns.map(function (column) { return column[1]; }), 'source-table');
    }
    function evidence(parent, sources) {
        if (!sources || !sources.length) return;
        if (window.IvtChatAnalytics) window.IvtChatAnalytics.renderEvidence(parent, sources);
        var details = element('details');
        details.appendChild(element('summary', 'Source figures and reporting period'));
        sources.forEach(function (source) {
            var data = source.Data;
            var result = data.result;
            if (!result) return;
            var card = element('div', null, 'ivt-chat-source');
            card.appendChild(element('strong', data.source + ' \u00b7 ' + data.scope));
            card.appendChild(element('p', 'Reporting week: ' + (data.reportingWeek ? data.reportingWeek.slice(0, 10) : 'Not available')));
            var refresh = data.refreshedTo ? data.refreshedTo.replace('T', ' ') : 'Not available';
            if (data.refreshedFrom && data.refreshedFrom !== data.refreshedTo) refresh = data.refreshedFrom.replace('T', ' ') + ' to ' + refresh;
            card.appendChild(element('p', 'Dashboard refreshed: ' + refresh + ' (database local time)'));
            if (result.totalExceptionOccurrences !== undefined) card.appendChild(element('p', 'Exception occurrences: ' + result.totalExceptionOccurrences));
            if (result.current !== undefined) card.appendChild(element('p', 'Current: ' + result.current + ' \u00b7 Previous: ' + result.previous + ' \u00b7 Change: ' + result.change));
            table(card, result.rules || result.byRule, [['Code', 'Rule'], ['Description', 'Description'], ['Current', 'Current'], ['Previous', 'Previous']]);
            table(card, result.customers, [['Customer', 'Customer'], ['Current', 'Current'], ['Previous', 'Previous'], ['change', 'Change']]);
            table(card, result.exceptions, [['connote', 'Connote'], ['customer', 'Customer'], ['ruleCode', 'Rule'], ['description', 'Description']]);
            table(card, result.definitions, [['code', 'Rule'], ['description', 'Definition'], ['priority', 'Priority']]);
            if (result.truncated) card.appendChild(element('p', 'Only the first matching results are shown.'));
            if (result.metric || result.note) card.appendChild(element('p', result.metric || result.note));
            card.appendChild(element('p', data.periodNote));
            details.appendChild(card);
        });
        parent.appendChild(details);
    }
    // Hold/release prepared by the assistant: nothing changes until the user presses Confirm here.
    function pendingCard(parent, action) {
        if (!action) return;
        var card = element('div', null, 'ivt-chat-action');
        card.setAttribute('role', 'group');
        card.setAttribute('aria-label', 'Confirm ' + action.kind.toLowerCase());
        var label = { HOLD: 'Confirm hold', RELEASE: 'Confirm release', BULK_RELEASE: 'Confirm bulk release', VETTING: 'Confirm vetting completed', EMAIL: 'Confirm and send email' }[action.kind] || 'Confirm';
        card.appendChild(element('strong', label));
        card.appendChild(element('p', action.summary));
        var buttons = element('div', null, 'ivt-chat-action-buttons');
        var confirm = element('button', label, 'ivt-chat-confirm');
        var cancel = element('button', 'Cancel', 'ivt-chat-cancel');
        confirm.type = cancel.type = 'button';
        buttons.appendChild(confirm); buttons.appendChild(cancel); card.appendChild(buttons);
        var outcome = element('p', null, 'ivt-chat-action-outcome');
        outcome.setAttribute('role', 'status');
        card.appendChild(outcome);
        function done(text) { confirm.remove(); cancel.remove(); outcome.textContent = text; }
        confirm.addEventListener('click', async function () {
            if (busy) return;
            setBusy(true); confirm.disabled = cancel.disabled = true;
            status.textContent = 'Applying the change\u2026';
            try {
                var result = await request(root.dataset.confirmUrl, { token: token, actionId: action.id });
                done('\u2714 ' + result.message);
                status.textContent = '';
            } catch (error) {
                done('Not applied: ' + error.message);
                status.textContent = '';
            } finally { setBusy(false); }
        });
        cancel.addEventListener('click', async function () {
            if (busy) return;
            try { await request(root.dataset.cancelUrl, { token: token }); } catch (error) { /* nothing was pending server-side */ }
            done('Cancelled. Nothing was changed.');
        });
        parent.appendChild(card);
    }
    // Emails the latest answer (and its source figures as CSV) to the signed-in user's own mailbox.
    function emailButton(parent) {
        messages.querySelectorAll('.ivt-chat-email').forEach(function (b) { b.remove(); });  // only the latest answer can be emailed
        var button = element('button', '\u2709 Email this answer', 'ivt-chat-email');
        button.type = 'button';
        button.title = 'Send this answer and its source figures (CSV) to your own mailbox';
        button.addEventListener('click', async function () {
            if (busy) return;
            setBusy(true); button.disabled = true; button.textContent = 'Sending\u2026';
            try {
                var result = await request(root.dataset.emailUrl, { token: token });
                button.replaceWith(element('p', '\u2714 ' + result.message, 'ivt-chat-action-outcome'));
            } catch (error) {
                button.disabled = false; button.textContent = '\u2709 Email this answer';
                status.textContent = error.message;
            } finally { setBusy(false); }
        });
        parent.appendChild(button);
    }
    async function send() {
        var question = input.value.trim();
        if (busy || !question) return;
        setBusy(true);
        status.textContent = 'Checking your data\u2026';
        try {
            if (!token) await context();
            message('user', question);
            var result = await request(root.dataset.askUrl, { question: question, token: token });
            if (result.token !== token) { clear(); throw new Error('Your conversation changed. Please ask again.'); }
            scopeLabel.textContent = result.scope;
            var reply = message('assistant', result.answer);
            evidence(reply, result.evidence);
            // Only the newest proposal can be confirmed; older cards are disabled.
            messages.querySelectorAll('.ivt-chat-confirm').forEach(function (b) { b.disabled = true; b.title = 'Superseded by a newer request'; });
            pendingCard(reply, result.pendingAction);
            if (result.canEmail && !result.pendingAction && root.dataset.emailUrl) emailButton(reply);
            feedback(reply, result.turnId);
            restoredToken = token;
            input.value = '';
            status.textContent = '';
        } catch (error) {
            status.textContent = error.name === 'AbortError' ? 'The request timed out. Please try again.' : error.message;
        } finally { setBusy(false); input.focus(); }
    }
    async function open() {
        panel.hidden = false;
        launcher.setAttribute('aria-expanded', 'true');
        pref('open', '1');
        if (!busy) {
            setBusy(true);
            try { await context(); } catch (error) { scopeLabel.textContent = 'Not available'; status.textContent = error.message; }
            finally { setBusy(false); input.focus(); }
        }
    }
    function close() { panel.hidden = true; launcher.setAttribute('aria-expanded', 'false'); pref('open', null); launcher.focus(); }

    // Maximise and drag-to-resize (top-left grip, since the panel is anchored bottom-right); remembered per browser.
    var maxButton = root.querySelector('.ivt-chat-max-toggle');
    function maximise(on) {
        panel.classList.toggle('ivt-chat-max', on);
        maxButton.setAttribute('aria-pressed', on ? 'true' : 'false');
        maxButton.title = on ? 'Restore size' : 'Maximise';
        maxButton.innerHTML = on ? '<i class="fas fa-compress-alt"></i>' : '<i class="fas fa-expand-alt"></i>';
        pref('max', on ? '1' : null);
    }
    maxButton.addEventListener('click', function () { maximise(!panel.classList.contains('ivt-chat-max')); });
    if (pref('max') === '1') maximise(true);
    var size = (pref('size') || '').split('x');
    if (size.length === 2 && +size[0] > 0 && +size[1] > 0) { panel.style.width = size[0] + 'px'; panel.style.height = size[1] + 'px'; }
    var grip = root.querySelector('.ivt-chat-grip');
    grip.addEventListener('pointerdown', function (event) {
        event.preventDefault();
        var startX = event.clientX, startY = event.clientY, startW = panel.offsetWidth, startH = panel.offsetHeight;
        grip.setPointerCapture(event.pointerId);
        function move(e) {
            var w = Math.max(340, Math.min(window.innerWidth - 40, startW + (startX - e.clientX)));
            var h = Math.max(360, Math.min(window.innerHeight - 100, startH + (startY - e.clientY)));
            panel.style.width = w + 'px'; panel.style.height = h + 'px';
        }
        function up() {
            grip.removeEventListener('pointermove', move);
            grip.removeEventListener('pointerup', up);
            pref('size', panel.offsetWidth + 'x' + panel.offsetHeight);
        }
        grip.addEventListener('pointermove', move);
        grip.addEventListener('pointerup', up);
    });

    // "Teach the assistant": the user's own glossary terms.
    var termsButton = root.querySelector('.ivt-chat-terms-toggle');
    var terms = root.querySelector('.ivt-chat-terms');
    async function loadTerms() {
        var list = terms.querySelector('.ivt-chat-term-list');
        list.textContent = '';
        try {
            var result = await request(root.dataset.termsUrl);
            if (!result.terms.length) list.appendChild(element('li', 'You have not added any terms yet.', 'muted'));
            result.terms.forEach(function (t) {
                var li = element('li');
                li.appendChild(element('strong', t.term));
                li.appendChild(document.createTextNode(' = ' + t.meaning + ' '));
                li.appendChild(element('span', t.status === 'APPROVED' ? '(approved for everyone)' : t.status === 'REJECTED' ? '(not approved)' : '(yours, awaiting review)', 'muted'));
                var del = element('button', '\u00d7'); del.type = 'button'; del.title = 'Delete';
                del.addEventListener('click', async function () {
                    try { await request(root.dataset.deleteTermUrl, { id: t.id }); loadTerms(); } catch (error) { status.textContent = error.message; }
                });
                if (t.status !== 'APPROVED') li.appendChild(del);
                list.appendChild(li);
            });
        } catch (error) { list.appendChild(element('li', error.message, 'muted')); }
    }
    termsButton.addEventListener('click', function () {
        terms.hidden = !terms.hidden;
        termsButton.setAttribute('aria-expanded', terms.hidden ? 'false' : 'true');
        if (!terms.hidden) { loadTerms(); terms.querySelector('.ivt-chat-term').focus(); }
    });
    terms.querySelector('.ivt-chat-term-save').addEventListener('click', async function () {
        var term = terms.querySelector('.ivt-chat-term'), meaning = terms.querySelector('.ivt-chat-meaning');
        if (!term.value.trim() || !meaning.value.trim()) { status.textContent = 'Enter a term and its meaning.'; return; }
        try {
            var result = await request(root.dataset.addTermUrl, { term: term.value.trim(), meaning: meaning.value.trim(), kind: terms.querySelector('.ivt-chat-kind').value });
            term.value = ''; meaning.value = '';
            status.textContent = result.message;
            loadTerms();
        } catch (error) { status.textContent = error.message; }
    });
    launcher.addEventListener('click', function () { if (panel.hidden) open(); else close(); });
    root.querySelector('.ivt-chat-close').addEventListener('click', close);
    // "i" help: what the assistant covers, with example questions that can be clicked to ask.
    var helpButton = root.querySelector('.ivt-chat-help-toggle');
    var help = root.querySelector('.ivt-chat-help');
    if (helpButton && help) {
        helpButton.addEventListener('click', function () {
            help.hidden = !help.hidden;
            helpButton.setAttribute('aria-expanded', help.hidden ? 'false' : 'true');
        });
        help.querySelectorAll('[data-example]').forEach(function (example) {
            example.addEventListener('click', function () {
                input.value = example.getAttribute('data-example');
                help.hidden = true; helpButton.setAttribute('aria-expanded', 'false');
                input.focus();
            });
        });
    }
    root.addEventListener('keydown', function (event) { if (event.key === 'Escape') close(); });
    form.addEventListener('submit', function (event) { event.preventDefault(); send(); });
    input.addEventListener('keydown', function (event) {
        if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) { event.preventDefault(); send(); }
    });
    suggestions.querySelectorAll('button').forEach(function (button) {
        button.addEventListener('click', function () { input.value = button.textContent; send(); });
    });
    root.querySelector('.ivt-chat-reset').addEventListener('click', async function () {
        if (busy) return;
        setBusy(true);
        try { await request(root.dataset.resetUrl, {}); clear(); await context(); status.textContent = ''; }
        catch (error) { status.textContent = error.message; }
        finally { setBusy(false); input.focus(); }
    });
    // Re-open with the conversation restored when the user moves to another IVT page.
    if (pref('open') === '1') open();
    window.addEventListener('focus', async function () {
        if (panel.hidden || busy) return;
        setBusy(true);
        try { await context(); } catch (error) { clear(); scopeLabel.textContent = 'Not available'; status.textContent = error.message; }
        finally { setBusy(false); }
    });
})();
