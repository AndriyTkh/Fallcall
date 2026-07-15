# Effect: Sky gradient + sun-elevation LUT (T0)

**Status:** BUILT — verified in Editor 2026-07-15.
**Started:** 2026-07-15 · **Landed:** 2026-07-15
**Goal:** make the day/night master scalar real. No clouds. Every later tier reads from this.

### Why this first

`TimeOfDay` → sun elevation → ramp LUT is the spine of the whole weather system. Every cloud
tier samples the same LUT for lit/shadow/fog colour. Build the spine before anything hangs
off it.

### Files

| File | Role |
|---|---|
| `T0_Sky/SkyRamp.cs` | ScriptableObject. 5 gradients → one `RGBAHalf` LUT texture. CPU reads gradients, GPU reads the bake. |
| `T0_Sky/TimeOfDaySky.cs` | `[ExecuteAlways]` driver. TimeOfDay → sun dir → `_SunElevation01` → globals, light, ambient, fog, skybox. |
| `T0_Sky/T0_Sky.shader` | Hand-written URP skybox. `dir.y` ramp over 3 LUT rows + sun disc. |
| `T0_Sky/Editor/T0_SkySetup.cs` | `Fallcall > VFX > Setup T0 Sky Rig` — builds the test rig, no scene asset needed. |

### Steps

- [x] `TimeOfDaySky` — `float TimeOfDay` 0..24, `AnimationCurve` drive, scrub in inspector
- [x] Sun elevation/azimuth from TimeOfDay; rotate the directional light
- [x] Ramp LUT texture: x = sun elevation. Rows = sky top, horizon, fog, ambient, **sun light**
- [x] Skybox shader: `dot(viewDir, up)` gradient, samples the LUT
- [x] Push LUT + sun params as globals via `Shader.SetGlobalXxx`
- [x] Ambient + fog colour driven off the same LUT sample
- [x] **Verify in Editor** — passed 2026-07-15, including the first post-URP-migration import.

### Done when — met

- Scrubbing `TimeOfDay` 0→24 in the inspector drives sky, sun angle, ambient, fog together
  from one scalar, with no per-frame allocation.
- Nothing outside `Assets/VFX/` references any of it.

### Open questions — resolved

- **Shader Graph or hand-written HLSL?** → **Hand-written.** URP 14 has no skybox master
  node; Shader Graph would mean faking it on an inverted sphere and fighting depth. And this
  is the fundamentals tier — the point is reading the HLSL.
- **LUT baked as an asset, or from `Gradient` fields?** → **Both, and it wasn't either/or.**
  Author gradients (tweakable), bake to `Texture2D` on `OnValidate` (shipping-shaped). The
  CPU evaluates the gradient directly, the GPU samples the bake — one source, two consumers.

### Open questions — carried forward

*(Neither blocked the effect; both are still live. Restated in `_VFX.md` if they outlive this.)*

- Fog *density* is still a plain scene setting; only fog colour is on the ramp. Put density
  on the ramp's fog-row alpha, like the sun row already does with intensity?
- `LatitudeTilt` + `NorthOffset` is a made-up 2-knob sun model. Enough forever, or will the
  tower's fixed skyline eventually want a real declination/azimuth pair?

### Notes

*(The durable half of these now lives in `_VFX.md` → **The LUT contract**. Kept here as the
working record.)*

- **Sun intensity is packed into the sun row's alpha**, 0..1, scaled by `MaxSunIntensity`.
  Alpha was free (a light colour has no opacity) and it keeps colour and brightness on one
  curve so they can't drift apart when the day is retimed. Alpha is *not* gamma-converted at
  bake — it's a scalar, not a colour.
- **LUT x is `sin(elevation)`, not elevation in degrees.** Both are monotonic in sun height,
  but `sin` changes fastest at the horizon, which is where the colour changes fastest — the
  texels land where they're needed instead of on a featureless noon. `u = dir.y*0.5+0.5`.
- LUT wrap mode **must** be Clamp. Repeat wraps midnight into noon at the ends.
- Ambient is `AmbientMode.Flat`, not Skybox — skybox ambient triggers a GPU convolution per
  change, i.e. every frame while scrubbing. The ramp already knows the answer.
- Sun disc isn't in the spec. Added anyway: it's the only on-screen proof that the azimuth
  maths and the LUT coordinate agree. A sun rising due north looks perfectly fine without it.
- Globals live **outside** `CBUFFER_START(UnityPerMaterial)`. Putting a `SetGlobal` value
  inside that block breaks SRP Batcher compatibility.
