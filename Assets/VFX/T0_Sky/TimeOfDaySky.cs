using UnityEngine;
using UnityEngine.Rendering;

namespace OsuUnity.VFX.Sky
{
    /// <summary>
    /// The master scalar, made real. TimeOfDay drives a sun direction; the sun's height becomes one
    /// number, <c>_SunElevation01</c>; sky, ambient, fog and the directional light all read that number
    /// out of the same <see cref="SkyRamp"/>. One scalar in, a whole sky out.
    ///
    /// Every later tier hangs off this: T2 clouds will sample the same LUT for lit/shadow colour rather
    /// than growing their own notion of time.
    ///
    /// Drop on an empty GameObject and scrub TimeOfDay. Works in edit mode — no Play needed.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Fallcall/VFX/Time Of Day Sky")]
    public class TimeOfDaySky : MonoBehaviour
    {
        // Global shader IDs. Strings hash to these once, not every frame.
        private static readonly int SkyRampLutId     = Shader.PropertyToID("_SkyRampLut");
        private static readonly int SunElevation01Id = Shader.PropertyToID("_SunElevation01");
        private static readonly int SunDirectionId   = Shader.PropertyToID("_SunDirection");
        private static readonly int CloudWindOffsetId = Shader.PropertyToID("_CloudWindOffset");

        private const string SkyShaderName = "Fallcall/VFX/T0_Sky";

        [Header("Time")]
        [Tooltip("Hours, 0..24. The one input. Scrub it.")]
        [Range(0f, 24f)] public float TimeOfDay = 9f;

        [Tooltip("Advance TimeOfDay automatically in play mode. Off by default — scrubbing beats waiting.")]
        public bool Animate;

        [Tooltip("Real seconds for one full 24h cycle.")]
        [Min(0.1f)] public float SecondsPerDay = 120f;

        [Tooltip("Maps cycle progress (0..1) to hours (0..24). Linear = a plain clock. Flatten it around " +
                 "dusk to dwell there; steepen it over night to skip it. Decoupled from realtime on purpose.")]
        public AnimationCurve DayCurve = AnimationCurve.Linear(0f, 0f, 1f, 24f);

        [Header("Sun")]
        [Tooltip("Tilts the sun's arc away from straight overhead. 0 = sun passes through the zenith.")]
        [Range(-90f, 90f)] public float LatitudeTilt = 25f;

        [Tooltip("Spins the whole arc around Y. Which way is 'east'.")]
        [Range(0f, 360f)] public float NorthOffset;

        [Tooltip("Directional light to drive. Auto-found if left empty.")]
        public Light Sun;

        [Tooltip("Intensity at full noon. The ramp's alpha row scales this 0..1.")]
        [Min(0f)] public float MaxSunIntensity = 1.5f;

        [Header("Wind")]
        [Tooltip("Compass direction the cloud layer drifts. Normalised on use.")]
        public Vector2 WindDirection = new Vector2(1f, 0.35f);

        [Tooltip("Noise-space units the cloud layer drifts per in-game hour. Small values — the layer " +
                 "is projected at kilometre scale, so a little goes a long way.")]
        public float WindSpeed = 0.02f;

        [Header("Ramp")]
        [Tooltip("The LUT. Leave empty for a built-in default (Create > Fallcall > VFX > Sky Ramp to author one).")]
        public SkyRamp Ramp;

        public bool DriveSky = true;
        public bool DriveAmbient = true;
        public bool DriveFog = true;

        /// <summary>World-space direction pointing at the sun. Read-only, recomputed each Apply.</summary>
        public Vector3 SunDirection { get; private set; }

        /// <summary>Sun height remapped to the LUT's x axis, 0 = straight down, 0.5 = horizon, 1 = zenith.</summary>
        public float SunElevation01 { get; private set; }

        private SkyRamp _fallbackRamp;
        private Material _skyMaterial;
        private Material _previousSkybox;
        private bool _tookOverSkybox;
        private float _cycle;

        private void OnEnable() => Apply();

        private void OnValidate() => Apply();

        private void Update()
        {
            if (Animate && Application.isPlaying)
            {
                _cycle = Mathf.Repeat(_cycle + Time.deltaTime / SecondsPerDay, 1f);
                TimeOfDay = Mathf.Repeat(DayCurve.Evaluate(_cycle), 24f);
            }

            Apply();
        }

        /// <summary>Recomputes the sun and pushes everything downstream. Cheap and allocation-free —
        /// safe to call every frame, which is what makes scrubbing feel live.</summary>
        public void Apply()
        {
            var ramp = ResolveRamp();
            if (ramp == null) return;

            SunDirection = ComputeSunDirection(TimeOfDay, LatitudeTilt, NorthOffset);

            // sin(elevation), not elevation in degrees. Both are monotonic in sun height, but sin changes
            // fastest at the horizon, which is exactly where the colours change fastest — so the LUT spends
            // its texels where they're needed instead of on a featureless noon.
            SunElevation01 = SunDirection.y * 0.5f + 0.5f;

            PushGlobals(ramp);
            DriveLight(ramp);
            if (DriveSky) DriveSkybox();
            if (DriveAmbient) DriveAmbientLight(ramp);
            if (DriveFog) RenderSettings.fogColor = ramp.Sample(SkyRamp.RowFog, SunElevation01);
        }

        /// <summary>
        /// The sun rides a great circle. At 06:00 it sits due east on the horizon, at 12:00 it hits the
        /// arc's high point, at 18:00 due west, at 00:00 it is directly opposite the high point. Tilting
        /// the high point away from the zenith by <paramref name="latitudeTilt"/> tilts the whole arc —
        /// that is the entire model, and it is enough: it gives correct-feeling low winter suns and
        /// overhead tropical ones without any real astronomy.
        /// </summary>
        private static Vector3 ComputeSunDirection(float timeOfDay, float latitudeTilt, float northOffset)
        {
            // +latitudeTilt leans the arc's peak from up (+Y) toward north (+Z).
            var yaw = Quaternion.AngleAxis(northOffset, Vector3.up);
            Vector3 east = yaw * Vector3.right;
            Vector3 peak = yaw * (Quaternion.AngleAxis(latitudeTilt, Vector3.right) * Vector3.up);

            // theta = 0 at 06:00 (sun on the east horizon), 90 deg at 12:00 (sun at the peak).
            float theta = (timeOfDay - 6f) / 24f * Mathf.PI * 2f;
            return (Mathf.Cos(theta) * east + Mathf.Sin(theta) * peak).normalized;
        }

        private void PushGlobals(SkyRamp ramp)
        {
            // Globals, not per-material. Every cloud tier that lands later gets the sky's state for free,
            // with no reference to this component and no material wiring to forget.
            Shader.SetGlobalTexture(SkyRampLutId, ramp.Lut);
            Shader.SetGlobalFloat(SunElevation01Id, SunElevation01);
            Shader.SetGlobalVector(SunDirectionId, SunDirection);

            // Wind is a pure function of TimeOfDay, not an accumulator over _Time. That makes it
            // deterministic and scrubbable — drag the slider back and the clouds rewind with it — and
            // it means edit mode, where there is no reliable deltaTime, works for free.
            // Cost: a visible jump at the 24->0 wrap. Fine for a scratch layer; see the plan.
            Vector2 wind = WindDirection.normalized * (WindSpeed * TimeOfDay);
            Shader.SetGlobalVector(CloudWindOffsetId, new Vector4(wind.x, wind.y, 0f, 0f));
        }

        private void DriveLight(SkyRamp ramp)
        {
            if (Sun == null) Sun = FindDirectionalLight();
            if (Sun == null) return;

            // A light shines from the sun toward the scene, so its forward is the opposite of SunDirection.
            Sun.transform.rotation = Quaternion.LookRotation(-SunDirection, Vector3.up);

            var c = ramp.Sample(SkyRamp.RowSunLight, SunElevation01);
            Sun.color = new Color(c.r, c.g, c.b, 1f);
            Sun.intensity = c.a * MaxSunIntensity;   // alpha is the intensity curve, see SkyRamp
        }

        private void DriveSkybox()
        {
            if (_skyMaterial == null)
            {
                var shader = Shader.Find(SkyShaderName);
                if (shader == null) return;   // shader missing — leave whatever skybox is set, don't go magenta
                _skyMaterial = new Material(shader)
                {
                    name = "T0_Sky (runtime)",
                    hideFlags = HideFlags.HideAndDontSave,   // scratch material, must never be saved into the scene
                };
            }

            if (RenderSettings.skybox != _skyMaterial)
            {
                if (!_tookOverSkybox)
                {
                    _previousSkybox = RenderSettings.skybox;
                    _tookOverSkybox = true;
                }
                RenderSettings.skybox = _skyMaterial;
            }
        }

        private void DriveAmbientLight(SkyRamp ramp)
        {
            // Flat, not Skybox: skybox ambient needs a GPU convolution pass per change, which would fire
            // every frame while scrubbing. The ramp already knows the answer.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ramp.Sample(SkyRamp.RowAmbient, SunElevation01);
        }

        private SkyRamp ResolveRamp()
        {
            if (Ramp != null) return Ramp;
            if (_fallbackRamp == null)
            {
                _fallbackRamp = ScriptableObject.CreateInstance<SkyRamp>();
                _fallbackRamp.name = "SkyRamp (default)";
                _fallbackRamp.hideFlags = HideFlags.HideAndDontSave;
            }
            return _fallbackRamp;
        }

        private static Light FindDirectionalLight()
        {
            foreach (var l in FindObjectsOfType<Light>())
                if (l.type == LightType.Directional) return l;
            return null;
        }

        private void OnDisable()
        {
            // Hand the skybox back before destroying ours, or RenderSettings keeps a destroyed material
            // and the scene renders black until something else reassigns it.
            if (_tookOverSkybox && RenderSettings.skybox == _skyMaterial)
                RenderSettings.skybox = _previousSkybox;
            _tookOverSkybox = false;

            if (_skyMaterial != null) DestroyImmediate(_skyMaterial);
            _skyMaterial = null;
            if (_fallbackRamp != null) DestroyImmediate(_fallbackRamp);
            _fallbackRamp = null;
        }
    }
}
