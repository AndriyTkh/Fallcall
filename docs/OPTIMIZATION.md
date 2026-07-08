# OPTIMIZATION

> How to optimize Fallcall. One playbook so later blocks don't each reinvent it.
> Every item below is tied to a **real file** in this repo, says **when it applies here**,
> and gives the **concrete change** to make. Read `STRUCTURE.md` for intent and `INDEX.md`
> for the file map first.

**Golden rule for this project:** hit objects spawn and die every second, and each one is a
small tree of `GameObject`s + `SpriteRenderer`s / `LineRenderer`s / `TextMesh`es. The two
biggest wins are (1) **pooling** those trees instead of `new`/`Destroy`, and (2) letting the
renderer **batch** them instead of issuing one draw call per sprite. Everything else is
secondary.

---

## 0. Measure first (profiling workflow)

Never optimize blind. Before and after every change below, capture numbers.

- **Unity Profiler** (`Window ▸ Analysis ▸ Profiler`, or `Ctrl+7`): watch the **CPU** track
  (look for `GC.Alloc` spikes on every spawn — see §1) and the **Rendering** track
  (**Batches** / **SetPass calls** — see §3). Enable **Deep Profile** only for short bursts;
  it distorts timings.
- **Frame Debugger** (`Window ▸ Analysis ▸ Frame Debugger`): step draw calls to see *why*
  two sprites didn't batch (it names the break reason: different material, texture, or
  sorting order — the last is our main offender, §3).
- **Rendering Statistics** overlay (Game view ▸ Stats): quick Batches / Saved-by-batching
  read without opening the profiler.
- What "good" looks like here: spawning a dense stream should produce **near-zero per-frame
  `GC.Alloc`**, and a full playfield of circles/sliders should render in **tens** of batches,
  not hundreds.

Target the profiler at a **dense stream section** of a real map — that's the worst case this
engine hits.

---

## 1. Object pooling  *(highest priority)*

**What it is.** Reuse a fixed set of hit-object instances (and their child renderers) instead
of allocating and destroying them.

**Why it matters here.** In [`GameManager.Spawn`](../Assets/Scripts/Gameplay/GameManager.cs)
every hit object does `new GameObject(...)` + `AddComponent<…Object>()`, and each drawable's
`Init` then builds **several more** child `GameObject`s:
- [`HitCircleObject.Init`](../Assets/Scripts/Visual/HitCircleObject.cs) → body + overlay +
  approach + a `SkinNumber` (one child per digit).
- [`SliderObject.Init`](../Assets/Scripts/Visual/SliderObject.cs) → 2 `LineRenderer`s, head,
  overlay, approach, tail, follow, number, one dot per tick, reverse arrows.
- [`SpinnerObject.Init`](../Assets/Scripts/Visual/SpinnerObject.cs) → bg, disc, ring, rotor,
  clear, a `TextMesh`.

On cull, [`GameManager.Update`](../Assets/Scripts/Gameplay/GameManager.cs) calls
`Destroy(d.gameObject)`. A stream can spawn/destroy tens of these per second → constant
`GC.Alloc`, `Destroy` overhead, and GC spikes that show as frame hitches.

**Concrete change.**
1. Add a small pool keyed by drawable type (circle / slider / spinner). `Spawn` rents from the
   pool and initializes; the cull path calls a new `Despawn()` that `SetActive(false)` and
   returns the instance instead of `Destroy`.
2. Make each drawable **re-initializable**: today `Init` *builds* children. Split it into a
   one-time `BuildOnce()` (create the child renderers) and a per-use `Reset(HitObject, ctx)`
   (reposition, recolour, re-enable, reset state fields). Sliders are the hard case — the
   `LineRenderer` point count and tick-dot count vary per object; grow the child lists to the
   max seen and disable the unused tail rather than destroying.
3. Pre-warm the pool at session start in `StartGame` (a handful of each type covers the
   on-screen max, which is bounded by preempt window × density).

**Payoff / cost.** Removes nearly all gameplay-time GC. Highest effort of the list because it
touches the drawables' lifecycle, but also the highest return. Do this **before** the sphere
work (block A) piles more per-object cost on.

Also pool [`FloatingText`](../Assets/Scripts/Visual/FloatingText.cs) — it does
`new GameObject` + `AddComponent` **per judgement** (i.e. per click) and `Destroy`s itself in
`Update`. Same pattern, same fix.

---

## 2. One manager updating many objects (avoid N × `Update()`)

**What it is.** A single driver loop ticks all live objects, instead of each object owning a
Unity `Update()` (each magic `Update` has managed↔native call overhead, and hundreds add up).

**Where we already do this (keep it).** [`GameManager.Update`](../Assets/Scripts/Gameplay/GameManager.cs)
already iterates `_active` and calls `d.Tick(time, isFront)`.
[`DrawableHitObject`](../Assets/Scripts/Visual/DrawableHitObject.cs) has **no** `Update` —
this is the right pattern and must be preserved as blocks A/C add behaviour. Do **not** add
`Update()` to drawables; extend `Tick` instead.

**Where we violate it (fix).** [`FloatingText`](../Assets/Scripts/Visual/FloatingText.cs) has
its own `Update()`. Because these spawn once per click, a fast section can have many running
at once. When you pool them (§1), also drive them from a manager tick (e.g. a
`FloatingTextManager` that `GameManager` ticks) rather than per-instance `Update`.

**Concrete change.** Route every per-frame animation through the existing `GameManager` tick.
`GameManager.Update` already holds the authoritative `time` from `GameClock`; prefer passing
that down over each object reading `Time.deltaTime` independently (which also keeps animations
correct across pause/seek — see [`GameClock`](../Assets/Scripts/Gameplay/GameClock.cs)).
Note [`SpinnerObject.Tick`](../Assets/Scripts/Visual/SpinnerObject.cs) already reads
`Time.deltaTime` for rotor spin — acceptable inside the manager tick, but it won't freeze on
pause the way clock-driven animation does; convert if pause-correctness matters.

---

## 3. Draw-call batching  *(second priority)*

**What it is.** Let Unity merge many renderers into few draw calls. Four levers apply here.

### 3a. Sorting-order fragmentation — the main batch-killer
Every drawable sets `sortingOrder = DepthOrder * 10 + offset`
([`HitCircleObject`](../Assets/Scripts/Visual/HitCircleObject.cs),
[`SliderObject`](../Assets/Scripts/Visual/SliderObject.cs),
[`SpinnerObject`](../Assets/Scripts/Visual/SpinnerObject.cs), and
[`DrawableHitObject.AddSprite`](../Assets/Scripts/Visual/DrawableHitObject.cs)). Since
`DepthOrder = HitObjects.Count - index` (set in `GameManager.Spawn`), **every object gets a
unique sorting band**, so no two objects' sprites can batch even when they share a texture and
material. This is the first thing the Frame Debugger will flag.

**Concrete change.** Collapse the sort space. Objects only need correct ordering *relative to
their few on-screen neighbours*, not a globally unique band:
- Give each drawable a small fixed **internal** offset table (body=0, overlay=1, approach=3…)
  and derive the **base** from a coarse bucket (e.g. `depth % N`) so far-apart objects reuse
  bands and can batch. On-screen objects at the same instant are few (bounded by preempt), so
  a handful of buckets keeps visual layering correct while letting most sprites share an order.

### 3b. SRP batcher / dynamic batching
Built-in pipeline (per `CLAUDE.md`) → the **SRP Batcher** is not available; rely on **dynamic
batching** (small meshes, **same material**). Static batching doesn't apply — everything moves.

- Sprites already share the default `Sprites/Default` material via
  [`SkinSprites`](../Assets/Scripts/Skinning/SkinSprites.cs) + Unity's sprite renderer, so once
  §3a and §3d are done they batch.
- Line meshes already share one material — see §3c.

### 3c. `MaterialFactory` sharing (keep + extend)
[`MaterialFactory.UnlitTransparent`](../Assets/Scripts/Util/MaterialFactory.cs) hands every
slider `LineRenderer` **one shared** material instance — good: shared materials are a
precondition for batching and for the frame debugger to consider merging them. **Rule:** never
create a `new Material(...)` per object; add any future shared material as another cached
static on `MaterialFactory`. Set per-object colour via the renderer's vertex colour (as
sliders do through `LineRenderer.startColor/endColor` in
[`SliderObject`](../Assets/Scripts/Visual/SliderObject.cs)) to keep the material shared;
touching `renderer.material.color` would silently **clone** the material per object — avoid.

### 3d. Texture atlasing for skin sprites + GPU instancing
Two renderers batch only if they also share a **texture**.
- The procedural art ([`TextureFactory`](../Assets/Scripts/Util/TextureFactory.cs)) is only 3
  textures (Disc / Ring / SoftRing), all shared statics — already good.
- **Skin** sprites ([`Skin`](../Assets/Scripts/Skinning/Skin.cs) via `SkinSprites`) load as
  **separate textures per element** (osu! skins ship loose PNGs). Combine gameplay elements
  (hitcircle, overlay, approachcircle, numbers, follow, reversearrow) into a runtime **Sprite
  Atlas** / packed `Texture2D` at skin-load time so a whole circle+number draws from one page.
- **GPU instancing** helps when many identical meshes share a material; it's a fallback if
  sorting-order collapse (§3a) can't be made clean, but atlas + shared material + collapsed
  sort orders is the simpler path for 2D sprites here.

---

## 4. Mesh reuse for sliders

**What it is.** Avoid rebuilding slider geometry more than once, and avoid per-frame mesh
churn.

**Why here.** [`SliderObject.NewLine`](../Assets/Scripts/Visual/SliderObject.cs) creates a
`LineRenderer` and writes every point through `Ctx.Playfield.ToWorld(...)` at spawn. Each
`LineRenderer` internally bakes a mesh. The point count is bounded because
[`SliderPath`](../Assets/Scripts/Beatmaps/SliderPath.cs) resamples to a fixed length — good.
The costs are: (a) the `ToWorld` transform of all points on every (re)spawn, and (b)
`LineRenderer`'s cap/corner vertex generation (currently `numCapVertices = 8`,
`numCornerVertices = 4`).

**Concrete change.**
- When pooling (§1), keep the `LineRenderer` and only **rewrite positions** on reuse — don't
  destroy/recreate it.
- The slider body doesn't animate its shape (only alpha/width), so its world points are
  **static after Init**. It already computes them once in `NewLine`; preserve that — don't move
  it to per-frame.
- If `LineRenderer` cap/corner tessellation shows up in the profiler, drop the cap/corner
  vertex counts or switch to a prebuilt `Mesh` + `MeshRenderer` with the shared material.

**Block A note:** the cylinder→sphere change routes through `Playfield.ToWorld`. Keep slider
world-point computation **one-shot at spawn**; do not make projection per-frame or slider cost
explodes.

---

## 5. Startup: procedural texture generation

[`TextureFactory`](../Assets/Scripts/Util/TextureFactory.cs) builds each 256×256 sprite with a
per-pixel `SetPixel` loop (~65k calls × 3, in `BuildDisc`/`BuildRing`). One-time, but it stalls
the first frame.

**Concrete change (low priority):** build into a `Color32[]` and call `SetPixels32` once
(single native upload) instead of `SetPixel` per texel. Only worth doing if startup hitch is
visible in the profiler; it doesn't affect gameplay frame rate.

---

## 6. GC hygiene in the per-frame paths

Small, steady allocations also cause GC. Watch these:
- **IMGUI HUD.** [`GameManager.OnGUI`](../Assets/Scripts/Gameplay/GameManager.cs) runs every
  frame (twice — layout + repaint) and builds interpolated strings (`$"{_score.Score:n0}"`,
  `$"{_score.Combo}x"`, …), each allocating. For a HUD that changes rarely, cache the strings
  and rebuild only when the value changes, or move the HUD off IMGUI (uGUI `Text`/`TMP`)
  during the eventual UI work (block B). IMGUI is fine for menus, not for a per-frame HUD.
- **Spinner text.** `_info.text = $"{(int)(progress*100)}%"` in
  [`SpinnerObject.Tick`](../Assets/Scripts/Visual/SpinnerObject.cs) allocates every frame while
  spinning; only reassign when the integer percent changes.
- Avoid LINQ / capturing lambdas in `Tick` paths.

---

## 7. Structs / Jobs / Burst — where (and where not)

- **Not needed for hit-object logic.** The object count on screen is small (bounded by the
  preempt window); a plain managed tick loop is fine. Don't Job-ify `Tick`.
- **Candidate:** the eventual **visual "fall" backdrop** (block A, `STRUCTURE.md` §3/§5) — if
  it becomes thousands of moving geometric elements, that's the right place for an
  `IJobParallelFor` + Burst transform update feeding a single `Graphics.DrawMeshInstanced`
  call, bypassing `GameObject`s entirely.
- **Candidate:** slider path resampling in [`SliderPath`](../Assets/Scripts/Beatmaps/SliderPath.cs)
  if map-load time becomes a problem — it's pure math over arrays, Burst-friendly. Measure
  first; today it's a load-time cost, not a frame-time one.

---

## Priority summary

| # | Change | Effort | Payoff | When |
| --- | --- | --- | --- | --- |
| 1 | Pool drawables + `FloatingText` (§1) | High | **Huge** (kills gameplay GC) | Before block A |
| 2 | Collapse sorting-order bands (§3a) | Low | **High** (unlocks batching) | Anytime |
| 3 | Atlas skin sprites (§3d) | Med | High (fewer textures) | With skin/UI work |
| 4 | Keep manager-tick, fix `FloatingText` Update (§2) | Low | Med | With §1 |
| 5 | Slider positions one-shot, keep shared line material (§3c/§4) | Low | Med | Guard during block A |
| 6 | HUD/spinner string alloc (§6) | Low | Low–Med | With block B (uGUI HUD) |
| 7 | Startup `SetPixels32`, Jobs for backdrop (§5/§7) | Low/High | Situational | Only if profiler flags |

**Do §1 and §2 (sorting) first** — together they remove the two costs that scale with note
density, which is exactly where this engine will be pushed.
