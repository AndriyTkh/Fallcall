using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace OsuUnity.Skinning
{
    /// <summary>
    /// Parsed <c>skin.ini</c> values (only the subset this game uses). Format reference:
    /// https://osu.ppy.sh/wiki/en/Skinning/skin.ini — sections [General], [Colours], [Fonts].
    /// All values fall back to osu!'s documented defaults when absent.
    /// </summary>
    public sealed class SkinConfig
    {
        // [General]
        public string Name = "";
        public string Author = "";
        public bool HitCircleOverlayAboveNumber = true; // default 1
        public bool AllowSliderBallTint = false;
        public bool CursorCentre = true;
        public bool CursorRotate = true;
        public bool CursorExpand = true;
        public float AnimationFramerate = -1f;

        // [Fonts]
        public string HitCirclePrefix = "default";
        public int HitCircleOverlap = -2;
        public string ScorePrefix = "score";
        public int ScoreOverlap = 0;
        public string ComboPrefix = "score";
        public int ComboOverlap = 0;

        // [Colours]
        public readonly List<Color> ComboColours = new List<Color>();
        public Color? SliderBorder;
        public Color? SliderTrackOverride;

        public static SkinConfig Parse(string iniPath)
        {
            var cfg = new SkinConfig();
            if (string.IsNullOrEmpty(iniPath) || !File.Exists(iniPath)) return cfg;

            // Combo colours can appear out of order (Combo1..Combo8); collect then sort by index.
            var combos = new SortedDictionary<int, Color>();
            string section = "";

            foreach (string raw in File.ReadLines(iniPath))
            {
                string line = StripComment(raw).Trim();
                if (line.Length == 0) continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                    continue;
                }

                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key = line.Substring(0, colon).Trim();
                string val = line.Substring(colon + 1).Trim();

                switch (section)
                {
                    case "general": ApplyGeneral(cfg, key, val); break;
                    case "fonts": ApplyFonts(cfg, key, val); break;
                    case "colours": ApplyColours(cfg, combos, key, val); break;
                }
            }

            foreach (var kv in combos) cfg.ComboColours.Add(kv.Value);
            return cfg;
        }

        private static void ApplyGeneral(SkinConfig c, string key, string val)
        {
            switch (key.ToLowerInvariant())
            {
                case "name": c.Name = val; break;
                case "author": c.Author = val; break;
                case "hitcircleoverlayabovenumber": c.HitCircleOverlayAboveNumber = ParseBool(val); break;
                case "allowsliderballtint": c.AllowSliderBallTint = ParseBool(val); break;
                case "cursorcentre": c.CursorCentre = ParseBool(val); break;
                case "cursorrotate": c.CursorRotate = ParseBool(val); break;
                case "cursorexpand": c.CursorExpand = ParseBool(val); break;
                case "animationframerate": c.AnimationFramerate = ParseFloat(val, -1f); break;
            }
        }

        private static void ApplyFonts(SkinConfig c, string key, string val)
        {
            switch (key.ToLowerInvariant())
            {
                case "hitcircleprefix": c.HitCirclePrefix = val; break;
                case "hitcircleoverlap": c.HitCircleOverlap = ParseInt(val, -2); break;
                case "scoreprefix": c.ScorePrefix = val; break;
                case "scoreoverlap": c.ScoreOverlap = ParseInt(val, 0); break;
                case "comboprefix": c.ComboPrefix = val; break;
                case "combooverlap": c.ComboOverlap = ParseInt(val, 0); break;
            }
        }

        private static void ApplyColours(SkinConfig c, SortedDictionary<int, Color> combos, string key, string val)
        {
            string k = key.ToLowerInvariant();
            if (k.StartsWith("combo"))
            {
                if (int.TryParse(k.Substring(5), out int idx) && TryParseColour(val, out Color col))
                    combos[idx] = col;
            }
            else if (k == "sliderborder" && TryParseColour(val, out Color sb)) c.SliderBorder = sb;
            else if (k == "slidertrackoverride" && TryParseColour(val, out Color st)) c.SliderTrackOverride = st;
        }

        // ----------------------------------------------------------------- helpers

        private static string StripComment(string line)
        {
            int i = line.IndexOf("//");
            return i >= 0 ? line.Substring(0, i) : line;
        }

        private static bool ParseBool(string v) => v.Trim() == "1" ||
            v.Trim().Equals("true", System.StringComparison.OrdinalIgnoreCase);

        private static int ParseInt(string v, int fallback) =>
            int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : fallback;

        private static float ParseFloat(string v, float fallback) =>
            float.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : fallback;

        private static bool TryParseColour(string v, out Color c)
        {
            c = Color.white;
            string[] parts = v.Split(',');
            if (parts.Length < 3) return false;
            if (!byte.TryParse(parts[0].Trim(), out byte r)) return false;
            if (!byte.TryParse(parts[1].Trim(), out byte g)) return false;
            if (!byte.TryParse(parts[2].Trim(), out byte b)) return false;
            byte a = 255;
            if (parts.Length >= 4) byte.TryParse(parts[3].Trim(), out a);
            c = new Color32(r, g, b, a);
            return true;
        }
    }
}
