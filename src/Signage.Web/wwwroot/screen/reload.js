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

/** Starts the once-a-minute check. getTimeZone() returns the system time zone from the current manifest. */
export function startDailyReload(carousel, getTimeZone) {
    let scheduled = false;
    setInterval(() => {
        if (scheduled || !isDue(performance.now() - startedAt, localHour(getTimeZone()))) {
            return;
        }
        if (carousel.isCycling) {
            scheduled = true;
            carousel.runBeforeNextTransition(async () => {
                scheduled = false;
                await reloadIfServerAnswers();
            });
        } else {
            reloadIfServerAnswers();
        }
    }, config.dailyReload.checkEveryMs);
}
