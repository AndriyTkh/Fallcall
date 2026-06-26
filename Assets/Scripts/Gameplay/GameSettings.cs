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

        public static float MusicVolume = 0.6f;
        public static float HitSoundVolume = 0.5f;
        public static float LookSensitivity = 3f;

        // 3D playfield tuning (applied on (re)start, not live).
        public static bool Curved = true;
        public static float PixelScale = 0.01f;
        public static float ProjectionDistance = 3f;
        public static float ArcDegrees = 0f;

        private static bool _loaded;

        // Snapshot of the defaults (built-in or scene-provided) captured at first load, so Reset can
        // restore them without re-reading the scene.
        private struct Defaults
        {
            public float MusicVolume, HitSoundVolume, LookSensitivity, PixelScale, ProjectionDistance, ArcDegrees;
            public bool Curved;
        }
        private static Defaults _defaults;

        /// <summary>Load once: take scene defaults, then apply any saved PlayerPrefs on top.</summary>
        public static void Load(Osu3DSettings sceneDefaults)
        {
            if (_loaded) return;
            _loaded = true;

            if (sceneDefaults != null)
            {
                Curved = sceneDefaults.Curved;
                PixelScale = sceneDefaults.PixelScale;
                ProjectionDistance = sceneDefaults.ProjectionDistance;
                ArcDegrees = sceneDefaults.ArcDegrees;
                LookSensitivity = sceneDefaults.LookSensitivity;
            }

            // Capture the defaults before saved values override them.
            _defaults = new Defaults
            {
                MusicVolume = MusicVolume,
                HitSoundVolume = HitSoundVolume,
                LookSensitivity = LookSensitivity,
                Curved = Curved,
                PixelScale = PixelScale,
                ProjectionDistance = ProjectionDistance,
                ArcDegrees = ArcDegrees,
            };

            MusicVolume = PlayerPrefs.GetFloat(Prefix + "music", MusicVolume);
            HitSoundVolume = PlayerPrefs.GetFloat(Prefix + "hit", HitSoundVolume);
            LookSensitivity = PlayerPrefs.GetFloat(Prefix + "look", LookSensitivity);
            Curved = PlayerPrefs.GetInt(Prefix + "curved", Curved ? 1 : 0) != 0;
            PixelScale = PlayerPrefs.GetFloat(Prefix + "pixel", PixelScale);
            ProjectionDistance = PlayerPrefs.GetFloat(Prefix + "dist", ProjectionDistance);
            ArcDegrees = PlayerPrefs.GetFloat(Prefix + "arc", ArcDegrees);
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat(Prefix + "music", MusicVolume);
            PlayerPrefs.SetFloat(Prefix + "hit", HitSoundVolume);
            PlayerPrefs.SetFloat(Prefix + "look", LookSensitivity);
            PlayerPrefs.SetInt(Prefix + "curved", Curved ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "pixel", PixelScale);
            PlayerPrefs.SetFloat(Prefix + "dist", ProjectionDistance);
            PlayerPrefs.SetFloat(Prefix + "arc", ArcDegrees);
            PlayerPrefs.Save();
        }

        /// <summary>Restore the captured defaults and persist them.</summary>
        public static void Reset()
        {
            MusicVolume = _defaults.MusicVolume;
            HitSoundVolume = _defaults.HitSoundVolume;
            LookSensitivity = _defaults.LookSensitivity;
            Curved = _defaults.Curved;
            PixelScale = _defaults.PixelScale;
            ProjectionDistance = _defaults.ProjectionDistance;
            ArcDegrees = _defaults.ArcDegrees;
            Save();
        }
    }
}
