# ADR-0010: Live updates, heartbeat and status

- Status: Accepted
- Date: 2026-10-18
- Work package: WP-10
- Requirements: R-25, R-26, R-30, R-33, R-63

## Context
ARCHITECTURE fixed the shape (SSE, Postgres LISTEN/NOTIFY, 60 s poll fallback, 30 s heartbeat, 90 s online window). It
left open what an event carries, what happens to events missed during a disconnect, how a player knows its own version,
and how forwarded headers behave without configured proxy networks.

## Decision
1. **Events carry no data.** `manifest-changed` only means "fetch your manifest". The manifest (with its ETag) stays the
   single source of truth, so a duplicated, late or merged event can't put a player in a wrong state. WP-11's identify
   events will carry their duration.
2. **Missed events are covered twice.** The player fetches its manifest whenever its event stream (re)opens. The
   server's listener, after it reconnects to Postgres, tells every connected player to fetch. The 60 s poll stays.
3. **Bounded per client.** Each SSE client has a 16-event buffer that drops the oldest; a stalled client costs no memory.
4. **The page knows its version from `/screen`.** The endpoint stamps `<meta name="signage-version">`. A fresh manifest
   with another `appVersion` triggers a reload, once per version (guarded in `sessionStorage`) so a stale cache can't
   cause a reload loop.
5. **Forwarded headers:** only `X-Forwarded-For` and `-Proto`, never the host. With no networks configured, the
   nearest hop is trusted (documented as safe only when the app is reachable through the proxy alone); an invalid
   network stops start-up.
6. **Heartbeat problems are logged, not stored.** Skipped videos and single-video failures arrive with the heartbeat
   and go to the server log as warnings. The brief asks for none of this in the admin; a column can follow later.

## Consequences
- Every replica holds one extra Postgres connection for LISTEN.
- An edit reaches a screen in well under 5 s (E2E), and within 60 s if push is blocked by a proxy.
- The status list re-reads itself every 15 s with htmx; online means a heartbeat within 90 s.
