// Things the player had to skip, collected for the next heartbeat (PLAYER_SPEC §4, R-33). Bounded, so a screen that
// can't reach the server doesn't pile them up.
const MAX = 20;
let pending = [];

/** Notes a problem: kind is 'video' or 'single-video', reason e.g. 'error', 'stalled', 'safety cap'. */
export function reportProblem(kind, slideId, reason) {
    console.warn(`Player problem (${kind}${slideId ? ` ${slideId}` : ''}): ${reason}`);
    pending.push({ kind, slideId: slideId ?? null, reason: String(reason) });
    if (pending.length > MAX) {
        pending = pending.slice(-MAX);
    }
}

/** Hands over what was collected; give it back with restoreProblems() if it couldn't be sent. */
export function takeProblems() {
    const taken = pending;
    pending = [];
    return taken;
}

export function restoreProblems(problems) {
    pending = [...problems, ...pending].slice(-MAX);
}
