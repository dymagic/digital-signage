# Architecture

> How we build what `PROJECT_BRIEF.md` describes. This is the starting design. Where you deviate,
> record why in an ADR (`docs/adr/`) and update this file in the same work package.

## 1. Stack (decided, see ADR-0001)

| Concern | Choice |
|---|---|
| Runtime | .NET 10, ASP.NET Core |
| Admin UI | Razor Pages + htmx (vendored, no CDN), hand-written CSS, no JS build step |
| Player | Static HTML/CSS/vanilla JS (ES2020 modules) served from `wwwroot/screen/` |
| Data | PostgreSQL 16+, EF Core 10 + Npgsql, code-first migrations |
| Push | Server-Sent Events; cross-replica fan-out via Postgres `LISTEN/NOTIFY` |
| Auth (admin) | Cookie auth; ASP.NET Core Identity *core* (`AddIdentityCore`) for hashing/lockout, own Razor pages |
| Auth (screen) | Opaque bearer token (256-bit random, stored as SHA-256 hash), delivered to the player as an HttpOnly cookie |
| Tests | xUnit, Testcontainers (PostgreSQL), `WebApplicationFactory`; Playwright for .NET **run inside the official Playwright Docker image** |
| Packaging | Multi-stage Dockerfile, chiseled/non-root runtime image, `docker buildx` for amd64+arm64 |

**Tooling constraint:** only the .NET 10 SDK and Docker may be installed on the dev machine. No Node,
npm, Python packages or global tools. .NET *local* tools (`dotnet new tool-manifest`, e.g.
`dotnet-ef`) are fine. Third-party JS (htmx, and SortableJS if needed) is vendored into
`wwwroot/lib/` with its licence file and version noted in `wwwroot/lib/README.md`.

## 2. Solution layout

```
/
├─ src/
│  └─ Signage.Web/                 ASP.NET Core app (single deployable)
│     ├─ Domain/                   entities, enums, value objects, sequence resolver (pure C#)
│     ├─ Data/                     DbContext, configurations, migrations
│     ├─ Features/                 vertical slices: Slides/, Playlists/, Screens/, Groups/,
│     │                            Users/, Settings/, Pairing/, Player/ (API), Events/
│     ├─ Pages/                    Razor Pages for admin (thin; call Features)
│     ├─ Resources/                .resx for i18n
│     └─ wwwroot/
│        ├─ admin/                 admin css/js
│        ├─ screen/                player: index.html, player.js, *.css (no build)
│        └─ lib/                   vendored htmx etc.
├─ tests/
│  ├─ Signage.UnitTests/           domain + resolver + pure logic
│  ├─ Signage.IntegrationTests/    WebApplicationFactory + Testcontainers Postgres
│  └─ Signage.E2E/                 Playwright .NET; runs in Docker
├─ deploy/                         compose overrides, cloud how-tos live in docs/deploy/
├─ docs/
├─ Dockerfile
├─ docker-compose.yml              app + postgres (dev/demo)
├─ docker-compose.e2e.yml          adds the playwright test runner
├─ Directory.Build.props           nullable, warnings-as-errors, analyzers, LangVersion
└─ Signage.sln
```

Keep it as **one web project**. Don't split into Core/Infrastructure/Application projects unless an
ADR justifies it. Vertical feature folders are enough at this size.

## 3. Domain model (initial)

```
User            Id, Email(unique, normalized), DisplayName, PasswordHash, Role{Admin,Editor},
                IsActive, MustChangePassword, LockoutEnd, AccessFailedCount, SecurityStamp, CreatedAt

Slide           Id, Title, Type{Image,Video,WebPage,Html}, Url?, Html?, DurationSeconds?,
                PlayAudio, ActiveFromUtc?, ActiveUntilUtc?, IsEnabled, CreatedAt, UpdatedAt, Version
Playlist        Id, Name(unique), Description?, CreatedAt, UpdatedAt
PlaylistSlide   PlaylistId, SlideId, Position                          (same slide may repeat)

Screen          Id, Name(unique), Location?, Mode{Carousel,SingleVideo}, SingleVideoUrl?,
                TokenHash?, PairedAt?, LastSeenAt?, LastIp?, PlayerVersion?, UserAgent?, CreatedAt
ScreenSlide     ScreenId, SlideId, Placement{Prepend,Append}, Position
ScreenPlaylist  ScreenId, PlaylistId, Position

ScreenGroup     Id, Name(unique), Description?, SortOrder
ScreenGroupMember  ScreenId, GroupId
GroupPlaylist   GroupId, PlaylistId, Position

PairingRequest  Id, Code(unique while active), DeviceSecretHash, CreatedAt, ExpiresAt,
                ClaimedScreenId?, ClaimedAt?, TokenDeliveredAt?
SystemSettings  (single row) DefaultSlideDurationSeconds=10, ShowInfoOverlay=false,
                TimeZoneId="UTC" (IANA), UpdatedAt
DataProtectionKey  (EF Core data-protection key store, for multi-replica cookies)
```

Rules:
- Positions are dense integers, rewritten on reorder. Use optimistic concurrency (`xmin` /
  row version) on aggregate roots edited by humans.
- Deleting a slide that is in use asks for confirmation and removes its links. Deleting a playlist
  likewise.
- A `Url` must be absolute `http(s)`. Video URLs should end in `.mp4` or be served as `video/mp4`.
  Only a warning: don't block, because CDNs vary.

### Sequence resolver
`SequenceResolver.Resolve(screen, nowUtc)` is a **pure function** over a loaded graph. It returns
an ordered `IReadOnlyList<ResolvedSlide>` with `Source` info (prepend / group:X / playlist:Y /
append), following R-12 exactly: playlist de-duplication, enabled filter and schedule filter. Unit
test it heavily. It is the heart of the system.

## 4. HTTP surface

### Admin (cookie auth, Razor Pages, antiforgery)
`/admin/...` pages for Slides, Playlists, Screens, Groups, Users, Settings, Pair, Login/Logout,
Setup (first run), Account (change password). htmx partials for reorder, toggle, identify and
status refresh. Authorization policies: `RequireAdmin`, `RequireEditor` (Editor ⊂ Admin).

### Player API (`/api/player/*`)
| Method & path | Auth | Purpose |
|---|---|---|
| `GET /screen` | none | Player shell (static) |
| `POST /api/player/pairing` | none, rate-limited | Start pairing → `{ code, pairingId, deviceSecret, expiresAt }` |
| `GET /api/player/pairing/{id}` | `deviceSecret` header | Poll; once claimed, sets the screen-token cookie and returns `{ paired: true }` (one-time delivery) |
| `POST /api/player/session` | `token` in body | Exchanges a pre-configured URL token for the cookie (R-41) |
| `GET /api/player/manifest` | screen cookie | Full state: `{ appVersion, etag, screen{id,name}, mode, singleVideoUrl, settings{defaultDuration, showInfoOverlay, timeZone}, observedIp, slides[] }`. Supports `If-None-Match` |
| `GET /api/player/events` | screen cookie | SSE stream: `manifest-changed`, `identify{seconds}`, `identify-all{seconds}`, `reload`, keep-alive comments every 15 s |
| `POST /api/player/heartbeat` | screen cookie | `{ playerVersion, currentSlideId }`; the server records IP/UA/LastSeen |

Slides in the manifest carry `{ id, type, url?, html?, durationSeconds (resolved), playAudio,
activeFromUtc?, activeUntilUtc? }`. The player re-checks the schedule client-side so offline playback
respects expiry (R-24).

A revoked or unknown token returns `401`. The player then wipes its token and shows the pairing screen.

Admin preview of a screen (R-13) uses `/screen?preview={screenId}`. That path authorises with the
**admin** cookie and never records heartbeats.

### Event fan-out
`IScreenNotifier` raises domain events like "screen X affected" and "all screens affected". The
Postgres implementation does `NOTIFY signage_events, '<json>'`. Each replica runs a background
`LISTEN` service that dispatches to its locally connected SSE clients. Each feature works out the
**affected screens**: a slide edit → all screens whose sequence contains the slide. Over-notifying
is acceptable, since a manifest ETag stops redundant re-renders.

## 5. Configuration (env vars, `SIGNAGE__` prefix)

| Key | Default | Notes |
|---|---|---|
| `ConnectionStrings__Default` | — | Postgres connection string (required) |
| `SIGNAGE__MIGRATEONSTARTUP` | `true` | |
| `SIGNAGE__BOOTSTRAP__ADMINEMAIL` / `__ADMINPASSWORD` | — | Only used if there are no users |
| `SIGNAGE__FORWARDEDHEADERS__ENABLED` | `false` | Trust `X-Forwarded-*` (behind ingress/load balancer) |
| `SIGNAGE__FORWARDEDHEADERS__KNOWNNETWORKS` | — | CIDRs of the proxies |
| `ASPNETCORE_URLS` | `http://+:8080` | |

Never commit real secrets. The compose file uses obvious demo values with a comment.

## 6. Cross-cutting conventions

- `Directory.Build.props`: `Nullable=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`, `ImplicitUsings=enable`.
- Time: inject `TimeProvider` everywhere. Never call `DateTime.Now`/`UtcNow` directly. Tests use `FakeTimeProvider`.
- Logging: structured `ILogger` with no PII beyond email in auth audit logs.
- Errors: ProblemDetails for APIs. Friendly error pages in the admin.
- Every admin string goes through `IStringLocalizer`/`IViewLocalizer`.
- The player JS has no dependencies. Organise it as small ES modules (`player.js`, `carousel.js`,
  `slides/*.js`, `connection.js`, `overlay.js`, `storage.js`). All timing constants live in one
  `config.js`.
- Player test hooks: the player exposes state through `data-*` attributes on `<body>` and slide
  elements (`data-state="entering|active|leaving|covered"`, `data-slide-id`), plus
  `?timeScale=0.1` (only honoured with `?test=1`) so E2E runs fast. No other test-only branches in
  production code.
