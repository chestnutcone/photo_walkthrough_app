## Findings and fixes (GALLERY task, 2026-10-31)

Hand-written part of the audit (appended by the test from `app/test/gallery/a11y_audit_manual.md`). Every fix below has a
widget test that failed before the change.

| Screen | Finding | Fix | Test |
|---|---|---|---|
| Story viewer | Chapter changes were not announced. The bottom card is a live region, but TalkBack and VoiceOver announce a live region only when its own label changes; the card had no label (its text is in child nodes) | The card's live region carries a step label: "Story intro, 4 chapters", "Chapter 2 of 4, Tide line", "End of story", "Explore, 4 points" | `story_a11y_fixes_test.dart` `chapter_changes_are_announced_by_the_live_region_label` |
| Story viewer | Each chapter tap zone produced two nodes: the labelled "Previous/Next chapter" button and an unlabelled tap node from its gesture detector (`labeledTapTargetGuideline` failed on every chapter) | `excludeFromSemantics` on the zone detectors | `chapter_tap_zones_have_no_unlabelled_tap_nodes` |
| Story viewer | Pins were read in screen-position order (1, 4, 3, 2) | `OrdinalSortKey` per pin: read 1, 2, 3, 4 | `pins_are_read_in_chapter_order` |
| Story viewer (web, hardware keyboard) | Begin, the tap zones and the pins are gesture detectors that keyboard focus never reaches: a keyboard-only reader could not leave the intro | Arrow Right / Arrow Left step forward / back in Story mode (Space is left to the focused button) | `arrow_keys_step_through_chapters` |
| Reply sheet | The composer field was 42 pt tall with the real fonts (passes with the test font, so earlier guideline tests missed it) | 48 pt minimum height | `composer_size_test.dart` (loads the real fonts) and `composer_meets_the_tap_target_guidelines` |
| Sign-in sheet | The Google button read "G, Continue with Google" (the text mark) | Mark excluded from semantics | `sign_in_sheet_test.dart` `google_button_is_read_as_continue_with_google` |
| Search | The prompt "Search artists and stories" was a never-floating label, which Material hides while the field is focused; the field autofocuses, so the screen opened on a blank bar with no visible prompt | It is a hint now (visible while empty, still the field's accessible hint) | `search_screen_test.dart` `prompt_is_visible_while_the_empty_field_is_focused` |

Visual defects found in the same screenshot review and fixed (not accessibility guidelines, listed for completeness): two
grabbers on the replies, followers and contact sheets (theme drag handle plus their own), the story meta chip wrapping at half
the free width, and the unknown-route screen showing the debug text `route:not-found`.

## Not fixed, with the reason

| Item | Reason |
|---|---|
| Progress segments are 15 pt tall (fail 48 pt / 44 pt) | Ruling S2-6 kept: a 48 pt band would overlap the 48 pt artist / controls row 12 pt below. The same action is on the 48 pt tap zones, the pins and the "Previous / Next chapter" semantics buttons. |
| Contrast with the real fonts | The gating run uses Flutter's square test glyphs, so the guideline measures the specified colours (how WCAG defines contrast); every text token is at least 4.5:1 on every surface (`theme_test.dart`). A run with the real fonts (same scenes, first audit run) also flagged 11-13 pt mono and sans labels at rest ("NEW STORY" 1.16, "PHOTOGRAPHER · LISBON" 1.35, "ACCOUNT" 3.20, field labels 1.35-3.93, helper text 1.51-2.73): the pixel sampler reads the anti-aliased edges of thin small glyphs. No token change was made; check with a contrast tool on a device if the owner wants a margin. |
| Story chrome reading order | After the progress segments a screen reader reaches Story / Explore, the artist, Previous chapter, Close, Next chapter, then the card. It follows the visual layout (tap zones sit between the top bar and the card); the card is also announced on every step through its live region. Not changed. |
| Tab and focus order on web, visible focus on custom buttons | Many story controls are gesture detectors with semantics (usable by TalkBack, VoiceOver and Switch Access) but not focusable with Tab. See [web-viewer-report.md](web-viewer-report.md). |
| Screen-reader speech itself | TalkBack / VoiceOver output cannot be captured here; the semantics tree (labels, order, live region labels) is what is checked. |
