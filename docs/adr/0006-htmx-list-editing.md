# ADR-0006: Editing ordered lists with htmx

- Status: Accepted
- Date: 2026-10-17
- Work package: WP-04
- Requirements: R-08 (and later R-11, R-12 for screens and groups)

## Context
Playlists are ordered lists of slides. ARCHITECTURE asks for reordering by drag "with a keyboard-accessible
up/down fallback, via htmx", dense positions, and a friendly message on concurrent edits. Screens (prepended
and appended slides, direct playlists) and groups (ordered playlists) will need the same pattern in WP-05.

## Options considered
1. **A JSON API plus a client-side list component**: more JavaScript, a second endpoint style, and it
   bypasses Razor Pages filters such as the forced password change.
2. **SortableJS + htmx**: a vendored dependency for something native HTML5 drag-and-drop can do in about 80 lines.
3. **Razor Page handlers that return a partial, with plain forms as the base**: every action is a normal
   `<form method="post">` (works without JavaScript: post, then redirect). htmx enhances the same forms to
   post in the background and swap the list. A small `reorder.js` adds native drag-and-drop that calls
   the same Move handler.

## Decision
Option 3.
- Handlers `Add`, `Remove` and `Move` on the edit page. An htmx request (`HX-Request: true`) gets the
  `_PlaylistEntries` partial. A plain post gets a redirect, with any error in TempData.
- Every change carries the playlist `Version` (`xmin`). Changing entries also marks the playlist row
  modified, so its version moves on even when only `playlist_slides` changed. A stale change returns the
  fresh list with "Someone else changed this in the meantime" and nothing is applied.
- After each swap, the partial also sends `#playlist-version` out of band (`hx-swap-oob`), so the details form
  on the same page carries the new version. Without that, renaming right after a reorder would conflict
  with itself.
- Positions are rewritten densely (0..n-1) on every change. Deletes that cascade (slide, playlist) renumber
  the affected lists in the same transaction (`PositionMaintenance`, SQL `ROW_NUMBER()`).
- The antiforgery token travels in the `RequestVerificationToken` header, which the layout's `hx-headers`
  sets for all htmx requests.
- Because the htmx endpoints are Razor Page handlers, the folder authorization and the
  `MustChangePasswordFilter` apply to them as they do to pages (WP-02 follow-up).
- Up/down buttons have `aria-label`s that name the slide. Drag-and-drop is an extra for mouse users.

## Consequences
- WP-05 reuses `_…Entries`-style partials, `reorder.js` (it posts to `data-move-url`) and
  `PositionMaintenance`.
- A large playlist re-renders its whole list on each change. That's fine at signage scale (tens of slides).
