# ADR-0004: Admin authentication details

- Status: Accepted
- Date: 2026-10-17
- Work package: WP-02
- Requirements: R-50, R-51, R-52, R-53, R-63, R-64

## Context
ARCHITECTURE §1 says "ASP.NET Core Identity *core* (`AddIdentityCore`) for hashing/lockout, own Razor
pages". When I built it, `AddIdentityCore` turned out to need a full `IUserStore` implementation
(password, lockout, security-stamp and email stores) just to use `UserManager`. `UserManager`'s lockout
also reads the system clock instead of the injected `TimeProvider`, so lockout expiry couldn't be tested
without real waiting. Other open points were the `Secure` cookie flag on plain-HTTP LAN installs, and how
quickly a deactivated user loses access.

## Options considered
1. **`AddIdentityCore` + a custom EF user store**: the framework does the lockout. But it adds about
   150 lines of store plumbing, depends on the system clock, and brings `UserManager` features we don't use.
2. **Identity's `PasswordHasher<User>` + our own small `UserAccounts` service**: the same proven hashing
   (PBKDF2 with rehash-on-upgrade). Lockout is about 30 lines on `TimeProvider`, and fully testable.

For the cookie: `Secure=Always` (breaks every plain-HTTP install, which is the default on a LAN) vs.
**`SameAsRequest`** (secure whenever the request is HTTPS, including behind a trusted proxy).

For revocation: validate the security stamp periodically (Identity's default is every 30 min) vs.
**on every request** (one indexed primary-key query per admin request).

## Decision
- Option 2. Passwords are hashed with `PasswordHasher<User>`. After 5 failed attempts the account is
  locked for 15 minutes. Every password check first *claims* a try in one atomic `UPDATE … WHERE not
  locked`, which also sets the lockout on the 5th claim. PostgreSQL row locks serialise the claims, so
  at most 5 passwords are checked per lockout window, however many requests arrive in parallel. A
  correct password clears the counter. Because a claim counts until its password is verified, when 5 or
  more sign-ins for the *same* account are in flight at once, the extra ones are refused as locked until one
  of them succeeds. That is accepted: it only happens under a guessing burst. Wrong current passwords
  on the change-password page go through the same check. Every sign-in failure (unknown email, wrong password,
  locked, deactivated) shows the same message, and unknown and locked accounts still pay for a (dummy) hash,
  so neither the response nor its timing reveals whether an account exists or is locked.
- The counter and lockout live in their own table, `user_lockouts`, not on `users`. Sign-ins therefore
  never change the user row: they can't bump its concurrency token (`xmin`), which would let someone
  holding a leaked password make an admin's deactivation fail as a conflict, and they never write the
  password hash back. A rehash after a hasher upgrade is conditional on the hash being unchanged.
- The user's own password change is also conditional: it writes only if the stored hash is still the one it
  just verified. An admin's password reset is unconditional (no version check). Together that means a reset
  always wins, whatever the user (or someone holding their password) does at the same moment.
- The concurrency token for admin edits of a user is an app-managed `version` column, not `xmin`. Only admin
  edits and resets increment it. The user's own sign-ins and password changes never do, so they can't make an
  admin's deactivation or role change fail as a conflict. The same applies to `Screen` (WP-05): heartbeats and
  pairing write its row, so it also has an app-managed `version` that only edits by people increment. Other
  entities (slides, playlists, groups, settings) keep `xmin` (ADR-0003), because only people write their rows.
- Passwords need 10–128 characters and have no composition rules (NIST 800-63B style). Temporary
  passwords are 16 random characters from an alphabet without look-alike characters.
- The cookie `signage.auth` is `HttpOnly` and `SameSite=Lax`, with `Secure` set when the request is
  HTTPS (`SameAsRequest`). It lasts 12 h, sliding.
- Every request re-reads the user's security stamp, active flag, role, name and must-change flag. A
  deactivation, role change, password reset or own password change ends the affected sessions at once,
  and the cookie never carries stale permissions. On its own password change, the user's current
  session is renewed.
- The first admin is created by the setup page or by the bootstrap env vars. A PostgreSQL advisory
  lock serialises both paths, so concurrent attempts create exactly one admin, also across replicas.
  The bootstrap admin must change the password at first sign-in, so the value in the environment
  stops working. Invalid bootstrap values stop start-up.
- Admins can't change their own role or deactivate themselves. The service also refuses to remove the
  last active admin.
- The global **Settings** page (default duration, info overlay, time zone) lands in this package. It
  belongs to the admin-only part of the role matrix (§3), and no later package owns it.
- Localization: a single `SharedResource.resx` with dotted keys (`Users.Field.Email`), accessed through
  `IStringLocalizer<SharedResource>` (`L[...]` in views). A unit test checks that every key used in code
  exists in the file.

## Consequences
- ARCHITECTURE §1 is updated: Identity's hasher only, no `AddIdentityCore`.
- One extra small query per admin request. That's negligible at office scale.
- Plain-HTTP installs send the cookie unencrypted on the LAN. The README security note (WP-12) must
  recommend HTTPS (a reverse proxy) when the network isn't trusted.
- If SSO arrives later (out of scope for M1), it plugs in as another authentication scheme that issues the
  same principal shape.
