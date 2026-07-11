# CLAUDE.md

Guidance for Claude Code (and any agent) working in this repo.

## What this project is

**Fallcall** — a Unity 3D reimagining of osu!standard. osu! gameplay is preserved and
projected into 3D space; the signature feel is falling through fast geometric space while
gameplay plays out on a sphere around the player. Read **`STRUCTURE.md`** for the full
design vision.

## Read these first (in order)

1. **`STRUCTURE.md`** — human-curated vision & end-state. Source of truth for *intent*.
2. **`PLAN.md`** — current progress + **the parallel-agent task board**. Read at session
   start, update at session end (context is reset often to save tokens).
3. **`INDEX.md`** — auto-generated per-file map of `Assets/Scripts`.
4. **`docs/OPTIMIZATION.md`** — how to optimize the game (pooling, managers, batching).
5. **`docs/osu-leniency.md`** — where gameplay must match real osu!/osu!lazer.
6. **`docs/UI-DESIGN.md`** — UI & game design principles. Binding for any menu, HUD, or
   interaction work; includes a checklist for new UI elements.

## "Continue the work" (parallel agents)

When a human says **"continue the work"**, `PLAN.md` is authoritative. Work is split into
**blocks**; multiple sessions run at once, each owning one block. **Follow the
Continue-the-work protocol at the top of `PLAN.md` before editing anything:**

1. **Preflight** — read the board, run `git status` + `git log`, and report block statuses,
   stale/claimed tasks, newly-unblocked tasks, and any drift. This report exists to
   **prevent false starts** — don't skip it.
2. **Pick** a `TODO` block that passes all guards: its `Deps` are `DONE`, its `Owns` files
   don't overlap any `IN-PROGRESS` block, and Current state matches git.
3. **Claim** it (set `IN-PROGRESS`, add an Activity-log line) — edit only that row/line so
   concurrent sessions don't merge-conflict.
4. **Finish** — verify "Done when", move to Done log, update Current state, note unblocked
   blocks. Never edit another block's owned files.

## Doc maintenance rules

- **`INDEX.md` is generated — never hand-edit it.** To change a file's description, edit
  that script's `/// <summary>` or add a `// INDEX: <text>` line in its first 40 lines
  (the `// INDEX:` marker wins over the summary). Then regenerate:
  ```
  powershell -ExecutionPolicy Bypass -File Tools/gen-index.ps1
  ```
  Run this whenever scripts are added/removed/renamed or a header changes.
- **`STRUCTURE.md`** — only edit to reflect a real change in the vision; flag drift, don't
  silently diverge.
- **`PLAN.md`** — keep current; move finished items to the Done log as one-liners; use
  absolute dates.

## Architecture at a glance

Namespaces under `Assets/Scripts` (details in `INDEX.md`):
`Beatmaps/` (osu! data + math) · `Gameplay/` (runtime, projection, camera, scoring) ·
`Visual/` (drawables) · `Skinning/` (osu! skins) · `Util/` (helpers).

**Key seam:** every drawable routes positions through `Playfield.ToWorld` /
`OrientationAt`. Projection changes (e.g. the pending cylinder→sphere conversion) live
almost entirely in `Assets/Scripts/Gameplay/Playfield.cs`. Do **not** bake world math into
drawables.

## Conventions

- Unity 2022, built-in render pipeline. No external art assets required — procedural
  fallback in `Util/TextureFactory`.
- Everything tunable is a **setting** (testing-first design): scene-level in `Osu3DSettings`,
  persisted/runtime in `GameSettings`. Add new tunables there, don't hardcode.
- `Osu3DSettings` applies on session (re)start — press **R** in play mode to re-apply.
- Entry point auto-spawns (`Bootstrap`), so pressing Play needs no scene wiring.

## Game & UI design

All UI/UX and game-feel decisions follow **`docs/UI-DESIGN.md`** (osu!lazer is the
reference client). Priority order when principles conflict: **gameplay first** (never
obstruct the playfield or move the camera from UI) → contrast/affordance → single-function
clarity → persistent navigation (toolbar + shortcut + main-screen route) → keyboard-only
operability. Every UI tunable is a setting; run the checklist at the end of that doc
before adding any UI element.

## How to run

Open the project in Unity 2022 and press Play. `Bootstrap` scans for `.osz` beatmaps,
shows a difficulty picker, and starts a session. (No headless/CI run path is set up.)
