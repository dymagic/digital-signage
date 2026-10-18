# ADR-0001: Foundational decisions from the product-owner interview

- Status: Accepted
- Date: 2026-10-16
- Work package: — (project set-up)
- Requirements: all

## Context
`docs/opening-spec.md` left the platform, terminology and several behaviours open. They were settled in an
interview with the product owner before development started.

## Decisions
| Topic | Decision | Main alternatives rejected |
|---|---|---|
| Backend | .NET 10 / ASP.NET Core | Node/TS, Go (not installed; more to maintain) |
| Admin UI | Razor Pages + htmx, no JS build | Blazor Server, SPA |
| Database | PostgreSQL only, EF Core | + SQLite (double provider testing) |
| Live updates | SSE + 60 s poll fallback; Postgres LISTEN/NOTIFY across replicas | SignalR, polling only |
| Terminology | Screen / Slide / Playlist / Screen Group | Monitor / Card / Feed |
| Groups | Groups carry ordered playlists; screens are in many groups | Organisation-only groups |
| Composition | Prepend → group playlists → screen playlists → append; playlist de-dup | Fully manual per-screen ordering |
| Enrolment | Pairing code **and** pre-configured token URL | Only one of them |
| Offline | Keep cycling the last known sequence | Show an error page |
| Scheduling | Per-slide active-from/until only | None; full dayparting |
| Browsers | Modern Chromium/Firefox, ES2020, no build | Legacy Tizen/webOS support |
| HTML slides | Trusted authors; sandboxed `srcdoc` iframe with scripts but no same-origin | Sanitised HTML only |
| Identify | Red border + name (one), name on all, global name/IP overlay, online status | — |
| Single video | Explicit screen mode | "Playlist with one video" |
| i18n | English, resource-file based | Danish in M1; hardcoded |
| First admin | First-run setup page + optional bootstrap env vars | Env vars only |
| Editor role | Content only; admins own screens, groups, settings, users | Everything except users |
| Deployment (M1) | Dockerfile + compose; Azure/AWS how-to docs | K8s manifests (later) |
| Video audio | Muted by default; per-slide opt-in documented as needing kiosk flags | Always muted |
| Licence / name | MIT, working name "Signage" | Apache-2.0 |
| Review | `principal-reviewer` subagent gates each package, with an isolated context | Manual separate session |
| Git | Local repo, branch per package, `--no-ff` merges, no remote | PR flow on GitHub |
| Autonomy | Run all of M1; stop only on blockers | Check in per package |
| E2E | Playwright .NET inside Docker | Browsers installed on the host |

## Consequences
- The player is plain JS without a bundler. Keep its modules small and its dependencies at zero.
- Postgres is required even for the smallest install. The quick start uses compose.
- The IP shown on screens is the server-observed client IP, not the device's LAN IP (a browser
  limitation). Future Pi scripts can pass the local IP explicitly.
