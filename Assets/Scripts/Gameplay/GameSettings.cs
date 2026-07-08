using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Runtime-adjustable, persisted game settings. Seeded from any <see cref="Osu3DSettings"/> in the
    /// scene (or built-in defaults), then overridden by previously saved values. The pause menu edits
    /// these live; <see cref="GameManager"/> reads them when building a play session.
    /// </summary>
    public static class GameSettings
    {
        private const string Prefix = "osu3d.";

        // Bump when the built-in defaults below change so existing installs adopt them instead of
        // masking them with stale saved PlayerPrefs (see Load).
        private const int DefaultsVersion = 12;

        public static float MusicVolume = 0.2f;
        public static float HitSoundVolume = 0.15f;
        public static float LookSensitivity = 1.4f;

        // Starting view mode for a session (applied on (re)start). [Tab] still cycles modes live in-game;
        // this just decides which one a session opens in. Overrides Curved for mode selection.
        public static ViewMode StartMode = ViewMode.Sphere;

        // 3D playfield tuning (applied on (re)start, not live).
        public static bool Curved = true;
        public static float PixelScale = 0.0135f;
        public static float ProjectionDistance = 3.5f;
        public static float ChunkHDegrees = 120f;
        public static float ChunkVDegrees = 90f;

        // Cursor (applied on (re)start). CursorHitboxOsu is in osu! pixels (scales with CS like the
        // circle radius); 0 = faithful point-in-circle hit test (osu! default), opt-in above that.
        public static float CursorSize = 1f;
        public static float CursorHitboxOsu = 1f;

        // Video background (applied on (re)start). Off skips VideoPlayback entirely for maps that have one.
        public static bool EnableVideo = true;

        // Ortho2D dynamic camera zoom (A.4). Enable + framing knobs apply live in that mode; the
        // group/stream *classification* thresholds are baked when the session builds, so they apply on
        // restart. Off = static full-field ortho framing.
        public static bool OrthoZoom = true;
        public static float OrthoZoomLeadMs = 350f;        // lead before the FIRST group's first note (live)
        public static float OrthoZoomSmooth = 0.22f;       // SmoothDamp time (s) for pan+zoom (live)
        public static float OrthoZoomMargin = 1.6f;        // padding around a group, in circle radii (live)
        // Click-group partitioning (baked on session build → applies on restart). Groups aim for
        // OrthoGroupTargetCount notes but are cut at a *pause* (the sightread window) rather than a fixed
        // gap, so group size floats with map density/difficulty. See OrthoZoomer.Build.
        // Master grouping knob (restart). 0 = calm: big groups sized by the Target/Max counts below.
        // 1 = hyperactive: target shrinks to 1 → the camera cuts to every native click-group (each little
        // gap-separated cluster in the map). Scales Target/Max toward tiny as it climbs.
        public static float OrthoAggressiveness = 0.3f;
        public static int OrthoGroupTargetCount = 16;      // notes per group at aggressiveness 0
        public static int OrthoGroupMaxCount = 28;         // hard cap at aggressiveness 0 (force a cut mid-stream)
        public static float OrthoGroupBreakGapMs = 160f;   // once at target, first pause >= this ends the group
        public static float OrthoGroupGapMs = 900f;        // a pause longer than this always ends a group
        public static float OrthoStreamGapMs = 130f;       // max object gap counted as a stream (restart)
        public static float OrthoStreamSpacingOsu = 130f;  // max spacing counted as a stream (restart)
        // Kiai time ("hyper" mode): during kiai groups the camera goes all-out — snappier transitions
        // (shorter cooldown) and a tighter, punchier frame (live).
        public static float OrthoKiaiSmoothMul = 0.5f;     // smoothing time multiplier in kiai (<1 = snappier)
        public static float OrthoKiaiZoomMul = 0.82f;      // ortho-size multiplier in kiai (<1 = tighter)

        // Falling view mode (A.5 / STRUCTURE §3c): flat gameplay, perspective camera projected onto a
        // sphere-cap above the plane, tilting toward the mouse. All live in that mode.
        public static float FallingRadius = 7f;            // camera sphere radius above the plane (world units)
        public static float FallingMaxTiltDeg = 18f;       // max camera tilt at the screen edge
        public static float FallingZoom = 0.9f;            // plane fills this fraction of the view height
        public static float FallingSmooth = 0.15f;         // SmoothDamp time (s) for the handheld camera drift

        // Background dim (applied live). 0 = untouched, 1 = fully black. Darkens everything behind the
        // gameplay layer — the video, the skybox, and any future far background scene — while leaving hit
        // objects and the cursor at full brightness (osu!'s "background dim").
        public static float BackgroundDim = 0.3f;

        // Follow points: the guide-arrow line between consecutive in-combo objects (STRUCTURE §4). The
        // toggle is baked when the session builds (applied on restart); the scale is read live each frame.
        public static bool ShowFollowPoints = true;
        public static float FollowPointScale = 1f;

        private static bool _loaded;

        // Snapshot of the defaults (built-in or scene-provided) captured at first load, so Reset can
        // restore them without re-reading the scene.
        private struct Defaults
        {
            public float MusicVolume, HitSoundVolume, LookSensitivity, PixelScale, ProjectionDistance, ChunkHDegrees, ChunkVDegrees;
            public float CursorSize, CursorHitboxOsu;
            public bool Curved;
            public ViewMode StartMode;
            public bool EnableVideo;
            public float BackgroundDim;
            public bool ShowFollowPoints;
            public float FollowPointScale;
            public bool OrthoZoom;
            public int OrthoGroupTargetCount, OrthoGroupMaxCount;
            public float OrthoAggressiveness, OrthoZoomLeadMs, OrthoZoomSmooth, OrthoZoomMargin, OrthoGroupBreakGapMs, OrthoGroupGapMs, OrthoStreamGapMs, OrthoStreamSpacingOsu, OrthoKiaiSmoothMul, OrthoKiaiZoomMul;
            public float FallingRadius, FallingMaxTiltDeg, FallingZoom, FallingSmooth;
        }
        private static Defaults _defaults;

        /// <summary>Load once: take scene defaults, then apply any saved PlayerPrefs on top.</summary>
        public static void Load(Osu3DSettings sceneDefaults)
        {
            if (_loaded) return;
            _loaded = true;

            if (sceneDefaults != null)
            {
                StartMode = sceneDefaults.StartMode;
                Curved = sceneDefaults.Curved;
                PixelScale = sceneDefaults.PixelScale;
                ProjectionDistance = sceneDefaults.ProjectionDistance;
                ChunkHDegrees = sceneDefaults.ChunkHDegrees;
                ChunkVDegrees = sceneDefaults.ChunkVDegrees;
                LookSensitivity = sceneDefaults.LookSensitivity;
                ShowFollowPoints = sceneDefaults.ShowFollowPoints;
                FollowPointScale = sceneDefaults.FollowPointScale;
                OrthoZoom = sceneDefaults.OrthoZoom;
                OrthoZoomLeadMs = sceneDefaults.OrthoZoomLeadMs;
                OrthoZoomSmooth = sceneDefaults.OrthoZoomSmooth;
                OrthoZoomMargin = sceneDefaults.OrthoZoomMargin;
                OrthoAggressiveness = sceneDefaults.OrthoAggressiveness;
                OrthoGroupTargetCount = sceneDefaults.OrthoGroupTargetCount;
                OrthoGroupMaxCount = sceneDefaults.OrthoGroupMaxCount;
                OrthoGroupBreakGapMs = sceneDefaults.OrthoGroupBreakGapMs;
                OrthoGroupGapMs = sceneDefaults.OrthoGroupGapMs;
                OrthoStreamGapMs = sceneDefaults.OrthoStreamGapMs;
                OrthoStreamSpacingOsu = sceneDefaults.OrthoStreamSpacingOsu;
                OrthoKiaiSmoothMul = sceneDefaults.OrthoKiaiSmoothMul;
                OrthoKiaiZoomMul = sceneDefaults.OrthoKiaiZoomMul;
                FallingRadius = sceneDefaults.FallingRadius;
                FallingMaxTiltDeg = sceneDefaults.FallingMaxTiltDeg;
                FallingZoom = sceneDefaults.FallingZoom;
                FallingSmooth = sceneDefaults.FallingSmooth;
            }

            // Capture the defaults before saved values override them.
            _defaults = new Defaults
            {
                MusicVolume = MusicVolume,
                HitSoundVolume = HitSoundVolume,
                LookSensitivity = LookSensitivity,
                StartMode = StartMode,
                Curved = Curved,
                PixelScale = PixelScale,
                ProjectionDistance = ProjectionDistance,
                ChunkHDegrees = ChunkHDegrees,
                ChunkVDegrees = ChunkVDegrees,
                CursorSize = CursorSize,
                CursorHitboxOsu = CursorHitboxOsu,
                EnableVideo = EnableVideo,
                BackgroundDim = BackgroundDim,
                ShowFollowPoints = ShowFollowPoints,
                FollowPointScale = FollowPointScale,
                OrthoZoom = OrthoZoom,
                OrthoZoomLeadMs = OrthoZoomLeadMs,
                OrthoZoomSmooth = OrthoZoomSmooth,
                OrthoZoomMargin = OrthoZoomMargin,
                OrthoAggressiveness = OrthoAggressiveness,
                OrthoGroupTargetCount = OrthoGroupTargetCount,
                OrthoGroupMaxCount = OrthoGroupMaxCount,
                OrthoGroupBreakGapMs = OrthoGroupBreakGapMs,
                OrthoGroupGapMs = OrthoGroupGapMs,
                OrthoStreamGapMs = OrthoStreamGapMs,
                OrthoStreamSpacingOsu = OrthoStreamSpacingOsu,
                OrthoKiaiSmoothMul = OrthoKiaiSmoothMul,
                OrthoKiaiZoomMul = OrthoKiaiZoomMul,
                FallingRadius = FallingRadius,
                FallingMaxTiltDeg = FallingMaxTiltDeg,
                FallingZoom = FallingZoom,
                FallingSmooth = FallingSmooth,
            };

            // First run on a build whose defaults changed: adopt the new defaults and persist them,
            // rather than letting stale saved values from an older default set mask them.
            if (PlayerPrefs.GetInt(Prefix + "ver", 0) != DefaultsVersion)
            {
                PlayerPrefs.SetInt(Prefix + "ver", DefaultsVersion);
                Save();
                return;
            }

            MusicVolume = PlayerPrefs.GetFloat(Prefix + "music", MusicVolume);
            HitSoundVolume = PlayerPrefs.GetFloat(Prefix + "hit", HitSoundVolume);
            LookSensitivity = PlayerPrefs.GetFloat(Prefix + "look", LookSensitivity);
            StartMode = (ViewMode)PlayerPrefs.GetInt(Prefix + "startmode", (int)StartMode);
            Curved = PlayerPrefs.GetInt(Prefix + "curved", Curved ? 1 : 0) != 0;
            PixelScale = PlayerPrefs.GetFloat(Prefix + "pixel", PixelScale);
            ProjectionDistance = PlayerPrefs.GetFloat(Prefix + "dist", ProjectionDistance);
            ChunkHDegrees = PlayerPrefs.GetFloat(Prefix + "hdeg", ChunkHDegrees);
            ChunkVDegrees = PlayerPrefs.GetFloat(Prefix + "vdeg", ChunkVDegrees);
            CursorSize = PlayerPrefs.GetFloat(Prefix + "cursize", CursorSize);
            CursorHitboxOsu = PlayerPrefs.GetFloat(Prefix + "curhitbox", CursorHitboxOsu);
            EnableVideo = PlayerPrefs.GetInt(Prefix + "video", EnableVideo ? 1 : 0) != 0;
            BackgroundDim = PlayerPrefs.GetFloat(Prefix + "bgdim", BackgroundDim);
            ShowFollowPoints = PlayerPrefs.GetInt(Prefix + "fp", ShowFollowPoints ? 1 : 0) != 0;
            FollowPointScale = PlayerPrefs.GetFloat(Prefix + "fpscale", FollowPointScale);
            OrthoZoom = PlayerPrefs.GetInt(Prefix + "ozoom", OrthoZoom ? 1 : 0) != 0;
            OrthoZoomLeadMs = PlayerPrefs.GetFloat(Prefix + "ozlead", OrthoZoomLeadMs);
            OrthoZoomSmooth = PlayerPrefs.GetFloat(Prefix + "ozsmooth", OrthoZoomSmooth);
            OrthoZoomMargin = PlayerPrefs.GetFloat(Prefix + "ozmargin", OrthoZoomMargin);
            OrthoAggressiveness = PlayerPrefs.GetFloat(Prefix + "ozaggr", OrthoAggressiveness);
            OrthoGroupTargetCount = PlayerPrefs.GetInt(Prefix + "oztarget", OrthoGroupTargetCount);
            OrthoGroupMaxCount = PlayerPrefs.GetInt(Prefix + "ozmax", OrthoGroupMaxCount);
            OrthoGroupBreakGapMs = PlayerPrefs.GetFloat(Prefix + "ozbreak", OrthoGroupBreakGapMs);
            OrthoGroupGapMs = PlayerPrefs.GetFloat(Prefix + "ozgap", OrthoGroupGapMs);
            OrthoStreamGapMs = PlayerPrefs.GetFloat(Prefix + "ozsgap", OrthoStreamGapMs);
            OrthoStreamSpacingOsu = PlayerPrefs.GetFloat(Prefix + "ozsspace", OrthoStreamSpacingOsu);
            OrthoKiaiSmoothMul = PlayerPrefs.GetFloat(Prefix + "ozksmooth", OrthoKiaiSmoothMul);
            OrthoKiaiZoomMul = PlayerPrefs.GetFloat(Prefix + "ozkzoom", OrthoKiaiZoomMul);
            FallingRadius = PlayerPrefs.GetFloat(Prefix + "fallr", FallingRadius);
            FallingMaxTiltDeg = PlayerPrefs.GetFloat(Prefix + "falltilt", FallingMaxTiltDeg);
            FallingZoom = PlayerPrefs.GetFloat(Prefix + "fallzoom", FallingZoom);
            FallingSmooth = PlayerPrefs.GetFloat(Prefix + "fallsmooth", FallingSmooth);
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat(Prefix + "music", MusicVolume);
            PlayerPrefs.SetFloat(Prefix + "hit", HitSoundVolume);
            PlayerPrefs.SetFloat(Prefix + "look", LookSensitivity);
            PlayerPrefs.SetInt(Prefix + "startmode", (int)StartMode);
            PlayerPrefs.SetInt(Prefix + "curved", Curved ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "pixel", PixelScale);
            PlayerPrefs.SetFloat(Prefix + "dist", ProjectionDistance);
            PlayerPrefs.SetFloat(Prefix + "hdeg", ChunkHDegrees);
            PlayerPrefs.SetFloat(Prefix + "vdeg", ChunkVDegrees);
            PlayerPrefs.SetFloat(Prefix + "cursize", CursorSize);
            PlayerPrefs.SetFloat(Prefix + "curhitbox", CursorHitboxOsu);
            PlayerPrefs.SetInt(Prefix + "video", EnableVideo ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "bgdim", BackgroundDim);
            PlayerPrefs.SetInt(Prefix + "fp", ShowFollowPoints ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "fpscale", FollowPointScale);
            PlayerPrefs.SetInt(Prefix + "ozoom", OrthoZoom ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "ozlead", OrthoZoomLeadMs);
            PlayerPrefs.SetFloat(Prefix + "ozsmooth", OrthoZoomSmooth);
            PlayerPrefs.SetFloat(Prefix + "ozmargin", OrthoZoomMargin);
            PlayerPrefs.SetFloat(Prefix + "ozaggr", OrthoAggressiveness);
            PlayerPrefs.SetInt(Prefix + "oztarget", OrthoGroupTargetCount);
            PlayerPrefs.SetInt(Prefix + "ozmax", OrthoGroupMaxCount);
            PlayerPrefs.SetFloat(Prefix + "ozbreak", OrthoGroupBreakGapMs);
            PlayerPrefs.SetFloat(Prefix + "ozgap", OrthoGroupGapMs);
            PlayerPrefs.SetFloat(Prefix + "ozsgap", OrthoStreamGapMs);
            PlayerPrefs.SetFloat(Prefix + "ozsspace", OrthoStreamSpacingOsu);
            PlayerPrefs.SetFloat(Prefix + "ozksmooth", OrthoKiaiSmoothMul);
            PlayerPrefs.SetFloat(Prefix + "ozkzoom", OrthoKiaiZoomMul);
            PlayerPrefs.SetFloat(Prefix + "fallr", FallingRadius);
            PlayerPrefs.SetFloat(Prefix + "falltilt", FallingMaxTiltDeg);
            PlayerPrefs.SetFloat(Prefix + "fallzoom", FallingZoom);
            PlayerPrefs.SetFloat(Prefix + "fallsmooth", FallingSmooth);
            PlayerPrefs.Save();
        }

        /// <summary>Restore the captured defaults and persist them.</summary>
        public static void Reset()
        {
            MusicVolume = _defaults.MusicVolume;
            HitSoundVolume = _defaults.HitSoundVolume;
            LookSensitivity = _defaults.LookSensitivity;
            StartMode = _defaults.StartMode;
            Curved = _defaults.Curved;
            PixelScale = _defaults.PixelScale;
            ProjectionDistance = _defaults.ProjectionDistance;
            ChunkHDegrees = _defaults.ChunkHDegrees;
            ChunkVDegrees = _defaults.ChunkVDegrees;
            CursorSize = _defaults.CursorSize;
            CursorHitboxOsu = _defaults.CursorHitboxOsu;
            EnableVideo = _defaults.EnableVideo;
            BackgroundDim = _defaults.BackgroundDim;
            ShowFollowPoints = _defaults.ShowFollowPoints;
            FollowPointScale = _defaults.FollowPointScale;
            OrthoZoom = _defaults.OrthoZoom;
            OrthoZoomLeadMs = _defaults.OrthoZoomLeadMs;
            OrthoZoomSmooth = _defaults.OrthoZoomSmooth;
            OrthoZoomMargin = _defaults.OrthoZoomMargin;
            OrthoAggressiveness = _defaults.OrthoAggressiveness;
            OrthoGroupTargetCount = _defaults.OrthoGroupTargetCount;
            OrthoGroupMaxCount = _defaults.OrthoGroupMaxCount;
            OrthoGroupBreakGapMs = _defaults.OrthoGroupBreakGapMs;
            OrthoGroupGapMs = _defaults.OrthoGroupGapMs;
            OrthoStreamGapMs = _defaults.OrthoStreamGapMs;
            OrthoStreamSpacingOsu = _defaults.OrthoStreamSpacingOsu;
            OrthoKiaiSmoothMul = _defaults.OrthoKiaiSmoothMul;
            OrthoKiaiZoomMul = _defaults.OrthoKiaiZoomMul;
            FallingRadius = _defaults.FallingRadius;
            FallingMaxTiltDeg = _defaults.FallingMaxTiltDeg;
            FallingZoom = _defaults.FallingZoom;
            FallingSmooth = _defaults.FallingSmooth;
            Save();
        }
    }
}
