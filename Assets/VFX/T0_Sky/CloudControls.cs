using UnityEngine;
using UnityEngine.Rendering;

namespace OsuUnity.VFX.Sky
{
    /// <summary>Which noise basis builds the cloud field. Mirrors the _CLOUDBASIS_* keywords in
    /// T0_Sky.shader — change one, change both.</summary>
    public enum CloudBasis
    {
        /// <summary>fbm value noise. The 2026-07-15 verified baseline. Soft, smeared.</summary>
        Value = 0,
        /// <summary>fbm over inverted worley. Rounded, clumped, billowy.</summary>
        Worley = 1,
        /// <summary>Value noise masses with worley bitten out of the edges. Should look best.</summary>
        Erode = 2,
    }

    /// <summary>
    /// Every cloud knob, in one inspector, live in edit mode and play mode.
    ///
    /// This exists because the knobs were unreachable. They were material properties, and
    /// <see cref="TimeOfDaySky"/> builds its material at runtime with HideAndDontSave — so there was
    /// no material inspector to put them in, and the only way to change a cloud was to edit the
    /// shader's default values and wait for a recompile.
    ///
    /// Everything here is pushed as a **global**, per the weather-state rule in _VFX.md: shaders are
    /// told the weather, they never ask. That is what lets a later tier (T2's boxes) read the same
    /// coverage as this layer with no reference to this component and no material wiring to forget —
    /// and it is why two clouds in one sky can't disagree about the weather.
    ///
    /// Drop next to TimeOfDaySky and scrub. No Play needed.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Fallcall/VFX/Cloud Controls")]
    public class CloudControls : MonoBehaviour
    {
        // Hashed once, not every frame. Same reason as TimeOfDaySky's ids.
        private static readonly int HeightId       = Shader.PropertyToID("_CloudHeight");
        private static readonly int ScaleId        = Shader.PropertyToID("_CloudScale");
        private static readonly int CoverageId     = Shader.PropertyToID("_CloudCoverage");
        private static readonly int SoftnessId     = Shader.PropertyToID("_CloudSoftness");
        private static readonly int OctavesId      = Shader.PropertyToID("_CloudOctaves");
        private static readonly int LacunarityId   = Shader.PropertyToID("_CloudLacunarity");
        private static readonly int GainId         = Shader.PropertyToID("_CloudGain");
        private static readonly int ErodeId        = Shader.PropertyToID("_CloudErode");
        private static readonly int HorizonFadeId  = Shader.PropertyToID("_CloudHorizonFade");
        private static readonly int SunGlowId      = Shader.PropertyToID("_CloudSunGlow");
        private static readonly int ShadowLiftId   = Shader.PropertyToID("_CloudShadowLift");

        // GlobalKeyword, not the string overloads: Shader.EnableKeyword("...") looks the name up on
        // every call, and Apply() runs every frame. These resolve once — but NOT in a field
        // initialiser. GlobalKeyword.Create touches Unity's keyword registry, which is off-limits
        // during MonoBehaviour construction; `static readonly` would run it in the type initialiser,
        // which Unity can trigger while deserialising this component, and it throws. Resolved on the
        // first Apply instead: every entry point goes through it, and all of them are past
        // construction.
        private static GlobalKeyword _layerKw;
        private static GlobalKeyword _basisValueKw;
        private static GlobalKeyword _basisWorleyKw;
        private static GlobalKeyword _basisErodeKw;
        private static bool _keywordsResolved;

        private static void EnsureKeywords()
        {
            if (_keywordsResolved) return;
            _layerKw       = GlobalKeyword.Create("_CLOUD_LAYER");
            _basisValueKw  = GlobalKeyword.Create("_CLOUDBASIS_VALUE");
            _basisWorleyKw = GlobalKeyword.Create("_CLOUDBASIS_WORLEY");
            _basisErodeKw  = GlobalKeyword.Create("_CLOUDBASIS_ERODE");
            _keywordsResolved = true;
        }

        [Header("Layer")]
        [Tooltip("Off leaves a clean sky. The keyword is global, so this also switches the cost off, " +
                 "not just the visibility.")]
        public bool EnableClouds = true;

        [Tooltip("Metres above the camera. The layer is camera-anchored, so this is an apparent scale " +
                 "knob more than a height — T0 has no parallax to make it a real distance.")]
        [Range(1f, 5000f)] public float Height = 800f;

        [Tooltip("Noise frequency. Small — the layer is projected at kilometre scale.")]
        [Range(0.0001f, 0.02f)] public float Scale = 0.0015f;

        [Header("Shape")]
        [Tooltip("Threshold on the noise field, not a multiply. Turn it up and clouds GROW AND MERGE " +
                 "into overcast. If they fade in evenly instead, something turned the threshold back " +
                 "into a multiply.")]
        [Range(0f, 1f)] public float Coverage = 0.45f;

        [Tooltip("Width of the threshold's ramp. Also feeds the fake self-shadowing depth.")]
        [Range(0.001f, 0.5f)] public float Softness = 0.12f;

        [Header("Basis")]
        [Tooltip("Which noise builds the field. Value is the verified baseline — switch to it and the " +
                 "sky must look exactly as it did on 2026-07-15.")]
        public CloudBasis Basis = CloudBasis.Value;

        [Tooltip("Erode basis only. How hard worley bites into the value-noise masses. 0 collapses " +
                 "Erode to exactly Value — a free correctness check on the whole erode path.")]
        [Range(0f, 1f)] public float ErodeStrength = 0.5f;

        [Header("fbm")]
        [Tooltip("Detail levels. The silhouette must NOT move as you raise this — only edge detail. " +
                 "If the whole sky brightens or darkens, the norm division in Fbm2D is broken.")]
        [Range(1, 8)] public int Octaves = 5;

        [Tooltip("How much finer each octave is. 2 = each octave is double the frequency.")]
        [Range(1.5f, 4f)] public float Lacunarity = 2f;

        [Tooltip("How much quieter each octave is. gain ~= 1/lacunarity is the natural-looking ratio; " +
                 "higher goes electric, lower and the fine octaves stop mattering.")]
        [Range(0.1f, 0.9f)] public float Gain = 0.5f;

        [Header("Compositing")]
        [Tooltip("Hides the singularity where the camera-anchored plane meets the horizon. Raise it if " +
                 "you see a hard band or aliasing crawl down there.")]
        [Range(0.001f, 0.5f)] public float HorizonFade = 0.06f;

        [Tooltip("How hard the sun punches through thin cloud.")]
        [Range(0f, 8f)] public float SunGlow = 2f;

        [Header("Lighting")]
        [Tooltip("Pulls the cloud SHADOW colour toward the cloud LIT colour. 0 is the ramp as " +
                 "authored; 1 makes shadow and lit the same colour and clouds go flat-lit. This is " +
                 "the contrast dial between a cloud's sunny side and its own shade — real clouds are " +
                 "bright in shadow because light bounces around inside them, and nothing here " +
                 "simulates that, so it's a knob. Applies to EVERY cloud tier at once, on purpose: " +
                 "it's a property of the weather, not of a tier.")]
        [Range(0f, 1f)] public float ShadowLift = 0f;

        private void OnEnable() => Apply();

        private void OnValidate() => Apply();

        // Every frame, so dragging a slider is live in both modes. Allocation-free — SetGlobalFloat and
        // SetKeyword both take value types, and the ids/keywords above are already resolved.
        private void Update() => Apply();

        /// <summary>Pushes every knob to the GPU as a global. Cheap; safe to call per frame.</summary>
        public void Apply()
        {
            EnsureKeywords();

            Shader.SetGlobalFloat(HeightId, Height);
            Shader.SetGlobalFloat(ScaleId, Scale);
            Shader.SetGlobalFloat(CoverageId, Coverage);
            Shader.SetGlobalFloat(SoftnessId, Softness);
            Shader.SetGlobalFloat(OctavesId, Octaves);
            Shader.SetGlobalFloat(LacunarityId, Lacunarity);
            Shader.SetGlobalFloat(GainId, Gain);
            Shader.SetGlobalFloat(ErodeId, ErodeStrength);
            Shader.SetGlobalFloat(HorizonFadeId, HorizonFade);
            Shader.SetGlobalFloat(SunGlowId, SunGlow);
            Shader.SetGlobalFloat(ShadowLiftId, ShadowLift);

            Shader.SetKeyword(_layerKw, EnableClouds);

            // Exactly one basis keyword on at a time. The shader's #else catches "none set" and falls
            // back to Value, so a half-applied state degrades to the baseline rather than to black.
            Shader.SetKeyword(_basisValueKw, Basis == CloudBasis.Value);
            Shader.SetKeyword(_basisWorleyKw, Basis == CloudBasis.Worley);
            Shader.SetKeyword(_basisErodeKw, Basis == CloudBasis.Erode);
        }

        // Global keyword state is global: it outlives this component and survives play-mode exit. Leave
        // _CLOUD_LAYER on after being disabled and the clouds keep rendering off stale globals with no
        // component in the scene to explain why.
        private void OnDisable()
        {
            // Not resolved means Apply never ran, so the keyword was never enabled and there is
            // nothing to turn off — and teardown is no place to start touching the keyword registry.
            if (!_keywordsResolved) return;
            Shader.SetKeyword(_layerKw, false);
        }
    }
}
