// Small progressive enhancements for the admin. Everything works without this file.
// Forms with data-confirm ask before submitting (e.g. revoking a screen's access).
document.addEventListener('submit', (e) => {
    const message = e.target instanceof HTMLFormElement ? e.target.dataset.confirm : undefined;
    if (message && !window.confirm(message)) {
        e.preventDefault();
    }
}, true);
