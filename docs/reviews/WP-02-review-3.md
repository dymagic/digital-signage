# Review WP-02 — round 3
- Branch: wp-02-auth @ 822d0c6
- Verdict: CHANGES_REQUIRED
- Verified:
  - `dotnet build` and `dotnet build --no-incremental`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: 163 total, 159 passed, 0 failed, 4 skipped (the 4 E2E tests, which only run inside Docker).
  - `scripts/test-e2e.sh`: exit 0, Signage.E2E total 4, failed 0.
  - Glossary scan (`grep -rniwE "card|cards|monitor|monitors|feed|feeds" src tests`): no hits.
  - Manual concurrency checks against `SIGNAGE_DB_PORT=55499 docker compose -p signage-review3 up -d --build`, driven with curl (real antiforgery tokens and cookies). I created an admin via `/admin/setup` and an Editor "victim" via `/admin/users/create`. The victim signed in and set the password "attacker pass A". Then I ran one "attacker" loop that holds the victim's session and keeps changing the victim's own password on `/admin/account/password` (A→B→A…). The loop needs only the leaked password, and the page renews the attacker's cookie after each change.
    - **Admin "Reset password" during the loop: in 7 of 12 trials the reset was silently undone.** Each reset returned HTTP 200 and showed a 16-character temporary password. Afterwards (after the loop stopped and `user_lockouts` was cleared), the temporary password was refused (200), the attacker's current password signed in (302), and `users.must_change_password` was `f`. In the other 5 trials the reset won.
    - **Admin deactivation during the loop, with a 3 s gap between loading the edit form and saving it (a realistic think time): 5 of 5 attempts were refused** with "Someone else changed this in the meantime". `is_active` stayed `t`. The attacker made 121 password changes in that time. With no gap (GET immediately followed by POST), deactivation got through.
    - Plain sign-in storms (the round-2 scenario) no longer touch `users`. The new test `Signing_in_never_changes_the_user_rows_version` passes, and I agree with the structure of that fix.
    - Then `docker compose -p signage-review3 down -v`.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | BLOCKER | `src/Signage.Web/Features/Users/UserAccounts.cs:127, 144, 151-156` (with `:243-246`); `docs/adr/0004-admin-authentication.md` ("An admin's password reset is unconditional … so it always wins") | **A user's own password change can silently undo an admin reset (the same lost update as round 2 #1, through the change-password path).** `ChangePasswordAsync` reads a snapshot (l.127), runs a full PBKDF2 verification of the current password against the *snapshot's* hash (l.144), hashes the new password, and then writes `password_hash`, `must_change_password = false` and a new stamp **unconditionally** (l.153). If an admin reset commits inside that window, the change overwrites it. An attacker who holds a leaked password signs in, loops "change my password", and defeats the reset in most attempts. I reproduced it in 7 of 12 trials. The admin still sees a temporary password and believes the account is secured. The ADR's claim that a reset "always wins" is not true. The round-2 expectation ("make it conditional on the hash you verified against") was applied to the rehash but not to this write. | Make the change conditional on what was verified: `… WHERE id = @id AND password_hash = @verifiedHash` (and arguably `AND security_stamp = @snapshotStamp`). If 0 rows are updated, fail with the wrong-current-password or concurrency message and don't renew the cookie. Add a deterministic integration test, e.g. a test `IPasswordHasher` that blocks inside `VerifyHashedPassword` until the test has run the admin reset. Then assert that the change is refused, the temporary password works, and `MustChangePassword` is still true. Correct the ADR sentence. |
| 2 | BLOCKER | `UserAccounts.cs:153-156` (bumps `xmin`), `:262-304` (`UpdateAsync` checks `Version` = `xmin`), `Data/Configurations/SystemConfigurations.cs` (User `Version` is `xmin`) | **Anyone holding a user's password can still stop an admin from deactivating that account (round 2 #2, by another route).** Sign-ins no longer write `users`, but the user's own password change does, and every change bumps `xmin`, which is the token the admin's edit form is checked against. The attacker's loop changed the password about every 150 ms (121 changes during my 5 attempts). Any admin who takes more than a moment between opening the edit form and clicking Save gets the conflict message every time (5/5 with a 3 s think time). Together with #1, an admin under attack can neither reliably reset nor deactivate through the UI. | Keep the user's own writes (password change, stamp rotation) from invalidating the admin's access edit. For example, use an app-managed `version` column on `users` that only profile/access edits (display name, role, active) increment, and keep `ExecuteUpdate` password writes from touching it. Alternatively, make deactivation (`IsActive=false`) not version-checked, like the reset. Add an integration test: load the edit form's version, perform an own password change for that user, then submit the deactivation and assert it succeeds. |
| 3 | MINOR | `tests/Signage.IntegrationTests/Admin/ConcurrentSignInTests.cs:89, 110, 134` | The race tests are probabilistic. They rely on `Task.Delay(200/100)` warm-ups and a background storm landing inside the window, so they can pass without exercising the race. Round 2 asked for deterministic interleaving. They also only model sign-in storms, which is why #1 and #2 slipped through. The deactivation test posts a freshly loaded version with no gap, so it can't detect a version bump between load and save. | Use a blocking test hasher (or a hook) to force the interleaving deterministically, and drop the sleeps. `Signing_in_never_changes_the_user_rows_version` is the right kind of test. Keep it. |
| 4 | NIT | `ConcurrentSignInTests.cs:90-99, 138-145` | `for (var i = 0; ; i++) { … if (i == 4) break; }` is a plain 5-iteration loop written awkwardly. `round--; continue;` has no upper bound, so the test can loop forever if the change never succeeds. | `for (var i = 0; i < 5; i++)`, and bound the retries. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| R-50 Accounts, Identity hasher, lockout | Yes | Lockout bookkeeping is now in `user_lockouts` (`UserAccounts.cs:174-187`, an atomic `INSERT … ON CONFLICT DO UPDATE … WHERE not locked`). `SignInTests.A_burst_of_parallel_guesses_gets_at_most_the_allowed_number_of_password_checks`, `After_a_lockout_expires_the_count_starts_again`, `Repeated_failures_lock_the_account_until_the_lockout_expires`. The rehash is conditional (`:209-215`). |
| R-51 First run page / bootstrap env vars | Yes | `SetupTests.*`, `BootstrapTests.*`. |
| R-52 Admins create/role/deactivate/reset; forced change | Partly | `UserAdminTests.*`, `ConcurrentSignInTests.*` pass. **But an own password change can undo a reset (#1) and block deactivation (#2).** |
| Deactivated user can't log in; cookie invalidated (security stamp) | Yes | `SessionInvalidationTests.*`. `UserPrincipal.ValidateAsync` re-checks on every request, so a cookie issued from a stale sign-in snapshot dies on its first request. |
| §3 matrix: every role/endpoint permission tested | Yes | `RoleMatrixTests.*` (unchanged since round 2). |
| R-53 Antiforgery, cookies, encoding, no secrets | Yes | Unchanged. Tests pass. |
| R-64 i18n | Yes | `ResourceCoverageTests`. |
| Glossary | Yes | The scan is clean. |
| Data: migration | Yes | `20261016224343_SeparateUserLockouts` is reversible (Down re-adds the columns with defaults). The PK on `user_id` serves as the lookup index. Cascade delete from `users`. |
| Multi-replica readiness | Yes | The lockout claim is atomic in Postgres. The start-up advisory lock (`DatabaseInitializer.cs:24-45`) is session-scoped on a held connection, released in `finally`, and covered by the existing two-replica start-up test. ADR-0003 is amended. |
| Docs truthful | No | ADR-0004 says the reset "always wins" (#1). ARCHITECTURE and ADR-0003 are updated for `UserLockout` and the advisory lock. |

## Previous round follow-up
| Round-2 # | Status | Evidence |
|---|---|---|
| 1 BLOCKER sign-in writes a stale hash back | Fixed for sign-in | Success no longer writes `users`. The rehash is guarded by `password_hash = verifiedHash`. **The same pattern remains in `ChangePasswordAsync` (new #1).** |
| 2 BLOCKER sign-ins block reset/deactivate | Fixed for sign-in; open via change-password | The reset is unconditional `ExecuteUpdate` (no 500). Sign-ins don't bump `xmin` (`Signing_in_never_changes_the_user_rows_version`). **An own-password-change loop still bumps `xmin` and blocks deactivation (new #2).** |
| 3 MINOR in-flight counting vs ADR | Fixed | ADR-0004 now describes the in-flight behaviour accurately. |
| 4 NIT using order | Fixed | `RoleMatrixTests.cs:1-4`. |

## Notes for the product owner
- What's left is narrow but real. Someone who has a user's password and keeps that user's session open can run a script that changes the password over and over. That script makes an admin reset silently fail about half the time and blocks deactivation from the edit form. The fixes are local: a conditional `WHERE` on the password-change write, plus a concurrency token that the user's own writes don't bump. Everything else in the package looks solid.
- The round-1 notes still apply: the `Secure` cookie behind a TLS proxy depends on the forwarded-headers package, and sign-out doesn't revoke a cookie on the server.
