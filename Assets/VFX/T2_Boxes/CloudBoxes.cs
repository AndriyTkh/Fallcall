using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Random = System.Random;

namespace OsuUnity.VFX.Sky
{
    /// <summary>Which primitive gets scattered. Both are flat-shaded and instanced; the only real
    /// difference is fill cost and whether there is an inside.</summary>
    public enum CloudPrimitive
    {
        /// <summary>A box. Has volume, so it survives being looked at from any angle.</summary>
        Box = 0,
        /// <summary>A flat rectangle, drawn double-sided. Quarter of the fill and half the tris of a
        /// box, but it vanishes edge-on — needs enough rotation jitter that some always face you.</summary>
        Quad = 1,
    }

    /// <summary>Opaque or alpha-blended. Not a cosmetic choice — see <see cref="CloudBoxes"/>.</summary>
    public enum CloudSurface
    {
        /// <summary>Blend One Zero, ZWrite on, Geometry queue. Overlap is nearly free (early-Z) and
        /// no sorting exists. The branch that should ship.</summary>
        Opaque = 0,
        /// <summary>Alpha blend, ZWrite off, Transparent queue. Full fill cost per overlapping layer
        /// and it must be sorted back-to-front, but overlapping boxes accumulate density for free.</summary>
        Transparent = 1,
    }

    /// <summary>
    /// T2 — clouds as scattered primitives. **The distribution is the shape.**
    ///
    /// Every earlier tier turns a field into pixels: sample it per view-ray (T0), or extrude it into
    /// a grid of geometry (T0.5). This one doesn't render the field at all. It uses it as a *spawn
    /// probability* and throws boxes at it, and what you see is where the boxes landed. That's the
    /// other branch of _VFX.md's "shape from a field vs. shape from placement", and it's why Fallcall
    /// wants it: discrete clouds around a tower, not a whole sky.
    ///
    /// Three knobs carry the whole look, and they are doing jobs a noise function would otherwise do:
    ///
    ///   - **Rotation jitter kills the grid.** Axis-aligned boxes read as Minecraft instantly. A few
    ///     degrees of yaw/pitch is the entire difference between "voxels" and "chunky organic mass".
    ///   - **Size-from-density is fbm, done with geometry.** Big boxes in the cores carry mass, small
    ///     ones at the edges fluff the silhouette. The placement rule replaces the octave loop.
    ///   - **Count is coverage's second half.** The field says *where*; count says *how solidly*.
    ///
    /// The field comes from <see cref="CloudField"/> — the same bake T0.5 reads, evaluated by the
    /// same HLSL the sky samples. This class contains no noise and does not own a weather map;
    /// _VFX.md called that out before T2 existed and it holds: a fourth consumer calls CloudField2D,
    /// it does not reimplement anything.
    ///
    /// Known and accepted: **overlapping boxes leave hard intersection seams.** A voxel grid never
    /// does. Embrace it — in a geometric art style seams read as facet detail, and the fix (SDF union
    /// + marching cubes) is expensive and dissolves the exact look you wanted.
    ///
    /// Owns its transform: XZ is wind, Y is altitude. Don't place it by hand — see
    /// <see cref="CloudExtrude.WindWorldOffset"/> for why the frame moves and the field doesn't.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Fallcall/VFX/Cloud Boxes")]
    public class CloudBoxes : MonoBehaviour
    {
        private const string ShaderName = "Fallcall/VFX/T2_CloudBox";

        // Graphics.DrawMeshInstanced's hard cap, and therefore the length of every scratch array
        // here. Not a tuning number.
        private const int InstancesPerBatch = 1023;

        // Read back off the same globals CloudControls pushes. Not a reference to that component:
        // shaders are told the weather, and so is this — same contract, same source of truth, and
        // the reason T0, T0.5 and T2 can't disagree about what coverage means.
        private static readonly int ScaleId      = Shader.PropertyToID("_CloudScale");
        private static readonly int CoverageId   = Shader.PropertyToID("_CloudCoverage");
        private static readonly int OctavesId    = Shader.PropertyToID("_CloudOctaves");
        private static readonly int LacunarityId = Shader.PropertyToID("_CloudLacunarity");
        private static readonly int GainId       = Shader.PropertyToID("_CloudGain");
        private static readonly int ErodeId      = Shader.PropertyToID("_CloudErode");
        private static readonly int WindId       = Shader.PropertyToID("_CloudWindOffset");

        private static readonly int InstanceParamsId  = Shader.PropertyToID("_InstanceParams");
        private static readonly int WrapId            = Shader.PropertyToID("_Wrap");
        private static readonly int RimPowerId        = Shader.PropertyToID("_RimPower");
        private static readonly int RimStrengthId     = Shader.PropertyToID("_RimStrength");
        private static readonly int AlphaId           = Shader.PropertyToID("_Alpha");
        private static readonly int AlphaFromDensityId = Shader.PropertyToID("_AlphaFromDensity");
        private static readonly int ShadeJitterId     = Shader.PropertyToID("_ShadeJitter");
        private static readonly int SrcBlendId        = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId        = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId          = Shader.PropertyToID("_ZWrite");
        private static readonly int CullId            = Shader.PropertyToID("_Cull");

        // ---- Field -------------------------------------------------------------------------------

        [Header("Field")]
        [Tooltip("Bake resolution of the spawn-density map, cells per side. This is NOT detail — it " +
                 "only decides how finely placement can follow a field the octaves already made. " +
                 "Placement smooths it anyway, so this can sit lower than T0.5 needs.")]
        [Range(8, 256)] public int Resolution = 96;

        [Tooltip("World size of the patch, in metres. Finite on purpose: placement-based clouds " +
                 "cannot fill a sky, and that limit IS the lesson. Fly far enough and you leave it.")]
        [Min(10f)] public float WorldExtent = 6000f;

        // ---- Placement ---------------------------------------------------------------------------

        [Header("Placement")]
        [Tooltip("How many primitives to place. The field decides WHERE; this decides how solidly. " +
                 "Thousands of 12-tri boxes in one instanced draw is nothing — raise it before you " +
                 "raise anything else.")]
        [Range(1, 20000)] public int Count = 3000;

        [Tooltip("Reshuffles the scatter without touching the weather. Same seed, same clouds.")]
        public int Seed = 1;

        [Tooltip("How far past the coverage threshold the field has to get before a spot counts as " +
                 "fully inside a cloud. Wide = fluffy, gradual edges; narrow = a hard rim of boxes " +
                 "at the silhouette.")]
        [Range(0.001f, 1f)] public float EdgeFalloff = 0.25f;

        [Tooltip("Bends spawn chance against density. 1 is linear. Below 1 scatters more strays out " +
                 "at the edges (wispier); above 1 packs everything into the cores and strips the " +
                 "silhouette bare.")]
        [Range(0.1f, 4f)] public float DensityExponent = 1f;

        [Tooltip("Rejection-sampling budget per placed box. Raise it if Count can't be met at low " +
                 "coverage — most of the patch is empty sky and every dart that lands there is a miss.")]
        [Range(1, 200)] public int MaxAttemptsPerBox = 20;

        // ---- Layer -------------------------------------------------------------------------------

        [Header("Layer")]
        [Tooltip("Height of the layer above the world origin.")]
        public float Altitude = 800f;

        [Tooltip("Vertical spread of the scatter. This is the parallax knob as much as the shape one " +
                 "— a flat sheet of boxes reads as painted-on until it has depth to slide against.")]
        [Min(1f)] public float Thickness = 200f;

        [Tooltip("Thin the layer out toward cloud edges so it domes instead of ending in a cliff. " +
                 "The field has no height channel, so this fakes one out of coverage.")]
        public bool ThicknessFollowsDensity = true;

        [Tooltip("Drift with TimeOfDay, matching T0 and T0.5. Moves the transform rather than " +
                 "re-baking — see CloudExtrude.WindWorldOffset for why that isn't a shortcut.")]
        public bool FollowWind = true;

        // ---- Shape -------------------------------------------------------------------------------

        [Header("Shape")]
        [Tooltip("Box has volume and survives any viewing angle. Quad is a flat rectangle at a " +
                 "quarter of the fill — cheaper, but it disappears edge-on unless rotation jitter " +
                 "keeps some facing you.")]
        public CloudPrimitive Primitive = CloudPrimitive.Box;

        [Tooltip("Size of a primitive out at the cloud edge, in metres. Small edge + big core is " +
                 "fbm done with geometry: mass in the middle, fluff at the rim.")]
        [Min(0.01f)] public float SizeAtEdge = 60f;

        [Tooltip("Size of a primitive in a cloud core, in metres.")]
        [Min(0.01f)] public float SizeAtCore = 220f;

        [Tooltip("Random size spread, as a fraction. 0 makes every box at a given density identical, " +
                 "which reads as a pattern however well they're placed.")]
        [Range(0f, 1f)] public float SizeJitter = 0.35f;

        [Tooltip("Per-axis shape of one primitive before size is applied. Squashing Y is what turns " +
                 "cubes into slabs and stops the mass looking like gravel.")]
        public Vector3 Stretch = new Vector3(1f, 0.45f, 1f);

        [Tooltip("Random per-axis spread on Stretch, as a fraction.")]
        [Range(0f, 1f)] public float StretchJitter = 0.25f;

        [Tooltip("Max random rotation per axis, in degrees. THIS is the knob that kills the grid " +
                 "look — axis-aligned reads as Minecraft instantly. 180 on an axis is fully random " +
                 "about it; a handful of degrees on pitch/roll with free yaw is the chunky-organic " +
                 "middle.")]
        public Vector3 RotationJitter = new Vector3(12f, 180f, 12f);

        // ---- Surface -----------------------------------------------------------------------------

        [Header("Surface")]
        [Tooltip("Opaque is the cheap branch: overlap costs almost nothing because early-Z kills " +
                 "buried fragments. Transparent pays full fill per layer AND needs sorting, but " +
                 "overlapping boxes accumulate density for free.")]
        public CloudSurface Surface = CloudSurface.Opaque;

        [Tooltip("Transparent only.")]
        [Range(0f, 1f)] public float Alpha = 0.5f;

        [Tooltip("Transparent only. How much alpha comes from how deep in the cloud a box landed. " +
                 "Edge boxes go wispy, cores stay solid — the thing transparency is actually for.")]
        [Range(0f, 1f)] public float AlphaFromDensity = 0.6f;

        [Tooltip("Per-box brightness variation. Without it, boxes sharing a normal merge into one " +
                 "flat surface — the exact read the scatter exists to avoid.")]
        [Range(0f, 1f)] public float ShadeJitter = 0.15f;

        [Tooltip("Sort instances by depth before submitting. REQUIRED for Transparent — blending is " +
                 "order-dependent and unsorted boxes are soup. For Opaque it's an optimisation: " +
                 "front-to-back submission lets early-Z reject buried boxes before they shade, which " +
                 "is the property the whole opaque branch rests on. Off to see what it's buying.")]
        public bool DepthSort = true;

        [Header("Lighting")]
        [Tooltip("Wrap diffuse. 0 is half-Lambert; 1 lights every face evenly. Softens the facet " +
                 "steps without erasing them.")]
        [Range(0f, 1f)] public float Wrap = 0.7f;

        [Range(0.5f, 8f)] public float RimPower = 3f;
        [Range(0f, 2f)] public float RimStrength = 0.5f;

        [Tooltip("Thousands of small shadow casters is not free, and cloud-on-cloud shadows are " +
                 "mostly invisible from below. Off by default; the LUT already fakes the lighting.")]
        public bool CastShadows = false;

        // ---- Build -------------------------------------------------------------------------------

        [Header("Build")]
        [Tooltip("Rebuild when a knob changes. Off freezes the current scatter — handy for flying " +
                 "around one you like without a scrub re-rolling it.")]
        public bool AutoRebuild = true;

        [Header("Measured (read-only)")]
        [Tooltip("Primitives actually placed. Short of Count means rejection sampling ran out of " +
                 "attempts — raise Max Attempts Per Box or coverage.")]
        [SerializeField] private int _placed;
        [Tooltip("Triangles submitted per frame, across all batches.")]
        [SerializeField] private int _triangles;
        [Tooltip("Instanced draw calls per frame. Count / 1023, rounded up.")]
        [SerializeField] private int _batches;
        [Tooltip("Milliseconds for the last field bake + readback. The GPU stall — why baking is " +
                 "kept out of the scrub path.")]
        [SerializeField] private float _bakeMs;
        [Tooltip("Milliseconds for the last placement pass.")]
        [SerializeField] private float _placeMs;

        private CloudField _field;
        private Mesh _mesh;
        private CloudPrimitive _meshPrimitive;
        private MaterialPropertyBlock _props;

        // One material per batch, identical but for renderQueue. Not a micro-optimisation gone wrong
        // — it is the only way to pin cross-batch draw order. See EnsureMaterials.
        private readonly List<Material> _materials = new List<Material>();

        // The scatter, in the transform's local space. Kept local, not world, because wind moves the
        // transform every frame and re-placing 3000 boxes per frame of a scrub would be absurd.
        private Matrix4x4[] _local = Array.Empty<Matrix4x4>();
        private Vector4[] _params = Array.Empty<Vector4>();
        private Vector3[] _centers = Array.Empty<Vector3>();

        // Draw order: near-to-far for opaque (early-Z), far-to-near for transparent (blending).
        private int[] _order = Array.Empty<int>();
        private float[] _sortKeys = Array.Empty<float>();
        private Vector3 _lastSortFrom = new Vector3(float.MaxValue, 0f, 0f);
        private bool _lastSortFarFirst;
        private bool _wasShort;

        private readonly Matrix4x4[] _batchMatrices = new Matrix4x4[InstancesPerBatch];
        private readonly Vector4[] _batchParams = new Vector4[InstancesPerBatch];

        // Two keys, not one, and the split is the point. The *field* changes when the noise changes;
        // everything else only changes what you do with it. Scrubbing rotation or size re-places but
        // never re-bakes, which is the only reason a synchronous readback is affordable here.
        private FieldKey _bakedKey;
        private PlaceKey _placedKey;
        private bool _hasBaked;
        private bool _hasPlaced;

        private struct FieldKey
        {
            public float Scale, Octaves, Lacunarity, Gain, Erode;
            public int Basis, Resolution;
            public float Extent;
        }

        private struct PlaceKey
        {
            public float Coverage, EdgeFalloff, DensityExponent;
            public int Count, Seed, MaxAttempts, Primitive;
            public float SizeEdge, SizeCore, SizeJitter, StretchJitter, Thickness;
            public Vector3 Stretch, Rotation;
            public bool ThicknessFollowsDensity;
        }

        private void OnEnable()
        {
            _field ??= new CloudField();
            _props ??= new MaterialPropertyBlock();
            _hasBaked = false;    // globals may have moved while we were off
            _hasPlaced = false;

            // -= before += : a script reload can re-run OnEnable without an intervening OnDisable,
            // and a double subscription submits the whole scatter twice per camera.
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;

            Rebuild();
            // Ready to draw now rather than at the first Update: a camera can render before Update
            // ever runs, and Render would find no mesh and skip a frame.
            EnsureMeshAndMaterial();
            ApplyMaterialState();
        }

        // No OnValidate, deliberately. The two keys below already notice every knob on every Update,
        // so an inspector hook would be redundant — and it would drag a GPU blit + readback into
        // OnValidate, which Unity does not want graphics work in.
        //
        // State only. Submission is OnBeginCamera's job and the split is load-bearing — see there.
        private void Update()
        {
            DriveTransform();
            if (AutoRebuild) Rebuild();

            // Outside the AutoRebuild guard: freezing the scatter shouldn't also freeze the
            // lighting and blend knobs, which cost nothing to re-push and are what you scrub while
            // flying around a scatter you like.
            EnsureMeshAndMaterial();
            ApplyMaterialState();
        }

        /// <summary>
        /// Submits the scatter, once per camera that is about to render it.
        ///
        /// **This is not in Update, and that is the whole point.** `Graphics.DrawMeshInstanced` is a
        /// per-frame *submission*, not persistent state: what isn't submitted during a frame isn't in
        /// that frame. `Update()` and "a camera is about to render" are two different clocks, and in
        /// edit mode they diverge hard — the Editor ticks Update on its own idle schedule while the
        /// Scene view repaints on every camera nudge. Draw from Update and every repaint that didn't
        /// get an Update draws nothing: boxes strobe while you fly, then take up to a second to come
        /// back when you stop. That second is the idle tick rate, and the whole thing looks exactly
        /// like a broken bake, which is what makes it worth this comment.
        ///
        /// Per-camera submission also **fixes** the depth sort instead of approximating it — the sort
        /// now knows which camera it is sorting for, and the order goes only to that camera.
        /// </summary>
        private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            // Material-preview thumbnails and reflection probes would each pay for a re-sort and a
            // few thousand boxes to render something nobody is looking at.
            if (cam.cameraType is CameraType.Preview or CameraType.Reflection) return;
            Render(cam);
        }

        [ContextMenu("Rebuild Now")]
        public void ForceRebuild()
        {
            _hasBaked = false;
            _hasPlaced = false;
            Rebuild();
        }

        private void DriveTransform()
        {
            float scale = Shader.GetGlobalFloat(ScaleId);
            Vector2 offset = Vector2.zero;

            if (FollowWind)
            {
                Vector4 wind = Shader.GetGlobalVector(WindId);
                // One conversion, one home. T0.5 derived it; duplicating the arithmetic here is how
                // two tiers start disagreeing about the weather while both look plausible.
                offset = CloudExtrude.WindWorldOffset(new Vector2(wind.x, wind.y), scale);
            }

            // Only write when it actually moved. This runs every frame in edit mode, and an
            // unconditional assignment marks the scene dirty forever — a permanent unsaved-changes
            // asterisk from a component that hasn't done anything.
            var p = new Vector3(offset.x, Altitude, offset.y);
            if (transform.position != p) transform.position = p;
        }

        // ---- Build -------------------------------------------------------------------------------

        private void Rebuild()
        {
            _field ??= new CloudField();

            float scale = Shader.GetGlobalFloat(ScaleId);
            if (scale <= 0f)
            {
                // Zeroed globals mean no CloudControls in the scene. The field would be constant and
                // the scatter would be either empty or a uniform 6km carpet of boxes — both of which
                // look like a broken shader rather than a missing component. Say so instead.
                _placed = 0;
                _triangles = 0;
                _batches = 0;
                return;
            }

            var fieldKey = new FieldKey
            {
                Scale = scale,
                Octaves = Shader.GetGlobalFloat(OctavesId),
                Lacunarity = Shader.GetGlobalFloat(LacunarityId),
                Gain = Shader.GetGlobalFloat(GainId),
                Erode = Shader.GetGlobalFloat(ErodeId),
                Basis = CurrentBasis(),
                Resolution = Resolution,
                Extent = WorldExtent,
            };

            if (!_hasBaked || !SameField(fieldKey, _bakedKey))
            {
                var sw = Stopwatch.StartNew();
                float half = WorldExtent * 0.5f;
                if (!_field.Bake(Resolution, new Vector2(-half, -half), new Vector2(WorldExtent, WorldExtent)))
                    return;
                sw.Stop();

                _bakeMs = (float)sw.Elapsed.TotalMilliseconds;
                _bakedKey = fieldKey;
                _hasBaked = true;
                _hasPlaced = false;   // field moved, so the scatter is stale whatever its own key says
            }

            var placeKey = new PlaceKey
            {
                Coverage = Shader.GetGlobalFloat(CoverageId),
                EdgeFalloff = EdgeFalloff,
                DensityExponent = DensityExponent,
                Count = Count,
                Seed = Seed,
                MaxAttempts = MaxAttemptsPerBox,
                Primitive = (int)Primitive,
                SizeEdge = SizeAtEdge,
                SizeCore = SizeAtCore,
                SizeJitter = SizeJitter,
                StretchJitter = StretchJitter,
                Thickness = Thickness,
                Stretch = Stretch,
                Rotation = RotationJitter,
                ThicknessFollowsDensity = ThicknessFollowsDensity,
            };

            if (_hasPlaced && SamePlacement(placeKey, _placedKey)) return;

            var sw2 = Stopwatch.StartNew();
            Place(placeKey);
            sw2.Stop();
            _placeMs = (float)sw2.Elapsed.TotalMilliseconds;

            _placedKey = placeKey;
            _hasPlaced = true;
            _lastSortFrom = new Vector3(float.MaxValue, 0f, 0f);   // force a re-sort against the new scatter
        }

        /// <summary>
        /// Rejection sampling: throw a dart at the patch, ask the field how cloudy it is there, keep
        /// the dart with that probability. **The distribution is the shape** — there is no threshold
        /// pass, no mesh, no occupancy grid. This loop is the entire T2 idea.
        /// </summary>
        private void Place(in PlaceKey key)
        {
            int res = _field.Resolution;
            if (res <= 0) return;

            EnsureCapacity(key.Count);

            // The same threshold the sky and T0.5 use, on the same field. They smoothstep it into an
            // alpha and hard-compare it into occupancy; this one turns it into a probability. Three
            // consumers, one field — that's the rule, and it's why one Coverage slider still means
            // one thing.
            float threshold = 1f - key.Coverage;
            float falloff = Mathf.Max(key.EdgeFalloff, 1e-4f);
            float half = WorldExtent * 0.5f;

            var rng = new Random(key.Seed);
            int placed = 0;
            int budget = key.Count * Mathf.Max(1, key.MaxAttempts);

            for (int attempt = 0; attempt < budget && placed < key.Count; attempt++)
            {
                float wx = Range(rng, -half, half);
                float wz = Range(rng, -half, half);

                float field = SampleField(wx, wz);

                // Density: 0 at the coverage threshold, 1 once the field is EdgeFalloff past it.
                // smoothstep and not a linear ramp for the same reason value noise uses it — a slope
                // that jumps at the boundary is visible, here as a ring of same-sized boxes.
                float density = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((field - threshold) / falloff));
                if (density <= 0f) continue;

                float chance = Mathf.Pow(density, key.DensityExponent);
                if (Next01(rng) > chance) continue;

                float spread = key.ThicknessFollowsDensity ? density : 1f;
                float wy = Range(rng, -1f, 1f) * key.Thickness * 0.5f * spread;

                var center = new Vector3(wx, wy, wz);

                // Size from density: the placement rule standing in for an octave loop.
                float size = Mathf.Lerp(key.SizeEdge, key.SizeCore, density);
                size *= 1f + Range(rng, -key.SizeJitter, key.SizeJitter);

                var scale = new Vector3(
                    key.Stretch.x * (1f + Range(rng, -key.StretchJitter, key.StretchJitter)) * size,
                    key.Stretch.y * (1f + Range(rng, -key.StretchJitter, key.StretchJitter)) * size,
                    key.Stretch.z * (1f + Range(rng, -key.StretchJitter, key.StretchJitter)) * size);

                var rot = Quaternion.Euler(
                    Range(rng, -key.Rotation.x, key.Rotation.x),
                    Range(rng, -key.Rotation.y, key.Rotation.y),
                    Range(rng, -key.Rotation.z, key.Rotation.z));

                _local[placed] = Matrix4x4.TRS(center, rot, scale);
                _centers[placed] = center;
                _params[placed] = new Vector4(density, Next01(rng), 0f, 0f);
                _order[placed] = placed;
                placed++;
            }

            _placed = placed;
            _triangles = placed * (key.Primitive == (int)CloudPrimitive.Box ? 12 : 2);
            _batches = Mathf.CeilToInt(placed / (float)InstancesPerBatch);

            // Not an error — at low coverage most of the patch is empty sky and most darts miss.
            // Worth saying once, because "Count says 3000, I placed 400" otherwise looks like the
            // placement rule is broken when it's the budget that ran out. Only on the transition:
            // placement re-runs on every frame of a slider drag, and a per-frame log is a log nobody
            // reads.
            bool isShort = placed < key.Count;
            if (isShort && !_wasShort)
            {
                Debug.Log($"[T2] Placed {placed}/{key.Count} — rejection sampling ran out of attempts. " +
                          $"Raise Max Attempts Per Box, Coverage, or Density Exponent.", this);
            }
            _wasShort = isShort;
        }

        /// <summary>
        /// Bilinear sample of the baked field at a world XZ. Bilinear and not nearest: the field is a
        /// continuous thing that happens to be stored on a grid, and sampling it per-cell would fold
        /// the grid back into placement — reintroducing the exact voxel read the scatter exists to
        /// avoid, at the bake resolution instead of the cell size.
        ///
        /// Mirrors CloudFieldBake.shader's texel-to-world mapping (uv = (i + 0.5) / res). If clouds
        /// ever sit half a patch away from T0's, this inverse is the first place to look.
        /// </summary>
        private float SampleField(float worldX, float worldZ)
        {
            int res = _field.Resolution;
            if (res <= 0) return 0f;

            float half = WorldExtent * 0.5f;
            float u = (worldX + half) / WorldExtent * res - 0.5f;
            float v = (worldZ + half) / WorldExtent * res - 0.5f;

            int x0 = Mathf.FloorToInt(u);
            int y0 = Mathf.FloorToInt(v);
            float fx = u - x0;
            float fy = v - y0;

            // CloudField.At clamps, so probing off the edge is safe without guarding here.
            float a = Mathf.Lerp(_field.At(x0, y0), _field.At(x0 + 1, y0), fx);
            float b = Mathf.Lerp(_field.At(x0, y0 + 1), _field.At(x0 + 1, y0 + 1), fx);
            return Mathf.Lerp(a, b, fy);
        }

        // ---- Render ------------------------------------------------------------------------------

        private void Render(Camera cam)
        {
            if (_placed <= 0 || _mesh == null || _materials.Count == 0) return;

            // Transparent must go far-to-near or the blend is wrong. Opaque wants the exact opposite:
            // near-to-far, so early-Z has already written the near depth by the time a buried box is
            // rasterised. One sort, one sign — the two branches disagree about direction, not about
            // whether order matters.
            if (DepthSort) SortByDepth(cam, farthestFirst: Surface == CloudSurface.Transparent);

            var baseMatrix = transform.localToWorldMatrix;
            var shadows = CastShadows && Surface == CloudSurface.Opaque
                ? ShadowCastingMode.On
                : ShadowCastingMode.Off;

            for (int start = 0; start < _placed; start += InstancesPerBatch)
            {
                // Batch b holds slab b of the depth sort and draws in material b's queue. The two
                // indices being the same number is the fix: slab order IS queue order, so URP has
                // nothing left to reorder.
                int b = start / InstancesPerBatch;
                if (b >= _materials.Count) break;   // placement outran EnsureMaterials by a frame

                int n = Mathf.Min(InstancesPerBatch, _placed - start);
                for (int i = 0; i < n; i++)
                {
                    int src = _order[start + i];
                    _batchMatrices[i] = baseMatrix * _local[src];
                    _batchParams[i] = _params[src];
                }

                // Full-length array on purpose: resizing a MaterialPropertyBlock array between calls
                // is an error, and the batch is the only thing whose length varies.
                _props.SetVectorArray(InstanceParamsId, _batchParams);

                // Submitted to this camera alone. The order was just computed for it, and handing a
                // camera-specific order to every camera is how a depth sort quietly stops being one.
                Graphics.DrawMeshInstanced(_mesh, 0, _materials[b], _batchMatrices, n, _props,
                                           shadows, false, gameObject.layer, cam);
            }
        }

        /// <summary>
        /// Orders instances by distance to <paramref name="cam"/>, the camera about to render them.
        ///
        /// The 1-metre cache below is shared across cameras rather than kept per camera. That can
        /// only ever cost a redundant sort — two cameras more than a metre apart each invalidate the
        /// other, so both re-sort and both are right. A per-camera cache would buy back a few hundred
        /// microseconds and owe a dictionary keyed on something with a lifetime.
        /// </summary>
        private void SortByDepth(Camera cam, bool farthestFirst)
        {
            if (cam == null) return;

            Vector3 from = cam.transform.position;
            // Boxes are hundreds of metres wide; re-sorting for a metre of camera drift is churn.
            if (farthestFirst == _lastSortFarFirst && (from - _lastSortFrom).sqrMagnitude < 1f) return;
            _lastSortFrom = from;
            _lastSortFarFirst = farthestFirst;

            Vector3 origin = transform.position;
            float sign = farthestFirst ? -1f : 1f;
            for (int i = 0; i < _placed; i++)
            {
                _order[i] = i;
                // Array.Sort is ascending, so the sign is the entire difference between the two modes.
                _sortKeys[i] = sign * (origin + _centers[i] - from).sqrMagnitude;
            }

            Array.Sort(_sortKeys, _order, 0, _placed);
        }

        // ---- Resources ---------------------------------------------------------------------------

        private void EnsureCapacity(int count)
        {
            if (_local.Length >= count && _local.Length > 0) return;
            _local = new Matrix4x4[count];
            _params = new Vector4[count];
            _centers = new Vector3[count];
            _order = new int[count];
            _sortKeys = new float[count];
        }

        private void EnsureMeshAndMaterial()
        {
            // Tracked in a field rather than compared against _mesh.name: this runs every frame, and
            // Unity's name getter marshals a fresh string out of native every call — a GC allocation
            // per frame to answer a question a bool already knows.
            if (_mesh == null || _meshPrimitive != Primitive)
            {
                if (_mesh != null) DestroyAppropriate(_mesh);
                _mesh = Primitive == CloudPrimitive.Box ? BuildBox() : BuildQuad();
                _meshPrimitive = Primitive;
            }

            EnsureMaterials(Mathf.Max(1, _batches));
        }

        /// <summary>
        /// One material per batch. They are identical except for <c>renderQueue</c>, and that is the
        /// entire reason they exist.
        ///
        /// **Draw order is only guaranteed *inside* one DrawMeshInstanced call.** Across calls, URP
        /// sorts the transparent queue back-to-front itself, using each batch's bounds centre — so it
        /// re-sorts my slabs on top of my sort. Far from the layer it agrees with me and nothing shows.
        /// Move close and the farthest boxes wrap *around* the camera, their collective bounds centre
        /// collapses toward it, and the batch order scrambles: 1023 boxes flip in and out at once.
        /// That is the "covered boxes on top while dollying" bug, and no amount of sorting harder in
        /// C# touches it, because the sort being overruled was never the problem.
        ///
        /// The lever is that **renderQueue is a hard ordering and distance is only a tiebreak within
        /// one queue.** Batch i gets queue base+i, so URP cannot reorder the slabs it is handed.
        ///
        /// Cost: one Material per 1023 primitives — 20 at Count 20000. They share a shader and are
        /// SRP-Batcher compatible, so it is object overhead, not draw overhead. Opaque doesn't need
        /// any of this (early-Z, not blending) but uses the same path with a flat queue, because two
        /// submission paths that differ by branch is how the two branches start disagreeing.
        /// </summary>
        private void EnsureMaterials(int count)
        {
            if (_materials.Count >= count) return;

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[CloudBoxes] Shader '{ShaderName}' not found.", this);
                return;
            }

            while (_materials.Count < count)
            {
                var m = new Material(shader)
                {
                    name = $"T2_CloudBox (runtime {_materials.Count})",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                m.enableInstancing = true;
                _materials.Add(m);
            }
        }

        /// <summary>Blend state lives here rather than in two SubShaders, so Surface stays one enum
        /// on one component instead of a material the inspector can't reach anyway.</summary>
        private void ApplyMaterialState()
        {
            if (_materials.Count == 0) return;

            bool transparent = Surface == CloudSurface.Transparent;
            int baseQueue = (int)(transparent ? RenderQueue.Transparent : RenderQueue.Geometry);

            for (int i = 0; i < _materials.Count; i++)
            {
                var mat = _materials[i];
                if (mat == null) continue;

                mat.SetFloat(SrcBlendId, (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
                mat.SetFloat(DstBlendId, (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
                mat.SetFloat(ZWriteId, transparent ? 0f : 1f);

                // The whole fix, in one line. Transparent: batch i draws strictly after batch i-1,
                // whatever URP thinks about their bounds. Opaque: one flat queue — order there is an
                // early-Z optimisation, not correctness, and spreading it over queues would only
                // forbid URP from batching state changes it is right to batch.
                mat.renderQueue = transparent ? baseQueue + i : baseQueue;

                // Quads have no back: cull them and half of every one is a hole. Boxes are closed, so
                // backface culling there is free.
                mat.SetFloat(CullId, Primitive == CloudPrimitive.Quad
                    ? (float)CullMode.Off
                    : (float)CullMode.Back);

                mat.SetFloat(WrapId, Wrap);
                mat.SetFloat(RimPowerId, RimPower);
                mat.SetFloat(RimStrengthId, RimStrength);
                mat.SetFloat(AlphaId, transparent ? Alpha : 1f);
                mat.SetFloat(AlphaFromDensityId, AlphaFromDensity);
                mat.SetFloat(ShadeJitterId, ShadeJitter);
            }
        }

        private static string MeshNameFor(CloudPrimitive p) =>
            p == CloudPrimitive.Box ? "Cloud Box (runtime)" : "Cloud Quad (runtime)";

        /// <summary>A unit box with unshared verts. Shared verts would average the normals and round
        /// it off; faceted is the point — "falling through fast geometric space", per CLAUDE.md.
        /// 24 verts instead of 8, and it buys the whole look.</summary>
        private static Mesh BuildBox()
        {
            var verts = new Vector3[24];
            var normals = new Vector3[24];
            var tris = new int[36];

            Vector3[] dirs =
            {
                Vector3.up, Vector3.down, Vector3.left,
                Vector3.right, Vector3.back, Vector3.forward,
            };

            for (int f = 0; f < 6; f++)
            {
                Vector3 n = dirs[f];

                // Rotating the normal's components gives an axis perpendicular to it for any of the
                // six (each term of dot(n, u) has a zero factor when n is axis-aligned), and v
                // completes the frame. Cheaper than a six-case table and impossible to get wrong in
                // only one of the cases.
                Vector3 u = new Vector3(n.y, n.z, n.x);
                Vector3 v = Vector3.Cross(n, u);
                Vector3 c = n * 0.5f;

                int i = f * 4;
                verts[i + 0] = c + (-u - v) * 0.5f;
                verts[i + 1] = c + (-u + v) * 0.5f;
                verts[i + 2] = c + (u + v) * 0.5f;
                verts[i + 3] = c + (u - v) * 0.5f;

                for (int k = 0; k < 4; k++) normals[i + k] = n;

                // Winding is 0-2-1 / 0-3-2, not 0-1-2 / 0-2-3. Unity faces a triangle along
                // (p1-p0) x (p2-p0), and for this vertex ring that cross product works out to
                // u x v == u x (n x u) == n — i.e. outward — only in this order. The other order
                // builds all six faces inside-out, which with Cull Back renders a box you can only
                // see the inside of.
                int t = f * 6;
                tris[t + 0] = i; tris[t + 1] = i + 2; tris[t + 2] = i + 1;
                tris[t + 3] = i; tris[t + 4] = i + 3; tris[t + 5] = i + 2;
            }

            var mesh = new Mesh { name = MeshNameFor(CloudPrimitive.Box), hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0, true);
            return mesh;
        }

        /// <summary>A unit rectangle in the XY plane, facing +Z. Drawn double-sided — the shader
        /// flips the normal on backfaces.</summary>
        private static Mesh BuildQuad()
        {
            var verts = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),   new Vector3(0.5f, -0.5f, 0f),
            };
            var normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            var tris = new[] { 0, 1, 2, 0, 2, 3 };

            var mesh = new Mesh { name = MeshNameFor(CloudPrimitive.Quad), hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0, true);
            return mesh;
        }

        // ---- Keys --------------------------------------------------------------------------------

        private static int CurrentBasis()
        {
            if (Shader.IsKeywordEnabled("_CLOUDBASIS_WORLEY")) return 1;
            if (Shader.IsKeywordEnabled("_CLOUDBASIS_ERODE")) return 2;
            return 0;
        }

        private static bool SameField(in FieldKey a, in FieldKey b) =>
            Mathf.Approximately(a.Scale, b.Scale) &&
            Mathf.Approximately(a.Octaves, b.Octaves) &&
            Mathf.Approximately(a.Lacunarity, b.Lacunarity) &&
            Mathf.Approximately(a.Gain, b.Gain) &&
            Mathf.Approximately(a.Erode, b.Erode) &&
            a.Basis == b.Basis && a.Resolution == b.Resolution &&
            Mathf.Approximately(a.Extent, b.Extent);

        private static bool SamePlacement(in PlaceKey a, in PlaceKey b) =>
            Mathf.Approximately(a.Coverage, b.Coverage) &&
            Mathf.Approximately(a.EdgeFalloff, b.EdgeFalloff) &&
            Mathf.Approximately(a.DensityExponent, b.DensityExponent) &&
            a.Count == b.Count && a.Seed == b.Seed &&
            a.MaxAttempts == b.MaxAttempts && a.Primitive == b.Primitive &&
            Mathf.Approximately(a.SizeEdge, b.SizeEdge) &&
            Mathf.Approximately(a.SizeCore, b.SizeCore) &&
            Mathf.Approximately(a.SizeJitter, b.SizeJitter) &&
            Mathf.Approximately(a.StretchJitter, b.StretchJitter) &&
            Mathf.Approximately(a.Thickness, b.Thickness) &&
            a.Stretch == b.Stretch && a.Rotation == b.Rotation &&
            a.ThicknessFollowsDensity == b.ThicknessFollowsDensity;

        // System.Random, not UnityEngine.Random: the seed has to be this component's, and
        // Random.InitState would stomp the global stream every rebuild — which in edit mode means
        // every scrub silently reseeds whatever else in the scene uses it.
        private static float Next01(Random rng) => (float)rng.NextDouble();

        private static float Range(Random rng, float lo, float hi) => lo + (hi - lo) * Next01(rng);

        private void OnDisable()
        {
            // Before the mesh and material go: the callback survives a disabled component otherwise,
            // and would submit a destroyed mesh on the next camera.
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

            _field?.Dispose();
            _field = null;

            if (_mesh != null) DestroyAppropriate(_mesh);
            _mesh = null;

            foreach (var m in _materials)
                if (m != null) DestroyAppropriate(m);
            _materials.Clear();

            _placed = 0;
            _hasBaked = false;
            _hasPlaced = false;
        }

        // Edit mode never runs Destroy's deferred path, so a plain Destroy here would leak a mesh and
        // a material per script reload while scrubbing.
        private static void DestroyAppropriate(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
