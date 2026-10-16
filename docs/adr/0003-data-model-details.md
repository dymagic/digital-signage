# ADR-0003: Data model details

- Status: Accepted
- Date: 2026-10-16
- Work package: WP-01
- Requirements: R-03, R-10, R-11, R-12, R-60, R-63

## Context
ARCHITECTURE §3 sketches the entities but leaves the key type, naming, how "unique name" behaves
with case, schedule boundaries, and start-up behaviour open. These are hard to change once data
exists.

## Options considered
1. **Keys**: `int` identity (compact, but sequential ids leak counts and appear in preview URLs) vs.
   **UUIDv7** generated client-side (`Guid.CreateVersion7()`: time-ordered so B-tree inserts stay
   local, not guessable, no round-trip to get an id, and future imports or merges don't collide).
2. **Name uniqueness**: exact (so "Grand hall" and "grand hall" could both exist, which confuses
   admins) vs. a `lower(name)` unique index (queries must remember to use `lower`) vs. `citext` (needs an
   extension, which managed Postgres may restrict) vs. an **ICU nondeterministic collation** on the column
   (built into PG ≥ 12, equality and uniqueness ignore case, original spelling kept).
3. **Schedule window**: closed `[from, until]` vs. **half-open `[from, until)`**.
4. **Naming**: EF default PascalCase (quoted identifiers in every raw SQL) vs. **snake_case** via
   `EFCore.NamingConventions`.

## Decision
- UUIDv7 keys for entities. Pure link tables use composite keys, except `PlaylistSlide` and
  `ScreenSlide`, which have their own id because the same slide may appear twice.
- Playlist, Screen and ScreenGroup names use the `case_insensitive` ICU collation (`und-u-ks-level2`)
  with a unique index. Users are unique on `NormalizedEmail` (upper-invariant, as ASP.NET Core Identity
  does). Pattern matching (`LIKE`) isn't supported on nondeterministic collations in PG 16. Search
  features must use `ILIKE` on a non-collated column, or `lower()`. Slide titles, the only searched
  text so far, use the default collation.
- Schedules are half-open: active when `from <= now < until`. "Until 17:00" therefore stops showing at
  17:00, and back-to-back windows never overlap. The player JS will mirror `SlideSchedule`.
- Position columns are indexed, not unique (reorders rewrite them in one batch).
- snake_case tables and columns.
- Optimistic concurrency uses `xmin` on every human-edited root: User, Slide, Playlist, Screen,
  ScreenGroup, SystemSettings.
- Start-up: migrate (when enabled) and then `INSERT … ON CONFLICT DO NOTHING` the settings row.
  EF Core's migration lock makes concurrent replica start-ups safe, and an integration test starts two
  at once. With migrations switched off and pending migrations, the app refuses to start with a clear
  message instead of failing later on a missing table. If the database is unreachable at start-up,
  the process exits so the orchestrator can retry.
- The data-protection key ring is stored in `data_protection_keys`. The keys are not encrypted at rest
  (ASP.NET logs a warning about this). The database is already the trust boundary: anyone who can read
  it can read everything else. Operators who want key encryption can add a certificate later.

## Consequences
- GUIDs are wider than ints. That's irrelevant at office scale.
- Case-insensitive names need no special handling in queries.
- Every new raw SQL statement uses snake_case names.
- Start-up depends on the database being reachable. `/healthz` and `/readyz` only answer once
  migrations have finished.
