# Effect: Worley basis swap (T0)

**Status:** BUILT — confirmed working in the Editor 2026-07-15.
**Started:** 2026-07-15 · **Landed:** 2026-07-15
**Goal:** swap the noise basis under the cloud layer that already works. Learn cellular noise —
a field defined by distance to *placed* points — and the erode remap that makes clouds billow.

> **What "verified" covers here, precisely.** The human confirmed it works after the
> `GlobalKeyword` crash fix: all three bases render and switch live from the inspector. The
> per-step verify list below was **not** walked item by item, and in particular **step 6's frame
> numbers were never recorded** — see *Not measured*. Don't read this as every box ticked.

### Why this next

**One variable changes.** The cloud layer, the LUT, the wind, the threshold, the compositing all
stay exactly as verified. Only the function behind the `Fbm2D` call moves. Anything that changes on
screen is the basis and nothing else — a controlled experiment, only possible because the previous
rung landed green.

**Worley is the bridge to the placement branch.** Value noise hashes a lattice of *values* and
interpolates. Worley hashes a lattice of *points* and shades by distance to the nearest one. The
feature points **are** placement. So *shape from a field vs. shape from placement* isn't two rival
branches — it's a dial, and Worley sits in the middle of it. That reframing is the real payoff, and
it's what makes T0.5 and T2 legible as the same idea at different densities.

**T3 needs perlin-worley specifically.** Getting the erode remap right here, in 2D, at zero risk,
means T3 is a dimension change rather than a new concept.

### Files

| File | Role |
|---|---|
| `T0_Sky/Noise.hlsl` | **The artifact, again.** +`Hash22` → `Worley2D` → `WorleyBillow2D` → `WorleyFbm2D` → `ErodeFbm2D`. Same build-each-from-the-one-above shape as the existing half. |
| `T0_Sky/T0_Sky.shader` | Basis keyword, 3 states, at the one `Fbm2D` seam. Cloud knobs moved from properties to globals. |
| `T0_Sky/CloudControls.cs` | **New.** Every cloud knob, one inspector, both modes. Pushes globals + basis keywords. |
| `T0_Sky/Editor/T0_SkySetup.cs` | Adds `CloudControls` to the rig; repairs pre-2026-07-15 rigs that lack it. |

`SkyRamp.cs` and `TimeOfDaySky.cs` untouched — the test that the LUT contract holds. A new basis is
not a new idea of what time it is.

### Scope added mid-build: CloudControls

The verify steps originally said "toggle the basis on the material". **There is no material
inspector, and there never was** — `TimeOfDaySky` builds the sky material at runtime with
`HideAndDontSave`, so every cloud property was unreachable except by editing shader defaults. The
rung was unverifiable as written; caught only when the knobs were first needed.

So the cloud knobs moved off material properties onto `CloudControls`, pushed as globals — what
*Weather cycle is a state machine* prescribed anyway, arriving early because the verify demanded it
rather than because T2 asked.

Cost, stated plainly: verify step 1's pixel-identity check ended up covering the basis swap *and*
the property→global move at once — exactly the one-variable discipline this rung was built on.
Unavoidable: the check couldn't be run at all without the thing it was testing.

### Steps

- [x] `Hash22` — coord → `float2` feature-point offset, in 0..1 (Hoskins, `sin`-free)
- [x] `Worley2D` — 3×3 cell search, F1 distance, `[unroll]`ed like the fbm loop
- [x] Normalise F1 → 0..1 so `_CloudCoverage` reads the same across all three bases
- [x] Invert to billows (`WorleyBillow2D`). Raw F1 is cracked veins, which is not a cloud
- [x] `WorleyFbm2D` — the existing fbm loop, basis swapped. Same `norm`, same reason
- [x] Erode remap — worley fbm carves the value fbm
- [x] Basis keywords `_CLOUDBASIS_VALUE` / `_WORLEY` / `_ERODE`, default Value
- [x] `CloudControls` — every knob in one inspector, live in edit *and* play mode
- [x] **Verify in Editor** — works; see the caveat at the top and *Not measured* below

### Not measured — carry to the next rung that cares

- **Step 6: frame cost per basis was never recorded.** Predicted ~2× for Worley (9 hashes/octave
  vs value's 4), multiplied by octave count. **T3's budget rests on this number and it does not
  exist yet.** Cheapest time to get it is while the three bases still toggle from one inspector.
- `WORLEY_F1_NORM = 0.72` was never checked against a measured F1 distribution. It is not obviously
  wrong (coverage was not reported as needing a retune across bases, which was the tell), but
  "nobody complained" is not "confirmed".

### Bug worth not re-learning

`GlobalKeyword.Create` in a `static readonly` field initialiser **throws** —
`CreateGlobalKeyword is not allowed to be called from a MonoBehaviour constructor (or instance
field initializer)`. The type initialiser runs when Unity deserialises the component, which is
inside that forbidden window. Resolve lazily on first use instead (`EnsureKeywords()` in
`CloudControls`). `Shader.PropertyToID` in a field initialiser is fine — the restriction is
specific to the keyword registry. Self-inflicted: the "resolve once" micro-optimisation caused it.

### Open questions — resolved while building

- **Erode remap shape?** → `saturate(Remap(base, billow * strength, 1, 0, 1))`, and Guerrilla's
  magic `0.6` became `_CloudErode`, a slider. A constant nobody can scrub is a constant nobody can
  justify. It also bought the strength-0 identity check for free, which was not the plan.
- **Shared fbm loop for both bases?** → No. HLSL has no function pointers; a macro or an in-loop
  basis branch both cost more than the duplicated loop saves. `WorleyFbm2D` is `Fbm2D` with one
  line changed, on purpose.
- **Should the cloud knobs be globals or material properties?** → Globals, forced. See *Scope added
  mid-build*. Keywords too — `multi_compile`, not `shader_feature`, because a runtime-generated
  material is invisible to variant stripping.

### Open questions — carried forward

*(Live ones restated in `_VFX_PLAN.md` / `_VFX.md` where they still bite.)*

- **F2−F1 gives cell edges** (veins, cracks), nearly free once F1's loop exists. Not a cloud — but
  it's how you'd do lightning or crackle. Add, or wait until something wants it?
- **3D.** All of this is 2D because the layer is a plane. T3 wants `Worley3D` at 27 cells/octave,
  and `ErodeFbm2D` is the 2D rehearsal for its per-step perlin-worley. Write it now while the 2D is
  fresh, or when T3 asks?
- Erode samples the billow at `p * 2.0` with `octaves - 2`. Both are taste, both hardcoded.
  Promote to knobs, or is that sprawl for a scratch tier?
- **WeatherState, half-answered.** Globals landed; presets + lerp did not.
- `CloudControls` and `TimeOfDaySky` both push globals every frame and neither knows about the
  other. Fine at two; at four it's a hidden ordering dependency.
- Wind is still on `TimeOfDaySky`, not `CloudControls`, because it's a function of `TimeOfDay`.
  Defensible, or cloud state living in the sky driver?
- Carried and still open: wind jumps at the 24→0 wrap.

### Notes

*(The durable half now lives in `_VFX.md` → **Noise fundamentals**. Kept here as the working
record.)*

- **Worley is placement wearing a field's clothes.** One hashed point per cell, shade by distance
  to it. Cells up → scattered blobs; cells down → a field. Same construction, different density.
- **Jitter must stay inside its cell.** A feature point that wanders outside its own cell breaks
  the 3×3 search's guarantee that the nearest point is in the set: F1 jumps, hard straight seams on
  cell lines. `frac`-range offsets only. This is *the* Worley bug.
- **Compare squared distances in the loop; `sqrt` once at the end.** `min` commutes with `sqrt`
  because `sqrt` is monotonic. Free win, 9× per octave.
- **F1's normalisation is empirical, not `1/sqrt(2)`.** The theoretical bound is real but
  vanishingly rare, so normalising by it leaves the field bunched near 0.2 and coverage stops
  meaning what it means for value noise.
- **`1 - F1` is what makes it a cloud.** F1 is 0 *at* a feature point, so inverting puts a bright
  billow on each. Un-inverted: dark blobs joined by bright veins — a Voronoi diagram, correct and
  useless here.
- **strength 0 collapses `ErodeFbm2D` to `Fbm2D` exactly.** `Remap(base, 0, 1, 0, 1) == base`. Free
  correctness check on the whole erode path before any of it is a matter of taste.
