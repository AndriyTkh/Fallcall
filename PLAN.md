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

_Last updated: 2026-07-15_

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
- **Song select (U4)**: `SongSelectUI.cs` rebuilt on the U1 kit (TMP + rounded panels) — set carousel +
  detail panel with **glance metadata** (stars, CS/AR/OD/HP, length, BPM), **audio preview** from
  `PreviewTime` (40% fallback), **star/length/BPM filters** + sort cycle, **type-to-search** (no field
  focus) and full keyboard flow (↑↓ set · ←→ diff · Enter play; Esc→back owned by `Bootstrap`).
  Download-by-id retained. Metadata beyond stars is
  parsed lazily from the `.osu` + cached (block owns only `SongSelectUI.cs`, not `BeatmapLibrary`).
  Pending in-editor verify.
- **Online search (U5)**: `BeatmapDownloader` gains a `Search(query, mode)` coroutine (nerinyan `/search`
  primary → catboy `/api/v2/search` fallback; osu!-API JSON parsed via a `{"items":…}` wrapper since
  `JsonUtility` can't read top-level arrays). `SongSelectUI` gets a **View: Local ⇄ Online** top-bar toggle
  that reuses the same carousel/detail widgets: online typing runs a debounced mirror search, results show
  glance metadata straight from the API (stars/CS/AR/OD/HP/len/BPM), audio preview streams `b.ppy.sh/preview/
  {id}.mp3` + cover from `assets.ppy.sh`, and the primary button becomes **Download** → `BeatmapLibrary.DownloadSet`
  auto-imports through the existing `.osz` pipeline (already-imported sets show ✓ and jump to Local to play).
  Same keyboard flow (↑↓/←→/Enter). Pending in-editor verify.
- **Nav shell (U3)**: `UI/MainScreen.cs` (title + Play/Browse/Settings/Quit, per-entry shortcut
  hints, first-run = no dead Play) + `UI/NavBar.cs` (toggleable top toolbar, Ctrl+T, shared
  `MenuRoute` + `UiInput.Typing` guard + toast). `Bootstrap` flow now: Scan → main → song select,
  Esc backs out; NavBar suppressed during load/play. Settings route uses `Bootstrap.OpenSettingsHook`
  (U2 plugs its overlay in there). Pending in-editor verify.
- **Video**: `VideoPlayback.cs` plays the `Video` event, synced to `GameClock`.
- **HUD**: `HudSkin.cs` renders score/acc/combo/health with skin fonts (IMGUI). Follow points,
  background-dim setting landed.
- **Settings**: split `Osu3DSettings` (scene) + `GameSettings` (persisted); legacy IMGUI pause menu
  still present. **Settings overlay (U2)**: `UI/SettingsOverlay.cs` — self-bootstrapping slide-over
  (Ctrl+O / nav Settings route via `Bootstrap.OpenSettingsHook`), sidebar sections (Gameplay/Visuals/
  Audio/Skin/Input/UI) + search-with-highlight, live-applying sliders/toggles with per-setting +
  per-section reset, and a rebindable keybinds section (conflict detection) backed by a new
  `GameSettings` keybind store + `Changed` event. Pending in-editor verify.
- **UI kit (U1)**: `UI/UiTheme.cs` (design tokens: grey/blue placeholder palette, type scale, spacing/
  radii, motion, shared TMP font, procedural rounded-rect sprites) + `UI/UiKit.cs` (runtime uGUI+TMP
  widget factory: canvas w/ live UI-scale, panel, button, toggle, range slider, search field, section
  header, list row — all with hover/press/keyboard-focus states). `GameSettings.UiScale` added (live via
  `UiScaler`). **U2–U4 build from this; don't re-style ad hoc.** Pending in-editor compile/verify.
- **Editor-authoring pivot (UED, in progress 2026-07-12)**: moving UI from code-only toward
  *developer-authorable in the editor* without losing the "press Play, no wiring" default. Landed:
  `UI/UiScaffold.cs` (idempotent get-or-create `Child`/`Ensure<T>` reconcile primitives + `HealRoundedSprite`
  self-heal for non-serializable runtime sprites; `UiPlaceholder` marker; `UiListView` placeholder-in-editor/
  data-at-play list binder) + `UI/UiRow.cs` (row presentation contract — named slots, prefab-serialized so it
  gets its own file). `SongSelectUI` now routes **all four** row builders (local/online × set/diff) through
  `UiListView` + `UiRow` with **optional `setRowPrefab`/`diffRowPrefab`** — null = the code default row
  (byte-identical to before), assign a styled prefab to restyle rows with custom art/effects. Right-click the
  component → **"Fallcall/Build or Refresh editor preview"** builds the screen + sample rows in-scene for
  styling (throwaway; tagged + auto-removed on Play, or "Clear editor preview"). `Bootstrap` reuses a
  developer-placed scene `SongSelectUI` if present, else auto-spawns. **Not yet:** in-scene per-node reconcile
  of the *static chrome* (panels/top-bar/detail) — today the chrome is still code-built each run; the row
  prefab is the authoring surface this pass. Pending in-editor compile/verify (no headless path).
- **Docs**: `STRUCTURE.md` (vision), `INDEX.md` (auto-gen), `docs/OPTIMIZATION.md`,
  `docs/osu-leniency.md`, **`docs/UI-DESIGN.md`** (UI/design principles — binding for this wave),
  `docs/osu-format.md` (RES1 format audit — feeds E1–E5; MD5-sidecar approach confirmed),
  **`docs/osu-api.md`** (RES2 online-API/mirror/CDN audit — binding for U6; answers auth,
  download feasibility, rate-limit topology, and the exact preview-clip sync formula).
- **Map browser (U6)**: **pass 1 code-complete, IN-PROGRESS, unverified at runtime** — see
  `docs/U6-progress.md` (the resume point). `UI/Browser/` holds the screen + 5 helpers; the Browse route
  now opens it; **Layout B frame** with the autoplay slot reserved but empty (**no preview this pass** —
  human call). Compiles; the "done when" (an audibly playing demo) still needs an editor run. The
  `AudioType.MPEG` → `OGGVORBIS` bug is **fixed** in both paths. Research (RES2) backs it: browser is
  **credential-free**, `.osz` **must** come from mirrors (`*`-scope wall), autoplay preview **viable at
  ~200 KB/map**. Also fixed en route: catboy search took `q=` and silently answered with its default
  listing — the fallback mirror had never actually searched.
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
| **U1** | UI design language / theme foundation (shared components + tokens) | DONE (2026-07-11) | — | new `UI/UiTheme.cs`, `UI/UiKit.cs` (shared widgets), `UI/` folder |
| **U2** | Settings overlay (global shortcut, sections, search, sliders, per-setting reset, keybinds) | DONE (2026-07-11) | U1 | new `UI/SettingsOverlay.cs`; reads/writes `GameSettings.cs` |
| **U3** | Main screen + navigation shell (toolbar, shortcut hints, routing) | DONE (2026-07-11) | U1 | new `UI/MainScreen.cs`, `UI/NavBar.cs`, `Bootstrap.cs` |
| **U4** | Song-select refinement (audio preview, filters, keyboard nav, theming) | DONE (2026-07-11) | U1 | `SongSelectUI.cs` |
| **U5** | Online beatmap search (mirror API, no account) into song select | DONE (2026-07-12) | U4 | `SongSelectUI.cs` (shares w/ U4 — serialize), `BeatmapDownloader.cs` |
| **U6** | **Map browser** — two-layout browse screen + autoplay preview panel | IN-PROGRESS (opus, 2026-07-15) | U5, RES2 | new `UI/Browser/*.cs`; `BeatmapDownloader.cs`, `SongSelectUI.cs` (serialize w/ U4/U5), `Bootstrap.cs` (Browse route — U3 DONE) |

**Overlap notes (read before claiming):**
- **U1 is the keystone** — it defines the shared theme + widget kit every other block draws
  with. Land it first; U2–U4 all depend on it. It owns a fresh `UI/` namespace so it collides
  with nothing existing.
- **U4, U5 and U6 all touch `SongSelectUI.cs`** → serialize (whoever is `IN-PROGRESS` wins; the
  other waits). U5/U6 also own `BeatmapDownloader.cs` for search/fetch endpoints.
- **U6 should land its screen in a new `UI/MapBrowser.cs`**, not by growing the already-1283-line
  `SongSelectUI.cs`. It reuses the U1 kit + `UiListView`/`UiRow`; it edits `SongSelectUI.cs` only
  where the Online tab hands off (and to fix the `AudioType` bug, see U6 detail).
- **U3 edits `Bootstrap.cs`** (currently drives `SongSelectUI` directly) — only U3 touches it.
- Pause-menu settings currently live in `GameManager.DrawPauseMenu` (IMGUI). **U2 should not
  rip that out** in its first pass — build the new overlay alongside; migrating/retiring the
  IMGUI pause settings is a later, separate step (note it, don't do it silently).

### Dependency graph

```
U1 (theme/kit) ─┬─▶ U2 (settings overlay)
                ├─▶ U3 (main screen + nav)
                └─▶ U4 (song select) ─▶ U5 (online search) ─▶ U6 (map browser)
                                                              ▲
                                            RES2 (osu API audit) ┘
```

**U1–U5 `DONE`** (pending in-editor verify). **U6 (map browser) is the active block** — deps
`U5` + `RES2` both `DONE`, so it is startable now. Next wave (E1–E5 + editor) stays parked below;
promote it when the human is ready.

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

### U6 — Map browser  _(needs U5, RES2)_
A dedicated browse screen per `docs/UI-DESIGN.md`, backed by the API audit in **`docs/osu-api.md`
(read it first — it resolves every auth/feasibility question below)**. Two layouts, both viable:

- **Layout A — column list.** osu!-style multi-column result grid: filter + scroll, each row
  offering play-audio-demo or download. Familiar, dense.
- **Layout B — list + preview pane.** Single scrolling map column on the **left**; right side
  splits **[top]** searchbar + filters, **[bottom]** map preview with the **autoplay panel**.

**Autoplay preview (the headline feature — confirmed viable, `docs/osu-api.md` §1–2):** fetch the
preview ogg (~100 KB) + the `.osu` (~90 KB, public, unauthenticated) and render ~10 s of real
autoplay gameplay synced to the audio demo. **Never fetch the `.osz` to preview** (~13 MB). Exact
sync relation — the preview clip is the song windowed to `[PreviewTime − 100 ms, +10000 ms]`:

```
songTimeMs = clipPlaybackMs + PreviewTime - 100
```

Reuse `BeatmapParser` (already parses `PreviewTime`) + route through `Playfield.ToWorld` — do not
bake world math into the panel.

**Decided (do not re-litigate — sourced in `docs/osu-api.md`):**
- **Credential-free.** Mirror search + ppy CDN covers the whole browser. No OAuth in v1.
- **Mirrors for `.osz` forever.** Official download needs the `*` scope, which no third-party app
  can ever hold (§5). Not a preference — a wall.
- **Never proxy mirror traffic.** Limits are per-IP/per-token; a proxy would collapse them into one
  app-wide bucket (§6).
- **"Default difficulty" doesn't exist server-side** (§3) — pick our own rule as an
  `Osu3DSettings` tunable; suggested default = highest-star osu!std diff.

**Bug to fix in this block:** `SongSelectUI.cs:999` streams the preview as `AudioType.MPEG`, but
`b.ppy.sh/preview/{id}.mp3` is **Ogg/Vorbis despite the extension** — failure path is a silent
`yield break`, so online previews are almost certainly dead today. → `AudioType.OGGVORBIS`.

**Done when:** a browse screen ships one of the two layouts (human picks — see Open questions),
autoplay preview plays ~10 s of synced real gameplay beside the audio demo, filters/sort +
keyboard-only flow work per §2.2/§2.3, download→auto-import reuses the existing `.osz` pipeline,
no account required, and previews actually audibly play (AudioType fix verified in-editor).

---

## Parallel research (startable now — read-only, no file overlap)

| ID | Block | Status | Deps | Owns (primary files) |
| --- | --- | --- | --- | --- |
| **RES1** | osu! `.osu` format audit — catalogue every field we can exploit | DONE (2026-07-11) | — | `docs/osu-format.md` |
| **RES2** | osu! online API / mirror / preview-CDN audit — feeds U6 | DONE (2026-07-15) | — | `docs/osu-api.md` |

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

### RES2 — osu! online API / mirror / preview-CDN audit
What Fallcall can pull from osu!'s servers for the **map browser (U6)**, and what is structurally
impossible. Everything verified against **live endpoints + `ppy/osu-web`/`ppy/osu` source**
(2026-07-15) — the published docs are wrong or vague on several points, so **cite `docs/osu-api.md`,
not the docs site**. Headline results: preview clip fully characterised (Ogg/Vorbis despite the
`.mp3` extension, always 10.1 s, = song windowed to `[PreviewTime − 100 ms, +10000 ms]`, proven by
FFT cross-correlation on two sets); `.osu` files are public → **autoplay preview costs ~200 KB/map,
no `.osz`**; `.osz` download needs the `*` scope that no third-party app can hold → **mirrors are
mandatory**; rate limits are **per-token / per-IP, not app-wide** → never proxy; official search
works on client_credentials but adds nothing v1 needs. Also caught a live bug (`SongSelectUI.cs:999`
`AudioType.MPEG` on a Vorbis stream) — assigned to U6.
**Done when:** `docs/osu-api.md` exists, every claim sourced to a live check or a source line, and
U6's auth/feasibility questions are all answered. ✔

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

- **U1 palette (human call, 2026-07-11):** proposed dark/pastel variants **rejected** — ship a
  neutral **grey/blue placeholder** palette; real art direction decided later against **complete
  layouts** (post-U3), not swatches. Palette core colors are **player-editable settings**
  (semantic slots in `GameSettings`, live-apply); U2 surfaces the color controls.
- **U2:** global settings shortcut key (osu! uses `Ctrl+O`; pick ours) + whether to eventually
  retire the IMGUI pause-menu settings once the overlay covers them.
- **U5 (resolved 2026-07-12):** search primary = nerinyan `/search`, fallback = catboy `/api/v2/search`
  (matches the download order); both public/unauthenticated. Preview audio/cover pulled from `b.ppy.sh` /
  `assets.ppy.sh`. **Still to confirm in-editor:** live mirror responses parse as expected (field names
  `accuracy`=OD, `drain`=HP, `total_length` sec) and CORS/host access is fine in a desktop Unity build.
- **U6 — layout (human call, blocking the block):** **Layout A** (osu!-style multi-column list: filter,
  scroll, per-row audio demo / download) vs **Layout B** (single map column left; right = searchbar +
  filters on top, map preview + **autoplay panel** bottom). Layout B is what the RES2 autoplay finding
  unlocks; A is the safer/denser default. Pick one before claiming U6.
- **U6 — "default difficulty" rule (needs a default proposed):** no server-side default exists
  (`docs/osu-api.md` §3). Proposal: **highest-star osu!std diff**, as an `Osu3DSettings` tunable.
- **U6 — open (decides whether we ever need credentials):** do mirror search filters match the official
  set (star range / genre / status / language)? If they're weak, adding client_credentials is an
  isolated change that costs nothing app-wide (`docs/osu-api.md` §6–7). Unverified.
- **Release-time credentials (resolved 2026-07-15, human asked):** login was considered for map search
  and **rejected for v1** — client_credentials already unlocks every real search filter, a user login
  only adds favourites/played/recommended, it does **not** unlock downloads, and it does **not** remove
  the shipped client_secret (osu-web issues a secret for every app; no public/PKCE client type exists).
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

- 2026-07-15 — **U6 pass 1 code-complete, still `IN-PROGRESS`** (opus): `UI/Browser/MapBrowser.cs` landed (the
  screen: browse state, selection, keyboard flow, download→auto-import / play handoff) + `Bootstrap.cs`
  (`State.Browsing`, **Browse route now opens the browser**, Esc → main, `PlayRequested`/`SetImported` wired),
  `SongSelectUI.FocusLocalSet` wrapper. **Compiles (human-run editor); nothing verified at runtime** — the
  audible demo is the "done when", so the block stays claimed. Resume: `docs/U6-progress.md`. INDEX regen (55).
- 2026-07-15 — Claimed **U6 pass 1** (opus): map browser, **no autoplay preview this pass** (human call) —
  new `UI/Browser/` (MapBrowser + View/Rows/Search/Media/Model, split small on purpose), **Layout B frame**
  with the preview slot reserved but empty; + `BeatmapDownloader.cs` (catboy search params, CDN url helpers),
  `SongSelectUI.cs` (AudioType fix only), `Bootstrap.cs` (Browse route → browser). Deps U5+RES2 DONE, no
  IN-PROGRESS overlap.
- 2026-07-15 — **RES2 DONE** (opus): `docs/osu-api.md` landed — osu! online API / mirror / preview-CDN
  audit, every claim verified against live endpoints + `ppy/osu-web`/`ppy/osu` source (docs site is
  wrong/vague on several). Key: preview clip = song windowed to `[PreviewTime−100ms, +10000ms]`, always
  10.1 s, **Ogg/Vorbis despite the `.mp3` extension** (proven by FFT cross-correlation vs `.osz` audio on
  2 sets); `.osu` is public → **autoplay preview ~200 KB/map, no `.osz`**; `.osz` needs the `*` scope that
  no third-party app can hold → **mirrors mandatory**; throttles are **per-token/per-IP, not app-wide** →
  **never proxy**. Caught a live bug (`SongSelectUI.cs:999` `AudioType.MPEG` on Vorbis → silent no-op),
  assigned to U6. Added **U6 (map browser)** to the board. No code touched. **Unblocks U6.**
- 2026-07-12 — **UED (editor-authoring pivot) — pass 1** (opus): new `UI/UiScaffold.cs` (reconcile
  primitives + `UiListView` placeholder→data list binder + `UiPlaceholder`) & `UI/UiRow.cs` (row contract).
  `SongSelectUI` row builders (×4) now go through `UiListView`+`UiRow` with optional `setRowPrefab`/
  `diffRowPrefab` (null = code default, byte-identical); `[ContextMenu]` editor preview; `Bootstrap` reuses a
  scene `SongSelectUI` if present. Owns: `SongSelectUI.cs`, `Bootstrap.cs`, new `UI/UiScaffold.cs`,
  `UI/UiRow.cs`. INDEX regen (48). Next: static-chrome in-scene reconcile; then roll pattern to other screens.
- 2026-07-12 — **U5 DONE** (opus): `BeatmapDownloader.Search` (nerinyan primary → catboy fallback, osu!-API
  JSON via `{"items":…}` wrapper) + `SongSelectUI` **View: Local ⇄ Online** toggle reusing the carousel/detail
  widgets — debounced mirror search, API glance metadata, `b.ppy.sh` audio preview + `assets.ppy.sh` cover,
  primary button → Download→auto-import (imported sets show ✓ / jump to Local). Same keyboard flow.
  `INDEX.md` regen (46 scripts). Pending in-editor verify. **UI wave (U1–U5) complete.**
- 2026-07-12 — Claimed **U5** (opus): online beatmap search → mirror-API search in `BeatmapDownloader.cs` +
  Local/Online toggle in `SongSelectUI.cs` (shared card UI, download→auto-import). Deps U4 DONE; U4 not
  IN-PROGRESS so `SongSelectUI.cs` free.
- 2026-07-11 — **U4 DONE** (opus): `SongSelectUI.cs` rewritten on the U1 kit — set carousel + detail
  panel, glance metadata (stars/CS/AR/OD/HP/len/BPM), audio preview (PreviewTime → 40% fallback, debounced
  `AudioSource`), star/length/BPM filters + sort cycle, type-to-search (no field focus) + full keyboard nav
  (↑↓/←→/Enter), one-shot launch guard. Metadata parsed lazily from `.osu` + cached (did NOT edit
  `BeatmapLibrary`). Also fixed a drift compile error in `UI/UiKit.cs:269` (`Slider.Direction` →
  `UnityEngine.UI.Slider.Direction`, CS0119) — U1 DONE, not a live collision. `INDEX.md` regen (46 scripts).
  Pending in-editor verify. **Unblocks U5.**
- 2026-07-11 — Claimed **U4** (opus): song-select refinement → rewrite `SongSelectUI.cs` on the U1 kit.
  Deps U1 DONE; no Owns overlap (U2 DONE, U5 not started).
- 2026-07-11 — **U2 DONE** (opus): `UI/SettingsOverlay.cs` + `GameSettings` keybind store/`Changed`
  event. Plugs into `Bootstrap.OpenSettingsHook` (read-only use of U3's API — did NOT edit Bootstrap).
  Keeps `UiInput.Typing` true while open (focused search) so U3's menu Esc/shortcuts stand down.
  **Known limits (noted, not silent):** (1) during *gameplay*, Esc closes the overlay AND
  GameManager still toggles pause (GM reads Esc raw, not gated by `UiInput.Typing`); (2) gameplay
  keybinds (A/S/D, Tab, R, Esc) are stored + rebindable w/ conflict detection but the gameplay
  systems still read raw keys — only the settings shortcut honours the store yet; wiring the rest is a
  later migration. IMGUI pause settings left intact per board. Pending in-editor verify. **Unblocks:** —.
- 2026-07-11 — Claimed **U2** (opus): settings overlay → new `UI/SettingsOverlay.cs` + `GameSettings.cs`
  (Changed event, keybind store). Self-bootstraps (global shortcut) so it does NOT touch `Bootstrap.cs`
  (U3's). Deps U1 DONE; no Owns overlap with IN-PROGRESS U3.
- 2026-07-11 — **U3 DONE** (opus): `UI/MainScreen.cs` + `UI/NavBar.cs` landed, wired into
  `Bootstrap.cs` (Scan → main screen → song select; Esc backs out). Routes reachable 3 ways
  (main entry · Ctrl+T toolbar · shortcut). **Settings seam for U2:** `Bootstrap.OpenSettingsHook`
  (static `Action`) — U2's overlay assigns it and the Settings route opens it; until then the route
  toasts "arrives in U2". Pending in-editor compile/verify. `INDEX.md` regen (45 scripts).
- 2026-07-11 — Claimed **U3** (opus): main screen + nav shell → `UI/MainScreen.cs`, `UI/NavBar.cs`,
  wire into `Bootstrap.cs`. Deps U1 DONE; owns files free of IN-PROGRESS overlap.
- 2026-07-11 — **RES1 DONE**: `docs/osu-format.md` landed (reviewed by curator; parser claims
  spot-checked). MD5 = raw `.osu` bytes confirmed; sidecar approach validated. **Unblocks E1**
  when the next wave is promoted.
- 2026-07-11 — **U1 DONE** (opus): `UI/UiTheme.cs` + `UI/UiKit.cs` landed; `GameSettings.UiScale`
  added + wired through Load/Save/Reset/defaults; `INDEX.md` regenerated (43 scripts). Pending
  in-editor compile/verify (no headless path). **Unblocks U2, U3, U4.**
- 2026-07-11 — **Re-claimed U1** (opus): prior curator/subagent claim was **stale** — no `UI/`
  files ever landed (git clean of them). Building `UiTheme` + `UiKit` fresh + UI-scale setting.
- 2026-07-11 — Claimed **U1** (theme/kit; incl. small `GameSettings.cs` add for UI-scale — no
  IN-PROGRESS block owns it) — curator session, delegated to subagent.
- 2026-07-11 — Claimed **RES1** (osu format audit → `docs/osu-format.md`) — curator session,
  delegated to subagent.
- 2026-07-11 — Captured the environment/choreography/authoring vision in **STRUCTURE §5**;
  added **RES1** (osu format audit, startable now) + parked **Next wave E1–E5 + editor** to this
  board; new open questions (E1/E2/E5). No code touched. — opus
- 2026-07-11 — Archived the projection/gameplay/loader/video/HUD wave to
  `docs/archive/PLAN-2026-07-11.md`; reset this board to the **UI wave** (U1–U5) driven by the
  new `docs/UI-DESIGN.md`. — opus

## Done log

_Prior wave (R1/R2/A/C/D/B/E + follow-ups) is fully logged in `docs/archive/PLAN-2026-07-11.md`._

- 2026-07-15 — **RES2** osu! online API / mirror / preview-CDN audit → `docs/osu-api.md`: preview clip
  fully characterised (Ogg/Vorbis, 10.1 s, `[PreviewTime−100ms, +10000ms]`, cross-correlation-proven);
  public `.osu` → autoplay preview ~200 KB/map; `*`-scope wall → mirrors mandatory; per-token/per-IP
  throttles → never proxy; credential-free verdict for U6. Live `AudioType` bug found → U6.

- 2026-07-12 — **U5** online beatmap search → `BeatmapDownloader.Search` (mirror `/search`, nerinyan→catboy,
  JSON-array wrapper for `JsonUtility`) + `SongSelectUI` Local/Online toggle reusing the card UI: debounced
  mirror query, API glance metadata, streamed `b.ppy.sh` preview + `assets.ppy.sh` cover, Download→auto-import
  via the existing `.osz` pipeline (imported sets ✓ / jump to Local). **UI wave complete.**
- 2026-07-11 — **U4** song-select refinement → `SongSelectUI.cs` on the U1 kit: set carousel + detail
  panel, glance metadata (stars/CS/AR/OD/HP/len/BPM), audio preview (PreviewTime→40% fallback), star/
  length/BPM filters + sort, type-to-search + full keyboard nav, download-by-id retained. Lazy `.osu`
  metadata cache. Fixed drift `UiKit.cs:269` CS0119.
- 2026-07-11 — **U3** main screen + nav shell → `UI/MainScreen.cs` (Play/Browse/Settings/Quit +
  per-entry shortcut hints, first-run points to Browse) + `UI/NavBar.cs` (Ctrl+T toolbar, `MenuRoute`,
  `UiInput.Typing`, toast). `Bootstrap` routes all three ways; `OpenSettingsHook` seam for U2.
- 2026-07-11 — **U2** Settings overlay → `UI/SettingsOverlay.cs`: global Ctrl+O slide-over, sidebar
  sections + search-highlight, live-apply sliders/toggles w/ per-setting + per-section reset, rebindable
  keybinds w/ conflict detection. Added a keybind store + `Changed` event to `GameSettings`.
- 2026-07-11 — **U1** UI theme/kit foundation → `UI/UiTheme.cs` (grey/blue placeholder tokens,
  type scale, spacing/radii, motion, TMP font resolver, procedural rounded-rect sprites) +
  `UI/UiKit.cs` (runtime uGUI+TMP: canvas w/ live UI-scale, panel, button, toggle, range slider w/
  readout+reset, search field, section header, list row — hover/press/keyboard-focus baked in).
  `GameSettings.UiScale` added. Pending in-editor verify.
- 2026-07-11 — **RES1** osu format audit → `docs/osu-format.md`: every field tagged
  parsed-vs-unused + Fallcall use; top-5 ranked (SV×volume intensity curve, kiai, PreviewTime,
  Editor Bookmarks, hitsound/storyboard density); MD5-of-raw-bytes + `<md5>.fallcall` sidecar
  confirmed sound.
Summary of what shipped: optimization + leniency research docs; sphere projection + 3-mode
camera (Sphere/Ortho2D/Falling) with Ortho2D dynamic zoom; beatmap library + mirror downloader;
runtime song-select UI; synced background video; skinned HUD; follow points; background-dim.
