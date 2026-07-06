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

_Last updated: 2026-07-06_

---

## Continue-the-work protocol

When a human says **"continue the work"**, do this in order. **Do not skip the preflight —
it exists to stop false starts.**

### 1. Preflight check (report before doing anything)

1. Read `STRUCTURE.md` (intent), this file (board), `INDEX.md` (file map).
2. Run `git status` and `git log --oneline -8`. Compare reality against **Current state**
   and the board below.
3. Report to the human, in a short block:
   - which blocks are `DONE` / `IN-PROGRESS` / `BLOCKED` / `TODO`;
   - any `IN-PROGRESS` task whose owner claim looks **stale** (claimed but no matching
     uncommitted/committed changes) — flag it, don't silently steal it;
   - any `TODO` whose dependencies are **now satisfied** (newly startable);
   - any drift (board says X done, git disagrees).
4. **Stop and pick a task** (step 2). If nothing is startable, say so and ask the human.

### 2. Pick a task (false-start guards)

Choose the highest-priority `TODO` block that passes **all** guards:

- **Dependency guard:** every block listed in its `Deps` column is `DONE`.
- **Overlap guard:** none of the files in its `Owns` set intersect the `Owns` set of any
  `IN-PROGRESS` block. (If they do, the two would collide — wait or pick another.)
- **State guard:** Current state matches git. If not, report and stop.

If two blocks are both startable and don't overlap, either is fine — prefer lower ID.

### 3. Claim it

Edit **only this file**: set the block's status to `IN-PROGRESS (<who>, <date>)` and add a
one-line entry to the **Activity log**. Keep the edit tiny and localized (one row + one log
line) so concurrent sessions don't merge-conflict. Then start working.

### 4. Finish it

- Verify against the block's **Done when** criteria.
- Move finished subtasks to the **Done log**, set block status `DONE (<date>)`, and update
  **Current state**.
- If you edited any script header / `// INDEX:` marker, regenerate:
  `powershell -ExecutionPolicy Bypass -File Tools/gen-index.ps1`.
- Any block that was `BLOCKED` only by this one is now startable — note it in the log.

---

## Current state

- osu!standard gameplay engine implemented and playable: `.osu`/`.osz` parsing, timing,
  circles/sliders/spinners, scoring/HP, hit sounds, skins, procedural-art fallback.
- Presentation is **cylinder** projection (`Playfield.cs`, `Curved=true`) + first-person
  mouse-look (`FirstPersonCamera.cs`). Flat 2D plane exists as a fallback path.
- Minimal IMGUI menu: `Bootstrap` scans `.osz`, difficulty picker, pause menu.
- Settings split across `Osu3DSettings` (scene) and `GameSettings` (persisted).
- Docs scaffold: `INDEX.md` (auto-gen), `STRUCTURE.md` (vision), this file, `CLAUDE.md`.
- **No block claimed yet.** Board below is the starting state.

---

## Task board

Priority top-to-bottom. Statuses: `TODO` · `IN-PROGRESS (who, date)` · `BLOCKED (by X)` ·
`DONE (date)`. `Deps` = blocks that must be `DONE` first. `Owns` = files this block may
edit (overlap guard uses this).

| ID | Block | Status | Deps | Owns (primary files) |
| --- | --- | --- | --- | --- |
| **R1** | Optimization research → `docs/OPTIMIZATION.md` | TODO | — | `docs/OPTIMIZATION.md` only |
| **R2** | osu! leniency/faithfulness research → `docs/osu-leniency.md` | TODO | — | `docs/osu-leniency.md` only |
| **A** | Cylinder → sphere projection + camera + modes | TODO | — | `Playfield.cs`, `FirstPersonCamera.cs`, camera/mode scripts (new), `Osu3DSettings.cs` |
| **C** | Gameplay leniency fixes (match og osu!) | TODO | R2 | `GameManager.cs`, `ScoreProcessor.cs`, `SliderObject.cs`, `SpinnerObject.cs`, `CursorController.cs`, `GameSettings.cs` |
| **D** | Auto beatmap loader (download / cache / save) | TODO | — | `AssetLoader.cs`, `OszImporter.cs`, new `BeatmapLibrary.cs` + downloader |
| **B** | Song selection UI (osu!lazer style) | BLOCKED (by D) | D | `Bootstrap.cs`, new song-select UI scripts |
| **E** | Video playback during gameplay | TODO | — | new `VideoPlayback.cs`, `GameManager.cs` (spawn hook), `AssetLoader.cs` (coordinate w/ D) |

**Overlap notes (read before claiming):**
- **R1, R2** touch docs only → safe to run alongside anything.
- **A** owns `Playfield`/camera; **C** owns the drawables' scoring/input. They only brush on
  `SliderObject`/`SpinnerObject`: A must **not** edit scoring/combo logic there; C must
  **not** edit world-projection there. If both need the file, serialize (whoever is
  `IN-PROGRESS` wins; the other waits).
- **D** and **E** both touch `AssetLoader.cs`. Whoever claims first owns it; the other adds
  a small hook and coordinates via the Activity log.
- **B** consumes the library API that **D** produces → hard dep. Do not start B until D
  lands `BeatmapLibrary` (its listing/scan/download-status surface).

### Dependency graph

```
R1  (docs)            ── independent, do anytime
R2  (docs) ───┐
              └─▶ C   (leniency fixes use R2 findings)
A   (projection)      ── independent
D   (beatmap loader) ─▶ B   (song select needs library API)
E   (video)           ── independent (coordinate AssetLoader with D)
```

Startable **right now** (no unmet deps): **R1, R2, A, D, E**. **C** unlocks when R2 is
`DONE`; **B** unlocks when D is `DONE`.

---

## Blocks (detail)

### R1 — Optimization research → `docs/OPTIMIZATION.md`
Research and document **how to optimize this game**, so later blocks follow one playbook
instead of each reinventing it. Deliverable is a doc, no code.
Cover at least: object **pooling** (hit objects spawn/despawn every second); replacing
many per-object `MonoBehaviour`s with a **manager / batched update** (avoid N `Update()`
calls); **draw-call batching** (static/dynamic batching, **SRP batcher**, **GPU
instancing**, texture atlasing for skin sprites); mesh reuse for sliders; `MaterialFactory`
sharing vs. per-object materials; struct/Job/Burst options where relevant; profiling
workflow (Unity Profiler, Frame Debugger). For each: what it is, when it applies **here**,
and the concrete change to make.
**Done when:** `docs/OPTIMIZATION.md` exists with the above, each item tied to a real file
in this project, and it's linked from `STRUCTURE.md` §6 and `CLAUDE.md`.

### R2 — osu! leniency / faithfulness research → `docs/osu-leniency.md`
Research **where our gameplay diverges from real osu!/osu!lazer** so block C has a spec.
Cover: slider judgement (head/tick/end, **slider-end miss does NOT break combo — it just
doesn't add to it**), follow-circle leniency, **note-lock** ordering, **spinner** scoring &
combo (current combo is bugged), hit-window / OD mapping, and **cursor size / hitbox**
(og osu! forgiveness; we want a bigger cursor with an adjustable hitbox as a setting).
Cite osu!lazer behavior where possible.
**Done when:** `docs/osu-leniency.md` lists each divergence as *current vs. correct* with
the fix location, ready for C to implement. Link it from `STRUCTURE.md` and `CLAUDE.md`.

### A — Cylinder → sphere projection + camera + modes
The existing projection roadmap. Route stays through `Playfield.ToWorld` / `OrientationAt`.
1. Convert `Playfield` to wrap the playfield onto a **120°×90° sphere chunk** (was
   cylinder). Keep flat mode working. Expose chunk H/V degrees + radius as settings.
2. Camera baseline for sphere: free horizontal look, vertical clamp ±90°, FOV 90. Confirm
   "radius as a parameter" semantics (see `STRUCTURE.md` §2).
3. Mode system: switchable camera/projection enum (Sphere / 2D-ortho / Falling), changeable
   mid-map. Start Sphere + 2D-ortho.
4. 2D-ortho zoom logic: stream detect (equal gaps + close spacing) → follow+zoom; spinner →
   smooth zoom-in; else fixed rect covering next click group. Zoom only between clicks.
5. Falling mode: project the *camera* onto a sphere above/below the flat plane
   (`STRUCTURE.md` §3c).
6. Visual "fall" backdrop: fast geometric 3D space for spectacle.
**Done when:** sphere is default, flat still works, mode enum switches at runtime, all new
knobs are settings. (Large block — split into commits; update this list as subtasks land.)

### C — Gameplay leniency fixes  _(needs R2)_
Implement the divergences R2 documents. Known targets:
- Slider **end** miss must **not break combo** (currently does / mis-scores).
- **Spinner** combo bug — fix combo accounting.
- **Cursor**: bigger cursor + **adjustable hitbox** as a `GameSettings` value.
- Sweep the rest of R2's list (note-lock, follow circle, hit windows) as found.
**Done when:** each item in R2 is either fixed or explicitly deferred with a note here.

### D — Auto beatmap loader (download / cache / save)
Loading, **saving**, **caching**, and **downloading** osu! beatmaps. Produce a
`BeatmapLibrary` that scans local `.osz`, caches extracted maps, and downloads new ones
(mirror/API TBD — see Open questions). Expose a clean **listing API** for block B.
**Done when:** library scans + caches local maps, can download a map by id/set, and exposes
a stable listing/status API. Note the API shape here so B can build against it.

### B — Song selection UI (osu!lazer style)  _(needs D)_
Replace the minimal IMGUI difficulty picker with an osu!lazer-style song select (carousel
list, search/sort, difficulty panel, background). Consume D's `BeatmapLibrary` API.
**Done when:** song select browses the library, picks a difficulty, and starts a session;
old picker path removed or gated.

### E — Video playback during gameplay
Some beatmaps ship a background video (`Video` event in `.osu`). Play it behind gameplay
via Unity `VideoPlayer`, synced to `GameClock`. Coordinate `AssetLoader` changes with D.
**Done when:** maps with a video event play it in sync (with a setting to disable), no
regression for maps without video.

---

## Open questions / decisions to confirm with human

- **A:** default sphere radius + chunk degrees for "normal" feel; mode-switch trigger source
  (authored in beatmap / heuristic / manual toggle); stream-detection thresholds.
- **D:** _Decided 2026-07-06 → use a **mirror (nerinyan/catboy)**, no auth/API key,
  download `.osz` by set id._ Still open: on-disk cache location.
- **B:** _Decided 2026-07-06 → **uGUI** (Canvas) for the song-select UI._ Still open: how
  faithful to lazer's carousel (exact vs. simplified).
- **C:** default cursor size + hitbox range values.

## Tips for following agents

- Don't touch drawable world math directly — go through `Playfield`. Keeps gameplay
  projection-agnostic.
- `Osu3DSettings` values apply on session (re)start; press **R** in play mode to re-apply.
- Keep new tunables as **settings** (testing-first design), not hardcoded constants.
- Unity 2022, built-in render pipeline; no external art assets required (procedural fallback
  via `Util/TextureFactory`).
- After editing any script header/`// INDEX:` marker, run the gen-index script.

## Activity log

_One line per claim/finish so parallel sessions can see who's on what. Newest first._

- 2026-07-06 — Board created; blocks R1/R2/A/C/D/B/E defined with deps & file ownership.

## Done log

- 2026-07-06 — Restructured `PLAN.md` into a parallel-agent task board (claim protocol,
  preflight check, dependency graph, file-ownership overlap guards). Added blocks for
  optimization research, osu! leniency research + fixes, beatmap loader, song-select UI,
  video playback.
- 2026-07-06 — Added docs scaffold: `INDEX.md` + `Tools/gen-index.ps1` generator,
  `STRUCTURE.md`, `PLAN.md`, `CLAUDE.md`. Added `// INDEX:` header markers to weak scripts.
- Prior — osu!standard engine, cylinder projection, first-person camera, minimal menu,
  settings + pause menu, skins, hit sounds. (git: `f7a629a`, `fe2c8df`, `8df3e2e`,
  `eeb8f1d`, `d5a972f`.)
