# Review WP-04 — round 1
- Branch: wp-04-playlists @ aad1c9d
- Verdict: APPROVE
- Verified:
  - `git diff main...wp-04-playlists --stat`: 36 files, +1419/−23. I read the full diff.
  - `dotnet build --no-incremental`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: 273 total, 265 succeeded, 0 failed, 8 skipped (the E2E tests, which only run in the container).
  - `scripts/test-e2e.sh`: exit 0, `Signage.E2E Total: 8, Errors: 0, Failed: 0, Skipped: 0`. I looked at the screenshots
    `artifacts/e2e/screens/playlist-editor.png` (the order after a button move and a drag, disabled up/down at the ends, the "used by"
    panel) and `slides-edit-html.png` (the preview's right edge now lines up with the header at about 1486 px, so WP-03 NIT 7 is fixed).
  - Checked by hand: vendored htmx is 2.0.11 with `allowNestedOobSwaps: true`, so the `#playlist-version` OOB input nested inside
    `#entries` gets swapped. `htmx:afterSwap` fires on the newly swapped elements, so `reorder.js` re-initialises the new list.
    The layout's `<body hx-headers>` carries the antiforgery header, and `htmx.ajax` inherits it. The playlist name has a unique index
    with the case-insensitive collation (ContentConfigurations.cs:26-29). The `MustChangePasswordFilter` is a global page filter,
    so it also applies to the `Add`/`Remove`/`Move` handlers. The diff contains no glossary violations, `DateTime.Now`/`UtcNow` or `Html.Raw`.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MINOR | src/Signage.Web/Pages/Admin/Playlists/_PlaylistEntries.cshtml:49,55,60 | This is the keyboard fallback. After an htmx move or remove, the whole `#entries` block is replaced. htmx only restores focus to an element that has an `id`, and these buttons have none, so focus drops to `<body>`. To move a slide down three places, a keyboard or screen-reader user has to tab back through the nav and the list after every press. That weakens the "keyboard-accessible up/down fallback". | Give each button a stable id (for example `move-down-@entry.Id`) so htmx refocuses it. Better still, after a swap move focus to the moved entry's matching button. Also announce the new position (`aria-live`). A follow-up line on WP-05 is acceptable, because the same partial pattern is reused there. |
| 2 | MINOR | src/Signage.Web/Pages/Admin/Playlists/Edit.cshtml.cs:78-80 | If someone else deletes the playlist while it is open, every htmx change returns a 404. htmx 2's default `responseHandling` does not swap 4xx responses, so the click does nothing and no message appears. The acceptance criterion asks for a friendly message on concurrent edits. | For htmx requests, return the partial (or a small notice) with "That item no longer exists" and a link back to the list. Alternatively, send `HX-Redirect` to the index with a TempData error. Add a test. |
| 3 | MINOR | tests/Signage.IntegrationTests/Admin/PlaylistPagesTests.cs | Two paths have no test: (a) the details form (`OnPostAsync`) posted with a stale `Version` should show the conflict message and leave the name unchanged; only the entries path is covered by `A_change_based_on_a_stale_version_is_refused_with_a_friendly_message`. (b) ADR-0006 says the htmx handlers are subject to `MustChangePasswordFilter` (the WP-02 follow-up), but no test posts to `?handler=Move` as a user who must change their password. | Add both tests. |
| 4 | NIT | src/Signage.Web/Features/Slides/SlideService.cs:112; PlaylistService.cs:154-174 | Deleting a slide renumbers playlist entries but does not bump the playlist's `xmin`. Two things follow. (a) An open editor page keeps a valid version, and its stale `toIndex` values can put a moved entry one slot off from what the user saw. (b) If an `Add` runs at the same time as the slide delete, it can insert at `n` after the renumber, which leaves a gap. The order is preserved and the next change rewrites positions densely, so the impact is cosmetic. | Optionally also touch the affected playlists' rows during the renumber, or accept this as is. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| R-08 playlist CRUD | Yes | Create/Edit/Delete/Index pages; `An_editor_creates_a_playlist_and_adds_slides_in_order_including_duplicates`; `Names_are_required_and_unique_regardless_of_case`; `Unknown_playlists_are_a_404` |
| R-08 ordered list; same slide in several playlists and more than once in one | Yes | `PlaylistService.AddSlideAsync` (no de-dup); test above asserts `A,B,A` at positions 0..2 |
| Add/remove slides | Yes | `Removing_an_entry_closes_the_gap` (the slide itself is kept); `Adding_a_slide_that_was_deleted_meanwhile_reports_it_without_changing_anything` |
| Reorder via htmx, drag plus keyboard up/down | Yes (see finding 1) | `_PlaylistEntries.cshtml`, `reorder.js`; E2E `Slides_are_added_reordered_with_buttons_and_by_dragging_and_the_order_persists` (button and real drag) |
| Reorder persists densely | Yes | `Moving_an_entry_rewrites_positions_densely` (including clamping of -5/99); E2E verifies the order after a full page reload |
| Concurrent edit gets a friendly conflict message | Yes for entries; untested for details (finding 3); missing for concurrent delete (finding 2) | `A_change_based_on_a_stale_version_is_refused_with_a_friendly_message`; `Renaming_after_reordering_on_the_same_page_does_not_conflict` (OOB version) |
| "Used by" info (screens and groups) | Yes | `The_edit_page_shows_which_screens_and_groups_use_the_playlist`; delete confirmation lists them |
| Delete removes assignments and keeps lists dense | Yes | `Deleting_a_playlist_removes_its_assignments_and_keeps_the_other_assignments_dense` |
| Editor and Admin both allowed; anonymous redirected | Yes | `RoleMatrixTests` (4 playlist routes); all `PlaylistPagesTests` run as Editor |
| Antiforgery on htmx posts | Yes | `Htmx_posts_without_the_antiforgery_header_are_rejected` |
| WP-03 #1: dense positions after slide delete | Yes | `PositionMaintenance.cs`; `Deleting_a_slide_keeps_playlist_and_screen_positions_dense`; ARCHITECTURE §3 updated |
| WP-03 #3: E2E waits for the edit page properly | Yes | `SlideLibraryTests` uses a GUID regex and `Expect(...)` auto-retrying assertions |
| WP-03 NITs 5–9 | Yes | `Slides.Usage` reworded; `Slides.Column.Status`; `.split-preview` CSS (screenshot); `KeepWarningForNextPage` after save; `Split('#')[0]` |
| WP-02 follow-up: forced password change on htmx endpoints | Yes by design (Razor handlers, global page filter); untested (finding 3b) | AdminUi global filter; ADR-0006 |
| ADR for the decision | Yes | docs/adr/0006-htmx-list-editing.md; ARCHITECTURE §4 note |
| Glossary / scope / localization | Yes | No forbidden terms; all strings in `SharedResource.resx` (the coverage test passes) |
| Build warning-free, tests green | Yes | See Verified |

## Notes for the product owner
- After a conflict on the details form, the page shows the message and reloads with the latest version. If the user presses Save again,
  their values overwrite the other person's. This is the usual "last informed write wins" pattern and matches the slide editor.
- The playlist picker lists every slide, disabled ones included (they are marked in the list). That's reasonable, but with a very large
  library you may want search in the picker later.
