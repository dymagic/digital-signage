# Review WP-02 — round 4
- Branch: wp-02-auth @ de734e7
- Verdict: APPROVE
- Verified:
  - `dotnet build --no-incremental`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: 163 total, 0 failed, 4 skipped (the 4 E2E tests, which only run inside Docker).
  - `scripts/test-e2e.sh`: Signage.E2E total 4, failed 0.
  - `dotnet ef migrations has-pending-model-changes -p src/Signage.Web`: "No changes have been made to the model since the last migration."
  - Ran `ConcurrentSignInTests` 5 times in a row: 4/4 passed every time (about 10 s per run, no sleeps).
  - **Mutation check** in a throw-away git worktree (since removed). I reverted the two round-3 fixes in `UserAccounts.ChangePasswordAsync`: I removed `&& u.PasswordHash == verifiedHash` and made the change bump `Version`. Then `An_admin_reset_that_lands_during_the_users_own_password_change_wins` and `A_deactivation_form_opened_before_the_users_own_password_changes_still_saves` both **failed**. So the new tests catch exactly the regressions from round 3.
  - Glossary scan (`grep -rniwE "card|cards|monitor|monitors|feed|feeds" src tests`, excluding bin/obj): no hits.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MINOR | `docs/adr/0004-admin-authentication.md:46-47` | The new sentence "Other entities keep `xmin` (ADR-0003). Their rows are written only by human edits." isn't true for `Screen`. `Screen.LastSeenAt`, `LastIp`, `PlayerVersion` and `UserAgent` (`Domain/Screen.cs:16`, ARCHITECTURE l.77 and l.138) are written by the player heartbeat (WP-10). With `xmin` as the token, every heartbeat will turn an admin's open screen edit form into a conflict. That is the same class of bug as round 3 #2, and this ADR now says it can't happen. | Reword the sentence (e.g. "…except where a machine writes the row, see WP-10"). Add a follow-up line to WP-10 in `docs/BACKLOG.md`: heartbeat writes must not bump the Screen's admin-edit concurrency token (use an app-managed version like User, or put the heartbeat fields in a separate table). Can be deferred as a backlog line. |
| 2 | NIT | `docs/adr/0004-admin-authentication.md:47-50` | The sentence "Every sign-in failure (unknown email, wrong password, locked, deactivated) shows the same message…" now ends up in the bullet about the `version` column, where it doesn't belong. | Move it to its own bullet. |
| 3 | NIT | `tests/Signage.IntegrationTests/Admin/ConcurrentSignInTests.cs` | The class now mostly covers concurrent *password changes* and admin actions, not only sign-ins. | Optionally rename it (e.g. `AccountRaceTests`). |
| 4 | NIT | `tests/Signage.IntegrationTests/Infrastructure/GatedPasswordHasher.cs:37-44` | If a test fails between `Arm` and `Release`, `using var gate` disposes the `ManualResetEventSlim` while a server thread may still be in `Wait`. That thread then gets an `ObjectDisposedException` instead of a clean release. This only happens when a test has already failed, so it only adds noise. | Optionally call `Set()` before `Dispose()` in `Gate.Dispose`. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| R-50 Accounts, Identity hasher, lockout | Yes | Unchanged since round 3 and still correct. `UserAccounts.cs:179-226` (atomic claim in `user_lockouts`, conditional rehash l.222). `SignInTests.*`. |
| R-51 First run page / bootstrap env vars | Yes | `SetupTests.*`, `BootstrapTests.*`. |
| R-52 Admins create/role/deactivate/reset; forced change | Yes | The own password change is conditional on the verified hash (`UserAccounts.cs:153-164`), so an admin reset always wins: `ConcurrentSignInTests.An_admin_reset_that_lands_during_the_users_own_password_change_wins` (deterministic via `GatedPasswordHasher`; asserts the temporary password works, the attacker's doesn't, and `MustChangePassword` is still true). The admin-edit token is the app-managed `users.version`, bumped only by `UpdateAsync` (l.302) and the reset (l.256): `A_deactivation_form_opened_before_the_users_own_password_changes_still_saves`. Real admin-vs-admin conflicts are still detected: `UserAdminTests` (the stale-form test now uses a real second admin edit, and the assertion is as strong as before). |
| Deactivated user can't log in; cookie invalidated (security stamp) | Yes | `SessionInvalidationTests.*`. `UserPrincipal.ValidateAsync` (`UserPrincipal.cs:46-72`) re-reads IsActive, stamp and role on every request. A concurrent own password change that overwrites a stamp rotated by an admin therefore can't restore access or a role, because IsActive and Role are re-read from the row. `An_admin_reset_that_lands_during_a_sign_in_wins_and_that_sign_in_gets_no_usable_session`. |
| §3 matrix: every role/endpoint permission tested | Yes | `RoleMatrixTests.*`. |
| R-53 Antiforgery, cookies, encoding, no secrets | Yes | Unchanged. Tests pass. |
| R-64 i18n | Yes | `ResourceCoverageTests`. The concurrency message reuses the existing `CommonErrors.Concurrency` key. |
| Glossary | Yes | The scan is clean. |
| Data: migration | Yes | `20261017050057_AppManagedUserVersion` adds `version integer NOT NULL DEFAULT 0`, and its Down drops it, so it is reversible. The generator's attempt to rename `xmin` was correctly removed by hand and the reason is commented. The snapshot matches the model (no pending changes). The full-chain revert/re-apply test passes. |
| Multi-replica readiness | Yes | All races are settled in Postgres (a conditional `UPDATE`, an atomic version check). There is no in-memory state. |
| Docs truthful | Mostly | ADR-0004, ADR-0003 and ARCHITECTURE are updated for the `version` column and the "reset always wins" claim, which is now true. One sentence about other entities is inaccurate (#1). |

## Previous round follow-up
| Round-3 # | Status | Evidence |
|---|---|---|
| 1 BLOCKER own password change undoes an admin reset | Fixed | `UserAccounts.cs:156` `WHERE id = @id AND password_hash = @verifiedHash`. With 0 rows it returns `Concurrency` and the page doesn't renew the cookie (`Password.cshtml.cs:112-116`). A deterministic test is in place and the mutation check confirms it. The ADR sentence is corrected. |
| 2 BLOCKER own password changes block deactivation | Fixed | `users.version` is an app-managed concurrency token (`SystemConfigurations.cs:18`). The password change no longer touches it. A deterministic test is in place and the mutation check confirms it. |
| 3 MINOR probabilistic race tests | Fixed | `GatedPasswordHasher` forces the interleaving. There is no `Task.Delay`, and the waits use `WaitAsync(30 s)`. |
| 4 NIT awkward and unbounded loops | Fixed | The loops are gone. The remaining loop is `for (var i = 0; i < 3; i++)` and asserts each step. |

## Notes for the product owner
- The two round-3 security issues are fixed properly. I confirmed this by breaking the fix on purpose: the new tests then fail. Someone who holds a leaked password can no longer undo an admin's reset or block a deactivation.
- Watch out for heartbeats (WP-10). Screens will get the same "a machine keeps writing the row" problem when the player heartbeat updates `LastSeenAt`. Finding #1 asks for a backlog line so that it's designed in from the start.
- The round-1 notes still apply: the `Secure` cookie behind a TLS proxy depends on the forwarded-headers package, and sign-out doesn't revoke a cookie on the server (other sessions end only on a stamp change).
