using System;
using UnityEngine;

namespace OsuUnity.VFX.Sky
{
    /// <summary>
    /// The cloud field, on the CPU — by asking the GPU rather than by reimplementing it.
    ///
    /// Bakes <c>CloudFieldBake.shader</c> into a small RFloat texture and reads it back. The point is
    /// what this class does *not* contain: any noise. The field has exactly one implementation
    /// (<c>Noise.hlsl</c> via <c>CloudField.hlsl</c>), and this is a courier for its answers. A C#
    /// port would have been easier to write and would have drifted from the HLSL the first time
    /// either changed — which would quietly turn T0.5's whole field-vs-placement comparison into a
    /// comparison of two different fields. See _VFX_PLAN.md.
    ///
    /// Cost: a synchronous GPU readback, which stalls. Affordable only because baking is *rare* —
    /// the field changes when the noise knobs change, not when the weather drifts. Wind and coverage
    /// deliberately do not invalidate a bake (see <see cref="CloudExtrude"/>).
    ///
    /// Not a MonoBehaviour: T2's boxes will want the same field for spawn density, and this shouldn't
    /// have to be in the scene twice for that.
    /// </summary>
    public sealed class CloudField : IDisposable
    {
        private const string BakeShaderName = "Fallcall/VFX/CloudFieldBake";
        private static readonly int BakeWorldRectId = Shader.PropertyToID("_BakeWorldRect");

        private Material _bakeMaterial;
        private Texture2D _readback;

        /// <summary>Field values, row-major, <see cref="Resolution"/> squared. Raw and
        /// un-thresholded, 0..1 — thresholding is the caller's business.</summary>
        public float[] Values { get; private set; }

        /// <summary>Cells per side of the last successful bake.</summary>
        public int Resolution { get; private set; }

        /// <summary>The baked field as a texture — the "weather map". Handy to look at, and what T2
        /// and T3 will read instead of paying for their own evaluation.</summary>
        public Texture2D Texture => _readback;

        /// <summary>
        /// Evaluates the field over a world-XZ rect and reads it back. Returns false if the bake
        /// shader is missing, leaving <see cref="Values"/> untouched.
        /// </summary>
        /// <param name="resolution">Cells per side.</param>
        /// <param name="worldOrigin">World-XZ corner of the region to bake.</param>
        /// <param name="worldSize">World-XZ size of the region.</param>
        public bool Bake(int resolution, Vector2 worldOrigin, Vector2 worldSize)
        {
            if (!EnsureMaterial()) return false;

            resolution = Mathf.Max(2, resolution);
            EnsureReadbackTexture(resolution);

            _bakeMaterial.SetVector(BakeWorldRectId, new Vector4(worldOrigin.x, worldOrigin.y, worldSize.x, worldSize.y));

            // Linear, and RFloat rather than a colour format: this is a scalar field, not an image.
            // Pushing it through an sRGB or 8-bit target would quantise the thing every downstream
            // threshold is comparing against, and banding in a field reads as terracing in the mesh.
            var rt = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.RFloat,
                                                RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(Texture2D.whiteTexture, rt, _bakeMaterial);

                RenderTexture.active = rt;
                _readback.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0, false);
                _readback.Apply(false);
            }
            finally
            {
                // Restore before releasing: leaving a released RT active makes whatever renders next
                // fail in a place with no connection to this code.
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }

            var data = _readback.GetRawTextureData<float>();
            if (Values == null || Values.Length != data.Length) Values = new float[data.Length];
            data.CopyTo(Values);

            Resolution = resolution;
            return true;
        }

        /// <summary>Field value at a cell. Clamped, so callers can probe neighbours off the edge
        /// without guarding every access.</summary>
        public float At(int x, int y)
        {
            if (Values == null || Resolution <= 0) return 0f;
            x = Mathf.Clamp(x, 0, Resolution - 1);
            y = Mathf.Clamp(y, 0, Resolution - 1);
            return Values[y * Resolution + x];
        }

        private bool EnsureMaterial()
        {
            if (_bakeMaterial != null) return true;

            var shader = Shader.Find(BakeShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[CloudField] Shader '{BakeShaderName}' not found — no field to bake.");
                return false;
            }

            _bakeMaterial = new Material(shader)
            {
                name = "CloudFieldBake (runtime)",
                hideFlags = HideFlags.HideAndDontSave,
            };
            return true;
        }

        private void EnsureReadbackTexture(int resolution)
        {
            if (_readback != null && _readback.width == resolution) return;

            if (_readback != null) DestroyAppropriate(_readback);
            _readback = new Texture2D(resolution, resolution, TextureFormat.RFloat, false, true)
            {
                name = "CloudField (weather map)",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                // Clamp, not Repeat: the field is only defined over the baked rect. Repeat would
                // silently wrap the far edge into the near one when a consumer samples past it.
                wrapMode = TextureWrapMode.Clamp,
            };
        }

        public void Dispose()
        {
            if (_bakeMaterial != null) DestroyAppropriate(_bakeMaterial);
            _bakeMaterial = null;
            if (_readback != null) DestroyAppropriate(_readback);
            _readback = null;
            Values = null;
            Resolution = 0;
        }

        // Edit mode never runs Destroy's deferred path, so a plain Destroy here would leak a
        // material per script reload while scrubbing.
        private static void DestroyAppropriate(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
