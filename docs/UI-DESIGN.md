# UI & Game Design Principles

Source of truth for how Fallcall's UI (and, where noted, the game as a whole) is designed.
Read this before building or changing any menu, HUD element, or interaction flow.
osu!lazer is the primary reference — it is a best-in-class example of rhythm-game UI —
but principles here are stated generally so they survive visual redesigns.

---

## 0. Form vs. function — how we use osu! as a reference

Fallcall is **inspired by** osu!lazer's UI; it must not be a **clone** of it. There is a hard
line between the two, and every reference to osu! in this document sits on one side of it:

**Function — adopt freely.** Interaction *behaviors* and *principles* are not osu!'s to own;
most are broader industry conventions that lazer simply executes well. Adopt them:
- settings openable from anywhere via a global shortcut; live-apply; per-setting reset,
- search-as-you-type; filters; card/carousel browsing with hover preview,
- keyboard-first navigation; persistent toolbar; visible focus/hover states,
- HUD visibility modes; background dim; visual-settings quick panel.

**Form — do not copy.** The *look and brand* are protected trade dress and, just as important,
they belong to a different game's identity:
- **no** osu! branding, logo, the triangle motif, or the signature pink/`#FF66AA` palette,
- **no** reproduction of lazer's specific layouts, card art, iconography, or proportions,
- **no** osu! sound/menu-music assets.

**Why the split matters here, twice over:**
1. *Legal/ethical.* Behaviors and ideas aren't copyrightable; a specific visual design and
   brand identity are. Copying the former is normal craft; copying the latter is a clone.
2. *Design.* Fallcall's identity is **falling through fast geometric 3D space**. Lazer's flat,
   calm 2D aesthetic would actively fight that. Cloning its look would be both derivative *and*
   off-theme — a double loss.

**The rule for this whole document:** when osu! is cited, it is citing a **behavior that
works**, never **a screen to reproduce**. Fallcall builds its **own** visual language — palette,
typography, shape, motion — designed to express the geometric-fall theme (see §1.6 consistency
and the U1 theme-foundation block in `PLAN.md`). Form is ours; proven function is fair game.

**Reference screenshots** live in `docs/examples/` (`startup_menu`, `local_beatmap_selection_1/2`,
`beatmap_search_menu`, `settings_opened`). Read them as **behavior/layout-logic references** —
what's on screen, how it's grouped, what the flow is — **not** as skins to match pixel-for-pixel.

---

## 1. Core principles

Ordered roughly by priority. When two principles conflict, the earlier one wins.

### 1.1 Gameplay first

Nothing may degrade the player's ability to read and hit the map.

- Effects, HUD, and overlays must never obstruct visibility of hit objects, create
  clutter, or cover the active play area.
- **No rogue camera movement**: camera changes are minimized, predictable, and never
  triggered by UI. Zoom/transition rules from `STRUCTURE.md` §4 apply (zoom only between
  clicks, cursor drift minimized, cursor moves only on transitions).
- **No unclear or rapid changes**: UI state never flickers, jumps, or animates fast enough
  to pull attention mid-combo. Animations near the playfield are slow, small, or absent.
- **Never lock the player out of control.** No modal that steals input during gameplay;
  pause is the only interruption and it is always player-initiated (or map-end).
- HUD elements support visibility modes (always / hide during gameplay / never) — lazer's
  `HUDVisibilityMode` pattern. Background dim is a first-class setting.
- Corollary for effects/gamefeel: feedback on hits (sound, animation, cursor reaction)
  should be **fast and decaying** — strong at the moment of the hit, gone quickly, so the
  screen returns to a readable baseline (fast disappearance of clicked objects).

### 1.2 Contrast (affordance)

The player should always be able to tell what is clickable, at a glance, without trying.

- Interactive elements **stand out from the background**: brightness/saturation contrast,
  not just hue (colorblind-safe).
- **Hover highlights** on everything interactive: color shift, glow, or scale — plus a
  cursor reaction. If it doesn't react to hover, it must not be clickable.
- Positioning is part of contrast: interactive elements sit in predictable zones (edges,
  panels), never floating ambiguously over content.
- Disabled elements are visibly dimmed, not hidden — the player learns the layout once.
- Text over backgrounds (beatmap cards, main screen) always has a dim/scrim layer behind
  it; readability is never left to luck of the artwork.

### 1.3 Clarity / certainty (single function)

- Every element has **exactly one function**. No overloaded buttons, no
  click-vs-long-press ambiguity, no "this icon does different things in different screens".
- UI is limited to what the player actually uses. Cut speculative options; a smaller,
  certain surface beats a large vague one.
- Every action gives immediate feedback (visual + optionally audio) so the player is
  certain it registered. Destructive actions (delete beatmap, reset all) confirm once —
  everything else acts instantly.
- Labels state what happens, not what the feature is called internally.

### 1.4 Navigation (persistence + shortcuts)

Everything reachable, from anywhere, by a route the player can memorize.

- **Persistent placement**: each feature lives in one place with one icon, forever. No
  moving things between screens.
- Important menus are reachable three ways, all equivalent:
  1. **Navigation panel/toolbar** (persistent strip, lazer-style, toggleable),
  2. **Keyboard shortcut** (global — e.g. lazer's `Ctrl+O` opens settings from anywhere),
  3. **From the main screen**, which lists these shortcuts as small hint notes under each
     menu entry — the main screen doubles as the tutorial for the shortcut system.
- Back always works and always goes where the player expects (Esc = back/pause,
  consistently, everywhere).
- Overlays (settings, chat-like panels) slide over the current screen instead of replacing
  it — the player never loses their place (lazer pattern: settings is an overlay, not a
  scene).

### 1.5 Accessibility (keyboard-first)

osu! is often played with tablet + keyboard and no mouse. Full keyboard-only operation is
a requirement, not a nice-to-have.

- Every menu navigable with keyboard only: arrows/tab to move focus, Enter to confirm,
  Esc to back out. Focus state is visually obvious (same contrast rules as hover).
- Beatmap switching without mouse: arrows move through the list, typing filters
  (search-as-you-type without needing to click a search box first — lazer does this at
  song select).
- All shortcuts rebindable (see Settings → keybinds below).
- Respect general accessibility basics: UI scale setting, colorblind-safe palettes,
  no information encoded by color alone, readable minimum font sizes.

### 1.6 Consistency & hierarchy (from general practice)

- One visual language: shared spacing, corner radii, fonts, animation curves across all
  screens. A component built once (slider, toggle, button) is reused everywhere.
- Information hierarchy by priority: the thing the player came for is biggest and first;
  metadata and secondary actions are visually subordinate.
- Transitions communicate structure: overlays slide from the edge they're anchored to,
  screens crossfade; the same motion always means the same thing.

---

## 2. Menus

### 2.1 Settings

- **Always accessible**: global shortcut opens settings from any screen, including pause
  during gameplay. Implemented as an overlay panel (slide-in), not a separate scene.
- **Live-apply**: anything that *can* change live *does* change live — no Apply button,
  no restart prompts unless technically unavoidable (and then the setting says so inline).
  This matches the existing testing-first design (`GameSettings` edits live).
- **Persistent**: all settings persist (`GameSettings` / `PlayerPrefs` today).
- **Structure**: sections in a sidebar (Gameplay, Visuals/Camera, Audio, Skin, Input,
  UI) + **search bar with text highlight** — typed query filters settings and highlights
  matched text (lazer's settings search is the model).
- **Controls**: proper range sliders with the real min/max, current value shown
  numerically, **keyboard adjustment** (arrows = small step, modifiers = large step),
  and direct text entry for exact values.
- **Per-setting reset**: every individual setting has its own reset-to-default button
  (small icon next to the control), plus per-section reset. No all-or-nothing reset.
- **Keybinds section**: rebind gameplay keys *and* UI-navigation shortcuts. Detect
  conflicts, show them, never silently double-bind.
- Scene-tuning values (`Osu3DSettings`) stay inspector-side; anything a *player* would
  touch migrates to `GameSettings` and appears here.

### 2.2 Beatmap selection (song select)

- Lists **all imported beatmaps**, grouped by set, expandable into difficulties
  (carousel/card layout; lazer's card-based carousel is the model).
- **Preview images/video**: card shows the beatmap background; selected card may play
  video preview.
- **Audio preview**: on selection, play the song from its preview point. osu! files
  define this — `PreviewTime` (ms) in the `[General]` section of the `.osu` file; when
  it's missing/`-1`, fall back to a heuristic (e.g. 40% into the song).
- **Search bar + filters**: search-as-you-type across title/artist/creator/tags;
  filters for difficulty (star range), length, BPM. Sort by title/artist/difficulty/
  recently played.
- Keyboard-only flow: type to search, arrows to move, Enter to play, Esc to back out.
- Shows per-difficulty metadata at a glance: star rating, CS/AR/OD/HP, length, BPM.
- Replaces the current minimal difficulty picker in `Bootstrap`.

### 2.3 Beatmap search (online)

- Goal: browse/download beatmaps in-client, ideally without leaving the game.
- **Without account**: the public osu! website search can be scraped/queried and `.osz`
  downloads for many maps work anonymously via mirrors (e.g. catboy.best / beatconnect /
  nerinyan APIs) — this is the pragmatic v1: mirror API search + download + auto-import
  through the existing `.osz` pipeline (`ArchiveExtractor`).
- **With account**: official osu! API v2 (OAuth) gives proper search, metadata, and
  download; can be added later as an optional login.
- Same UI shell as local song select (same cards, same filters) with a Local/Online
  toggle — one learned interface, two sources.

### 2.4 Main screen

- Minimal: logo/title, then entries — **Play**, **Settings**, **Browse (online)**,
  **Quit**. Each entry shows its keyboard shortcut as a small note beneath it (per §1.4).
- Doubles as the ambient state: plays menu music, background shows a beatmap backdrop
  (dimmed per §1.2 text rules).
- First-run: if no beatmaps imported, main screen says so and points to Browse/import
  instead of a dead Play button (certainty over surprise).

### 2.5 Pause / results (gameplay-adjacent)

- Pause: player-initiated only; offers Resume / Retry / Settings / Quit-to-select. Live
  settings edits from pause apply immediately on resume (existing behavior — keep).
- A lazer pattern worth copying: **visual settings quick-panel** available while the map
  loads or is paused (background dim, HUD visibility) — the two knobs players most often
  want mid-session, without opening full settings.
- Results screen: hierarchy = grade/score first, then accuracy/combo, then details.
  Retry and back are one key each.

---

## 3. Fallcall-specific constraints

Where 3D makes this game's UI different from lazer's:

- **UI is screen-space, gameplay is world-space.** Menus and HUD never live on the sphere
  with hit objects — no world math in UI, and no UI elements that move with the camera.
  (Same seam philosophy as `Playfield.ToWorld`: projection stays out of UI code.)
- **HUD placement respects the 3D playfield**: hit objects can appear anywhere the camera
  looks, so HUD hugs screen edges/corners and stays out of the center. Center of screen is
  sacred.
- **Mode switches (sphere/2D/falling) are gameplay events, not UI events**: any UI shown
  during a transition follows the no-rapid-change rule (§1.1) — fade, don't pop.
- Every new UI tunable (dim level, HUD scale, visibility mode) goes into the settings
  surfaces (`GameSettings` / `Osu3DSettings`), never hardcoded — existing convention.

---

## 4. osu!lazer patterns adopted (quick reference)

These are **behaviors**, adopted per §0 — the visual execution of each is Fallcall's own.


| Pattern | Where it applies here |
|---|---|
| Settings as slide-over overlay, openable anywhere (`Ctrl+O`) | §2.1 |
| Settings search with match highlight, live-apply everything | §2.1 |
| Toggleable persistent toolbar (`Ctrl+T`) | §1.4 |
| Card-based beatmap carousel, hover/select previews | §2.2 |
| Type-to-search at song select without focusing a field | §2.2, §1.5 |
| `HUDVisibilityMode` (always/hide-during-gameplay), background dim | §1.1 |
| Visual-settings quick panel at map load/pause | §2.5 |
| Known lazer weakness to avoid: hidden functionality with undiscoverable shortcuts | §1.4 — main screen displays shortcuts |

---

## 5. Checklist for any new UI element

1. Does it obstruct gameplay or move the camera? → redesign (§1.1)
2. Is it obviously clickable (contrast + hover) or obviously not? (§1.2)
3. Does it do exactly one thing, with instant feedback? (§1.3)
4. Does it have a permanent home + shortcut + main-screen route? (§1.4)
5. Fully operable by keyboard, visible focus state? (§1.5)
6. Built from shared components, consistent motion? (§1.6)
7. Are its tunables in `GameSettings`/`Osu3DSettings`? (§3)
