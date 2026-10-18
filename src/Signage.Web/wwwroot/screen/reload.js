// Daily reload (PLAYER_SPEC §9, ADR-0008): bounds slow memory growth in browsers that run for weeks.
// Due after 20 h of uptime inside 02:00–05:00 (system time zone), or after 26 h at any time. A cycling carousel
// reloads at its next transition, a static screen at the check. Immediately before reloading, GET /screen must
// answer 2xx; otherwise the reload waits for the next check, so an offline screen keeps playing (R-24).
import { config } from './config.js';

const startedAt = performance.now();

function localHour(timeZone) {
    try {
        return Number(new Intl.DateTimeFormat('en-GB', { hour: 'numeric', hourCycle: 'h23', timeZone }).format(new Date()));
    } catch {
        return new Date().getHours();
    }
}

export function isDue(uptimeMs, hour) {
    const c = config.dailyReload;
    return uptimeMs >= c.forceAfterMs || (uptimeMs >= c.minUptimeMs && hour >= c.windowStartHour && hour < c.windowEndHour);
}

async function serverAnswers() {
    try {
        const response = await fetch('/screen', { cache: 'no-store' });
        return response.ok;
    } catch {
        return false;
    }
}

async function reloadIfServerAnswers() {
    if (await serverAnswers()) {
        location.reload();
        await new Promise(() => {}); // hold the carousel until the page goes
    }
}

/**
 * Reloads at the carousel's next transition, or now if it doesn't transition (one slide, empty, single video); only
 * when the server answers 2xx right before (PLAYER_SPEC §9). Used for a new app version (R-26). If the server doesn't
 * answer at that moment (a rollout in progress), it tries again at the next transition or after a minute, until it
 * succeeds: the new version is never left waiting for the daily reload.
 */
export function reloadSoon(carousel, beforeReload) {
    let armed = false;
    let watch = 0;
    const arm = () => {
        if (armed) {
            return;
        }
        armed = true;
        if (carousel.isCycling) {
            carousel.runBeforeNextTransition(reload);
            // If the carousel stops cycling before that transition (one slide left, single video), take it back.
            watch = setInterval(() => {
                if (armed && !carousel.isCycling) {
                    carousel.runBeforeNextTransition(null);
                    reload();
                }
            }, config.dailyReload.checkEveryMs);
        } else {
            setTimeout(reload, 0);
        }
    };
    const reload = async () => {
        armed = false;
        clearInterval(watch);
        if (await serverAnswers()) {
            beforeReload?.();
            location.reload();
            await new Promise(() => {}); // hold the carousel until the page goes
        }
        setTimeout(arm, config.dailyReload.checkEveryMs); // not now: try again in a minute
    };
    arm();
}

/** Starts the once-a-minute check. getTimeZone() returns the system time zone from the current manifest. */
export function startDailyReload(carousel, getTimeZone) {
    let scheduled = false;
    setInterval(() => {
        if (!isDue(performance.now() - startedAt, localHour(getTimeZone()))) {
            return;
        }
        if (carousel.isCycling) {
            if (!scheduled) {
                scheduled = true;
                carousel.runBeforeNextTransition(async () => {
                    scheduled = false;
                    await reloadIfServerAnswers();
                });
            }
        } else {
            // Static (one slide or empty), possibly after the reload was handed to a carousel that has stopped cycling
            // since: take it back and reload at the check.
            scheduled = false;
            carousel.runBeforeNextTransition(null);
            reloadIfServerAnswers();
        }
    }, config.dailyReload.checkEveryMs);
}
