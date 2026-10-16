# Vendored third-party JavaScript

Screens and the admin often sit on closed networks, so nothing is loaded from a CDN at runtime.
Every library is copied here at a pinned version with its licence next to it.

| Library | Version | Licence | Source | File SHA-256 |
|---|---|---|---|---|
| htmx | 2.0.11 | 0BSD (`htmx/LICENSE`) | npm `htmx.org@2.0.11`, `dist/htmx.min.js` (tarball integrity verified against the registry) | `d6fdc75f204e6bdefa99b69bf1e6d4ac69b8a364f77929f45c13476b4000f717` |

To upgrade: download the new tarball from `https://registry.npmjs.org/htmx.org/-/htmx.org-<version>.tgz`
(with `curl`, no npm needed), check its `sha512` against the registry's `dist.integrity`, copy
`package/dist/htmx.min.js` and `package/LICENSE` here, and update this table.
