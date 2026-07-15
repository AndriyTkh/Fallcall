using UnityEngine;

namespace OsuUnity.VFX.Sky
{
    /// <summary>
    /// The sun-elevation LUT. Five gradients baked into one tiny texture: x = sun height, one row per
    /// channel. Every sky/cloud shader samples this and nothing else, so they can never disagree about
    /// what time it is.
    ///
    /// Authored as <see cref="Gradient"/> (tweakable, scrubbable, artist-shaped), consumed as a texture
    /// (one sample, no branches, no per-frame work). Same source of truth on both sides — the CPU reads
    /// the gradients directly via <see cref="Sample"/>, the GPU reads the bake.
    /// </summary>
    [CreateAssetMenu(menuName = "Fallcall/VFX/Sky Ramp", fileName = "SkyRamp")]
    public class SkyRamp : ScriptableObject
    {
        // Row layout. T0_Sky.shader mirrors these as #defines — change one, change both.
        // Rows are cheap. Every tier that needs a new colour adds one here rather than inventing a
        // second LUT — that's what keeps the tiers agreeing about what time it is.
        public const int RowSkyTop      = 0;
        public const int RowHorizon     = 1;
        public const int RowFog         = 2;
        public const int RowAmbient     = 3;
        public const int RowSunLight    = 4;
        public const int RowCloudLit    = 5;
        public const int RowCloudShadow = 6;
        public const int RowCount       = 7;

        /// <summary>Samples across the elevation axis. Gradients are smooth, so this is already generous.</summary>
        private const int Resolution = 128;

        [Header("Sky")]
        [SerializeField, GradientUsage(true)] private Gradient _skyTop = MakeGradient(
            (0.00f, new Color(0.015f, 0.020f, 0.050f)),   // midnight
            (0.45f, new Color(0.060f, 0.080f, 0.160f)),
            (0.50f, new Color(0.160f, 0.170f, 0.280f)),   // sun exactly on the horizon
            (0.58f, new Color(0.130f, 0.220f, 0.420f)),
            (1.00f, new Color(0.120f, 0.300f, 0.620f)));  // noon

        [SerializeField, GradientUsage(true)] private Gradient _horizon = MakeGradient(
            (0.00f, new Color(0.020f, 0.025f, 0.060f)),
            (0.44f, new Color(0.100f, 0.090f, 0.140f)),
            (0.50f, new Color(0.550f, 0.280f, 0.160f)),   // the warm band is narrow on purpose — sunset is brief
            (0.55f, new Color(0.750f, 0.550f, 0.380f)),
            (0.70f, new Color(0.620f, 0.680f, 0.780f)),
            (1.00f, new Color(0.600f, 0.720f, 0.880f)));

        [Header("Scene")]
        [SerializeField, GradientUsage(true)] private Gradient _fog = MakeGradient(
            (0.00f, new Color(0.020f, 0.025f, 0.050f)),
            (0.50f, new Color(0.350f, 0.260f, 0.220f)),
            (1.00f, new Color(0.550f, 0.630f, 0.750f)));

        [SerializeField, GradientUsage(true)] private Gradient _ambient = MakeGradient(
            (0.00f, new Color(0.020f, 0.025f, 0.050f)),
            (0.50f, new Color(0.120f, 0.110f, 0.130f)),
            (1.00f, new Color(0.300f, 0.330f, 0.380f)));

        /// <summary>Directional-light colour. Alpha is not opacity here — it is the light's intensity,
        /// normalised 0..1 and scaled by the driver. Packing it in the unused channel keeps colour and
        /// brightness on one curve, so they can't drift apart when you retime the day.</summary>
        [SerializeField, GradientUsage(true)] private Gradient _sunLight = MakeGradient(
            (0.00f, new Color(0.05f, 0.07f, 0.15f, 0.03f)),   // moonlight stand-in
            (0.47f, new Color(0.35f, 0.18f, 0.08f, 0.10f)),
            (0.52f, new Color(1.00f, 0.55f, 0.28f, 0.55f)),   // low sun, warm and weak
            (0.65f, new Color(1.00f, 0.88f, 0.72f, 0.85f)),
            (1.00f, new Color(1.00f, 0.96f, 0.90f, 1.00f)));

        [Header("Clouds")]
        /// <summary>Sunlit face of a cloud. Shared by every cloud tier — the T0 noise layer and the
        /// T2 boxes must agree, or two clouds in one sky will disagree about the time of day.</summary>
        [SerializeField, GradientUsage(true)] private Gradient _cloudLit = MakeGradient(
            (0.00f, new Color(0.030f, 0.040f, 0.090f)),
            (0.47f, new Color(0.180f, 0.140f, 0.160f)),
            (0.52f, new Color(1.000f, 0.620f, 0.420f)),   // low sun rakes across the tops
            (0.65f, new Color(1.000f, 0.920f, 0.850f)),
            (1.00f, new Color(1.000f, 0.990f, 0.970f)));

        /// <summary>Self-shadowed underside. Never black — a cloud's base is lit by bounced sky, and
        /// crushing it to black is the single fastest way to make a sky look fake.</summary>
        [SerializeField, GradientUsage(true)] private Gradient _cloudShadow = MakeGradient(
            (0.00f, new Color(0.015f, 0.020f, 0.050f)),
            (0.47f, new Color(0.080f, 0.070f, 0.100f)),
            (0.52f, new Color(0.350f, 0.200f, 0.200f)),
            (0.65f, new Color(0.450f, 0.470f, 0.550f)),
            (1.00f, new Color(0.500f, 0.550f, 0.660f)));

        private Texture2D _lut;
        private bool _dirty = true;

        /// <summary>The baked LUT. Rebuilt only when a gradient changes, never per frame.</summary>
        public Texture2D Lut
        {
            get
            {
                if (_lut == null || _dirty) Bake();
                return _lut;
            }
        }

        /// <summary>CPU-side read of one row, for the things that aren't shaders (ambient, fog, the light).
        /// Evaluates the gradient rather than reading the texture back — same curve, no GPU round-trip.</summary>
        public Color Sample(int row, float elevation01) => GradientForRow(row).Evaluate(Mathf.Clamp01(elevation01));

        private Gradient GradientForRow(int row)
        {
            switch (row)
            {
                case RowSkyTop:      return _skyTop;
                case RowHorizon:     return _horizon;
                case RowFog:         return _fog;
                case RowAmbient:     return _ambient;
                case RowSunLight:    return _sunLight;
                case RowCloudLit:    return _cloudLit;
                case RowCloudShadow: return _cloudShadow;
                default:             return _skyTop;
            }
        }

        private void Bake()
        {
            if (_lut == null)
            {
                // RGBAHalf: the sun row and a bright noon sky go over 1.0, and the project renders HDR.
                // linear:true means "the bytes are already linear, don't sRGB-decode on sample".
                _lut = new Texture2D(Resolution, RowCount, TextureFormat.RGBAHalf, mipChain: false, linear: true)
                {
                    name = "SkyRampLut",
                    // Clamp, not Repeat — u past either end must hold midnight/noon, not wrap one into the other.
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            var px = new Color[Resolution * RowCount];
            for (int row = 0; row < RowCount; row++)
            {
                var g = GradientForRow(row);
                for (int x = 0; x < Resolution; x++)
                {
                    float u = x / (float)(Resolution - 1);
                    var c = g.Evaluate(u);
                    // GradientUsage defaults to gamma authoring, so convert on the way to a linear texture.
                    // Alpha carries a scalar (sun intensity), not colour — it must not be converted.
                    var lin = c.linear;
                    px[row * Resolution + x] = new Color(lin.r, lin.g, lin.b, c.a);
                }
            }

            _lut.SetPixels(px);
            _lut.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            _dirty = false;
        }

        private void OnValidate() => _dirty = true;

        private void OnDisable()
        {
            // ScriptableObjects survive domain reloads; the texture does not. Drop it so the next
            // access rebakes instead of handing out a destroyed object.
            if (_lut != null) DestroyImmediate(_lut);
            _lut = null;
            _dirty = true;
        }

        private static Gradient MakeGradient(params (float t, Color c)[] keys)
        {
            var g = new Gradient();
            var colorKeys = new GradientColorKey[keys.Length];
            var alphaKeys = new GradientAlphaKey[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                colorKeys[i] = new GradientColorKey(keys[i].c, keys[i].t);
                alphaKeys[i] = new GradientAlphaKey(keys[i].c.a, keys[i].t);
            }
            g.SetKeys(colorKeys, alphaKeys);
            return g;
        }
    }
}
