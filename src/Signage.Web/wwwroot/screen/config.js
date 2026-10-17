// Every timing constant of the player lives here (PLAYER_SPEC). Tests may speed time up with
// ?test=1&timeScale=0.1; the scale is ignored without test=1.
const params = new URLSearchParams(location.search);
const testMode = params.get('test') === '1';
const requestedScale = Number(params.get('timeScale'));

export const timeScale = testMode && requestedScale > 0 ? requestedScale : 1;

/** Scales a duration in milliseconds for test runs. */
export const ms = (value) => Math.max(1, Math.round(value * timeScale));

export const config = Object.freeze({
    pairingPollMs: 3000,
    // Ask for a fresh code this long before the current one expires (PLAYER_SPEC §8).
    pairingRefreshBeforeExpiryMs: 60_000,
    manifestPollMs: 60_000,
    retryAfterErrorMs: 5000,
});
