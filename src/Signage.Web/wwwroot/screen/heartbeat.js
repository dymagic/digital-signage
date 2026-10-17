// Heartbeat every 30 s (PLAYER_SPEC §9, R-33): the server records last seen, IP, user agent and player version; the
// player adds the slide on screen and any problems since the last heartbeat. Failures are quiet (R-24).
import { config, ms } from './config.js';
import { takeProblems, restoreProblems } from './problems.js';

export function startHeartbeat({ playerVersion, currentSlideId }) {
    const beat = async () => {
        const problems = takeProblems();
        try {
            const response = await fetch('/api/player/heartbeat', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                cache: 'no-store',
                body: JSON.stringify({ playerVersion, currentSlideId: currentSlideId() ?? null, problems }),
            });
            if (!response.ok) {
                restoreProblems(problems);
            }
        } catch {
            restoreProblems(problems);
        }
    };
    beat();
    setInterval(beat, ms(config.heartbeatMs));
}
