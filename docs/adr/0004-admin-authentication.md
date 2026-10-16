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
  locked for 15 minutes. The failure counter is updated in one atomic SQL statement, so parallel
  attempts all count and never conflict. Every sign-in failure (unknown email, wrong password, locked,
  deactivated) shows the same message, and unknown emails still go through a dummy hash, so neither
  the response nor its timing reveals whether an account exists.
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
