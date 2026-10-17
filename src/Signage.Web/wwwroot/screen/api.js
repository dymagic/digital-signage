// Calls to /api/player/*. The screen token travels in an HttpOnly cookie the page can't read (ADR-0007).
export class Unauthorized extends Error {}

async function json(response) {
    return response.status === 204 ? null : response.json();
}

export async function startPairing() {
    const response = await fetch('/api/player/pairing', { method: 'POST' });
    if (!response.ok) {
        throw new Error(`pairing ${response.status}`);
    }
    return json(response);
}

/** Resolves to { status: 'pending' | 'paired' | 'expired' }. */
export async function pollPairing(pairingId, deviceSecret) {
    const response = await fetch(`/api/player/pairing/${pairingId}`, { headers: { 'X-Device-Secret': deviceSecret } });
    if (response.status === 410 || response.status === 404) {
        return { status: 'expired' };
    }
    if (!response.ok) {
        throw new Error(`poll ${response.status}`);
    }
    return json(response);
}

/**
 * Exchanges a pre-configured token (from /screen?token=…) for the cookie.
 * Resolves to true (accepted) or false (unknown token); throws when the server can't be reached or fails,
 * so the caller can retry instead of losing the token.
 */
export async function exchangeToken(token) {
    const response = await fetch('/api/player/session', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token }),
    });
    if (response.ok) {
        return true;
    }
    if (response.status === 401) {
        return false;
    }
    throw new Error(`session ${response.status}`);
}

export async function getManifest() {
    const response = await fetch('/api/player/manifest', { cache: 'no-store' });
    if (response.status === 401) {
        throw new Unauthorized();
    }
    if (!response.ok) {
        throw new Error(`manifest ${response.status}`);
    }
    return json(response);
}
