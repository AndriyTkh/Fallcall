using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OsuUnity.VFX.Sky
{
    /// <summary>
    /// T0.5 — the same cloud field, turned into geometry instead of pixels.
    ///
    /// T0's sky asks the field "what colour is this ray?" once per pixel. This asks it "where is
    /// there cloud?" once per grid cell, thresholds the answer, and extrudes the occupied cells into
    /// a slab of boxes. Nothing about the noise changes — if the weather looks different from T0's
    /// layer, the consumer is wrong, not the field. Minecraft's clouds.png is exactly this: a
    /// weather map, extruded.
    ///
    /// **The deliverable is parallax.** T0's layer is anchored to the camera and can never sit
    /// *around* anything — move and it follows you. This one exists in world space, which is the
    /// whole reason the ladder continues past T0. To see the point of this component, move the
    /// camera; everything else here is scaffolding.
    ///
    /// Owns its transform: XZ is wind, Y is altitude. Don't place it by hand — see WindWorldOffset.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Fallcall/VFX/Cloud Extrude")]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class CloudExtrude : MonoBehaviour
    {
        private const string GeoShaderName = "Fallcall/VFX/T0_CloudGeo";

        // Read back off the same globals CloudControls pushes. Not a reference to that component:
        // shaders are told the weather, and so is this — same contract, same source of truth.
        private static readonly int ScaleId      = Shader.PropertyToID("_CloudScale");
        private static readonly int CoverageId   = Shader.PropertyToID("_CloudCoverage");
        private static readonly int OctavesId    = Shader.PropertyToID("_CloudOctaves");
        private static readonly int LacunarityId = Shader.PropertyToID("_CloudLacunarity");
        private static readonly int GainId       = Shader.PropertyToID("_CloudGain");
        private static readonly int ErodeId      = Shader.PropertyToID("_CloudErode");
        private static readonly int WindId       = Shader.PropertyToID("_CloudWindOffset");

        [Header("Grid")]
        [Tooltip("Cells per side. Cost is this squared — 64 is 4096 cells. Raising it makes clouds " +
                 "finer, NOT more detailed: the field's detail comes from octaves. This only decides " +
                 "how finely the mesh can trace it.")]
        [Range(8, 256)] public int Resolution = 64;

        [Tooltip("World size of the patch, in metres. Finite on purpose — a placement-based cloud " +
                 "system can't fill a sky, and neither can this. That limit IS the lesson.")]
        [Min(10f)] public float WorldExtent = 6000f;

        [Header("Slab")]
        [Tooltip("Height of the layer above the world origin.")]
        public float Altitude = 800f;

        [Tooltip("How thick the extruded slab is. This is the parallax knob as much as the shape " +
                 "one — a flat slab reads as painted-on until it has depth to slide against.")]
        [Min(1f)] public float Thickness = 200f;

        [Header("Wind")]
        [Tooltip("Drift with TimeOfDay, matching T0's layer. Moves the transform rather than " +
                 "re-baking — see WindWorldOffset for why that isn't a shortcut.")]
        public bool FollowWind = true;

        [Header("Build")]
        [Tooltip("Rebuild when a knob changes. Off freezes the current mesh — handy for flying " +
                 "around one you like without a scrub re-triggering a bake.")]
        public bool AutoRebuild = true;

        [Header("Measured (read-only)")]
        [Tooltip("Occupied cells in the last build.")]
        [SerializeField] private int _cloudCells;
        [Tooltip("Triangles in the current mesh.")]
        [SerializeField] private int _triangles;
        [Tooltip("Milliseconds for the last bake + readback. This is the GPU stall — the reason " +
                 "baking is kept rare.")]
        [SerializeField] private float _bakeMs;
        [Tooltip("Milliseconds for the last mesh build.")]
        [SerializeField] private float _meshMs;

        private CloudField _field;
        private Mesh _mesh;
        private Material _material;

        // Rebuild keys. Two, not one, and the split is the point: the *field* changes when the noise
        // changes; coverage only changes what you do with it. Scrubbing coverage re-meshes but never
        // re-bakes, which is the "same field, different consumer" idea showing up as a cache policy.
        private FieldKey _bakedKey;
        private MeshKey _meshedKey;
        private bool _hasBaked;

        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<int> _indices = new List<int>();

        private struct FieldKey
        {
            public float Scale, Octaves, Lacunarity, Gain, Erode;
            public int Basis, Resolution;
            public float Extent;
        }

        private struct MeshKey
        {
            public float Coverage, Thickness;
        }

        /// <summary>
        /// World offset that keeps this mesh registered to T0's layer.
        ///
        /// The sky adds wind into its noise uv, so at world XZ p it shows <c>F(p*scale + wind)</c>.
        /// The bake is wind-free, so a cell at local l holds <c>F(l*scale)</c>. Placing it at
        /// <c>p = l + offset</c> shows <c>F((p - offset)*scale)</c>. Equate the two and
        /// <c>offset = -wind / scale</c> — a noise-space drift converted back into metres.
        ///
        /// The alternative (fold wind into the bake) re-bakes and re-meshes on every frame of a
        /// TimeOfDay scrub, for a field that is only sliding. Static field, moving frame.
        ///
        /// Static, and shared: T2's <see cref="CloudBoxes"/> is wind-driven off the same wind-free
        /// bake and needs the identical conversion. Two copies of this arithmetic is the most likely
        /// way for two tiers to drift apart while both still look plausible.
        /// </summary>
        public static Vector2 WindWorldOffset(Vector2 windNoise, float scale)
        {
            if (scale <= 1e-6f) return Vector2.zero;
            return -windNoise / scale;
        }

        private void OnEnable()
        {
            _field ??= new CloudField();
            _hasBaked = false;   // globals may have moved while we were off
            Rebuild();
        }

        private void OnValidate()
        {
            // OnValidate can fire before OnEnable on a domain reload; Rebuild handles a null field.
            if (isActiveAndEnabled) Rebuild();
        }

        private void Update()
        {
            DriveTransform();
            if (AutoRebuild) Rebuild();
        }

        [ContextMenu("Rebuild Now")]
        public void ForceRebuild()
        {
            _hasBaked = false;
            Rebuild();
        }

        private void DriveTransform()
        {
            float scale = Shader.GetGlobalFloat(ScaleId);
            Vector2 offset = Vector2.zero;

            if (FollowWind)
            {
                Vector4 wind = Shader.GetGlobalVector(WindId);
                offset = WindWorldOffset(new Vector2(wind.x, wind.y), scale);
            }

            // Only write when it actually moved. This runs every frame in edit mode, and an
            // unconditional assignment marks the scene dirty forever — you'd get a permanent
            // unsaved-changes asterisk from a component that hadn't done anything.
            var p = new Vector3(offset.x, Altitude, offset.y);
            if (transform.position != p) transform.position = p;
        }

        private void Rebuild()
        {
            _field ??= new CloudField();

            float scale = Shader.GetGlobalFloat(ScaleId);
            if (scale <= 0f)
            {
                // Zeroed globals mean no CloudControls in the scene. The field would be a constant
                // and the mesh would be either empty or a solid 6km slab — both of which look like a
                // broken shader rather than a missing component. Say so instead.
                if (_mesh != null) _mesh.Clear();
                _cloudCells = 0;
                _triangles = 0;
                return;
            }

            var key = new FieldKey
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

            if (!_hasBaked || !SameField(key, _bakedKey))
            {
                var sw = Stopwatch.StartNew();
                float half = WorldExtent * 0.5f;
                if (!_field.Bake(Resolution, new Vector2(-half, -half), new Vector2(WorldExtent, WorldExtent)))
                    return;
                sw.Stop();

                _bakeMs = (float)sw.Elapsed.TotalMilliseconds;
                _bakedKey = key;
                _hasBaked = true;
                _meshedKey = default;   // field moved, so the mesh is stale whatever its own key says
            }

            var meshKey = new MeshKey
            {
                Coverage = Shader.GetGlobalFloat(CoverageId),
                Thickness = Thickness,
            };

            if (SameMesh(meshKey, _meshedKey)) return;

            var sw2 = Stopwatch.StartNew();
            BuildMesh(meshKey.Coverage);
            sw2.Stop();
            _meshMs = (float)sw2.Elapsed.TotalMilliseconds;

            _meshedKey = meshKey;
        }

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

        private static bool SameMesh(in MeshKey a, in MeshKey b) =>
            Mathf.Approximately(a.Coverage, b.Coverage) &&
            Mathf.Approximately(a.Thickness, b.Thickness);

        private void BuildMesh(float coverage)
        {
            EnsureMeshAndMaterial();

            int res = _field.Resolution;
            if (res <= 0) return;

            // The same threshold the sky uses, on the same field. The sky smoothsteps across it for
            // a soft edge; geometry can't be half-there, so this is a hard compare. That difference
            // is why a T0.5 cloud has crisper edges than a T0 one at identical coverage — the field
            // agrees, the consumers round differently.
            float threshold = 1f - coverage;

            float cell = WorldExtent / res;
            float half = WorldExtent * 0.5f;
            float hy = Thickness * 0.5f;

            _verts.Clear();
            _normals.Clear();
            _indices.Clear();
            _cloudCells = 0;

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    if (_field.At(x, y) <= threshold) continue;
                    _cloudCells++;

                    float x0 = -half + x * cell, x1 = x0 + cell;
                    float z0 = -half + y * cell, z1 = z0 + cell;

                    // Top and bottom always. Sides only where the neighbour is empty — not an
                    // optimisation pass, just not emitting faces sealed inside an opaque mass.
                    // (Greedy meshing is the optimisation, and it isn't here yet by design: make it
                    // work, measure, then optimise what's actually slow.)
                    AddQuad(new Vector3(x0, hy, z0), new Vector3(x0, hy, z1),
                            new Vector3(x1, hy, z1), new Vector3(x1, hy, z0), Vector3.up);

                    AddQuad(new Vector3(x0, -hy, z0), new Vector3(x1, -hy, z0),
                            new Vector3(x1, -hy, z1), new Vector3(x0, -hy, z1), Vector3.down);

                    if (!Occupied(x - 1, y, threshold))
                        AddQuad(new Vector3(x0, -hy, z0), new Vector3(x0, -hy, z1),
                                new Vector3(x0, hy, z1), new Vector3(x0, hy, z0), Vector3.left);

                    if (!Occupied(x + 1, y, threshold))
                        AddQuad(new Vector3(x1, -hy, z0), new Vector3(x1, hy, z0),
                                new Vector3(x1, hy, z1), new Vector3(x1, -hy, z1), Vector3.right);

                    if (!Occupied(x, y - 1, threshold))
                        AddQuad(new Vector3(x0, -hy, z0), new Vector3(x0, hy, z0),
                                new Vector3(x1, hy, z0), new Vector3(x1, -hy, z0), Vector3.back);

                    if (!Occupied(x, y + 1, threshold))
                        AddQuad(new Vector3(x0, -hy, z1), new Vector3(x1, -hy, z1),
                                new Vector3(x1, hy, z1), new Vector3(x0, hy, z1), Vector3.forward);
                }
            }

            _mesh.Clear();
            // 16-bit indices cap at 65535 verts, which a 64-grid can pass. Silently wrapping past it
            // corrupts the mesh rather than erroring, so decide per build.
            _mesh.indexFormat = _verts.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.SetVertices(_verts);
            _mesh.SetNormals(_normals);
            _mesh.SetTriangles(_indices, 0, true);   // true = recalculate bounds while it's at it

            _triangles = _indices.Count / 3;
        }

        /// <summary>Occupancy with an out-of-bounds rule. Off the grid counts as empty, so the patch
        /// gets walled edges instead of open-ended tubes — the boundary is honest about being a
        /// boundary rather than hiding it.</summary>
        private bool Occupied(int x, int y, float threshold)
        {
            if (x < 0 || y < 0 || x >= _field.Resolution || y >= _field.Resolution) return false;
            return _field.At(x, y) > threshold;
        }

        private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int i = _verts.Count;

            // Four verts per face, not shared between faces: shared verts would average the normals
            // and round the boxes off. Faceted is the point — "falling through fast geometric
            // space", per CLAUDE.md. Costs 4x the verts and buys the whole look.
            _verts.Add(a); _verts.Add(b); _verts.Add(c); _verts.Add(d);
            _normals.Add(normal); _normals.Add(normal); _normals.Add(normal); _normals.Add(normal);

            _indices.Add(i); _indices.Add(i + 1); _indices.Add(i + 2);
            _indices.Add(i); _indices.Add(i + 2); _indices.Add(i + 3);
        }

        private void EnsureMeshAndMaterial()
        {
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Cloud Extrude (runtime)", hideFlags = HideFlags.HideAndDontSave };
                _mesh.MarkDynamic();   // rebuilt often while scrubbing
                GetComponent<MeshFilter>().sharedMesh = _mesh;
            }

            if (_material == null)
            {
                var shader = Shader.Find(GeoShaderName);
                if (shader == null)
                {
                    Debug.LogWarning($"[CloudExtrude] Shader '{GeoShaderName}' not found.", this);
                    return;
                }
                _material = new Material(shader)
                {
                    name = "T0_CloudGeo (runtime)",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                GetComponent<MeshRenderer>().sharedMaterial = _material;
            }
        }

        private void OnDisable()
        {
            _field?.Dispose();
            _field = null;

            // The MeshFilter holds a reference; clear it before destroying or the inspector shows a
            // missing-mesh slot on the next enable.
            var filter = GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh == _mesh) filter.sharedMesh = null;

            if (_mesh != null) DestroyAppropriate(_mesh);
            _mesh = null;
            if (_material != null) DestroyAppropriate(_material);
            _material = null;
        }

        private static void DestroyAppropriate(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
