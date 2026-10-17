# Review WP-05 — round 3
- Branch: wp-05-screens-groups @ 3fab443
- Verdict: APPROVE
- Verified:
  - Read the fix commit `7d59591` in full (`AdminUi.cs` +5, `GroupPagesTests.cs` +33, `ScreenPagesTests.cs` +13). Because the fix is a **global** MVC option, I checked every page model that reads `ModelState.IsValid` to make sure none of them depended on the implicit `[Required]`: Login, Setup, Users/Create, Users/Edit, Account/Password, Settings, Slides (Create/Edit via `SlideFormPage`), Playlists (Create/Edit), Screens (Create/Detail) and Groups (Create/Detail). Every required string on the user/auth/settings pages has an explicit `[Required(ErrorMessage = "Validation.Required")]`. `SlideInput` strings are all nullable. Playlist, screen and group names are checked in the services with `string.IsNullOrWhiteSpace` (`PlaylistService.cs:218`, `GroupService.cs:198`, `ScreenService.cs:301`), so a `null` from `ConvertEmptyStringToNull` is handled. There are no controllers (`AddRazorPages` only), so the option has no other effect.
  - `dotnet build`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: Passed. Total 335, failed 0, succeeded 325, skipped 10 (the E2E tests, which only run inside the container).
  - `scripts/test-e2e.sh`: Signage.E2E total 10, errors 0, failed 0, skipped 0 (exit 0).

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | NIT | `Pages/Admin/Screens/Detail.cshtml.cs` `DetailsInput.Name`, `Pages/Admin/Groups/GroupInput.cs` `Name`, `Pages/Admin/Playlists/PlaylistDetailsInput.cs` `Name`, `Pages/Admin/Screens/Create.cshtml.cs` `Name` | These are declared `string Name = ""`, but because MVC turns an empty field into `null`, they can be `null` at runtime. That was true before this round too, and every service handles it, so nothing breaks today. Still, the annotation now says something false, and a later contributor could call `Name.Trim()` in a page model. | Optional: declare them `string?`, which matches what binding actually produces. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| R-10 Screens: name required & unique (ci), location/notes, mode Carousel/Single video | Yes | `ScreenPagesTests.A_blank_name_on_the_details_form_says_enter_a_name` (`""`, `"   "`; also checks the name is unchanged), `An_admin_creates_a_screen_and_names_are_unique_regardless_of_case` |
| R-11 Groups CRUD, membership, ordered playlists, sort order | Yes | `GroupPagesTests.A_blank_group_name_says_enter_a_name_on_create_and_on_edit` (create and edit; also checks that no group was created and the name is unchanged), `Group_names_are_unique_regardless_of_case`, other `GroupPagesTests.*` |
| R-12 Composition order, de-dup, filters | Yes | `ScreenPagesTests.The_resolved_sequence_on_the_page_equals_the_resolvers_output` |
| R-13 (partly) resolved sequence with sources | Yes | as above |
| R-14 (config) mode + single video URL | Yes | `Single_video_mode_needs_a_valid_video_url_and_non_mp4_addresses_only_warn`, `Values_the_form_cannot_produce_are_refused_not_turned_into_defaults` (binding failures are still refused after the option change) |
| Role split §3 | Yes | `RoleMatrixTests`; `RequireAdmin()` still runs before the `ModelState` checks |
| Domain events through `IScreenNotifier` | Yes | `ScreenPagesTests.Deleting_a_screen_notifies_it` |
| Accept: page sequence equals resolver output | Yes | as R-12 |

## Previous round follow-up
| Round-2 # | Status | Evidence |
|---|---|---|
| 1 MAJOR blank name gives generic "invalid value" error | **Fixed** | `AdminUi.cs:37-40` sets `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true`, with a comment explaining why. Real binding failures (`Mode=Hologram`, `Version=abc`, `placement=Sideways`) still make `ModelState` invalid and are still refused; their tests pass. The new tests cover blank and whitespace names on screen details, group create and group details, and assert the right message and that nothing changed. I checked the other pages for side effects of the global switch (see Verified) and found none. |
| 2 NIT playlist delete version bump untested | **Fixed** | `GroupPagesTests.Deleting_a_playlist_moves_the_version_of_groups_that_used_it` |

## Notes for the product owner
- WP-05 is ready to merge. The round-2 regression is fixed properly at the root, not patched page by page, and blank names now show "Enter a name." again. The one remaining item is an optional nullability tidy-up.
- Because of the global change, a future page model that wants a required string must add an explicit `[Required]`. The comment in `AdminUi.cs` says so, and all existing auth/user/settings forms already do it.
