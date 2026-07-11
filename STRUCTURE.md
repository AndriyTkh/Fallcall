# STRUCTURE

> **Human-curated.** This is the source-of-truth vision doc, and the document humans read
> most. Agents use/edit it. When code and this file disagree about *intent*, this file wins — update the
> code or flag the drift. (For the current *state* of the code, see `PLAN.md`; for a
> per-file map, see `INDEX.md`.)

---

## 1. What Fallcall is

A Unity 3D reimagining of osu!standard. Core osu! gameplay (circles, sliders, spinners,
timing, scoring) is preserved and projected into a 3D space. The signature feel: **falling
through a fast geometric 3D space** (mostly visual spectacle) while osu! gameplay plays out

The gameplay logic stays osu!-faithful. What changes is *how the playfield is presented in
space* and *how the camera moves* — and these are switchable mid-map as part of the
choreography.

---

## 2. Player & camera baseline

- **Free camera** across the horizontal axis; vertical look **clamped to ±90°**.
- **Target FOV: 90°.**
- **Projection chunk: ~120° × 90°** of the sphere (horizontal × vertical). Currently the
  code wraps onto a **cylinder** — this must become a **sphere** (see §4 and `PLAN.md`).
- Everything here is a **setting** (exposed for testing), not a hardcoded constant.

### "Radius as a parameter"
Whenever the term **radius** is used, it means **how warped the original flat plane is** —
larger radius = larger radius of the projection screen.

- Bigger radius does **not** mean a bigger playing field.
- The active gameplay chunk stays **120° relative to the 90° FOV** regardless of radius.
- So at radius larger than "normal", seeing the whole chunk **requires moving the camera**.

### Clickable elements
All clickable gameplay elements **rotate to align to the camera** (billboard toward the
viewer), so they stay readable from any angle.

---

## 3. Game rules / camera modes (switchable mid-map)

All camera motion below is described **relative to the surrounding space**. Modes can switch
during a map as a choreographed effect and/or gameplay change.

### 3a. Sphere projection (the default "3D" mode) — *replaces current cylinder*
- osu! playfield wrapped onto a **120°×90° sphere chunk** around the player.
- The chunk can **slide across the sphere**:
  - small steady drift, or
  - quick large-angle, curve-driven transitions (mostly visual effect, not a gameplay
    change).
- Player looks around within the chunk; elements billboard to camera.

### 3b. 2D mode (orthographic)
- Classic flat osu! plane, **orthographic** camera.
- Camera **zoom varies** from showing 100% of the screen down to a specific rectangle,
  driven by gameplay conditions:
  - **Streams** (clicks with equal timing gaps and close spacing detected) → **closer zoom**,
    camera **follows** the stream.
  - **Spinners** → smooth **zoom-in**.
  - Otherwise → **fixed camera** locked to a rectangle computed to cover the next click group.

### 3c. Falling mode
- Camera points **straight up or straight down**; **perspective** projection but keeps the
  **default osu! flat-plane** projection of gameplay (gameplay is NOT wrapped on a sphere
  here).
- Zoom ~**90%**, plus slight **cursor-following** to emulate a hand-held camera (future ref).
- **Defining trait:** instead of projecting gameplay onto a sphere, we project the **camera**
  onto a sphere above/below the plane. Target feel: **you** are the one moving above the
  screen.
- Math sketch: draw a normal vector from the center of the gameplay plane with length =
  sphere radius; increase the angle between it and the camera-view vector (which points at
  the sphere center) as a function of the **cursor's distance from the screen center**.

---

## 4. Key constraints (design guardrails)

### Readability (take cues from osu! / osu!lazer)
- Lines/arrows pointing toward the next clicks when they're far enough apart.
- **Fast disappearance** of clicked objects.
- Clear click response: **sounds / animations / cursor reactions**.

### Minimize cursor drift from camera changes
- Only move the cursor **on transitions**, keeping its relative position **as still as
  possible**.
- **Zoom only between clicks.** Keep cursor position clear across zooms and transitions.

---



## 5. Environment, choreography & authoring

The camera modes (§3) are the mechanics. This section is the **show** they perform inside,
and **how a map decides what happens when**.

### 5.1 Atmosphere / visual language
- **Reference:** *Alto's Odyssey* — low-poly, pastel, minimalist; **mostly grey** with extra
  color pulled **live from the beatmap** (combo colors → scene tint). Peak / TABS are weaker
  secondary refs. Goal is *atmosphere*, even when the beatmap's own music/feel fully overrides it.
- **World fiction:** you stand on a pillar high in the sky, floor of simple grey cubism, a
  clear **day/night-cycle** sky above, clouds around/below the tower. Later: lighting passes.
- Built with the procedural/low-poly fallback already in the pipeline (built-in RP). No
  external art required to prototype.
- **Impact frames:** noted, **not built** — stutter fights rhythm-game timing feel.

### 5.2 The choreographed arc (illustrative, NOT a fixed script)
A representative run: ortho opener while the music sets up → effects ramp with intensity
(wind, lingering click particles, cursor cross-lines) → **drop**: title card + floor breaks →
**Falling/Sphere** mode, circles appear, tower interior with holes to the sky outside → clouds
approach (**must NOT reduce visibility** — clouds sit around/below, camera stays in clear air) →
**Falling mode** where decorations sweep close for a *dodging* feel → song ends → **fade to
black + score**.
- This arc is **one possible output** of the authoring system below — **not** hardcoded.
  Real maps vary wildly in length and structure; a fixed 5-stage script would fit most maps
  badly. The arc emerges from markers + segmentation, per map.
- **Falling-mode decorations are pure VE:** they never obstruct gameplay. **Circles render on
  top of everything.** The "dodge" is camera motion + peripheral geometry, never anything
  crossing the read path. No damage; destruction-on-crush is a future idea.

### 5.3 Authoring: one merged event track, three sources
Modes and visual effects are driven by a single **event track** (each event: `time`, `type`,
`param` — where `type` is a mode-switch or a VE toggle/level). The track is assembled by
**merging three sources, highest priority first; each fills what the previous left blank**:

1. **Game-native markers** — authored in Fallcall's own editor (§5.5). Includes mode segments
   and VE markers. Highest authority.
2. **Beatmap-author markers** — signals the mapper already left: **kiai, SV changes, combo
   colors, breaks**. Usually sparse (often 1–2 kiai for a whole map, frequently **none** —
   kiai is *not* assumed present). Expanded into fuller structure via the algorithmic pool.
3. **Generated from song** — for everything still unmarked (§5.4).

Gameplay reads the **merged** track and does not care which source authored each event.

### 5.4 Generated tier: segmentation + constrained mode pick
- **Segment** the map at boundaries = breaks ∪ large note-density changes ∪ (kiai edges when
  present). Then **weighted-pick a mode per segment**.
- **Not pure random** — picks run under constraints: minimum segment length per mode (no
  Falling in a 2 s window), opener rules, no rapid mode ping-pong. The constraint layer is the
  real design work; the RNG is trivial.
- **Two seed modes:**
  - **Rated** — seed derived from the **beatmap MD5 hash** (stable, unique per difficulty,
    offline-derivable) → identical choreography for everyone.
  - **Free** — user-entered/regenerated seed, overrides the hash.

### 5.5 Editor (its own later wave)
Timeline-based, in-engine. Scope: play the **song** and the **circles**; **scripted autoplay**
(perfect follow + click) to watch camera/cursor drift against §4; a **VE-marker track** (toggle
effects on/off/level from a side panel); a **mode-marker track** (incl. choosing to start on the
floor for scripted openers). **No click/hit-object editing in v1.**

### 5.6 Authored data lives in a **sidecar**, never in the `.osu`
Fallcall data is written to a sidecar keyed by the beatmap MD5 hash (e.g.
`<hash>.fallcall`), **not appended to the `.osu`**. Appending would change the map's hash —
which breaks the rated seed (hash-keyed), re-import/dedup, and corrupts the user's original
beatmap. Sidecar keeps the original untouched and the hash stable.

### 5.7 Pacing helpers already present in `.osu`
- **Skip intro:** derive from first-hit-object time − `AudioLeadIn` (no authoring needed).
- **Rest / break timer:** `.osu` `[Events]` **breaks are already parsed** (`BreakPeriod`);
  show a countdown during them. Auto-fallback: treat any gap > ~3 s as a rest when the map
  declares no break. Not yet wired to gameplay/VE.

---

## 6. Code architecture (namespaces under `Assets/Scripts`)

Full per-file table: **`INDEX.md`** (auto-generated).
Performance playbook (pooling, batching, profiling): **`docs/OPTIMIZATION.md`**.
osu! faithfulness / leniency spec (where gameplay must match real osu!): **`docs/osu-leniency.md`**.

- **`Beatmaps/`** — pure osu! data + math. `.osu` parsing, difficulty formulas, slider
  geometry, timing. No Unity scene dependency beyond `Vector2/3`.
- **`Gameplay/`** — the runtime: `Bootstrap` (entry) → `GameManager` (session driver) →
  `Playfield` (osu→world projection, **the file the sphere work lives in**), `GameClock`,
  `CursorController`, `FirstPersonCamera`, scoring, settings, hit sounds, asset/osz loading.
- **`Visual/`** — drawables (`DrawableHitObject` base + circle/slider/spinner), floating
  judgements, shared resources.
- **`Skinning/`** — osu! skin loading (`skin.ini`, `@2x` sprites, number fonts) with
  procedural fallback.
- **`Util/`** — dependency-light helpers: archive extraction, WAV decode, procedural
  textures/materials.

### Projection is centralized
Every drawable routes its positions through `Playfield.ToWorld` and orients via
`Playfield.OrientationAt`. **This is the key seam:** the cylinder→sphere change lives almost
entirely in `Playfield.cs` because gameplay never touches world coordinates directly.

---

## 7. Settings surface

- `Osu3DSettings` — scene inspector component (per-scene tuning; edit + press **R** to
  restart a session and apply).
- `GameSettings` — runtime, persisted via `PlayerPrefs`; pause menu edits these live.

Because the whole design is "everything is a setting for testing", new projection/camera/zoom
parameters should be added to these two surfaces rather than hardcoded.

---

## 8. UI & game-feel vision

Full principles + the new-element checklist live in **`docs/UI-DESIGN.md`**. The vision in
brief:

- **Gameplay first.** UI, HUD, and effects never obstruct the playfield, add clutter, or move
  the camera. In 3D this is stricter: UI is **screen-space** (never on the sphere with hit
  objects) and the **center of the screen is sacred** — HUD hugs the edges. Mode switches fade,
  never pop. The player is never locked out of control (pause is the only interruption).
- **Own identity, borrowed behavior.** osu!lazer is the reference for *how UI should behave* —
  we adopt its interaction **principles** (settings openable anywhere, live-apply, search-as-
  you-type, card carousel, per-setting reset, keyboard-first). We do **not** copy its **look**:
  Fallcall has its own visual language built to express the falling-geometric-space theme.
  Form is ours; proven function is fair game. (This form-vs-function split is `UI-DESIGN.md` §0.)
- **Contrast, single-function clarity, persistent navigation, keyboard-first accessibility** are
  the other pillars — osu! is often played mouseless, so full keyboard operation is required.
- Every UI tunable is a **setting** (§7), same testing-first rule as the rest of the project.
