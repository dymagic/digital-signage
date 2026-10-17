// Every timing constant and look-and-feel value of the player lives here (PLAYER_SPEC). Tests may speed time up with
// ?test=1&timeScale=0.1; the scale is ignored without test=1 and never applies to the daily reload (real hours).
const params = new URLSearchParams(location.search);
const testMode = params.get('test') === '1';
const requestedScale = Number(params.get('timeScale'));

export const timeScale = testMode && requestedScale > 0 ? requestedScale : 1;

/** Scales a duration in milliseconds for test runs. */
export const ms = (value) => Math.max(1, Math.round(value * timeScale));

const HOUR = 3_600_000;

export const config = Object.freeze({
    // Carousel (PLAYER_SPEC §2)
    transitionMs: 900,
    transitionEasing: 'cubic-bezier(0.16, 1, 0.3, 1)',
    transitionShadow: '-24px 0 48px -8px rgba(0, 0, 0, 0.45)',
    transitionFallbackExtraMs: 400, // if transitionend never fires
    preloadGraceMs: 3000, // wait this much longer for an unready next slide, then go anyway
    maxLayers: 3,
    iframeLoadTimeoutMs: 10_000,
    staticRecheckMs: 1000, // one slide or empty: how often to look for schedule changes

    // Stage (PLAYER_SPEC §1)
    cursorHideMs: 3000,

    // Pairing (PLAYER_SPEC §8)
    pairingPollMs: 3000,
    pairingRefreshBeforeExpiryMs: 60_000,

    // Connectivity (PLAYER_SPEC §9)
    manifestPollMs: 60_000,
    retryAfterErrorMs: 5000,
    manifestCacheKey: 'signage.manifest',

    // Daily reload (PLAYER_SPEC §9, ADR-0008). Real time, never scaled.
    dailyReload: Object.freeze({
        checkEveryMs: 60_000,
        minUptimeMs: 20 * HOUR,
        windowStartHour: 2, // local time in the system time zone, inclusive
        windowEndHour: 5, // exclusive
        forceAfterMs: 26 * HOUR,
    }),
});
