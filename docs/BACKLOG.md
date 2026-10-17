# Backlog — Milestone 1

Work strictly top to bottom unless a package says it can run earlier. Status values: `Todo`,
`In progress`, `In review`, `Done`, `Blocked (reason)`. Update the status in the same commit that
changes it. Each package lists the requirement IDs it satisfies (see `PROJECT_BRIEF.md`).

Every package's Definition of Done (on top of its own acceptance criteria):
- builds with zero warnings; `dotnet test` green; E2E green for affected areas
- tests added for the new behaviour (unit for logic, integration for endpoints/pages/DB, E2E for player)
- docs updated (ARCHITECTURE, README, ADRs) where reality changed
- principal-reviewer verdict `APPROVE` saved to `docs/reviews/WP-xx-review-N.md`
- merged to `main` with `--no-ff`, branch deleted, `docs/PROGRESS.md` updated

---

### WP-00 Repository foundation — `Done`
`git` hygiene, `.gitignore`, `.editorconfig`, `Directory.Build.props`, `Signage.sln`, the
`Signage.Web` skeleton with `/healthz` + `/readyz`, three test projects, a multi-stage `Dockerfile`
(non-root, arm64+amd64 buildable), `docker-compose.yml` (app + postgres:16 with healthcheck),
`docker-compose.e2e.yml` with a Playwright .NET runner image whose version **matches** the
`Microsoft.Playwright` package, and one smoke E2E test that loads `/healthz`. A
`scripts/test-e2e.sh` wrapper. Vendor htmx into `wwwroot/lib`.
**Accept:** `dotnet build` clean; `dotnet test` runs (Testcontainers) green; `docker compose up`
serves `/healthz` 200; `scripts/test-e2e.sh` passes. Covers R-60 (partly), R-61 (partly).

### WP-01 Data model & settings — `Done`
EF Core model per ARCHITECTURE §3, the initial migration, auto-migrate on start-up (switchable),
`SystemSettings` seeding, the data-protection key store in the DB, `TimeProvider` registration. Includes
the pure `SequenceResolver` with exhaustive unit tests (ordering, de-dup, schedule boundaries,
disabled, empty, repeated slide).
**Accept:** resolver tests cover every R-12 rule; the migration applies on an empty DB in integration tests. R-02, R-03, R-04, R-12.
*Follow-ups from WP-00 review:* (a) make the host dev loop work: publish db 5432 on 127.0.0.1 and align the dev password with compose, or document an override; (b) add the dotnet-ef local tool manifest and re-verify the CLAUDE.md command line; (c) E2E: dispose pages via `await using` contexts, capture trace/screenshot to `artifacts/e2e/` on failure; (d) use a Stopwatch instead of `DateTimeOffset.UtcNow` in `BrowserFixture`; (e) make Npgsql GSS opt-out detection less fragile (parse the connection string); (f) fix the run-and-verify comment "app and db healthy?" (the app has no HEALTHCHECK).

### WP-02 Authentication & users — `Done`
Cookie auth, login/logout, lockout, first-run setup page, bootstrap via env vars, forced password
change, user management (admins only), role policies, antiforgery, the admin layout shell with
navigation and the i18n plumbing (resx + localizers). Also the global Settings page (admin only; no other
package owns it, ADR-0004).
**Accept:** integration tests for every role/endpoint permission in the PROJECT_BRIEF §3 matrix;
setup is unreachable once a user exists; a deactivated user can't log in and their existing cookie is
invalidated (security stamp). R-50, R-51, R-52, R-53, R-64.

### WP-03 Slide library — `Done`
Slide CRUD for all 4 types, validation (absolute http/https URLs, required fields per type),
the duration override, the schedule (entered in the system TZ, stored UTC), enable/disable, the
play-audio flag with a help text, the WebPage framing note, a thumbnail/preview in the list (an
image thumbnail, a video poster via `#t=0.1`, iframe/HTML as a small scaled iframe), and a search
filter.
**Accept:** integration tests for validation and TZ conversion; Editor and Admin both allowed. R-01–R-07.
*Follow-ups from WP-01 review:* note in ARCHITECTURE that bulk `ExecuteUpdate` bypasses `TimestampInterceptor` (set `UpdatedAt` explicitly there); test that updates always overwrite `UpdatedAt` while inserts keep an explicit one.

### WP-04 Playlists — `Done`
Playlist CRUD, add/remove slides, reorder (drag with keyboard-accessible up/down fallback, via htmx),
duplicate slides allowed, "used by" info (screens/groups).
**Accept:** reorder persists densely; concurrent edit gets a friendly conflict message. R-08.
*From WP-03 review:* (1) deleting a slide cascades away playlist/screen links and leaves gaps in `Position`;
renumber densely in the same transaction (slide and playlist deletes) or reword ARCHITECTURE §3 "dense";
(3) `SlideLibraryTests` waits on `**/admin/slides/*`, which already matches the create URL; wait for the edit page
properly; NITs 5–9: singular/plural in `Slides.Usage`, own `Slides.Column.Status` key, `.split` preview overflow at
1920 px, set `TempData["Warning"]` only after a successful save, strip an existing `#fragment` before `#t=0.1`.
*From WP-02 review:* `MustChangePasswordFilter` is a Razor *page* filter. Admin endpoints mapped outside Razor
Pages (e.g. minimal-API htmx endpoints) must enforce the forced password change too, so move the check into
an authorization requirement or middleware when the first such endpoint appears.

### WP-05 Screens, groups & composition — `Done`
Screen CRUD (admin), mode + single video URL (admin), group CRUD + membership + sort order
(admin), assigning playlists to groups and screens and managing prepended/appended slides (editor+).
The screen detail page shows the resolved sequence with its sources (R-13; the preview link
comes in WP-08). Domain events emitted through `IScreenNotifier` (a no-op implementation for now).
**Accept:** the role split matches the brief; the resolved sequence on the page equals the resolver's output
(integration test). R-10, R-11, R-12, R-13 (partly), R-14 (config).
*From WP-04 review (apply to playlist lists and the new screen/group lists):* (1) keep keyboard focus after
up/down/remove swaps (stable ids on the buttons, or refocus after `htmx:afterSwap`); (2) when the parent was
deleted meanwhile, htmx changes get a 404 that htmx 2 doesn't swap: show a message or redirect (e.g. `HX-Redirect`
to the list); (3) add tests: saving the playlist details form with a stale version, and the forced password
change blocking htmx handlers; NIT: a slide delete renumbers playlist entries without bumping the playlist version.
*From WP-02 review round 4:* heartbeats (WP-10) will write `LastSeenAt`/`LastIp`/… on `screens`, which bumps
`xmin` and would make an open screen edit form fail with a conflict, the same class of bug as on users. Give
`Screen` an app-managed `version` like `User` (ADR-0004), or keep heartbeat data in its own table, and reword
the ADR-0004 sentence that says other entities are "written only by human edits". Add a test: a heartbeat
between loading and saving a screen form doesn't cause a conflict. Optional NITs from the same review: tidy the
ADR sentence placement, rename `ConcurrentSignInTests` (it now covers password changes too), and make
`GatedPasswordHasher.Gate.Dispose` release a waiting thread.

### WP-06 Pairing & screen tokens — `Done`
Pairing request API, the pairing screen in the player, the admin "Pair screen" page (enter code → new
or existing unpaired screen), token issuing as an HttpOnly cookie, one-time delivery, the
pre-configured URL + `/api/player/session` exchange, revoke/re-pair, rate limiting on pairing
endpoints.
**Accept:** E2E — a device shows a code, the admin claims it, and the device lands in the player; a revoked
device returns to pairing. Integration tests for expiry, reuse and rate limits. R-40, R-41, R-42.
*From WP-05 review round 3 (NIT):* the `Name` input properties on the screen, group and playlist forms are
non-nullable but arrive as null when empty; make them `string?` (services already handle null).

### WP-07 Spike: video lifecycle & memory — `Done` *(may run any time after WP-00)*
Build a throwaway harness page (`tests/Signage.E2E/spikes/`) that cycles videos and images with the
planned transition and measures memory/decoder behaviour over 200+ transitions under different
strategies (PLAYER_SPEC §4). Use short, openly licensed sample MP4s served from a local container
(e.g. nginx with test media generated by `ffmpeg` in a container). Don't commit large binaries.
**Output:** `docs/adr/000X-video-lifecycle.md` with the data, the recommendation and the decision;
harness code kept but not shipped. The reviewer checks the method, not polish.

### WP-08 Player core — `Done`
Manifest endpoint with ETag, the player shell, carousel engine, transition per PLAYER_SPEC §2,
progress bar, Image/WebPage/Html slides, preloading, the 3-layer cap, empty state, localStorage
manifest cache and offline cycling, schedule re-check on the client, admin preview mode
(`/screen?preview=`), test hooks (`data-state`, `timeScale`). A demo seed (a sample playlist with public
domain images + an HTML clock slide), applied only in Development or with `SIGNAGE__SEEDDEMO=true`.
*From WP-07 (ADR-0008):* implement the video release order (release the covered slide before preparing the one after
next) and the daily reload (PLAYER_SPEC §9, confirmed by the product owner). Its E2E tests use Playwright's Clock API
(`page.Clock`) to move time forward, not test-only branches in the player (ARCHITECTURE §6): a due reload with the server unreachable (route abort) is postponed, and the
player keeps cycling; a due reload with the server up happens at a transition; a one-slide screen reloads at the check.
*From WP-07 review round 4 (NITs):* ADR-0008 should say outright that ~0.4 GB start + ~1.3 GB growth is tight on a
2 GB Pi and that the reload interval can be shortened in `config.js`; and use PLAYER_SPEC's "2xx immediately before
reloading" wording instead of "server answers".
*From WP-06 review round 2:* (MINOR) the device stops polling a code 60 s before it expires but the server accepts
claims until expiry: keep polling the old code until a new one is shown, or stop accepting claims in the last
minute; (NIT) do the token write and `CancelPendingClaimsAsync` in one transaction in revoke and screen address;
(NIT) cancel a screen's earlier claims only after this claim succeeded.
*From WP-03 review:* the admin Html preview should use the player's base stylesheet for srcdoc (PLAYER_SPEC §5),
so admin preview and screen match. Share one helper for the srcdoc wrapper.
**Accept:** E2E — order follows the resolved sequence; the outgoing slide's transform stays
`translateX(0)` while the incoming one animates; the progress bar fills over the duration; at most 3 layers;
it keeps cycling when the server is stopped. R-20, R-21, R-23, R-24, R-13.

### WP-09 Video & single video mode — `In progress`
Video slide per PLAYER_SPEC §4 plus the WP-07 ADR decision; single video mode per §6; error/stall
skip.
**Accept:** E2E — the video doesn't play until fully in view (`currentTime` stays 0 during the transition);
the next slide starts on `ended`; a broken URL is skipped; single video mode loops with no progress bar;
the mode switch applies live. R-07 (player side), R-14, R-22.
*From WP-08 review round 3:* (MINOR) add an E2E test for "the slide on screen expired or was removed and nothing
else loads → empty state" (`carousel.js` `isStale`); (NIT) make the one-slide "settles" check in
`CarouselTests.An_edit_to_the_only_slide…` wait past a repeat manifest poll; (NIT) guard both empty-state fallbacks in
`carousel.js` with `!this.stopped` so a stop can't draw over pairing or the preview message; (NIT) mention the
expired/removed exception in PLAYER_SPEC §2 "Skipping".

### WP-10 Live updates, heartbeat & status — `Todo`
SSE endpoint, Postgres LISTEN/NOTIFY notifier + listener background service, affected-screen
calculation in each feature, the 60 s poll fallback, heartbeat endpoint, online/offline status in the admin
list (htmx refresh), `appVersion` self-reload, forwarded-headers configuration.
**Accept:** E2E — an edit in the admin reaches the screen within 5 s; integration test — two app
instances sharing one DB both deliver an event (R-63); the status flips to offline after 90 s (fake time).
R-25, R-26, R-33, R-63.

### WP-11 Identification — `Todo`
Identify one (red border + name, 30 s), identify all, the global info overlay toggle with
server-observed IP.
**Accept:** E2E for each, including timer restart and the overlay appearing live. R-30, R-31, R-32.

### WP-12 Open-source packaging & deployment docs — `Todo`
README (screenshots taken with Playwright, quick start, configuration table, security/firewall note,
no-warranty statement), `CONTRIBUTING.md`, `docs/deploy/azure-container-apps.md`,
`docs/deploy/aws.md`, multi-arch build instructions, demo-data instructions, `LICENSE` check.
**Accept:** following the quick start on a clean clone works (the reviewer verifies it by actually running
it). R-60, R-61, R-62, R-65.
*Follow-up from WP-01 review:* document that compose publishes Postgres on `127.0.0.1:55432` for the
host dev loop and how to change it (`SIGNAGE_DB_PORT`) or remove it.
*From WP-03 review:* the play-audio hint says "see the README": document Chromium/Firefox autoplay-with-sound
kiosk flags there (R-07).
*From WP-02 (ADR-0004):* the security note recommends HTTPS via a reverse proxy when the network isn't
trusted. Over plain HTTP the admin cookie can't carry the `Secure` flag.

### WP-13 Milestone hardening & acceptance — `Todo`
Write `docs/M1_ACCEPTANCE.md` (every R-ID → proof), close gaps, run the full suite, run a 30-minute
soak of the player in E2E (memory stable), and request the **milestone-level** review (the reviewer reviews
the whole repo, not a diff).
*From WP-07 (ADR-0008):* the soak runs the shipped player for 30 min with an alternating 3–4 s video / image sequence at
`?test=1&timeScale=0.05` (images and transitions sped up; videos play at normal speed, so roughly 900 transitions).
It samples browser PSS from `/proc` every 100 transitions, with tracing off and *without* the CDP Media domain (so the
measurement doesn't take part; the spike had it on, which may have added a little to its figures). This is a regression guard, not a plateau proof (the daily reload bounds growth). It
fails if the growth between the end of the first third and the end is more than 2× the spike's rate for the same
number of transitions (2 × 0.15 MB per transition). Record the numbers in M1_ACCEPTANCE.
**Accept:** PROJECT_BRIEF §8 is fully satisfied.
