# ADR-0007: Pairing and screen tokens

- Status: Accepted
- Date: 2026-10-17
- Work package: WP-06
- Requirements: R-40, R-41, R-42, R-53, R-63

## Context
R-40 says a paired device "keeps a token in localStorage". ARCHITECTURE §1 and §4 say the token is
"delivered to the player as an HttpOnly cookie" and that tokens are "stored hashed" (R-53). The brief
doesn't say what an admin does to re-pair, where a pre-configured token comes from, or how pairing
endpoints are protected against guessing.

## Options considered
1. **Token in localStorage, sent as a header**: matches the R-40 wording, but any script that runs in the
   page can read it. HTML slides are sandboxed without same-origin (R-05), but one mistake there would leak the
   screen's identity for good.
2. **HttpOnly cookie** (ARCHITECTURE): no script can read it. The browser keeps it across restarts like localStorage,
   so pairing survives a reboot. The server can delete it on 401.

For delivery: store the token (encrypted) at claim time and hand it out on the next poll, vs. **mint it at delivery
time**.

## Decision
- Option 2. The token is a 256-bit random value in the `signage.screen` cookie: `HttpOnly`, `SameSite=Lax`,
  `Secure` on HTTPS, `Path=/api/player`, valid for 10 years. Only its SHA-256 hash is stored (`screens.token_hash`).
  A 401 from the player API deletes the cookie, and the player falls back to pairing. The user-visible behaviour
  of R-40 ("the device stays paired") is the same. Only the storage mechanism differs from the brief's wording.
- Pairing: `POST /api/player/pairing` returns a 6-character code (alphabet without look-alikes, shown
  `XXX-XXX`, valid 15 min), a pairing id and a device secret (hashed at rest). The device polls with the secret.
  When an admin has claimed the code, the *next* poll mints the token, stores its hash, sets the cookie and
  marks the request delivered, all in one conditional update. A usable token is therefore never at rest, and
  it's delivered once. Later polls get `410 Gone`, as do expired codes. A wrong secret gets `404`.
- Claiming is admin-only (`/admin/screens/pair`): a new screen, or an existing unpaired one. **Re-pair**
  (R-42) is an explicit link from a paired screen's page (`?repair=`). The new device's token replaces the old
  one when it's delivered. **Revoke** clears the hash at once.
- **Pre-configured address** (R-41): an admin creates `/screen?token=…`. It replaces any current pairing and is
  shown once. The player posts it to `POST /api/player/session` for the cookie and removes it from the address
  bar (`history.replaceState`).
- Pairing writes the screen row with bulk updates that don't touch the screen's edit `version` (ADR-0004), and
  the screen is notified.
- Rate limits (ASP.NET Core rate limiter, fixed window per client IP): start pairing and token exchange 10/min,
  polling 60/min. They are per replica. With N replicas the budget is N times larger, which is still nothing
  against 25^6 codes (that live 15 min, and must be claimed by an admin) or 2^256 tokens.
- Old pairing requests are deleted one day after expiry, whenever a new request is made.

## Consequences
- A browser that blocks cookies can't be a screen. That's acceptable for kiosks and current browsers (PROJECT_BRIEF §7).
- Behind a reverse proxy the per-IP limits need the forwarded client IP (WP-10 configures forwarded headers).
- `/screen` and its modules are served with `Cache-Control: no-cache`, so an update reaches players on reload (R-26).
