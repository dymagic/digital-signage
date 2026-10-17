// The empty state (R-23, PLAYER_SPEC §10): calm and dark, the screen's name small at the top left, a large thin
// clock in the system time zone. Never an error.
let clockTimer = null;

function formatTime(timeZone) {
    try {
        return new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone }).format(new Date());
    } catch {
        return new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(new Date());
    }
}

export function showEmpty(stage, manifest) {
    let view = stage.querySelector('.empty-state');
    if (!view) {
        view = document.createElement('section');
        view.className = 'empty-state';
        view.innerHTML = '<p class="name" data-testid="empty-name"></p><p class="clock" data-testid="empty-clock"></p>';
        stage.prepend(view);
    }
    view.querySelector('.name').textContent = manifest?.screen?.name ?? '';
    const clock = view.querySelector('.clock');
    const tick = () => {
        clock.textContent = formatTime(manifest?.settings?.timeZone);
    };
    tick();
    clearInterval(clockTimer);
    clockTimer = setInterval(tick, 1000);
}

export function hideEmpty(stage) {
    clearInterval(clockTimer);
    clockTimer = null;
    stage.querySelector('.empty-state')?.remove();
}
