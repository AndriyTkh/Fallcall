# Effect: Cloud layer, fbm noise (T0)

**Status:** BUILT — verified in Editor 2026-07-15.
**Started:** 2026-07-15 · **Landed:** 2026-07-15
**Goal:** clouds from a noise field, in the skybox. Learn noise/fbm — the gate everything later
depends on.

> Written alongside the T0 sky plan, against this file's one-at-a-time rule — deliberate, because
> the sky was code-complete and blocked only on a human Editor check. Both verified together on
> 2026-07-15, so the risk (stacking unverified code on unverified code) never cashed in.

### Why this next

Not scattered boxes, even though they're the leading ship candidate — *Cheap → real, don't skip
tiers.* T2's placement jitter and T3's raymarched density are both this file with more
dimensions. And it extends the exact shader T0 just built, so it's the cheapest possible rung.

### Files

| File | Role |
|---|---|
| `T0_Sky/Noise.hlsl` | **The artifact.** Hash → value noise → fbm, each built from the one above. |
| `T0_Sky/T0_Sky.shader` | `CloudLayer()` behind `_CLOUD_LAYER`. Ray → plane at height, fbm on it, threshold, composite. |
| `T0_Sky/SkyRamp.cs` | +2 rows: cloud lit (5), cloud shadow (6). |
| `T0_Sky/TimeOfDaySky.cs` | +`_CloudWindOffset` global, +WindDirection/WindSpeed. |

### Steps

- [x] `Noise.hlsl` — hash, value noise, fbm with lacunarity/gain, normalised output
- [x] Ray→plane projection at `_CloudHeight`; horizon fade to hide the `dir.y→0` singularity
- [x] Coverage as a **threshold** on the field (clouds grow and merge, not fade)
- [x] Fake self-shadowing from threshold depth; cloud lit/shadow rows on the LUT
- [x] Wind as `f(TimeOfDay)`, pushed as a global — scrubbable, edit-mode safe
- [x] **Verify in Editor** — passed 2026-07-15. Drift rewinds under scrub, coverage grows and
      merges, octaves change edge detail only, horizon clean.

### Done when — met

- Coverage/octaves/wind all behave as above, off one scrubbable scalar, no per-frame alloc.
- Nothing outside `Assets/VFX/` references any of it.

### Open questions — resolved

- **Value noise, not Perlin/Worley.** Cheapest thing that teaches the lattice+interpolate idea.
  Worley is what actually makes clouds look like clouds (billows) and T3 needs it — is the next
  rung "swap the basis function" before moving to T1/T2? → **Yes.** Taken as the next effect,
  2026-07-15. The `Fbm2D` call at `T0_Sky.shader:117` is the single seam it swaps behind.

### Open questions — carried forward

- Wind is `f(TimeOfDay)`, so it **jumps at the 24→0 wrap**. Accept, or accumulate continuous
  hours in the driver and lose determinism-under-scrub?
- `_CloudCoverage` / `_CloudSoftness` / wind are material properties today. Per *Weather cycle
  is a state machine* they belong on `WeatherState` as globals. Build WeatherState before T1,
  or let it grow out of T2's needs?
- The layer is camera-locked, so it has zero parallax — by design at T0. Worth also proving the
  T0.5 version (extrude the same field into geometry, Minecraft-style) to feel the difference?
  → **Queued as the rung after Worley**, 2026-07-15.

### Notes

*(The durable half now lives in `_VFX.md` → **Noise fundamentals**. Kept here as the working
record.)*

- **Threshold, not multiply, is the whole trick.** `smoothstep(1-coverage, ..., n)` on a noise
  field is how coverage works everywhere in this ladder, including T2 (where it picks *which
  boxes spawn*) and T3 (where it's the density function). Same field, different consumer —
  which is the *shape from a field vs. placement* section, made concrete.
- **`gain ≈ 1/lacunarity`** is the natural-looking ratio. Higher gain goes electric; lower and
  the fine octaves stop contributing and you've paid for nothing.
- The `norm` accumulator in `Fbm2D` exists so octave count doesn't change output brightness —
  otherwise every threshold downstream needs re-tuning whenever you add an octave.
- `smoothstep`'s zero derivative at both ends is what kills lattice creases. Interpolating with
  raw `f` gives a visible grid of triangles. That one line is noise vs. not-noise.
- `[unroll(FBM_MAX_OCTAVES)]` — a dynamic fragment loop is a per-pixel branch; a bounded one is
  straight-line code.
- Clouds composite **after** the sun disc, so they occlude it instead of being lit through it.
