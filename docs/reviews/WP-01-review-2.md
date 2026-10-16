# Review WP-01 — round 2
- Branch: wp-01-data-model @ 10bfaeb
- Verdict: APPROVE
- Verified:
  - `dotnet build --no-incremental`: 0 warnings, 0 errors.
  - `dotnet test`: 68 total, 67 succeeded, 1 skipped (the E2E smoke test skips on the host by design), 0 failed. Integration tests ran against a real Postgres Testcontainer.
  - Migration round trip by hand against a scratch `postgres:16`: `dotnet ef database update` → `update 0` → `update` → `update 0` → `update`. All five steps finished with "Done.". At the end there was one `case_insensitive` collation and one history row. The round-1 failure (`42710 collation already exists`) is gone.
  - `dotnet ef migrations has-pending-model-changes`: "No changes have been made to the model since the last migration". The hand-written `DROP COLLATION` in `Down` doesn't put the model out of sync with the snapshot.
  - `docker compose config`: the db is published on `127.0.0.1:55432` only. `appsettings.Development.json` and the run-and-verify skill match.
  - `scripts/test-e2e.sh`: `Signage.E2E Total: 1, Errors: 0, Failed: 0, Skipped: 0`.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | NIT | `src/Signage.Web/Data/TimestampInterceptor.cs`; `docs/ARCHITECTURE.md` §3 ("set automatically on save") | The interceptor only runs on `SaveChanges`. `ExecuteUpdate`/`ExecuteSql` bypass it, so a future bulk update such as a reorder or "disable all" won't touch `UpdatedAt`. | Mention it in the ARCHITECTURE line ("…on `SaveChanges`; bulk `ExecuteUpdate` must set `UpdatedAt` itself") or keep it in mind in WP-03/WP-04. |
| 2 | NIT | `src/Signage.Web/Data/TimestampInterceptor.cs:39-45` | On insert, an explicit `UpdatedAt` is kept, but on modify it's always overwritten. That's reasonable, but the asymmetry isn't covered by a test. Only the explicit `CreatedAt` is tested. | Optional: one assertion, or a comment saying it's deliberate. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| EF Core model per ARCHITECTURE §3 | Yes | Unchanged since round 1. ADR-0003 is extended with the timestamp and collation-Down rules. |
| Initial migration applies on an empty DB (Accept) | Yes | `DatabaseStartupTests.Startup_applies_all_migrations_to_an_empty_database_and_seeds_default_settings` |
| Migration is reversible (rubric) | **Yes (fixed)** | `InitialCreate.cs:431-432` (`DROP COLLATION IF EXISTS`). `Every_migration_can_be_reverted_to_an_empty_schema_and_applied_again` checks `pg_collation` and re-migrates. Manual up/down ×2 round trip. |
| Auto-migrate on start-up, switchable (R-60) | Yes | As in round 1, tests still pass |
| Concurrent replica start-up safe (R-63) | Yes | `Two_replicas_starting_at_once_on_an_empty_database_both_succeed` |
| `SystemSettings` seeding | Yes | As in round 1 |
| Data-protection keys in DB (R-63) | Yes | `Data_protection_keys_are_shared_through_the_database` |
| `TimeProvider` registration and use | Yes | `Program.cs`, `DataServiceCollectionExtensions.cs:14-18` (interceptor gets the injected provider). `TimestampTests.Created_and_updated_times_come_from_the_time_provider` (FakeTimeProvider, +5 min on update) |
| R-02 / R-03 / R-04 / R-12 / R-13 model parts | Yes | Unchanged since round 1, all tests green |
| Cascade rules (ARCHITECTURE §3) | Yes | `Deleting_a_screen_removes_its_links_and_releases_pairing_requests_but_keeps_shared_content`, `Deleting_a_group_removes_memberships_and_assignments_but_keeps_screens_and_playlists`, plus the round-1 slide/playlist tests. These use `ExecuteDeleteAsync`, so they exercise the DB-level `ON DELETE` rules, not EF client cascades. That's the right thing to test. |
| Glossary / scope / hygiene | Yes | No forbidden terms. Nothing out of scope. Only demo passwords. |

## Previous round follow-up
| Round-1 # | Status | Evidence |
|---|---|---|
| 1 MAJOR: migration not reversible | Fixed | `InitialCreate.cs:431-432`. Strengthened test. Manual round trip ×2 succeeds. ADR-0003 adds the rule for future migrations. |
| 2 MINOR: compose binds 5432 | Fixed and deferred | Default host port moved to `127.0.0.1:55432` (no clash with a local Postgres). README note is recorded as a WP-12 follow-up in `docs/BACKLOG.md`. |
| 3 NIT: self-contradicting comment | Fixed | `SignageDbContext.cs:9` |
| 4 NIT: unused test package | Fixed | Removed from `Signage.UnitTests.csproj` |
| 5 NIT: screen/group cascade tests | Fixed | Two new tests in `ModelRulesTests.cs` |
| 6 NIT: timestamps never set | Fixed | `TimestampInterceptor` + `IHasCreatedAt`/`IHasUpdatedAt`, `TimestampTests` (2 tests), documented in ARCHITECTURE §3 and ADR-0003 |

## Notes for the product owner
- The round-1 notes still apply: several replicas starting cold create a few extra data-protection keys, which is harmless. Keys are stored unencrypted in the database, so protect your database backups.
