# Project Brief — Signage

> The source of truth for **what** we build. `docs/ARCHITECTURE.md` says **how**,
> `docs/PLAYER_SPEC.md` gives the exact screen behaviour, and `docs/BACKLOG.md` sets the **order**.
> The original request is preserved in `opening-spec.md`. Where this brief and the opening spec
> disagree, this brief wins. It was agreed with the product owner on 2026-10-16.

## 1. Vision

A small, self-hostable digital signage system for an office. TVs in departments, common areas and
the grand hall each show a carousel of content. Everything is administered from one web-based
admin. The system runs as one container plus Postgres and deploys anywhere containers run:
Docker Desktop, Kubernetes, Azure, AWS.

It will be published as an open-source GitHub repository (MIT, no warranty). Code, docs and
defaults must therefore suit strangers who clone it: no company-specific values, no secrets, and
a clear README.

## 2. Glossary (use these terms everywhere — code, UI, docs)

| Term | Meaning | Replaces (opening spec) |
|---|---|---|
| **Screen** | A physical TV or device running the player in a browser. Has a name, a mode and assignments. | Monitor |
| **Slide** | One item of content: Image, Video, Web page (iframe) or HTML. Lives in a shared slide library. | Card |
| **Playlist** | A named, ordered, reusable list of slides, e.g. "Canteen", "Marketing". | Feed |
| **Screen Group** | A named set of screens, e.g. "Grand hall", "Sales dept". Playlists assigned to a group play on all its screens. A screen can belong to several groups. | "group" |
| **Prepended / Appended slides** | Slides attached directly to one screen, played before / after everything else. | "manually appended or prepended" |
| **Sequence** | The resolved, ordered list of slides a screen actually plays. | — |
| **Player** | The browser app at `/screen` that renders a screen's sequence. | — |
| **Pairing** | Connecting a device to a Screen record. | — |
| **Admin** / **Editor** | The two user roles. | — |

## 3. Users and roles

| Capability | Admin | Editor |
|---|---|---|
| Manage slides, playlists | ✅ | ✅ |
| Assign playlists to screens / groups; edit a screen's prepended/appended slides | ✅ | ✅ |
| Identify screens, preview a screen | ✅ | ✅ |
| Pair, rename, delete screens; change screen mode; manage groups and membership | ✅ | ❌ |
| Global settings (default duration, info overlay, time zone) | ✅ | ❌ |
| Manage users (create, deactivate, change role, reset password) | ✅ | ❌ |

## 4. Milestone 1 requirements

Each requirement has an ID. Work packages and tests reference these IDs.

### Content

- **R-01 Slide types.** Image (URL), Video (MP4 URL), Web page (URL shown in an iframe) and HTML
  (a stored HTML chunk). In M1, images and videos are **always external URLs**: no uploads.
- **R-02 Duration.** A global default slide duration (seconds, default 10). Each slide can
  override it. Video slides play until the video ends. Duration does not apply to them.
- **R-03 Scheduling.** Optional `active from` / `active until` date-times per slide. Outside that
  window the slide is skipped. Times are entered in the system time zone (global setting) and
  stored as UTC.
- **R-04 Enable/disable.** A slide can be disabled without deleting it.
- **R-05 HTML slides are trusted but sandboxed.** Editors may include scripts. The HTML renders in
  a sandboxed iframe (`srcdoc`, `sandbox="allow-scripts"`, without `allow-same-origin`), so it can't
  touch the player, cookies or the admin.
- **R-06 Web-page slides.** The admin shows a note that some sites refuse to be framed
  (X-Frame-Options / CSP). No proxying in M1.
- **R-07 Video audio.** Muted by default. A per-slide "play audio" flag is offered. The docs say
  it needs a browser started with an autoplay-with-sound policy (kiosk flags).
- **R-08 Playlists.** CRUD, plus an ordered list of slides. The same slide may appear in several
  playlists.

### Screens, groups, composition

- **R-10 Screens.** Each screen has a name (required, unique), an optional location/notes, and a mode:
  **Carousel** (default) or **Single video**.
- **R-11 Groups.** CRUD. Screens are many-to-many with groups. Groups have an ordered list of playlists and a
  sort order among themselves.
- **R-12 Composition.** A screen's sequence is:
  1. the screen's **prepended** slides (ordered),
  2. playlists from its **groups**, by group sort order then group name, each group's playlists
     in their configured order,
  3. playlists assigned **directly** to the screen (ordered),
  4. the screen's **appended** slides (ordered).

  If a playlist would appear more than once, only its first occurrence counts. Disabled and
  out-of-schedule slides are filtered out. The whole sequence loops.
- **R-13 Sequence preview.** The screen page in the admin lists the resolved sequence, showing
  where each slide came from. A "Preview" opens the player for that screen in the admin's browser.
- **R-14 Single video mode.** The screen plays one configured video URL in a seamless loop. No
  transitions, no progress bar. A mode change applies live.

### Player behaviour (details in `PLAYER_SPEC.md`)

- **R-20 Carousel transition.** The new slide slides in from the right over the current one with
  a firm easing. A subtle shadow sits on its left edge. The previous slide stays still underneath,
  so the slides appear stacked.
- **R-21 Progress bar.** A very subtle bar at the bottom shows time remaining until the next slide.
  For video it follows playback position.
- **R-22 Video lifecycle.** A video starts only once its slide is fully in view. When it ends, it
  stops and the next slide slides over. Resource handling for videos (removal from the DOM etc.) is
  decided by an investigation spike (WP-07) and recorded in an ADR.
- **R-23 Empty state.** With no playable slides, the player shows a calm neutral screen (screen
  name, small clock). It does not show an error.
- **R-24 Offline resilience.** If the server can't be reached, the player keeps cycling its last
  known sequence, still respecting slide schedules. It reconnects quietly.
- **R-25 Live updates.** Content, assignment, mode and setting changes reach affected screens
  within ~5 s through push (SSE). Polling (≤ 60 s) is the fallback.
- **R-26 Self-update.** When the server version changes, players reload themselves.

### Identification and status

- **R-30 Info overlay.** A global toggle shows each screen's name and IP subtly at the bottom of every screen.
  The IP is the one the **server observes**. A browser can't read its own LAN IP, and behind
  NAT/proxies this is the forwarded client IP.
- **R-31 Identify one.** An admin button gives that screen a thick, pulsing red border with its
  name in large text for 30 s.
- **R-32 Identify all.** Every screen shows its name in large text for 30 s.
- **R-33 Status.** The screens list shows online/offline (heartbeat within the last 90 s), last seen,
  last IP, player version and user agent.

### Pairing

- **R-40 Pairing code.** Opening `/screen` on an unpaired device shows a large 6-character code
  (unambiguous alphabet, valid for 15 min). An admin enters the code, names the screen (or picks an
  existing unpaired one) and the device pairs. After that the device keeps a token in localStorage.
- **R-41 Pre-configured URL.** The admin can copy a screen URL with an embedded token
  (`/screen?token=…`) for kiosk and scripted setups. The player saves the token and strips it
  from the address bar.
- **R-42 Revoke.** An admin can re-pair or revoke a screen. Revoking invalidates its token at once.

### Users and security

- **R-50 Accounts.** Local accounts with email + password. Passwords are hashed with ASP.NET Core's
  password hasher. There is a lockout after repeated failures.
- **R-51 First run.** With zero users, the admin shows a one-time "create first admin" page.
  Alternatively, `SIGNAGE__BOOTSTRAP__ADMINEMAIL` / `…ADMINPASSWORD` env vars create the first
  admin at start-up (only if there are no users).
- **R-52 User admin.** Only admins create users, set a role, deactivate users and reset passwords. New or reset users must
  change their password at first login. (No email sending in M1: the admin hands over the
  temporary password.)
- **R-53 Web security baseline.** Antiforgery on all admin form posts, secure cookies
  (`SameSite=Lax`, `HttpOnly`), output encoding everywhere, no secrets in the repo. Screen
  tokens are random 256-bit values and are stored hashed. Network-level protection is the
  operator's responsibility: firewall it, and don't expose it to the internet. The README says so.

### Operations

- **R-60 Container.** One multi-arch (amd64 + arm64) image, a non-root user, configured via env
  vars, `/healthz` (liveness) and `/readyz` (DB reachable). Migrations are applied automatically
  at start-up, and that can be switched off.
- **R-61 docker-compose.** `docker compose up` gives a working system (app + Postgres) at
  `http://localhost:8080`.
- **R-62 Cloud docs.** How-to docs for Azure Container Apps + Azure Database for PostgreSQL, and
  for AWS (App Runner or ECS Fargate) + RDS PostgreSQL. Docs only, no IaC.
- **R-63 Multiple replicas.** Running more than one app replica works. Push events fan out across
  replicas through Postgres `LISTEN/NOTIFY`. Data-protection keys are persisted in the database.
- **R-64 i18n-ready.** All admin UI strings come from resource files (English in M1). The player's
  few strings are localisable too.
- **R-65 Open-source hygiene.** MIT `LICENSE`, `README` (what it is, screenshots, quick start,
  security note, no-warranty statement), `CONTRIBUTING.md`, and clean `.gitignore`.

## 5. Out of scope for M1 (do not build)

Uploaded media and asset storage · SSO (M365 etc.) · per-group user permissions · weekday/time-of-day
dayparting · Raspberry Pi images/scripts · support for old smart-TV browsers (Tizen/webOS < 2020) ·
emergency override broadcasts · analytics/proof-of-play · Kubernetes manifests/Helm · IaC for
clouds · email sending.

Keep the model friendly to these: don't make choices that block them. Don't build them.

## 6. Probable future additions (for design awareness only)

Raspberry Pi / mini-PC setup scripts (they would pass the local IP and a token in the screen URL) ·
SSO via Entra ID / OIDC · users scoped to specific screen groups · uploaded media · dayparting ·
Kubernetes manifests.

## 7. Target environment

- **Players:** current Chromium-based browsers (Raspberry Pi kiosk, mini-PCs, recent smart TVs) and
  current Firefox, at 1920×1080 and 3840×2160, landscape. Plain ES2020+, no build step.
- **Admin:** current desktop browsers. A tablet-usable layout is nice to have.

## 8. Definition of done — Milestone 1

M1 is done when **all** of these hold:

1. Every work package in `docs/BACKLOG.md` is `Done`, each with an approving review in `docs/reviews/`.
2. `dotnet test` passes (unit + integration with Testcontainers Postgres).
3. The Playwright E2E suite passes inside Docker. It covers pairing, the carousel transition and
   ordering, the video lifecycle, single video mode, identify, live update and offline behaviour.
4. On a clean machine with only Docker, `docker compose up` followed by the README quick start
   reaches a working screen showing a demo playlist.
5. A final milestone-level review by the principal reviewer returns APPROVE.
6. `docs/M1_ACCEPTANCE.md` lists every R-ID with the test(s) or manual check proving it.
