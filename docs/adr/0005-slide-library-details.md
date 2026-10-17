# ADR-0005: Slide library details

- Status: Accepted
- Date: 2026-10-17
- Work package: WP-03
- Requirements: R-01, R-02, R-03, R-05, R-06, R-07

## Context
The brief says schedule times are "entered in the system time zone and stored as UTC". It doesn't say what
happens around daylight-saving changes, whether a slide's type can change, how previews in the admin are
made safe, or how large an HTML slide may be.

## Options considered
1. **DST gap** (e.g. 02:30 on the spring-forward night doesn't exist): silently shift to 03:30 vs.
   **reject with a clear message**.
2. **DST overlap** (02:30 happens twice in autumn): ask the user vs. **pick one documented occurrence**.
3. **Changing a slide's type after creation**: allowed (forms and validation must handle every switch, and a
   Video→Image switch silently drops fields) vs. **fixed at creation** (create a new slide instead).
4. **Previews in the admin**: none vs. **the same rendering as the player, shrunk**.

## Decision
- A time in the spring-forward gap is rejected ("that time doesn't exist in this time zone"). An ambiguous
  autumn time means its **standard-time** occurrence, the later instant (.NET's `ConvertTimeToUtc`
  behaviour). Both are rare at the hours people schedule content.
- Schedule windows stay half-open `[from, until)` (ADR-0003). "Until" must be after "from".
- A slide's type is fixed at creation. Create pages are per type (`/admin/slides/create?type=Video`) and
  show only the fields that apply. Unknown type names are a 404.
- Per type, the validator keeps only what applies: Html slides drop any URL, URL slides drop any HTML,
  videos drop the duration (they play to the end, R-02), and play-audio sticks only on videos (R-07).
- URLs must be absolute `http`/`https` with a host. A video URL that doesn't end in `.mp4` saves with a
  warning only, because CDNs vary (ARCHITECTURE §3).
- HTML is capped at 200,000 characters. That is generous for hand-written slides, and it keeps the manifest small.
- Admin previews use the player's attributes: Html in `<iframe sandbox="allow-scripts" srcdoc>` (never
  `allow-same-origin`, R-05). Web pages use `<iframe src sandbox="allow-scripts allow-same-origin allow-forms"
  referrerpolicy="no-referrer">`, where the site keeps its own origin and can't reach the admin. Previews are
  rendered at 1920×1080 and scaled with CSS, so they look like a real screen. They load lazily and ignore
  pointer events.
- Enable/disable is a one-click toggle in the list. It's a bulk update and sets `UpdatedAt` explicitly.
- Search is a case-insensitive `ILIKE` on the title, with `%`/`_` escaped.

## Consequences
- Editors who need a different type re-create the slide. Playlists then need the new slide; WP-04 can
  make that easy.
- The player (WP-08) mirrors the sandbox values exactly. A test there should compare them.
- Previews of web pages hit the external sites from the admin's browser, which is the same thing the screens do.
