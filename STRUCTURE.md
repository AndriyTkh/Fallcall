# STRUCTURE

> **Human-curated.** This is the source-of-truth vision doc. Agents use/edit it. This is the document that is viewed by humans the most.
> edit it. When code and this file disagree about *intent*, this file wins — update the
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

## 5. Current implementation vs. vision

| Area | Now | Vision |
| --- | --- | --- |
| Projection surface | **Cylinder** wall (`Playfield.cs`, `Curved`) | **Sphere** 120°×90° chunk |
| Camera | First-person mouse-look, clamped to wall extents | Free horizontal, ±90° vertical, FOV 90 |
| Modes | Cylinder only (+ flat 2D fallback) | Sphere / 2D-ortho / Falling, switchable mid-map |
| Chunk motion | Static | Sliding + curve-driven transitions |
| Zoom logic | None | Stream/spinner/group-aware ortho zoom |
| Element facing | Billboard to camera (partial) | Full camera-aligned |
| Falling mode | Not started | Camera-on-sphere projection |
| Visual "fall" | Not started | Fast geometric space backdrop |

See `PLAN.md` for concrete task tracking and next steps.

---

## 6. Code architecture (namespaces under `Assets/Scripts`)

Full per-file table: **`INDEX.md`** (auto-generated).

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
