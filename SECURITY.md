# Security policy

## Reporting a vulnerability

Please report vulnerabilities privately through GitHub: **Security → Report a vulnerability** on this repository
(private vulnerability reporting). Don't open a public issue for a security problem.

Include what you found, how to reproduce it, and the version (the image tag or commit). You'll get an answer as soon as
possible; this is a small project maintained in spare time, so please allow a few days.

## Supported versions

Only the latest release gets fixes.

## Scope and deployment model

Signage is meant to run on a **trusted network behind a firewall**, not on the public internet (see the README's
"Security" section). Reports are still welcome for anything that breaks the security baseline described there:
authentication and lockout, authorization between admins and editors, antiforgery, output encoding, the sandbox of HTML
and web-page slides, and the handling of screen tokens.

Things that are out of scope by design: running it without HTTPS on an untrusted network, and the content of slides
that an editor deliberately points at (HTML slides may contain scripts; they run sandboxed).
