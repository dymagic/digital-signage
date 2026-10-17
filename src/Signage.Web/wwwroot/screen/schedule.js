// R-03 schedule windows, re-checked on the device so offline playback respects them (R-24).
// Half-open like the server (SlideSchedule.cs): active from activeFromUtc (inclusive) until activeUntilUtc (exclusive).

/** Slide types this player version can show. */
const supported = new Set(['Image', 'Video', 'WebPage', 'Html']);

export function isActive(slide, nowMs) {
    const from = slide.activeFromUtc ? Date.parse(slide.activeFromUtc) : null;
    const until = slide.activeUntilUtc ? Date.parse(slide.activeUntilUtc) : null;
    return (from === null || nowMs >= from) && (until === null || nowMs < until);
}

/** The slides to cycle through right now, in sequence order (duplicates kept). */
export function playable(manifest, nowMs) {
    return (manifest?.slides ?? []).filter((slide) => supported.has(slide.type) && isActive(slide, nowMs));
}
