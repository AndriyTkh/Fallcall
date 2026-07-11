# PLAN

> **Agent working doc — temporary & mutable.** Tracks progress across coding sessions,
> because context is reset often to save tokens. Keep it short and current.
>
> **This file is also the task board for parallel agents.** Multiple sessions may run at
> once, each owning one **block**. Before touching anything, follow the
> **[Continue-the-work protocol](#continue-the-work-protocol)** below — it prevents two
> agents fighting over the same files and prevents starting a task whose dependencies
> aren't done yet.
>
> Convention: absolute dates (not "yesterday"). Prune done subtasks into the Done log.
> **History before this file:** the whole projection/gameplay/loader/video/HUD wave lives in
> `docs/archive/PLAN-2026-07-11.md` (full Done + Activity logs). This file starts the **UI wave**.

_Last updated: 2026-07-11_

---

## Continue-the-work protocol

When a human says **"continue the work"**, do this in order. **Do not skip the preflight —
it exists to stop false starts.**

### 1. Preflight check (report before doing anything)

1. Read `STRUCTURE.md` (intent), this file (board), `INDEX.md` (file map), and for any UI
   block also `docs/UI-DESIGN.md` (binding UI/design principles).
2. Run `git status` and `git log --oneline -8`. Compare reality against **Current state**
   and the board below.
3. Report to the human, in a short block: which blocks are `DONE`/`IN-PROGRESS`/`BLOCKED`/
   `TODO`; any `IN-PROGRESS` claim that looks **stale** (claimed, no matching changes) —
   flag, don't steal; any `TODO` whose deps are **now satisfied**; any drift (board vs git).
4. **Stop and pick a task** (step 2). If nothing is startable, say so and ask the human.

### 2. Pick a task (false-start guards)

Choose the highest-priority `TODO` block that passes **all** guards:

- **Dependency guard:** every block in its `Deps` is `DONE`.
- **Overlap guard:** its `Owns` files don't intersect any `IN-PROGRESS` block's `Owns`.
- **State guard:** Current state matches git. If not, report and stop.

If two are both startable and don't overlap, either is fine — prefer lower ID.

### 3. Claim it

Edit **only this file**: set status `IN-PROGRESS (<who>, <date>)` and add one line to the
**Activity log**. Keep the edit tiny (one row + one log line) so concurrent sessions don't
merge-conflict. Then work.

### 4. Finish it

- Verify against **Done when**. Move finished subtasks to the **Done log**, set status
  `DONE (<date>)`, update **Current state**, note any block this unblocks.
- If you edited a script header / `// INDEX:` marker, regenerate:
  `powershell -ExecutionPolicy Bypass -File Tools/gen-index.ps1`.

---

## Current state

- **Gameplay engine** (osu!standard): `.osu`/`.osz` parse, timing, circles/sliders/spinners,
  scoring/HP, hit sounds, skins, procedural-art fallback. Leniency fixes landed (block C).
- **Projection & camera**: sphere chunk is default (`Playfield.cs`); `ViewModeController` toggles
  **Sphere ⇄ Ortho2D ⇄ Falling** mid-map via **[Tab]** (`StartMode` setting picks the opener).
  Ortho2D has dynamic click-group zoom. Only A.6 (visual "fall" backdrop) remains in that lane.
- **Beatmap loader**: `BeatmapLibrary` (scan/cache) + `BeatmapDownloader` (mirror download by
  set id, nerinyan→catboy). Cache: `.osz` in `persistentDataPath/Songs`.
- **Song select**: `SongSelectUI.cs` — runtime uGUI carousel/search/sort/detail/download-by-id,
  driven by `Bootstrap`. **Functional but visually minimal** — the UI wave refines it.
- **Video**: `VideoPlayback.cs` plays the `Video` event, synced to `GameClock`.
- **HUD**: `HudSkin.cs` renders score/acc/combo/health with skin fonts (IMGUI). Follow points,
  background-dim setting landed.
- **Settings**: split `Osu3DSettings` (scene) + `GameSettings` (persisted); pause menu is IMGUI.
- **Docs**: `STRUCTURE.md` (vision), `INDEX.md` (auto-gen), `docs/OPTIMIZATION.md`,
  `docs/osu-leniency.md`, **`docs/UI-DESIGN.md`** (UI/design principles — binding for this wave).
- Most of the last wave is **pending in-editor verification** (no headless Unity path).

---

## Task board — UI wave

Priority top-to-bottom. Statuses: `TODO` · `IN-PROGRESS (who, date)` · `BLOCKED (by X)` ·
`DONE (date)`. `Deps` = must be `DONE` first. `Owns` = files this block may edit.

**All UI blocks follow `docs/UI-DESIGN.md`.** Golden rule from it: **adopt osu!lazer's
interaction *principles*, invent Fallcall's own *visual language*** — never reproduce osu!'s
look/branding (form vs. function split, §0 of that doc).

| ID | Block | Status | Deps | Owns (primary files) |
| --- | --- | --- | --- | --- |
| **U1** | UI design language / theme foundation (shared components + tokens) | TODO | — | new `UI/UiTheme.cs`, `UI/UiKit.cs` (shared widgets), `UI/` folder |
| **U2** | Settings overlay (global shortcut, sections, search, sliders, per-setting reset, keybinds) | TODO | U1 | new `UI/SettingsOverlay.cs`; reads/writes `GameSettings.cs` |
| **U3** | Main screen + navigation shell (toolbar, shortcut hints, routing) | TODO | U1 | new `UI/MainScreen.cs`, `UI/NavBar.cs`, `Bootstrap.cs` |
| **U4** | Song-select refinement (audio preview, filters, keyboard nav, theming) | TODO | U1 | `SongSelectUI.cs` |
| **U5** | Online beatmap search (mirror API, no account) into song select | TODO | U4 | `SongSelectUI.cs` (shares w/ U4 — serialize), `BeatmapDownloader.cs` |

**Overlap notes (read before claiming):**
- **U1 is the keystone** — it defines the shared theme + widget kit every other block draws
  with. Land it first; U2–U4 all depend on it. It owns a fresh `UI/` namespace so it collides
  with nothing existing.
- **U4 and U5 both edit `SongSelectUI.cs`** → serialize (whoever is `IN-PROGRESS` wins; the
  other waits). U5 also owns `BeatmapDownloader.cs` for search endpoints.
- **U3 edits `Bootstrap.cs`** (currently drives `SongSelectUI` directly) — only U3 touches it.
- Pause-menu settings currently live in `GameManager.DrawPauseMenu` (IMGUI). **U2 should not
  rip that out** in its first pass — build the new overlay alongside; migrating/retiring the
  IMGUI pause settings is a later, separate step (note it, don't do it silently).

### Dependency graph

```
U1 (theme/kit) ─┬─▶ U2 (settings overlay)
                ├─▶ U3 (main screen + nav)
                └─▶ U4 (song select) ─▶ U5 (online search)
```

Startable **right now**: **U1** (no deps). Everything else waits on U1.

---

## Blocks (detail)

### U1 — UI design language / theme foundation
Build Fallcall's **own** visual identity + a reusable widget kit, per `docs/UI-DESIGN.md`
(§0 form-vs-function, §1.2 contrast, §1.6 consistency, §3 Fallcall-specific: screen-space UI,
center-of-screen sacred). **Do not port osu!'s look** — design for the falling-geometric-space
theme.

**UI tech stack (decided — do not re-litigate):** **uGUI (Canvas) + TextMeshPro**, runtime-built
in code (no scene wiring, as `SongSelectUI` does). Screen-Space-Overlay, one Canvas per surface.
**TMP** for all text (crisp scaling — never legacy `Text`). **CanvasScaler** ("Scale With Screen
Size") wired to the UI-scale setting. **EventSystem + input module** configured for full keyboard
navigation. `UiTheme` = C# design tokens, `UiKit` = widget prefabs built in code (the USS
substitute). _Not UI Toolkit_ — runtime UITK in Unity 2022 integrates poorly with the animated 3D
scene/effects and fights the build-in-code convention; reserve UITK only for possible future
Editor-side map-editor tooling (STRUCTURE §5.5). IMGUI (pause menu, `HudSkin`) is legacy → folds
into this kit over later blocks, not ripped out in U1.
- **Design tokens** (`UiTheme`): palette (contrast-first, colorblind-safe, its own colors — not
  osu! pink), typography scale, spacing, corner radii, motion curves/durations. All as settings
  where a player would reasonably tune them (UI scale at minimum).
- **Widget kit** (`UiKit`): button, toggle, range slider (with numeric readout + keyboard step +
  per-control reset icon), section header, search field, list/card row, focus-ring — each with the
  mandated hover + keyboard-focus states (§1.2, §1.5). uGUI, runtime-built (repo convention: no
  scene wiring; see `SongSelectUI` for the pattern).
**Done when:** a theme + reusable widget set exist that U2–U4 can build screens from without
re-inventing styling; hover/focus/contrast states are built in; UI-scale is a live setting.

### U2 — Settings overlay  _(needs U1)_
Slide-over settings panel per `docs/UI-DESIGN.md` §2.1: openable **anywhere via a global
shortcut** (incl. during pause), **live-apply everything**, sidebar sections (Gameplay,
Visuals/Camera, Audio, Skin, Input, UI), **search with match highlight**, range sliders with
keyboard step + **per-setting reset**, and a **keybinds** section (gameplay + UI shortcuts,
conflict detection). Backed by `GameSettings`. Build alongside the existing IMGUI pause
settings — don't remove them this pass.
**Done when:** settings open from a global shortcut, sections+search work, every control
live-applies and has its own reset, keybinds rebind without silent double-binding.

### U3 — Main screen + navigation shell  _(needs U1)_
Main screen + persistent nav per `docs/UI-DESIGN.md` §1.4, §2.4: entries (Play / Settings /
Browse / Quit), **each showing its keyboard shortcut as a small hint** (fixes osu!'s
undiscoverable-shortcut weakness); persistent toggleable **toolbar** giving the same routes;
ambient dimmed backdrop + menu state; graceful **first-run** (no maps → point to Browse, not a
dead Play). Routes into `SongSelectUI` / settings overlay.
**Done when:** main screen routes to play/settings/browse via click, shortcut, and toolbar;
shortcut hints shown; first-run has no dead ends.

### U4 — Song-select refinement  _(needs U1)_
Bring `SongSelectUI` up to `docs/UI-DESIGN.md` §2.2: restyle with the U1 kit; **audio preview
on select** from `.osu` `[General] PreviewTime` (ms; fall back ~40% into the song when missing/
`-1`); **filters** (star range / length / BPM) + sort; **type-to-search without focusing the
field**; full **keyboard-only** flow (type→search, arrows→move, Enter→play, Esc→back); glance
metadata (stars, CS/AR/OD/HP, length, BPM).
**Done when:** song select previews audio, filters/sorts, is fully keyboard-operable, and uses
the U1 visual language.

### U5 — Online beatmap search  _(needs U4)_
In-client search per `docs/UI-DESIGN.md` §2.3 — **no account** v1: query a mirror
(nerinyan/catboy — same source `BeatmapDownloader` already downloads from) for search results,
show them in the **same card UI** as local (Local/Online toggle), download → auto-import through
the existing `.osz` pipeline. Official osu! API v2 (OAuth) is a later optional add.
**Done when:** online search returns results in the shared card UI, downloads + imports a chosen
set, no account required.

---

## Parallel research (startable now — read-only, no file overlap)

| ID | Block | Status | Deps | Owns (primary files) |
| --- | --- | --- | --- | --- |
| **RES1** | osu! `.osu` format audit — catalogue every field we can exploit | TODO | — | new `docs/osu-format.md` |

### RES1 — osu! format audit
Full pass over the `.osu`/`.osz` format; produce `docs/osu-format.md` listing **everything we
can use** for choreography, pacing, and VE, and what we already parse ([Beatmap.cs](Assets/Scripts/Beatmaps/Beatmap.cs) /
[BeatmapParser.cs](Assets/Scripts/Beatmaps/BeatmapParser.cs)). Cover at least: `[TimingPoints]` (kiai `effect` bit, SV/uninherited,
sample set/volume), `[Colours]` combo colors, `[Events]` (breaks — already parsed, storyboard,
video, background), `[General]` (`AudioLeadIn`, `PreviewTime`, `StackLeniency`, `SampleSet`),
`[Difficulty]`, hit-object extras/hitsounds, and the beatmap **MD5 hash** source. Confirm the
**sidecar** approach (STRUCTURE §5.6). No code changes — research doc only. Feeds the next wave.
**Done when:** `docs/osu-format.md` exists, maps each usable field to a potential Fallcall use
(choreography / pacing / VE / seed), and flags parsed-vs-unused.

---

## Next wave — Environment, choreography & authoring  _(PARKED — do not start; UI wave is active)_

Captures STRUCTURE §5. Sketch only; promote to a full board when the UI wave winds down and
RES1 lands. IDs provisional.

- **E1 — Event-track + marker model.** One merged track (mode + VE events); three-source merge
  (native > beatmap > generated, STRUCTURE §5.3); **sidecar** `<md5>.fallcall` I/O (§5.6).
  _Deps: RES1._
- **E2 — Segmentation + constrained mode pick.** Segment on breaks ∪ density-change ∪ kiai;
  weighted per-segment mode under constraints; **rated seed = MD5 hash**, free seed override
  (§5.4). _Deps: E1._
- **E3 — Environment/atmosphere render.** Alto-style low-poly pastel sky, day/night cycle, grey
  cubism floor, tower + clouds (clouds must NOT reduce visibility); live beatmap-color tint.
  Absorbs the carried-over A.6 "fall backdrop". _Deps: —._
- **E4 — VE system.** Wind, lingering click particles, cursor cross-lines, ramp-with-intensity;
  driven by the E1 event track; falling-mode decorations pure-VE (circles render on top). _Deps: E1._
- **E5 — Pacing: skip-intro + rest/break timer.** Wire existing `AudioLeadIn`/first-object and
  `BreakPeriod` to a skippable intro + break countdown; >3 s auto-rest fallback (§5.7). _Deps: —._
- **EDITOR wave (own board).** Timeline; song + circle playback; scripted autoplay QA (camera/
  cursor drift vs §4); VE-marker track (side panel on/off/level); mode-marker track (incl.
  start-on-floor). No hit-object editing v1 (§5.5). _Deps: E1._

---

## Open questions / decisions to confirm with human

- **U1:** Fallcall's palette + type direction (should express falling-geometric-space; needs a
  human aesthetic call — propose 1–2 options rather than guessing).
- **U2:** global settings shortcut key (osu! uses `Ctrl+O`; pick ours) + whether to eventually
  retire the IMGUI pause-menu settings once the overlay covers them.
- **U5:** which mirror is primary for *search* (download already prefers nerinyan→catboy);
  confirm the search endpoint/terms of each is OK to use unauthenticated.
- Carried over from prior wave — **A:** default sphere radius + chunk degrees for "normal" feel;
  mode-switch trigger source (authored / heuristic / manual); stream-detection thresholds.
- **E2:** the constraint set for the generated mode-pick (min segment length per mode, opener
  rules, anti-ping-pong) + mode weights — needs playtest tuning, propose defaults.
- **E1/sidecar:** confirm `<md5>.fallcall` location (own store vs alongside `.osz`) + format
  (JSON vs custom text). MD5 hash source confirmed via RES1.
- **E5:** rest auto-fallback threshold (~3 s?) and whether skip-intro is manual-only or auto.

## Tips for following agents

- **UI:** read `docs/UI-DESIGN.md` first; build with the U1 kit, don't re-style ad hoc; keep UI
  **screen-space** (never on the sphere) and out of screen-center; every tunable is a setting.
- Don't touch drawable world math directly — go through `Playfield`.
- `Osu3DSettings` values apply on session (re)start; press **R** in play mode to re-apply.
- uGUI, runtime-built (no scene wiring) — copy `SongSelectUI` for the bootstrapping pattern.
- Unity 2022, built-in RP; procedural art fallback via `Util/TextureFactory`.
- After editing any script header / `// INDEX:` marker, run `Tools/gen-index.ps1`.

## Activity log

_One line per claim/finish so parallel sessions see who's on what. Newest first._

- 2026-07-11 — Captured the environment/choreography/authoring vision in **STRUCTURE §5**;
  added **RES1** (osu format audit, startable now) + parked **Next wave E1–E5 + editor** to this
  board; new open questions (E1/E2/E5). No code touched. — opus
- 2026-07-11 — Archived the projection/gameplay/loader/video/HUD wave to
  `docs/archive/PLAN-2026-07-11.md`; reset this board to the **UI wave** (U1–U5) driven by the
  new `docs/UI-DESIGN.md`. — opus

## Done log

_Prior wave (R1/R2/A/C/D/B/E + follow-ups) is fully logged in `docs/archive/PLAN-2026-07-11.md`._
Summary of what shipped: optimization + leniency research docs; sphere projection + 3-mode
camera (Sphere/Ortho2D/Falling) with Ortho2D dynamic zoom; beatmap library + mirror downloader;
runtime song-select UI; synced background video; skinned HUD; follow points; background-dim.
