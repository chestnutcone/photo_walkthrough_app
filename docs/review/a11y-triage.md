# Accessibility audit triage (S2)

> Update 2026-10-31 (GALLERY): the on-device suite was re-run (15 findings, see [e2e-report.md](e2e-report.md)) and a
> widget-level audit over every screen now exists: [a11y-audit.md](a11y-audit.md).

Triage of every line in [`a11y-violations.txt`](a11y-violations.txt) (E1 on-device run of Flutter's tap-target,
labelled-target and text-contrast guidelines on 19 screens). Fixed items are covered by widget tests; the on-device suite
(`app/integration_test/g_a11y_test.dart`) has not been re-run in S2 (no emulator session), so re-run it with `scripts/run-e2e.sh`
to regenerate the file.

## What changed

- One token, `kMinTapTarget = 48` (`app/lib/core/theme/tokens.dart`), replaces every 44 pt minimum (back buttons, follow pills,
  artist rows, search and bell icons, "Followed by", list rows, form buttons, pins in the viewer and the pin editor).
- `VelaColors.faint` is `0xFF8D877E` (4.67:1 on `input`, was 4.49). `theme_test.dart` now fails if any text token drops below
  4.5:1 on any surface (`tools/contrast_check.py` agrees).
- Feed header: the wordmark scales down when space is short, never the tabs (the old `FittedBox` shrank them to 36 pt).
- "Walk through" keeps its 37 pt pill inside a 48 pt target.
- Story top bar: artist button, Story / Explore items, More and Close are 48 pt (visuals unchanged: the pill is still drawn
  36 pt). When the controls leave the artist less than 120 pt (large font scales, 320 pt screens) the artist moves to its own
  line, and the two pill labels stop growing at 1.3x.
- Phones are locked to portrait (Android manifest, iPhone `Info.plist`); iPad keeps all orientations. Android 16+ ignores the
  lock on large screens (tablets, foldables), so those can still rotate.
- System Back from a story opened by deep link goes to the feed.

Tests: `test/features/feed/feed_a11y_test.dart` and `test/features/a11y_tap_targets_test.dart` (guideline checks on feed guest and
signed in, search, portfolio), `test/features/story/story_a11y_fixes_test.dart` (48 pt story chrome, artist visible at
320 pt / 2.0, Back to feed), `test/core/theme_test.dart` (contrast), `test/security/platform_config_test.dart` (portrait).

## Line by line

| Line | Screen, check | Finding | Disposition |
|---|---|---|---|
| 1 | guest-feed, tap target | artist row 44, Follow 44, Walk through 37, tabs 44, Search 44 | Fixed (48 pt; feed guideline test) |
| 2 | guest-feed, contrast | "Discover" 2.05; subline 4.04 | "Discover" measured `#484644`, not a token colour: captured mid-transition (the tab fades in). Subline: see note A |
| 3 | sign-in sheet, contrast | account note 2.27 (`#53504B`) | Captured while the sheet animated in (not a token colour); text is `muted` (6.78:1 on `sheet`). Not changed |
| 4 | story-intro, tap target | pins 44, segments 15 tall, artist 32, Story/Explore 30, More/Close 44 | Fixed except the progress segments: see note B |
| 5 | story-intro, contrast | "Tap the photo to walk through" 1.43 | Text over the photograph during the intro fade; the guideline samples photo pixels. Not changed (note C) |
| 6 | story-chapter, tap target | as line 4 | Fixed except segments (note B) |
| 7 | story-chapter, labelled target | two unlabelled tap nodes | Fixed in E1 (Previous / Next chapter semantics) |
| 8 | story-chapter, contrast | "CHAPTER 01 / 03" 2.52 | Accent kicker over the photograph scrim, mid-animation (note C) |
| 9 | search, tap target | Back 44, Follow 44 | Fixed (search guideline test) |
| 10 | search, contrast | "SUGGESTED ARTISTS" 4.04 | Note A |
| 11 | signed-in feed, tap target | artist 44, Walk through 37, scaled tabs 36, Search/bell 44 | Fixed (feed guideline test, signed in) |
| 12 | signed-in feed, contrast | scaled "Discover" 2.53; subline 4.04 | Scaling removed; subline note A |
| 13 | notifications, tap target | Back 44 | Fixed (48 pt) |
| 14 | settings, contrast | headers 1.77 / 2.50, notes 1.87, "Signed in as" 3.90 | Colours like `#4A3A25` are the accent and muted tokens part-way through the page transition; at rest the tokens are 4.6:1 or better. Not changed (note C) |
| 15 | edit-profile, contrast | helper 1.11, "WEBSITE" 2.50 | Mid-transition capture (helper measured almost the background colour). Not changed |
| 16 | portfolio-own, tap target | "Followed by" 44 | Fixed (portfolio guideline test) |
| 17 | portfolio-own, contrast | "Followed by" 1.12 | Mid-transition capture. Not changed |
| 18 | create-pick, contrast | "NEW STORY" 4.04 | Note A |
| 19 | create-photo, contrast | "NEW STORY" 1.16 | Over the picked photo while it fades in. Not changed |
| 20 | onboarding-welcome, contrast | account note 3.93 | Note A (12 pt muted text, measured from pixels) |
| 21 | onboarding-profile, contrast | label and note 1.17 to 2.86 | Mid-transition capture. Not changed |

**Note A (thin small text).** WCAG contrast is defined on the specified colours; every text token passes 4.5:1 on every surface
(`theme_test.dart`). Flutter's pixel-sampling guideline reads the anti-aliased edges of 11-12 pt mono and sans glyphs and so reports
about 4.0 for `faint`/`muted` text that is 5.2:1 or more by its colours. Raising those labels to `secondary` would change the
handoff's hierarchy for a measurement artefact; not changed. Re-check on device after the `faint` change.

**Note B (progress segments, ruling).** The 3 pt chapter segments keep their 15 pt tap band. A 48 pt band would overlap the
48 pt artist / controls row directly below it (the handoff puts them 12 pt apart). The same action is available at 48 pt or more
through the tap zones on the photo, the chapter pins and the "Previous chapter" / "Next chapter" semantics buttons, which
TalkBack and Switch Access use.

**Note C (text over photographs and transitions).** The guideline samples whatever is behind the text in the screenshot, so
it fails text over a photo or during a fade. The story chrome keeps its scrims; a per-screen on-device pass after transitions
settle is the follow-up (re-run `g_a11y_test.dart` with a settle delay).
