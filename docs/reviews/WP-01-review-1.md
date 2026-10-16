# Review WP-01 — round 1
- Branch: wp-01-data-model @ b436d7f
- Verdict: CHANGES_REQUIRED
- Verified:
  - `dotnet build --no-incremental`: 0 warnings, 0 errors.
  - `dotnet test`: 64 total, 63 passed, 1 skipped (the E2E smoke test skips on the host by design), 0 failed. Integration tests ran against a real `postgres:16` Testcontainer.
  - `Two_replicas_starting_at_once_on_an_empty_database_both_succeed` run 15 times in a row: 15/15 passed.
  - Stress test of my own: 6 real `Signage.Web.dll` processes started at the same time against a fresh database, repeated for 5 fresh databases. All 30 processes reached `/readyz` 200, exactly one migration was applied, and there was one settings row and no start-up errors.
  - `SIGNAGE__MIGRATEONSTARTUP=false` against an empty database: the process exits with "The database schema is out of date (1 pending migration(s)) …", and no table is created. Unreachable database: the process exits non-zero (134).
  - `dotnet tool restore` + `dotnet ef migrations has-pending-model-changes`: "No changes have been made to the model since the last migration". The local tool manifest works.
  - Migration round trip with `dotnet ef database update` → `update 0` → `update`: **the second Up fails** with `42710: collation "case_insensitive" already exists` (see finding 1). `dotnet ef migrations script InitialCreate 0` contains no `DROP COLLATION`.
  - `scripts/test-e2e.sh`: `Signage.E2E Total: 1, Errors: 0, Failed: 0, Skipped: 0`. `artifacts/e2e/` also holds a `.png` and a `.trace.zip` from a deliberately failing test the developer ran earlier. That shows the failure capture works (the test is not in the tree).
  - `docker compose up -d --build`: `/readyz` 200, one `system_settings` row, one `data_protection_keys` row, migration `20261016214039_InitialCreate` recorded. After `docker compose restart app`, `/readyz` is 200 and there is still exactly one key, so keys persist across restarts. The only warning is the expected "No XML encryptor configured" (accepted in ADR-0003). Afterwards I ran `docker compose down -v`.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MAJOR | `src/Signage.Web/Data/Migrations/20261016214039_InitialCreate.cs:390-430`; `tests/Signage.IntegrationTests/Data/DatabaseStartupTests.cs:102-115` | The migration is not reversible. `Up` creates the ICU collation `case_insensitive` (line 15-16), but `Down` never drops it. After reverting to `0`, the collation stays behind, so re-applying the migration fails with `42710 collation "case_insensitive" already exists` (reproduced). The test `Every_migration_can_be_reverted_to_an_empty_schema` passes only because it checks `information_schema.tables`. It doesn't check for leftover objects and never re-applies. Its name claims something that isn't true. | In `Down`, revert the database annotation (e.g. `migrationBuilder.AlterDatabase().OldAnnotation("Npgsql:CollationDefinition:case_insensitive", …)` or `Sql("DROP COLLATION IF EXISTS case_insensitive")` after the tables are dropped). Strengthen the test so that, after reverting, it asserts no `case_insensitive` collation exists in `pg_collation` and that `MigrateAsync()` succeeds again. |
| 2 | MINOR | `docker-compose.yml:13-16` | The quick-start compose file now publishes the database on `127.0.0.1:5432` for every user, not only developers. Anyone who already runs Postgres on 5432 sees `docker compose up` fail to bind until they discover `SIGNAGE_DB_PORT`. The README doesn't mention it. Binding to loopback only is the right call for security. | Either move the port to a dev-only override (e.g. `docker-compose.dev.yml` or the git-ignored override) or mention `SIGNAGE_DB_PORT` in the README quick start/troubleshooting. Can be deferred to the README package as a backlog line. |
| 3 | NIT | `src/Signage.Web/Data/SignageDbContext.cs:9` | The comment "ICU, case- and accent-sensitive but case-insensitive for equality and uniqueness" contradicts itself. | E.g. "ICU level-2: case-insensitive, accent-sensitive; original spelling kept." |
| 4 | NIT | `tests/Signage.UnitTests/Signage.UnitTests.csproj:12` | `Microsoft.Extensions.TimeProvider.Testing` is referenced but not used by any unit test. | Remove it until a unit test needs it. |
| 5 | NIT | `tests/Signage.IntegrationTests/Data/ModelRulesTests.cs` | Cascades are tested for deleting a slide and deleting a playlist. Deleting a screen (slides/playlists/memberships cascade, `pairing_requests.claimed_screen_id` set to null) and deleting a group are not tested. These are the rules ARCHITECTURE §3 states explicitly. | Add one test per side, ideally before WP-05/WP-06 rely on them. |
| 6 | NIT | `src/Signage.Web/Domain/*.cs` (`CreatedAt`/`UpdatedAt`) | Nothing sets the timestamps. Any feature that forgets will store `0001-01-01`. That's acceptable for now, since feature packages will set them through `TimeProvider`. | Consider a small `SaveChanges` interceptor that uses `TimeProvider`, or make sure each feature package tests its timestamps. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| EF Core model per ARCHITECTURE §3 | Yes | `Domain/*.cs`, `Data/Configurations/*.cs`. ARCHITECTURE §3 was updated to match (Ids on link rows with repeats, `Version`, ci names). ADR-0003 records the choices, and the reasoning holds. |
| Initial migration applies on an empty DB (Accept) | Yes | `DatabaseStartupTests.Startup_applies_all_migrations_to_an_empty_database_and_seeds_default_settings`. Snapshot in sync (`has-pending-model-changes`). |
| Migration is reversible (rubric) | **No** | Finding 1 |
| Auto-migrate on start-up, switchable (R-60) | Yes | `DatabaseInitializer.cs:23-44`. `With_migrations_switched_off_an_up_to_date_database_starts_normally`, `With_migrations_switched_off_an_empty_database_fails_startup_with_a_clear_message`. The env var `SIGNAGE__MIGRATEONSTARTUP=false` was verified by hand. |
| Concurrent replica start-up safe (R-63) | Yes | `Two_replicas_starting_at_once_on_an_empty_database_both_succeed`, plus my 6-process × 5 stress run |
| `SystemSettings` seeding | Yes | `DatabaseInitializer.cs:46-52` (ON CONFLICT DO NOTHING). `Restarting_keeps_existing_settings_and_never_adds_a_second_row`, `Only_one_settings_row_can_exist` (check constraint) |
| Data-protection key store in DB (R-63) | Yes | `DataServiceCollectionExtensions.cs:17-19` (`SetApplicationName` + `PersistKeysToDbContext`). `Data_protection_keys_are_shared_through_the_database`. Keys persisted across a container restart. |
| `TimeProvider` registration | Yes | `Program.cs:15`. The settings timestamp comes from the injected provider (`FakeTimeProvider` asserted in the startup test). No `DateTime.Now`/`UtcNow` in `src/`. |
| R-02 Duration (default 10, override, video ignores) | Yes (model part) | `SystemSettings.cs:7,13`, `SlideDuration.cs`, `SlideDurationTests` (6 + 2 cases), DB check `ck_slides_duration_positive` |
| R-03 Scheduling (stored UTC, window) | Yes (model part; TZ entry is WP-03) | `SlideSchedule.cs` half-open (ADR-0003). `SlideScheduleTests` (boundaries, inverted/empty window, offsets). `Schedule_times_round_trip_as_utc_instants` (timestamptz, offset 0) |
| R-04 Enable/disable | Yes (model part) | `Slide.IsEnabled`. `Disabled_slides_are_skipped_in_every_section` |
| R-12 prepend first, ordered | Yes | `Sections_come_in_order_…`, `Prepended_and_appended_slides_follow_their_positions_not_insertion_order` |
| R-12 groups by sort order then name; group playlists in order | Yes | `Groups_are_ordered_by_sort_order_then_name`, `Group_name_ordering_ignores_case`, `A_groups_playlists_follow_their_positions` |
| R-12 direct playlists ordered; append last | Yes | `Screen_playlists_follow_their_positions`, `Sections_come_in_order_…` |
| R-12 playlist de-dup, first occurrence | Yes | `A_playlist_in_two_groups_plays_only_in_the_first_group`, `A_playlist_in_a_group_and_assigned_directly_plays_only_at_the_group_position`, `A_playlist_assigned_twice_directly_plays_once`, `A_playlist_whose_slides_are_all_filtered_still_counts_for_deduplication` |
| R-12 repeated slide (not de-duplicated) | Yes | `A_slide_repeated_within_a_playlist_plays_each_time`, `The_same_slide_in_different_playlists_plays_in_each`, `A_slide_both_prepended_and_in_a_playlist_plays_in_both_places` |
| R-12 disabled / out-of-schedule filtered; empty | Yes | `Out_of_schedule_slides_are_skipped_in_every_section`, `Schedule_boundaries_are_inclusive_at_the_start_and_exclusive_at_the_end`, `Screen_with_nothing_assigned_…`, `Empty_playlists_contribute_nothing` |
| R-12 against a real DB graph | Yes | `SequenceFromDatabaseTests.A_screen_graph_loaded_from_the_database_resolves_per_R12` (split query, identity resolution) |
| R-13 sources (prep for WP-05) | Yes | `Sources_describe_where_each_slide_came_from` |
| WP-00 follow-ups (a)–(f) | Yes | (a) `docker-compose.yml:13-16` + `appsettings.Development.json:3` (see finding 2). (b) `dotnet-tools.json`, CLAUDE.md command verified. (c) `E2ETest.cs` per-test context, trace and screenshot on failure, disposal. (d) `BrowserFixture.cs` Stopwatch. (e) `DataServiceCollectionExtensions.cs:35-48` parses keys. (f) skill text fixed. |
| Glossary / scope / hygiene | Yes | No forbidden terms in `src/`. Nothing out of scope. Only demo passwords, labelled as such. |

## Notes for the product owner
- When several replicas start against a brand-new database at the same moment, each one creates its own data-protection key (I saw 5–6 keys after a 6-process start). This is harmless. ASP.NET Core refreshes the key ring when it meets an unknown key id, so cookies stay valid across replicas. It is only cosmetic.
- Data-protection keys are stored unencrypted in the database (ADR-0003). This is consistent with treating the database as the trust boundary, but it means a database backup includes everything needed to forge admin cookies. Protect backups accordingly.
