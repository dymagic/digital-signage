// Ordered lists edited with htmx (ADR-0006): playlist slides, a screen's prepended/appended slides and
// playlists, a group's playlists.
//
// 1. Drag and drop for lists marked data-reorder. On drop it posts { entryId, toIndex, Version } to
//    data-move-url and swaps data-target (default #entries). The version comes from data-version-input
//    (default #playlist-version). The up/down buttons stay the keyboard-accessible way.
// 2. Focus is kept across swaps: the focused control is found again by id after the list is re-rendered. If it
//    is gone or disabled (moved to the top, removed), focus goes to its sibling or the list's data-focus-fallback.
(() => {
    'use strict';

    let dragged = null;
    let focusedId = null;

    function clearMarkers(list) {
        list.querySelectorAll('.drop-before, .drop-after').forEach((el) => el.classList.remove('drop-before', 'drop-after'));
    }

    function setup(list) {
        if (list.dataset.reorderReady) {
            return;
        }
        list.dataset.reorderReady = 'true';

        list.addEventListener('dragstart', (e) => {
            dragged = e.target.closest('li[draggable]');
            if (!dragged) {
                return;
            }
            dragged.classList.add('dragging');
            e.dataTransfer.effectAllowed = 'move';
            e.dataTransfer.setData('text/plain', dragged.dataset.entryId);
        });

        list.addEventListener('dragend', () => {
            dragged?.classList.remove('dragging');
            dragged = null;
            clearMarkers(list);
        });

        list.addEventListener('dragover', (e) => {
            const over = e.target.closest('li[draggable]');
            if (!dragged || !over || over === dragged || over.parentElement !== dragged.parentElement) {
                return;
            }
            e.preventDefault();
            clearMarkers(list);
            const rect = over.getBoundingClientRect();
            over.classList.add(e.clientY < rect.top + rect.height / 2 ? 'drop-before' : 'drop-after');
        });

        list.addEventListener('drop', (e) => {
            const over = e.target.closest('li[draggable]');
            if (!dragged || !over || over === dragged || over.parentElement !== dragged.parentElement) {
                return;
            }
            e.preventDefault();
            const from = Number(dragged.dataset.index);
            let to = Number(over.dataset.index) + (over.classList.contains('drop-after') ? 1 : 0);
            if (to > from) {
                to -= 1; // the dragged item leaves its old slot first
            }
            clearMarkers(list);
            if (to === from) {
                return;
            }
            const versionInput = document.querySelector(list.dataset.versionInput || '#playlist-version');
            htmx.ajax('POST', list.dataset.moveUrl, {
                source: list,
                target: list.dataset.target || '#entries',
                swap: 'outerHTML',
                values: { entryId: dragged.dataset.entryId, toIndex: String(to), Version: versionInput?.value ?? '' },
            });
        });
    }

    function init(root) {
        root.querySelectorAll('[data-reorder]').forEach(setup);
    }

    function restoreFocus() {
        if (!focusedId) {
            return;
        }
        const id = focusedId;
        focusedId = null;
        const usable = (el) => el && !el.disabled;
        let target = document.getElementById(id);
        if (!usable(target)) {
            // "up-<id>" <-> "down-<id>": after moving to an end, the other direction is the useful one.
            const twin = id.startsWith('up-') ? id.replace('up-', 'down-') : id.startsWith('down-') ? id.replace('down-', 'up-') : null;
            target = twin ? document.getElementById(twin) : null;
        }
        if (!usable(target)) {
            target = document.querySelector('[data-focus-fallback]');
        }
        target?.focus();
    }

    document.addEventListener('DOMContentLoaded', () => init(document));
    document.addEventListener('htmx:beforeRequest', () => {
        focusedId = document.activeElement?.id || null;
    });
    document.addEventListener('htmx:afterSettle', (e) => {
        init(e.target.parentElement ?? document);
        restoreFocus();
    });
})();
