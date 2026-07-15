# _VFX_PLAN — active effect

**One effect at a time.** When it lands: move the plan body to `_archive/<effect>.md`, add a
one-liner to the `_VFX.md` Done log, reset this file to the template at the bottom.

Durable knowledge belongs in `_VFX.md`, not here. This file is disposable.

---

## Current state (2026-07-15)

T0 is done and **verified in the Editor**: sky gradient + sun LUT, cloud layer, three noise bases
(Value / Worley / Erode) switchable live from **Cloud Controls** on the `T0 Sky` object.
`Fallcall > VFX > Setup T0 Sky Rig` builds the rig from nothing.

**T0.5 (Cloud extrude) was written and never verified, and the ladder has been cut short past it.**
Human call, 2026-07-15: skip the intermediate rungs and build the scattered-primitive version
directly. T0.5's code still compiles and still runs; treat it as **abandoned in place, not
finished**. Its plan body is at `_archive/T0.5_cloud-extrude.md` with the verification steps that
were never run. It is not a dependency of anything below.

**What survived from T0.5 and is load-bearing for T2:** `CloudField.cs` (bake + readback),
`CloudFieldBake.shader`, `CloudField.hlsl`, `SkyRamp.hlsl`. T2 consumes all four unchanged. That is
the "one field, many consumers" rule paying for itself — the rung got cut and its spine didn't.

**Debt still unpaid, carried from T0:** the per-basis frame cost was never measured.

---

## Effect: Cloud scattered primitives (T2)

**Status:** IN-PROGRESS — code written 2026-07-15. **Opened in the Editor 2026-07-15: setup runs,
boxes render, parallax reads.** Verify steps 1–3 pass. Knobs and cost (4–8) still unrun.

**Editor pass 1 found one real bug, now fixed and itself unverified:** boxes strobed while the camera
moved and took ~1s to come back when it stopped. Cause was `Graphics.DrawMeshInstanced` being called
from `Update()` — a per-frame submission on the wrong clock. Moved to
`RenderPipelineManager.beginCameraRendering`. Durable version in `_VFX.md`, *Two clocks*; it closed
the one-camera sorting open question as a side effect. **Re-run verify step 3.**

**Editor pass 2: opaque confirmed clean, and the fixed clock exposed a second bug underneath it.**
Transparent drew covered boxes on top while dollying. Cause was cross-batch draw order — order only
holds *inside* one `DrawMeshInstanced` call, and URP re-sorts transparent draw calls off each batch's
bounds centre, which goes meaningless once the far boxes wrap around the camera. Fixed with one
material per batch at `renderQueue = base + i`. `Count = 500` (one batch) vs `3000` (three) is what
split it from the unfixable interpenetration limit sitting under it. Durable version in `_VFX.md`,
*Your draw order stops at the batch boundary*. **Unverified — re-run verify step 7 at Count ≥ 2046.**

**Added on request, same pass:** `_CloudShadowLift` on `CloudControls` — pulls cloud shadow toward
cloud lit. Shared by all three cloud shaders via `SampleCloudShadow()`, not a T2 knob; see `_VFX.md`,
*The LUT contract*. **Unverified — new verify step 9.**
**Started:** 2026-07-15
**Goal:** clouds whose shape *is* the distribution of the primitives placed into them. Transparent
or opaque rectangles/boxes, scattered against the existing field, every shape/rotation/scale knob
exposed.

### Why this, and why it skipped the rungs

Human call: the intermediate tiers were teaching material, and the destination was already known —
`_VFX.md` had scattered boxes as *"the leading candidate… likely what ships"* before T0.5 was
written. Building T1 and finishing T0.5 to arrive somewhere already decided was the expensive way
to be right.

**This is the placement branch.** Every earlier tier turns a field into pixels: sample per view-ray
(T0), extrude into a grid (T0.5). T2 doesn't render the field at all — it uses it as a *spawn
probability* and throws primitives at it. What you see is where they landed.

### Files

| File | Role |
|---|---|
| `T2_Boxes/CloudBoxes.cs` | `[ExecuteAlways]`. Bake → rejection-sample → instanced draw. Every knob. Contains **no noise**. |
| `T2_Boxes/T2_CloudBox.shader` | Instanced, flat-shaded, lit off `ROW_CLOUD_LIT`/`ROW_CLOUD_SHADOW`. Blend state driven from C#. |
| `T2_Boxes/Editor/T2_BoxesSetup.cs` | `Fallcall > VFX > Setup T2 Cloud Boxes`. Builds the sky rig first if missing. |
| `T0_Sky/CloudExtrude.cs` | **Touched, minimally.** `WindWorldOffset` made `static` so T2 shares the one wind→metres conversion instead of copying it. No behaviour change. |

Nothing else was modified. T2 reads `CloudField`, `CloudFieldBake.shader`, `CloudField.hlsl` and
`SkyRamp.hlsl` exactly as T0.5 left them.

### Decisions worth not re-deriving

**Placement is rejection sampling, and that's the whole effect.** Throw a dart at the patch, ask the
field how cloudy it is there, keep the dart with that probability. No threshold pass, no occupancy
grid, no mesh. `CloudBoxes.Place` is ~30 lines and is the entire T2 idea.

**Three knobs do a noise function's job.** Rotation jitter kills the grid read (axis-aligned is *the*
Minecraft tell). Size-from-density is fbm done with geometry — big boxes carry mass in cores, small
ones fluff the edge. Count is coverage's second half: the field says *where*, count says *how
solidly*.

**Both the primitive and the surface are toggles, not a decision made in advance.** Box vs Quad and
Opaque vs Transparent are enums on the component, because the point of exposing them is to look at
them. The expected answer is still **Box + Opaque** — overlap is nearly free under early-Z, and
transparent pays full fill per layer *plus* needs sorting. Transparent's one real prize is that
overlapping primitives accumulate density for free (Beer's law by accident).

**Depth sorting serves both branches, in opposite directions.** Transparent must submit far→near or
the blend is wrong. Opaque wants near→far so early-Z has written the near depth before a buried box
rasterises. One sort, one sign flip.

**The field is bilinearly sampled, not per-cell.** Nearest-sampling the bake would fold the grid
back into the placement and reintroduce the exact voxel read the scatter exists to avoid — just at
bake resolution instead of cell size.

**Wind moves the frame, not the field** — same as T0.5, via the now-static
`CloudExtrude.WindWorldOffset`. **Two keys, not one** — field knobs re-bake, placement knobs only
re-place. Both rules inherited wholesale; see `_VFX.md`.

### Steps

- [x] Placement: bake → bilinear sample → density → rejection sample → instanced matrices
- [x] Every shape knob exposed (primitive, size edge/core, jitter, stretch, per-axis rotation)
- [x] Opaque + transparent, blend state driven from the component
- [x] Shade off the existing LUT rows — new tier adds a row, never a second LUT
- [x] Depth sort: far→near transparent, near→far opaque
- [x] Bake/place timings + placed/triangle/batch counts surfaced read-only in the inspector
- [ ] **Verify in Editor** — nothing above counts until this is ticked. Steps 1–3 pass; 4–8 unrun.
- [ ] Measure: frame cost vs Count, Box vs Quad, Opaque vs Transparent. Write the numbers down.

### Verify

`Fallcall > VFX > Setup T2 Cloud Boxes` (builds the sky rig too if it's missing).

**Steps 1–3 ran 2026-07-15 and passed** — setup built the rig, `Placed` > 0, boxes on screen and
parallaxing. Kept below because they are the diagnostic ladder for any future regression, and
because step 2's fix is now *seen*, not reasoned: the hand-built box winding is correct.

1. ✅ **Does anything appear?** `Placed` = 0 in the inspector ⇒ suspect the readback or a coverage
   threshold nothing clears, not the placement loop. `Placed` > 0 and nothing on screen ⇒ suspect
   instancing or the blend state, not the field.
2. ✅ **Box winding.** The box mesh is built by hand and its winding was reasoned about, not seen
   (`BuildBox`, the 0-2-1 / 0-3-2 comment). If boxes look inside-out or vanish, that is the first
   place to look — flip the two triangles back and the whole thing inverts.
3. ⚠️ **Move the camera.** Parallax is the deliverable, same as T0.5. Toggle `Enable Clouds` on Cloud
   Controls to A/B against T0's flat backdrop. *Passed for parallax, failed for strobing — the draw
   was on `Update`'s clock. Fixed; **re-run.** Boxes must stay solid while flying and while
   scroll-wheel dollying, in both Scene and Game view.*
4. **Rotation Jitter 0 → 12.** This is *the* knob. At 0 it must read as an obvious grid of aligned
   boxes; a few degrees should go chunky-organic. If it doesn't, the scatter isn't earning its cost.
5. **Scrub `Coverage`.** Should re-place but **never** re-bake — `Bake ms` must not move. If it
   does, the two-key split is broken and the readback is in the scrub path.
6. **Scrub `TimeOfDay`.** The scatter should drift and rewind, registered with T0's layer. Drifting
   apart ⇒ `WindWorldOffset`.
7. **Box vs Quad, Opaque vs Transparent.** All four combinations. Quad + Opaque is the cheap corner;
   Box + Transparent is the expensive one. Write the frame costs down — T0's unmeasured basis cost
   is exactly the debt this shouldn't repeat.
   **Test transparent at `Count` ≥ 2046 and dolly.** Anything ordering-related is invisible in one
   batch, so the default 3000 is the real test and 500 is the control. Expect it *right*, not
   *correct*: interpenetrating boxes still have no valid order and will still pop. `Alpha` 1.0 makes
   a genuine flip unambiguous — at 0.5 a far box showing through a near one is transparency working.
8. `Count` 500 → 20000. Watch `Batches` (Count/1023) and the frame time.
9. **`Shadow Lift` 0 → 1**, on **Cloud Controls** (not `CloudBoxes` — it's a weather knob, and that
   placement is the point). Boxes must flatten toward their lit colour, and **T0's painted layer must
   flatten with them, by the same amount, in the same frame.** If only one tier reacts, a shader is
   still calling `SampleRamp(ROW_CLOUD_SHADOW)` directly. At 1 both go flat-lit — that's correct, not
   a bug. Then scrub `TimeOfDay` at a lift of ~0.5 and check dawn still reads: this lifts toward the
   cloud's lit colour, so it should stay a cloud and not start glowing sun-orange.

### Done when

- Clouds sit in world space, parallax when the camera moves, and read as cloud rather than as voxels.
- Every shape/rotation/scale knob visibly does what its tooltip says.
- Coverage scrubs without re-baking.
- Cost measured across primitive × surface.
- Nothing outside `Assets/VFX/` references any of it.

### Open questions

- **Intersection seams.** Overlapping boxes cut hard lines through each other. `_VFX.md` says embrace
  them as facet detail. Unlooked-at — decide once it's on screen, not before.
- **Quads may be the better primitive and the notes didn't predict it.** They're a quarter of the
  fill and vanish edge-on; whether rotation jitter alone keeps enough of them facing camera is
  exactly the kind of thing you cannot reason your way to.
- **The scatter is one flat layer, not discrete clouds around a tower.** Altitude/Thickness give it
  depth, but the patch is still a slab. Fallcall wants clouds *around* things — that's a placement
  rule this doesn't have yet, and it's probably the next real question.
- ~~**Transparent + one-camera sorting.**~~ **Closed 2026-07-15, for free.** Submission moved to
  `RenderPipelineManager.beginCameraRendering` to fix the strobing bug, and per-camera submission was
  the real answer to this all along — the sort now knows which camera it sorted for.
- **`ShadeJitter` is per-box, so a box is uniformly brighter or darker.** Cheap and effective, but
  it's per-primitive, not per-cloud — a whole cloud can't be shadowed by the one above it. That's
  the first thing T3 would fix.
- Carried, unresolved from T0/T0.5: per-basis frame cost never measured; WeatherState presets +
  lerp; the field's G channel (height/type) still unwritten; wind jumping at the 24→0 wrap.

### Notes

- **The component is the only thing you can reach.** The material is runtime `HideAndDontSave`, so it
  has no inspector — same trap that forced `CloudControls` into existence. Every material knob is
  mirrored on the component and re-pushed every frame; don't add one that isn't.
- **`Placed` < `Count` is not a bug.** At low coverage most of the patch is empty sky and most darts
  miss. It logs once on the transition and the number is in the inspector.
- No `OnValidate` on `CloudBoxes`, deliberately: the two keys already notice every knob on every
  Update, and an inspector hook would drag a GPU blit + readback into `OnValidate`.

---

## Template

```markdown
## Effect: <name> (<tier>)

**Status:** TODO | IN-PROGRESS | BLOCKED
**Started:** <absolute date>
**Goal:** <one line>

### Why this first
### Blocked on
### Steps
### Done when
### Open questions
### Notes
```
