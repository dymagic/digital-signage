// Drag-and-drop reordering for lists marked with data-reorder (playlist slides, later screen and group lists).
// The up/down buttons stay the keyboard-accessible way; dragging is a shortcut for mouse users.
// On drop, posts { entryId, toIndex, Version } to data-move-url through htmx, which swaps in the new list.
(() => {
    'use strict';

    let dragged = null;

    function indexOf(li) {
        return Number(li.dataset.index);
    }

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
            if (!dragged || !over || over === dragged) {
                return;
            }
            e.preventDefault();
            clearMarkers(list);
            const rect = over.getBoundingClientRect();
            over.classList.add(e.clientY < rect.top + rect.height / 2 ? 'drop-before' : 'drop-after');
        });

        list.addEventListener('drop', (e) => {
            const over = e.target.closest('li[draggable]');
            if (!dragged || !over || over === dragged) {
                return;
            }
            e.preventDefault();
            const from = indexOf(dragged);
            let to = indexOf(over) + (over.classList.contains('drop-after') ? 1 : 0);
            if (to > from) {
                to -= 1; // the dragged item leaves its old slot first
            }
            clearMarkers(list);
            if (to === from) {
                return;
            }
            const version = document.getElementById('playlist-version')?.value ?? '';
            htmx.ajax('POST', list.dataset.moveUrl, {
                source: list,
                target: '#entries',
                swap: 'outerHTML',
                values: { entryId: dragged.dataset.entryId, toIndex: String(to), Version: version },
            });
        });
    }

    function init(root) {
        root.querySelectorAll('[data-reorder]').forEach(setup);
    }

    document.addEventListener('DOMContentLoaded', () => init(document));
    document.addEventListener('htmx:afterSwap', (e) => init(e.target.parentElement ?? document));
})();
