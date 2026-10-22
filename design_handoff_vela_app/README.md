# Handoff: Vela — annotated photo stories + artist portfolios

## Overview
Vela is a mobile app for viewing photographs (and other visual art) with the story behind them. Artists drop numbered pins ("chapters") on details in a photo and write a note for each. Viewers either **walk through** the pins in order (Story mode — the camera zooms/pans to each detail) or **explore** them freely (Explore mode). Each detail has its own reply thread. Artists have portfolio pages with works, followers and contact/commission info.

## About the Design Files
`Vela App.dc.html` is a **design reference built in HTML** — an interactive prototype showing intended look and behavior, not production code. Recreate it in the target environment (recommended if none exists: React Native/Expo or SwiftUI) using that platform's patterns. Open the file in a browser to try it (it needs `support.js` beside it). All logic lives in the `class Component` script at the bottom of the file — it's the clearest spec of state and behavior; the markup above it holds exact styles.

## Fidelity
**High-fidelity** for layout, color, type, spacing and interactions. Exceptions:
- Photos are **CSS-gradient placeholders** — replace with real images (object-fit: cover).
- Status bar, phone bezel and the right-hand "Try" notes column are presentation only.
- Artists, works, replies, prices and contact details are sample data.

Design frame: **390 × 844 pt** (iPhone 14/15 class). Safe area top ≈ 50pt; bottom ≈ 34pt.

## Screens / Views

### 1. Feed
- Scroll view, padding top 54, bottom 96.
- Header row (padding 8/20/4): wordmark "Vela" — Instrument Serif 40/1, letter-spacing −0.5. Right: tabs "Following" / "Discover", Geist 500 13; active = `#F2EEE6` + 1px underline, inactive `#8A847B`.
- Subline: "PHOTOGRAPHS, WITH THE STORY LEFT IN" — Geist Mono 11, `#8A847B`, tracking .4, padding 0 20 18.
- Post cards (gap 36, side padding 16), each a column with gap 12:
  - Artist row: 34pt avatar circle (artist color, serif initial 18 in `#0F0E0D`), name Geist 500 14, "{city} · {date}" Geist Mono 11 `#8A847B`. Right: Follow pill — 1px `rgba(242,238,230,.18)` border, radius 99, padding 6×12, Geist 500 12, label "Follow"/"Following".
  - Photo 358 × 448, radius 14. Pin preview dots: 10pt accent circle + 5pt accent ring at 25% alpha, at each chapter's x/y.
  - Footer: title Instrument Serif 28/1.05; meta "{n} CHAPTERS · {n} REPLIES" Geist Mono 11 `#A39D93`. Right: "Walk through" pill — bg `#F2EEE6`, text `#0F0E0D`, Geist 600 13, padding 10×16.
- Following tab: followed artists' work first. Discover: reverse order (placeholder ranking).

### 2. Story viewer (core feature)
Full-screen, black bg. The photo layer is 390×844 and transformed (origin 0 0).
- **Zoom/pan math** for chapter at (x, y) ∈ [0,1], zoom z:
  `s = z * zoomMultiplier; tx = clamp(W/2 − s·x·W, W − s·W, 0); ty = clamp(0.32·H − s·y·H, H − s·H, 0)`; transform = `translate(tx,ty) scale(s)`. Transition 900ms `cubic-bezier(.2,.7,.1,1)`.
- **Spotlight ring**: 132pt circle, 1.5px accent border, `box-shadow: 0 0 0 1600px rgba(8,7,6,.5)` (dims everything else), positioned at screen point `(tx + s·x·W, ty + s·y·H)`; left/top animate with the same duration/easing. Hidden on intro/end.
- **Pins** (visible on intro and in Explore; hidden during chapters): 44pt hit area, counter-scaled by 1/s so they stay constant size. Style "numbered": 28pt circle, `rgba(15,14,13,.7)` + blur, 1.5px accent border, number Geist Mono 500 12. Alt style "glow": 12pt accent dot + rings at 30% / 12% alpha.
- Top gradient 170pt `rgba(0,0,0,.55)→transparent`.
- **Top bar** (left/right 12, top 52, gap 12): progress segments (Story mode only) — one per chapter, 3pt, radius 2, track `rgba(255,255,255,.25)`, fill `#F2EEE6`, width transition .4s; tapping a segment jumps to it. Row: avatar 32 + name (Geist 500 13) + work title (Geist 11, 75% ink) → taps to portfolio. Segmented pill "Story | Explore" (bg `rgba(15,14,13,.6)` blur, active item filled `#F2EEE6`/`#0F0E0D`, Geist 500 12, padding 7×12). Close button 36pt circle "×".
- **Tap zones** (chapters only): left 30% = previous, right 70% = next; y 110→510.
- **Bottom card**: left/right 10, bottom 24, bg `rgba(18,17,15,.86)` + blur 18, 1px `rgba(242,238,230,.1)` border, radius 24, padding 20/20/16, gap 10. Variants:
  - *Intro*: kicker "{DATE} · {N} CHAPTERS" (Geist Mono 11, accent, tracking .5); title Instrument Serif 34/1; intro Geist 15/1.5 `#D6D1C8`; "Tap the photo to walk through" + "Begin →" pill.
  - *Chapter*: kicker "CHAPTER 02 / 04"; title serif 32/1.02; body Geist 15/1.5; meta chip (Geist Mono 11, 1px `rgba(242,238,230,.14)` border, radius 6, padding 5×8, e.g. "85mm · f/4 · 1/500"); replies pill right ("2 replies" / "Reply", bg `rgba(242,238,230,.08)`). In Explore, a "Zoom out" text button sits top-right.
  - *End*: kicker "END OF STORY", headline "Keep looking, or move on." 2-col grid of 12-radius buttons: Explore freely, Replay, Follow {first name}, **Next story →** (filled), then full-width "Ask about a print of this work" (opens the portfolio contact sheet pre-filled with this work and type "Buy a print").
  - *Explore idle*: "EXPLORE" + "Tap any marked point in any order. {N} notes left by {first name}."
- **Optional autoplay**: advances a chapter every 6s in Story mode (paused while replies are open).

### 3. Replies sheet (per detail)
Scrim `rgba(0,0,0,.4)`; sheet 520pt high, bg `#161513`, radius 26 top, grabber 36×4. Header "REPLIES ON" (mono 11) + chapter title serif 24, "Done". List of replies (28pt avatar, name Geist 500 13, text Geist 14/1.45 `#D6D1C8`), empty state "No replies on this detail yet." Composer: pill input (bg `#201E1B`, placeholder "Reply to this detail…") + "Send"; Enter submits. Replies are keyed by `workId-chapterIndex`.

### 4. Artist portfolio
- Cover band 236pt using the artist's first work, overlay `linear-gradient(rgba(15,14,13,.45), transparent 45%, #0F0E0D)`. Floating blurred pills: "← Feed", "Share" (→ "Link copied"). Caption "COVER · {TITLE}" mono 10.
- Identity block (margin-top −46, padding 0 20 22, gap 14): avatar 88pt with 4pt `#0F0E0D` border; availability badge (accent 14% bg, accent text, 7pt dot) e.g. "Open for commissions"; name Instrument Serif 46/.95; "{DISCIPLINE} · {CITY}" mono 11; bio Geist 15/1.5.
- Stats: 4-column grid between hairlines — Stories, Chapters, **Followers ›** (tappable → followers sheet), Following. Numbers serif 24, labels Geist 11 `#8A847B`.
- "Followed by Teo, Mara and 2.4k others" with 3 overlapping 24pt avatars (−8 overlap) → followers sheet.
- Actions: **Follow** (filled when not following, outlined when following) + **Contact** (outlined). Radius 12, padding 13. Own profile shows "Edit profile" + "New story" instead.
- Sticky tabs: "Works · {n}" / "About & contact" (Geist 500 14, 1.5px underline when active).
- **Works tab**: "FEATURED STORY" (accent mono) → 358×300 photo with pin dots, title serif 26, meta, "Walk through". Then "ALL WORKS" 2-col grid (gap 8, aspect 4:5, radius 10, bottom gradient caption: serif 18 title + mono 10 "{n} CHAPTERS"). Empty state for own profile: dashed box "No stories yet. Tap + to annotate your first photograph."
- **About & contact tab** (gap 26): 
  - AVAILABLE FOR — rows: service (Geist 500 15), description (Geist 13 `#A39D93`), price right (Geist Mono 12).
  - CONTACT — rows label (72pt col, Geist 12 `#8A847B`) / value (Geist 500 14) / action: Email → "Copy" (copies, shows "Copied" in accent), Website → "Open ↗", Social → "Copy", Studio, Message → "Write →" (opens contact sheet). Note below e.g. "Usually replies within 2 days."
  - EXHIBITIONS & PRESS — year (mono 12) + line.
  - Full-width filled "Send {first} an inquiry".
- **Followers sheet**: title "{count} followers"; rows 40pt avatar, name, discipline, Follow/Following pill. Tapping an artist row opens their portfolio.
- **Contact sheet**: "WHAT IS THIS ABOUT" chips — Commission, Buy a print, Licensing, Collaboration, Just saying hi (selected = filled). Optional referenced-work chip ("About the work" + thumbnail). Email input, message textarea (placeholder changes by type), reply-time note, "Send inquiry" — **disabled until email contains "@" and message is non-empty**. Success state: accent ✓ circle, "Sent to {first}.", summary ("Your commission inquiry is in Ines's inbox. Usually replies within 2 days. Replies go to {email}."), "Back to portfolio".

### 5. Create story (authoring)
- Header: Cancel · step label ("NEW STORY" / "{n} CHAPTERS") · Publish pill (disabled style until ≥1 chapter).
- Pick: headline "Choose a photograph" serif 36 + helper; 3-col camera-roll grid, 3pt gaps, square.
- Annotate: photo 390×420, crosshair; tap anywhere → ghost pin (accent filled, 8pt halo) at tapped fraction (x, y). Below: title input (serif 28, underline), then draft form: "CHAPTER {n}", "What is this detail?" input, story textarea, Discard / **Add chapter**. Added chapters list (number in accent, title, body, × to remove) and appear as numbered pins on the photo. Empty hint: "Tap anything in the photograph to start a chapter. Chapters play in the order you add them."
- Publish → creates a work for the current user (default title "Untitled study", default zoom 2.4 per chapter), adds it to their portfolio and opens it in the Story viewer.

### 6. Tab bar (Feed, Portfolio screens)
84pt, gradient `rgba(15,14,13,0)→rgba(15,14,13,.96) 30%`. "Feed" · 48pt "+" circle (bg `#F2EEE6`) · "Portfolio" (own profile). Active label `#F2EEE6`, inactive `#8A847B`.

## Interactions & Behavior (summary)
- Feed card photo / Walk through → Story viewer (intro). Artist row → portfolio.
- Story: Begin / right tap → next; left tap → prev; segment tap → jump; pin tap on intro → jump to chapter. Story/Explore toggle resets to intro / unzoomed. Close → feed (or own portfolio for your own work).
- Explore: pin tap zooms to it; tap background or "Zoom out" → reset.
- Follow toggles are global (feed, story end card, portfolio, followers list).
- All camera moves: 900ms `cubic-bezier(.2,.7,.1,1)` (prototype exposes 300–1600ms as a tweak). Progress fill .4s; pin fade .4s.
- Use haptics on chapter change and pin drop in the native build.

## State Management
```
screen: 'feed'|'story'|'profile'|'create'
feedTab: 'following'|'discover'
story: { workId, mode:'story'|'explore', step (0=intro, 1..n chapters, n+1=end), exploreSel (-1|index), threadOpen }
replies: Record<`${workId}-${chapterIdx}`, {author, text}[]>
following: Record<artistId, boolean>
profile: { artistId, tab:'works'|'about', sheet: null|'followers'|'contact', copiedKey, shared }
inquiry: { type, email, message, sent, refWork? }
create: { phase:'pick'|'annotate', image, title, pins:[{x,y,z,t,b,m}], ghost:{x,y}|null, draftTitle, draftBody }
createdWorks: Work[]
```
Data models:
```
Artist { id, name, city, discipline, bio, followers, followingCount, color, availability, email, website, handle, studio, replyTime, services:[{title,desc,price}], shows:[{year,text}] }
Work   { id, artistId, title, date, image, intro, chapters:[{x,y,zoom,title,body,meta}] }
```
Backend needs: feed (following/discover), works + chapters, per-chapter reply threads, follow graph + follower lists, inquiries (stored and emailed/sent to the artist), image upload, publish.

## Design Tokens
Colors
- Background `#0F0E0D` · page/backdrop `#191817` · sheet `#161513` · input `#1F1D1A` / `#201E1B` / `#1A1816`
- Ink `#F2EEE6` · secondary `#D6D1C8` · muted `#A39D93` · faint `#8A847B`
- Hairlines `rgba(242,238,230,.08)`; outlines `.1 / .12 / .14 / .18 / .2 / .25`
- Accent `oklch(0.8 0.12 70)` ≈ `#E5AE63` (warm amber); accent text on tint `oklch(0.85 0.1 70)`
- Scrims `rgba(0,0,0,.4–.5)`; spotlight `rgba(8,7,6,.5)`
- Sample artist colors: Ines `#D39A74`, Teo `#8FB8B0`, Mara `#D8C08A`, You `#C9C3B8`

Typography (Google Fonts)
- Display: **Instrument Serif** 400 — 46 / 44 / 40 / 36 / 34 / 32 / 30 / 28 / 26 / 24 / 18
- UI: **Geist** 400/500/600 — 15 body (lh 1.5), 14, 13, 12, 11
- Labels/meta: **Geist Mono** 400/500 — 12, 11 (uppercase, tracking .4–.5), 10

Radii: 99 (pills), 26 (sheets), 24 (story card), 14 (feed photos), 12 (buttons), 10 (inputs, grid tiles), 6 (meta chip), 50% (avatars/pins)
Spacing: 4 / 8 / 10 / 12 / 14 / 16 / 20 / 22 / 26 / 36
Shadows: phone only; UI uses blur (`backdrop-filter: blur(10–18px)`) instead of shadows.

## Assets
No image assets. All photos are gradient placeholders; avatars are colored circles with serif initials. Icons are text glyphs (×, ←, ›, ↗, +, ✓) — replace with the platform icon set.

## Files
- `Vela App.dc.html` — the full interactive prototype (markup + logic class). Open in a browser.
- `support.js` — runtime required to open the prototype; not part of the app.

## Not yet designed
Direct messaging/inbox, edit profile, notifications, search, onboarding, real image upload/crop, moderation of replies.
