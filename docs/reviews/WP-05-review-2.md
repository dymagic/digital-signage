# Review WP-05 — round 2
- Branch: wp-05-screens-groups @ ceda4bb
- Verdict: CHANGES_REQUIRED
- Verified:
  - Read the round-1 fix commit `018572b` in full (17 files, +289/−34), plus the surrounding `ChangeAsync`/`SaveAsync` code in `ScreenService` and `GroupService`, the `_ScreenSlideList`/`_ScreenComposition` markup and `reorder.js`.
  - `dotnet build`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: Passed. Total 330, failed 0, succeeded 320 (the other 10 are the E2E tests, which only run inside the container).
  - `scripts/test-e2e.sh`: Signage.E2E total 10, errors 0, failed 0, skipped 0 (exit 0).
  - **Probe (scratch copy only, made with `git archive HEAD` in the session scratchpad; the repo was not touched):** I added two throw-away theories to `ScreenPagesTests`. They post `Details.Name` = `""` and `"   "` to `/admin/screens/{id}?handler=Details`, and `Input.Name` = `""` and `"   "` to `/admin/groups/create`. All four responses were 200 with the error summary *"The form contained a value this page can't use. Reload the page and try again."* instead of *"Enter a name."* (see finding 1).

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MAJOR | `Pages/Admin/Screens/Detail.cshtml.cs:72-75`, `Pages/Admin/Groups/Create.cshtml.cs:20-24`, `Pages/Admin/Groups/Detail.cshtml.cs:53-56` | **Regression from the round-1 fix for #4. A blank name now gives a wrong, misleading error.** `<Nullable>enable</Nullable>` is on (`Directory.Build.props:5`) and `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes` isn't set. MVC therefore treats the non-nullable `string Name` in `DetailsInput` and `GroupInput` as `[Required]`, and an empty or whitespace-only name makes `ModelState.IsValid` false. The new blanket `!ModelState.IsValid → CommonErrors.InvalidRequest` check then wins over the service's `NameRequired`. An admin who clears the name field (the inputs have no HTML `required`, so the browser doesn't stop it) is told to "Reload the page and try again" instead of "Enter a name." I reproduced this for screen details and group create (both `""` and `"   "`), and group details takes the same path. No test covers the blank-name case on these pages, which is why the suite stays green. R-10/R-11 say the name is required, and the user must get the right message for that. | Reject only *binding* failures, not validation of the input model. For example, check `ModelState` entries with `ValidationState == Invalid` whose errors came from binding (or a specific key list: `Version`, `Details.Mode`, `Input.SortOrder`, `placement`, ids). Another option is to set `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true` in `AdminUi` and confirm that no page relies on the implicit `[Required]`. Add integration tests: a blank or whitespace name on screen details, group create and group details shows "Enter a name." and changes nothing. |
| 2 | NIT | `Features/Playlists/PlaylistService.cs:147-150` | `SetProperty(g => g.SortOrder, g => g.SortOrder)` relies on Postgres writing a new tuple (and so a new `xmin`) for a no-op update. That is correct and has a comment, but no test covers it. If a later refactor turns it into a real no-op, the bump disappears without anyone noticing. | Optional: an integration test that deleting a playlist assigned to a group changes the group's `Version`. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| R-10 Screens: name required & unique (ci), location/notes, mode Carousel/Single video | **Partly** (see #1) | Unique/ci: `ScreenPagesTests.An_admin_creates_a_screen_and_names_are_unique_regardless_of_case`. Name required on edit: wrong message, reproduced by probe |
| R-11 Groups CRUD, membership, ordered playlists, sort order | **Partly** (see #1) | `GroupPagesTests.*`. Name required on create/edit: wrong message, reproduced by probe |
| R-12 Composition order, de-dup, filters | Yes | `ScreenPagesTests.The_resolved_sequence_on_the_page_equals_the_resolvers_output` (unchanged since round 1, passes) |
| R-13 (partly) resolved sequence with sources | Yes | as round 1 |
| R-14 (config) mode + single video URL | Yes | `Single_video_mode_needs_a_valid_video_url_and_non_mp4_addresses_only_warn`; undefined modes are now refused: `Values_the_form_cannot_produce_are_refused_not_turned_into_defaults` (`Hologram`, `7`) |
| Role split §3 | Yes | `RoleMatrixTests`; `RequireAdmin()` still runs before the new `ModelState` checks, so editors still get 403 and not an error message |
| Domain events through `IScreenNotifier` | Yes | `ScreenPagesTests.Deleting_a_screen_notifies_it`; `ScreenService.cs:211-222` |
| Accept: page sequence equals resolver output | Yes | as above |

## Previous round follow-up
| Round-1 # | Status | Evidence |
|---|---|---|
| 1 MAJOR OOB version refresh after a failed change | **Fixed** | `_ScreenComposition.cshtml:14-19`, `_GroupPlaylists.cshtml:9-10` and `_PlaylistEntries.cshtml:12-13` now require `…Error is null`. Tests: `ScreenPagesTests.A_failed_list_change_does_not_refresh_the_details_form_so_saving_it_still_reports_the_conflict` (it runs the full lost-update scenario end to end), `A_successful_list_change_refreshes_the_side_forms_versions`, `GroupPagesTests.A_failed_playlist_change_on_the_group_page_does_not_refresh_the_side_forms`, and the new `DoesNotContain("hx-swap-oob")` assertion in `PlaylistPagesTests`. |
| 2 MINOR membership edited from two aggregates | **Fixed** | `ScreenService.SetGroupsAsync` marks the touched groups `Modified` (new `xmin`). `GroupService.SetMembersAsync` bumps the touched screens' `Version`, which is a concurrency token, so a concurrent screen edit surfaces as a conflict. Tests: `A_membership_change_on_the_screen_page_makes_a_stale_members_form_on_the_group_page_conflict`, `A_membership_change_on_the_group_page_makes_a_stale_groups_form_on_the_screen_page_conflict`. |
| 3 MINOR screen delete doesn't notify | **Fixed** | `ScreenService.DeleteAsync` notifies after a successful delete; test `Deleting_a_screen_notifies_it`. |
| 4 MINOR `ModelState` ignored | **Fixed, but introduced a regression** | Binding failures (`Mode=Hologram`, `Version=abc`, `placement=Sideways`) are refused and tested. `Enum.IsDefined` in the service also catches numeric undefined enums, which binding accepts. The blanket `IsValid` check also catches the implicit `[Required]` on names: new finding 1. |
| 5 NIT playlist delete doesn't bump versions | Fixed (untested, new NIT 2) | `PlaylistService.cs:147-150` |
| 6 NIT focus fallback | Fixed | `reorder.js` `rememberFocus` picks the neighbouring entry's remove button, then the add control that belongs to the same list. The direct-playlist select now has `data-focus-fallback`. E2E `Keyboard_reordering_keeps_focus_on_the_moved_entry` asserts the neighbour gets focus. |
| 7 NIT unused `LastSeenAt` | Fixed | `ScreenListItem` no longer carries it. |

## Notes for the product owner
- The round-1 fixes are good, and the main concurrency hole is properly closed and tested end to end. One problem remains, and the fix itself caused it: leaving a screen or group name blank now shows "The form contained a value this page can't use. Reload the page and try again." instead of "Enter a name." It's a small change: only refuse real binding failures. A test for the blank-name case would have caught it.
