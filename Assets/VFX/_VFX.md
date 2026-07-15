# _VFX — effect catalog

**Scope:** VFX/shader learning scratch. Not shipped, not wired into gameplay. Nothing outside
`Assets/VFX/` may depend on anything inside it.

**This file is the durable record** — every effect researched or built, and the cross-effect
architecture they share. Active implementation work lives in `_VFX_PLAN.md` (one effect at a
time); when an effect lands, its plan is archived to `_archive/` and summarised here as a row.

**Pipeline:** URP 14.0.12, Unity 2022.3.62f3. Migrated from built-in 2026-07-15, **verified clean
on first Editor import the same day.** Unlocks Shader Graph (full URP target) + VFX Graph.

---

## Status legend

`RESEARCHED` — understood on paper, not built · `IN-PROGRESS` — has a live `_VFX_PLAN.md`
`BUILT` — works in a test scene · `SHELVED` — tried, parked, reason recorded
`ABANDONED` — code exists, was never verified, and the ladder moved past it

---

## Effects

| Effect | Status | Technique | Teaches | Lives in |
|---|---|---|---|---|
| Sky gradient (T0) | **BUILT** | `dot(viewDir, up)` ramp + sun-elevation LUT | HLSL basics, ramp LUTs | `T0_Sky/` |
| Cloud layer, noise (T0) | **BUILT** | fbm value noise in skybox shader | noise, fbm | `T0_Sky/` |
| Worley basis swap (T0) | **BUILT** | cellular F1 + erode remap behind the same fbm seam | cellular noise, field↔placement as one dial | `T0_Sky/` |
| Cloud extrude (T0.5) | **ABANDONED** | bake field → readback → threshold → extruded mesh | mesh gen, parallax, field-as-geometry, weather map | `T0_Sky/` |
| Cloud billboards (T1) | **SKIPPED** | camera-facing quads, soft-particle depth fade | blending, sorting, depth fade | — |
| Cloud mesh blobs (T2) | RESEARCHED | low-poly clusters, wrap diffuse + fresnel rim + vertex drift | custom lighting, vertex anim, instancing | — |
| **Cloud scattered primitives (T2)** | **IN-PROGRESS** | overlapping instanced boxes/quads, rejection-sampled off the field, random rot/scale, opaque + early-Z | instancing, early-Z, placement-as-shape | `T2_Boxes/` |
| Cloud voxel field (T2) | RESEARCHED | threshold a 3D noise field → box per cell, greedy-mesh | fields vs placement, greedy meshing | — |
| Volumetric clouds (T3) | RESEARCHED | raymarch 3D worley-perlin, weather map, Henyey-Greenstein, Beer + powder, blue-noise + TAA | the real thing | — |

### Cloud tier notes

*(Amended 2026-07-15. **The ladder was cut short at T0.5 by human call** — go straight to T2's
scattered primitives, Value basis, no intermediate rungs. The rule below stood while the rungs were
teaching something new; T1 and the back half of T0.5 were walking to a destination `_VFX.md` had
already named. Recorded rather than deleted, because "don't skip tiers" and "don't build what you've
already decided against" are both true and the second one won here.)*

Cheap → real. **Don't skip tiers** — raymarching without noise fundamentals is copy-paste,
not learning.

- **T0** has no parallax, so it can't sit "around/below the tower".
- **T1** pops under fast camera motion. Fallcall falls fast.
- **T2** is the Alto's Odyssey answer — opaque, so no sorting pain. Likely what ships.
  See *Scattered boxes* below — the current pick for **how** to build T2.
- **T3**'s payoff is *flying through* clouds, which the design forbids (camera stays in clear
  air, clouds never reduce visibility) — all of its cost, none of its benefit **for the game**.
  Still the best thing to learn from. Keep the two goals separate.

### Shape from a field vs. shape from placement

The tiers aren't unrelated techniques. There is **one coverage/density field** and a choice of
how to turn it into pixels — sample it per view-ray (T0/T0.5), extrude it into geometry
(Minecraft's `clouds.png` is exactly a weather map, extruded), or march through it (T3).

The other branch skips the field entirely: **place primitives, and the distribution *is* the
shape.** Field-based is procedural, uniform, hard to art-direct, and scales to a whole sky.
Placement-based is trivial to author, gives per-cloud control, and cannot fill a sky.

Fallcall has discrete clouds around a tower, not a full sky → **placement.**

Related cost rule: **raymarch scales with screen pixels; voxels scale with world volume.**
Clouds filling the sky → raymarch. A few clouds near camera → geometry.

**The two branches are one dial, not a fork.** *(Built and seen on screen, 2026-07-15 — Worley
rung. Held up.)* Worley noise hashes one feature **point** per cell and shades by distance to the
nearest: the feature points *are* placement, and the "field" is just what you get when the cells
are small. Widen the cells and the same construction is scattered blobs. So T0/T0.5/T2/T3 are not
four techniques — they are one construction read at four densities. That's what makes the T0.5-vs-T2
comparison worth building rather than arguing about, and it's the reason Worley came before T0.5.

### Scattered boxes — the candidate solution

*(Noted 2026-07-15. Prompted by modded-Minecraft clouds: many small **overlapping** boxes.
Overlap is the tell — a voxel grid never overlaps, so that look is placement, not a field.
It is T2 with the primitive swapped from blob to box.)*

Why it wins here:

- No field, no threshold, no meshing pass. Place N boxes in a cloud-shaped cluster — an
  afternoon, not a week.
- **Random rotation kills the grid look.** Axis-aligned is *the* Minecraft read; a few degrees
  of jitter goes chunky-organic instead of gridded.
- **Varying scale is fbm done with geometry** — big boxes carry mass, small boxes fluff edges.
  Placement rule replaces the noise function.
- One instanced draw. Thousands of 12-tri boxes is nothing.
- **Opaque, so overlap is nearly free** — early-Z kills buried fragments before shading.
  Submit front-to-back to maximise rejection. (Translucent boxes invert all of this: full fill
  cost per layer plus sorting. They do accumulate density for free — Beer's law by accident —
  but that is the expensive branch.)
- **On-theme.** `CLAUDE.md`: *"falling through fast geometric space."* Faceted angular cloud
  mass is a stronger read here than photoreal fluff, at a fraction of the cost.

The one real catch: **intersection seams.** Overlapping boxes leave hard visible lines where
they cut through each other; a voxel grid never does. Embrace it — in a geometric art style
seams read as facet detail. Fighting it means SDF union + marching cubes, which is expensive
and dissolves the exact look you wanted.

**Not blocked on URP.** Placement, instancing, rotation/scale jitter and perf can all be
prototyped on a stock URP/Lit material — no custom shader. Only the wrap-diffuse + rim + drift
pass needs the shader gate that `_VFX_PLAN.md`'s T0 is waiting on.

**Built 2026-07-15 as `T2_Boxes/`** — **renders; parallaxes.** Knobs and cost not yet exercised; see
`_VFX_PLAN.md`. Two things the notes above did not predict, both now knobs rather than assumptions:

- **The primitive is a toggle.** Box *or* flat quad. The case for boxes was made against Minecraft's
  clouds, which are boxes; a scattered flat rectangle is a quarter of the fill and might read just as
  well. Cheap to expose, so exposed.
- **Placement is rejection sampling against the T0.5 bake**, not a bespoke distribution. The field
  says where, `Count` says how solidly, and `_VFX.md` had already promised T2 wouldn't grow its own
  weather map. It didn't.

---

## Shared architecture

### Weather cycle is a state machine, not a shader

*(Half-landed 2026-07-15. `T0_Sky/CloudControls.cs` holds the cloud floats and pushes them as
globals; `TimeOfDaySky` does the same for sun/wind. **Presets and lerp-between-named-states do not
exist yet.** It arrived early not because a tier asked but because the knobs were unreachable — the
sky material is built at runtime with `HideAndDontSave`, so it has no inspector and every cloud
property was dead. Lesson worth keeping: **a tunable nobody can reach is not a tunable**, and a
runtime-generated material silently eats an inspector.)*

- One `WeatherState` of floats: coverage, density, wind, sun elevation/azimuth, fog
  colour+density, tint, precipitation. Lerp between named presets.
- Push via `Shader.SetGlobalXxx` — **global uniforms, not per-material**.
- **Sun elevation is the master scalar.** Bake one ramp texture (x = elevation) holding sky
  top, horizon, cloud lit, cloud shadow, fog, ambient. Everything reads one sample.
  Artist-editable.
- Day/night = directional light rotation + colour/intensity off that ramp. URP shaders read
  `_MainLightPosition` (was `_WorldSpaceLightPos0` in built-in).
- Drive from a `TimeOfDay` float 0..24 on a curve, **decoupled from realtime** so it can be
  scrubbed — matches the testing-first rule in `CLAUDE.md`.
- Weather map: T3 → scrolling 2D texture (R=coverage, G=type). T2 → drives spawn density +
  material params. **Landed 2026-07-15 as `T0_Sky/CloudField.cs`** — R only, G unwritten. T2 and T3
  read this rather than growing their own.

### The LUT contract

*(Landed in T0 as `T0_Sky/SkyRamp.cs` + `TimeOfDaySky.cs`. Every later tier consumes this
rather than growing its own idea of time — a tier that needs a new colour adds a **row**, not
a second LUT.)*

Pushed once per frame by `TimeOfDaySky` (sky, sun, wind) and `CloudControls` (clouds). A shader
never asks what time it is or what the weather is; it is told:

| Global | Meaning |
|---|---|
| `_SkyRampLut` | The baked LUT. x = sun height, y = row. |
| `_SunElevation01` | The x coordinate. `0` = sun straight down, `0.5` = horizon, `1` = zenith. |
| `_SunDirection` | World-space direction *toward* the sun. |
| `_CloudWindOffset` | `xy` = cloud-layer UV drift. A function of `TimeOfDay`, **not** `_Time` — so it scrubs. |
| `_Cloud*` | Every cloud knob (coverage, scale, softness, fbm, erode, fade, glow, shadow lift). Pushed by `CloudControls`, not `TimeOfDaySky`. |
| `_CLOUD_LAYER`, `_CLOUDBASIS_*` | Global **keywords**, set via `Shader.SetKeyword`. |

**Keywords on this shader must be `multi_compile`, never `shader_feature`.** `shader_feature`
strips variants by scanning materials for which keywords are set — and this shader's material is
generated at runtime with `HideAndDontSave`, so it is invisible to that scan. Every variant but the
default would be stripped from a build: basis switching works in the Editor and silently does
nothing in a player. Same reason keywords here can't be `_local` — local keywords are per-material
state and there is no material to hold them.

**`GlobalKeyword.Create` must not run in a `static readonly` field initialiser.** The type
initialiser fires when Unity deserialises the component, which is inside the window the API
forbids, and it throws `CreateGlobalKeyword is not allowed to be called from a MonoBehaviour
constructor`. Resolve lazily on first use (`CloudControls.EnsureKeywords`). `Shader.PropertyToID`
in a field initialiser is fine — the restriction is specific to the keyword registry.

Rows so far: `0` sky top · `1` horizon · `2` fog · `3` ambient · `4` sun light ·
`5` cloud lit · `6` cloud shadow. **`SkyRamp.cs` constants and `SkyRamp.hlsl` `#define`s mirror
each other by hand — change one, change both.** That seam is unavoidable (C# and HLSL can't share a
constant) but it is now the *only* one: the rows were inline in `T0_Sky.shader` until T0.5 became
the second consumer, and were extracted to `SkyRamp.hlsl` rather than copied.

Cloud lit/shadow are shared by *every* cloud tier, not owned by the one that added them. The
T0 noise layer and the T2 boxes read the same two rows; if they didn't, two clouds in one sky
could disagree about the time of day.

**A modifier on a row is the row's business, not a tier's.** *(Added 2026-07-15 with
`_CloudShadowLift` — pulls cloud shadow toward cloud lit, i.e. how self-shadowed clouds are, because
nothing here simulates the multiple scattering that makes real cloud shade bright.)* It went into
`SkyRamp.hlsl` as `SampleCloudShadow()` and **all three cloud shaders call it instead of
`SampleRamp(ROW_CLOUD_SHADOW)`.** Left as a per-tier knob it would be one component's opinion about
the weather, and T0's layer and T2's boxes would sit in one sky lit differently — the same failure
the shared rows exist to prevent, one level up. The rule generalises: **a knob that changes what a
row *means* is a global next to the row; a knob that changes what one tier *does* with it stays on
the tier.** Wrap/rim/jitter are the latter and correctly live on `CloudBoxes`.

Lift toward `ROW_CLOUD_LIT`, not `ROW_SUN_LIGHT`. The sun row is a *light*; the cloud rows are what a
cloud already looks like under it. Reaching for the sun row smuggles a light colour into an albedo
and breaks at dawn, when the sun is orange and the cloud is not.

Durable decisions worth not re-deriving:

- **Author gradients, consume a texture.** The "asset or `Gradient` fields?" question was a
  false choice. Gradients bake to a `Texture2D` on `OnValidate`; the CPU evaluates the
  gradient directly for ambient/fog/light, the GPU samples the bake. One source, two
  consumers, no GPU readback.
- **x is `sin(elevation)`, not degrees.** Same ordering, better distribution — `sin` changes
  fastest at the horizon, which is where the colours do. `u = sunDir.y * 0.5 + 0.5`.
- **Pack scalars in the alpha channel.** Sun *intensity* rides the sun row's alpha (a light
  colour has no opacity, so it was free). Keeps colour and brightness on one curve. Alpha is
  a scalar — do **not** gamma-convert it at bake time; the RGB needs it, alpha doesn't.
- **Clamp, never Repeat.** Repeat wraps midnight into noon at the LUT's ends.
- **Ambient is `AmbientMode.Flat`.** Skybox ambient needs a GPU convolution per change — i.e.
  every frame while scrubbing, which is the one thing the whole design is built for.
- **Globals go outside `CBUFFER_START(UnityPerMaterial)`.** A `SetGlobal` value inside that
  block breaks SRP Batcher compatibility.

### One field, many consumers

*(Landed 2026-07-15 with T0.5. The rule that made it buildable.)*

The field has **exactly one implementation**: `Noise.hlsl`, dispatched by `CloudField2D()` in
`CloudField.hlsl`. Every consumer calls that one function.

| Consumer | Asks | Gets |
|---|---|---|
| `T0_Sky.shader` | per view-ray, per pixel | "what colour is this ray?" → smoothstep to an alpha |
| `CloudFieldBake.shader` | per grid cell, once | "where is there cloud?" → hard compare to occupancy |

**The CPU does not own a copy of the noise.** T0.5 needed the field in C# to build a mesh; instead
of porting it, the bake shader evaluates the same HLSL into an RFloat texture and C# reads it back
(`CloudField.cs` contains no noise at all). A port would have been easier and would have drifted
the first time either side changed — turning the field-vs-placement comparison into a comparison of
two different fields, which is the one thing the ladder exists to avoid. **If a fourth consumer
appears, it calls `CloudField2D`. It does not reimplement anything.**

Corollaries worth keeping:

- **The field is raw; thresholding is the consumer's job.** The bake stores un-thresholded values.
  Coverage is not a property of the weather, it's a question you ask of it — which is why the sky
  can smoothstep it and the extruder can hard-compare it and both are right.
- **Wind moves the frame, not the field.** The bake is wind-free; the extruder translates its
  transform by `-wind / scale`. Folding wind into the field would re-bake on every frame of a
  `TimeOfDay` scrub, for a field that is only sliding. Minecraft does the same. **This is the most
  likely place for two tiers to silently disagree about the weather.**
- **Cache on what actually changed.** Noise knobs invalidate the bake; coverage only invalidates
  the mesh. That split is what makes a synchronous GPU readback affordable — it never runs in the
  scrub path.
- **Scalar fields want scalar formats.** RFloat + Linear. An 8-bit or sRGB target quantises the
  values every downstream threshold compares against, and banding in a field is terracing in the
  mesh.

### Two clocks: state ticks on Update, geometry ticks on the camera

*(Learned the hard way 2026-07-15, on T2. Applies to **any** tier that submits geometry from C#
rather than parking a MeshRenderer in the scene.)*

`Graphics.DrawMesh*` is a **per-frame submission, not persistent state.** What isn't submitted during
a frame isn't in that frame. So it must be driven by *"a camera is about to render"* —
`RenderPipelineManager.beginCameraRendering` under URP — and **never by `Update()`**.

Those are two different clocks and **edit mode is where they come apart**: the Editor ticks
`[ExecuteAlways]` `Update()` on its own idle schedule while the Scene view repaints on every camera
nudge. Every repaint that didn't get an `Update()` draws nothing.

**Know the symptom, because it lies about its cause:** geometry strobes while you fly the camera and
takes up to a second to reappear when you stop — that second being the idle tick rate. It looks like
a broken bake or a culling bug, and it is neither; nothing camera-dependent is even running. The
giveaway is that it correlates with camera *motion*, not camera *position*. **Camera motion is not an
input to any of this code** — if moving the camera changes what you see beyond parallax, suspect the
submission, not the field.

Corollaries:

- **Keep the split.** State (drive the transform, re-bake, re-place, push material knobs) belongs on
  `Update`. Submission belongs on the camera callback. Doing the expensive half per camera is how a
  two-view Editor pays twice for one scatter.
- **Per-camera submission is a fix, not a tax.** Pass the camera to `DrawMeshInstanced` and a depth
  sort finally knows *which* camera it sorted for. Sorting for `Camera.main` and submitting to
  everything is a sort that silently isn't one — T2 shipped that caveat as an open question and the
  callback closed it for free.
- **Unsubscribe in `OnDisable`, before destroying the mesh.** A live callback on a disabled component
  submits a destroyed mesh on the next camera.
- **Filter `CameraType.Preview`/`Reflection`.** Otherwise every material-preview thumbnail renders a
  few thousand boxes nobody is looking at.

### Your draw order stops at the batch boundary

*(Found on T2's transparent branch 2026-07-15, immediately after the clock bug above — same file,
same afternoon, and the first bug hid the second.)*

**Order is only guaranteed *inside* one `DrawMeshInstanced` call.** Instances within a batch rasterise
by instance id, so a CPU sort holds. Across batches it does not: URP sorts the transparent queue
back-to-front *itself*, per draw call, off each batch's **bounds centre** — so it re-sorts your slabs
on top of your sort, and its key is much worse than yours.

Far from the layer this is invisible: depth-sorted slabs have ordered bounds centres, so URP agrees
with you. **Move the camera close and it breaks** — the farthest boxes now wrap *around* the camera,
their collective bounds centre collapses toward it, batch order scrambles, and 1023 primitives flip
in and out as one. Reads as "covered geometry drawn on top while dollying".

- **Sorting harder in C# cannot fix it.** The sort being overruled was never the problem. If your fix
  is in the sort function, you have misread the bug.
- **The lever is that `renderQueue` is a *hard* ordering and distance is only a tiebreak within one
  queue.** Give batch *i* queue `base+i` — one material per batch, identical but for that int — and
  URP has nothing left to reorder. Cost is one Material per 1023 instances; they share a shader and
  stay SRP-Batcher compatible, so it's object overhead, not draw overhead.
- **The 1023 cap is a correctness boundary, not just a loop bound.** Anything relying on submission
  order has a bug that appears only above 1023 instances — which is why `Count = 500` (one batch) vs
  `Count = 3000` (three) is the diagnostic that splits this from the one below.
- **Opaque is immune** and that is not luck: early-Z makes cross-batch order an optimisation rather
  than a correctness property. One more entry on the list of things the opaque branch doesn't have to
  care about.

**Underneath it sits a limit no ordering fixes: interpenetrating transparent geometry has no correct
draw order.** Box A is in front of B in some pixels and behind it in others; one order per object
cannot say both. T2 overlaps boxes *on purpose* — overlap is the tell that this is placement and not
a field — so the transparent branch is asking for an order that doesn't exist. Sorting can be made
*right* (above); it cannot be made *correct*. The order-independent answer is alpha-to-coverage
(stochastic, wants MSAA); the cheap answer is the one the notes already reached — ship opaque.

### Noise fundamentals

*(Landed in T0 as `T0_Sky/Noise.hlsl`. Every later tier extends this file rather than starting a
second noise implementation — T2's placement jitter and T3's raymarched density are this file with
more dimensions and a better basis.)*

The load-bearing idea: **noise is not randomness.** Randomness is `Hash` — coordinate in,
repeatable garbage out, no state, no seed, which is the whole reason it can run per-pixel
per-frame without storing anything. *Noise* is randomness made **continuous**: hashed on a lattice
and interpolated between. And **fbm is not a noise function at all** — it's a loop that adds noise
to itself at shrinking scales.

- **Threshold, not multiply, is the whole trick.** `smoothstep(1-coverage, …, n)` on a field is
  how coverage works at *every* tier — T2 uses it to pick which boxes spawn, T3 as the density
  function. Same field, different consumer. Multiply and clouds fade in uniformly; threshold and
  they grow and merge, which is what weather does.
- **`smoothstep`'s zero derivative at both ends is what kills lattice creases.** Interpolate with
  raw `f` and you see a grid of triangles, because the slope jumps at every cell edge. That one
  line is the difference between noise and not-noise.
- **`gain ≈ 1/lacunarity`** is the natural-looking ratio. Higher goes electric; lower and the fine
  octaves stop contributing and you've paid for nothing.
- **Normalise by summed amplitude.** The `norm` accumulator in `Fbm2D` is why octave count doesn't
  change output brightness — without it, every threshold downstream needs re-tuning whenever an
  octave is added. Generalises: *any* basis swap must preserve the output range, or coverage
  silently means something different per basis.
- **Bound the loop.** `[unroll(FBM_MAX_OCTAVES)]` — a dynamic fragment loop is a per-pixel branch;
  a bounded one is straight-line code.

Then the cellular half, added by the Worley rung — same shape, different way of making a lattice
continuous. Value noise *works* for its smoothness (that's what the smoothstep buys); worley gets
it free, because distance-to-a-point is already continuous, and pays elsewhere: **9 hashes per
sample against value's 4.**

- **Jitter must stay inside its cell.** A feature point that wanders outside its own cell breaks
  the 3×3 search's guarantee that the true nearest point is in the search set — F1 jumps, and you
  get hard *straight* seams along cell lines. `frac`-range offsets only. This is *the* worley bug:
  if you see straight edges, look here before anywhere else.
- **`1 - F1` is what makes it a cloud.** F1 is 0 *at* a feature point, so inverting puts a bright
  billow on each one. Un-inverted you get dark blobs joined by bright veins — a Voronoi diagram,
  correct and useless for weather.
- **Compare squared distances in the loop, `sqrt` once at the end.** `min` commutes with `sqrt`
  because `sqrt` is monotonic, so a per-cell `sqrt` is 9× pure waste per octave.
- **F1's normalisation is empirical, not the theoretical bound.** `sqrt(2)` is a real bound and a
  useless one — that case is vanishingly rare, so normalising by it leaves the field bunched near
  0.2 and coverage silently stops meaning what it means for value noise. Same failure the `norm`
  accumulator prevents, one level up.
- **Erode is the remap, and it is T3's per-step maths in 2D.** Value fbm carries the masses; a
  finer billow field raises the floor they must clear (`Remap(base, billow*s, 1, 0, 1)`). Cores
  survive untouched, edges get bitten — which is what makes it erosion rather than multiplying two
  fields together (that just dims everything evenly). **`s = 0` collapses it to exactly `base`**,
  which is a free correctness check on the whole path before any of it is a matter of taste.
- **Prefer a knob to a magic constant.** Guerrilla's erode `0.6` became a slider. A constant nobody
  can scrub is a constant nobody can justify — and making it reachable turned "does it survive to
  T3?" into a question the Editor answers.

---

## References

- [Tyler Dodds — Volumetric Clouds tutorial](https://github.com/TylerDodds/VolumetricCloudsTutorial/blob/master/Assets/VolumetricCloudsTutorial/Tutorial.md) — built-in RP, but the raymarching maths ports
- [Cyanilux — Shader Graph tutorials](https://www.cyanilux.com/tutorials/intro-to-shader-graph/) — best URP shader resource
- [Unity — VFX Graph getting started](https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@8.2/manual/GettingStarted.html)

---

## Done log

*(one line per landed effect, absolute dates)*

- **2026-07-15** — **Sky gradient + sun-elevation LUT (T0)**. `TimeOfDay` 0..24 → sun direction →
  one baked ramp LUT drives sky, sun disc, light colour+intensity, ambient and fog off a single
  scrubbable scalar. Established *The LUT contract*. → `_archive/T0_sky-gradient-sun-lut.md`
- **2026-07-15** — **Cloud layer, fbm noise (T0)**. `Noise.hlsl` (hash → value noise → fbm) shaded
  on a ray→plane projection, coverage as a threshold, wind as `f(TimeOfDay)` so it scrubs.
  Established *Noise fundamentals*. → `_archive/T0_cloud-layer-fbm.md`
- **2026-07-15** — **Worley basis swap (T0)**. `Hash22` → `Worley2D` → `WorleyBillow2D` →
  `WorleyFbm2D` → `ErodeFbm2D` behind the one `Fbm2D` seam; three bases (Value/Worley/Erode)
  switchable live. Confirmed the field↔placement dial. Also forced `CloudControls` into existence
  (below). **Debt: per-basis frame cost never measured — T3's budget needs it.**
  → `_archive/T0_worley-basis-swap.md`
- **2026-07-15** — **`CloudControls` + cloud knobs → globals**. Not a planned effect; the Worley
  rung was unverifiable without it. The globals half of *WeatherState*, arriving early. Presets and
  lerp still don't exist.
- **2026-07-15** — URP 14.0.12 migration verified clean on first Editor import (no magenta, no
  console errors). Not an effect; recorded because both T0 plans were gated on it.
