(function () {
    'use strict';
    var body = document.body;
    // Mobile: hamburger opens the sidebar; backdrop, Esc or a link click closes it.
    document.querySelectorAll('[data-sidebar-toggle]').forEach(function (button) {
        button.addEventListener('click', function () {
            var open = !body.classList.contains('sidebar-open');
            body.classList.toggle('sidebar-open', open);
            button.setAttribute('aria-expanded', open ? 'true' : 'false');
        });
    });
    var backdrop = document.querySelector('.app-backdrop');
    if (backdrop) backdrop.addEventListener('click', function () { body.classList.remove('sidebar-open'); });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') body.classList.remove('sidebar-open'); });
    // Collapsible groups (e.g. History).
    document.querySelectorAll('.app-group > button.app-link').forEach(function (button) {
        button.addEventListener('click', function () {
            var group = button.parentElement;
            var open = !group.classList.contains('open');
            group.classList.toggle('open', open);
            button.setAttribute('aria-expanded', open ? 'true' : 'false');
        });
    });
})();
