using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace OsuUnity.Beatmaps
{
    /// <summary>Parses the osu! (.osu) file format, standard mode (Mode 0).</summary>
    public static class BeatmapParser
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static Beatmap ParseFile(string path)
        {
            var map = ParseText(File.ReadAllText(path));
            map.SourcePath = path;
            map.Directory = Path.GetDirectoryName(path);
            return map;
        }

        public static Beatmap ParseText(string text)
        {
            var map = new Beatmap();
            string section = "";
            _sawApproachRate = false;

            using var reader = new StringReader(text);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("//")) continue;

                if (trimmed.StartsWith("osu file format v"))
                {
                    int.TryParse(trimmed.Substring("osu file format v".Length), NumberStyles.Integer, Inv, out map.FormatVersion);
                    continue;
                }

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    section = trimmed.Substring(1, trimmed.Length - 2);
                    continue;
                }

                try
                {
                    ParseLine(map, section, trimmed);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[BeatmapParser] Skipped line in [{section}]: '{trimmed}' -> {e.Message}");
                }
            }

            DifficultyCalculator.Process(map);
            return map;
        }

        private static void ParseLine(Beatmap map, string section, string line)
        {
            switch (section)
            {
                case "General": ParseKeyValue(line, (k, v) => ParseGeneral(map, k, v)); break;
                case "Metadata": ParseKeyValue(line, (k, v) => ParseMetadata(map, k, v)); break;
                case "Difficulty": ParseKeyValue(line, (k, v) => ParseDifficulty(map, k, v)); break;
                case "Colours": ParseKeyValue(line, (k, v) => ParseColour(map, k, v)); break;
                case "Events": ParseEvent(map, line); break;
                case "TimingPoints": ParseTimingPoint(map, line); break;
                case "HitObjects": ParseHitObject(map, line); break;
            }
        }

        private static void ParseKeyValue(string line, Action<string, string> handler)
        {
            int idx = line.IndexOf(':');
            if (idx < 0) return;
            string key = line.Substring(0, idx).Trim();
            string value = line.Substring(idx + 1).Trim();
            handler(key, value);
        }

        private static void ParseGeneral(Beatmap map, string k, string v)
        {
            switch (k)
            {
                case "AudioFilename": map.General.AudioFilename = v; break;
                case "AudioLeadIn": map.General.AudioLeadIn = ParseInt(v); break;
                case "PreviewTime": map.General.PreviewTime = ParseInt(v); break;
                case "SampleSet": map.General.SampleSet = v; break;
                case "StackLeniency": map.General.StackLeniency = ParseDouble(v); break;
                case "Mode": map.General.Mode = ParseInt(v); break;
            }
        }

        private static void ParseMetadata(Beatmap map, string k, string v)
        {
            switch (k)
            {
                case "Title": map.Metadata.Title = v; break;
                case "TitleUnicode": map.Metadata.TitleUnicode = v; break;
                case "Artist": map.Metadata.Artist = v; break;
                case "ArtistUnicode": map.Metadata.ArtistUnicode = v; break;
                case "Creator": map.Metadata.Creator = v; break;
                case "Version": map.Metadata.Version = v; break;
                case "Source": map.Metadata.Source = v; break;
                case "Tags": map.Metadata.Tags = v.Split(' '); break;
                case "BeatmapID": map.Metadata.BeatmapID = ParseInt(v); break;
                case "BeatmapSetID": map.Metadata.BeatmapSetID = ParseInt(v); break;
            }
        }

        private static void ParseDifficulty(Beatmap map, string k, string v)
        {
            switch (k)
            {
                case "HPDrainRate": map.Difficulty.HPDrainRate = ParseFloat(v); break;
                case "CircleSize": map.Difficulty.CircleSize = ParseFloat(v); break;
                case "OverallDifficulty":
                    map.Difficulty.OverallDifficulty = ParseFloat(v);
                    // AR defaults to OD when not present (old maps); overwritten if AR line exists.
                    if (!_sawApproachRate) map.Difficulty.ApproachRate = map.Difficulty.OverallDifficulty;
                    break;
                case "ApproachRate":
                    map.Difficulty.ApproachRate = ParseFloat(v);
                    _sawApproachRate = true;
                    break;
                case "SliderMultiplier": map.Difficulty.SliderMultiplier = ParseDouble(v); break;
                case "SliderTickRate": map.Difficulty.SliderTickRate = ParseDouble(v); break;
            }
        }

        // Reset per parse via ParseText -> new Beatmap; tracked statically only within a single file parse.
        [ThreadStatic] private static bool _sawApproachRate;

        private static void ParseColour(Beatmap map, string k, string v)
        {
            if (!k.StartsWith("Combo")) return;
            string[] parts = v.Split(',');
            if (parts.Length < 3) return;
            float r = ParseInt(parts[0]) / 255f;
            float g = ParseInt(parts[1]) / 255f;
            float b = ParseInt(parts[2]) / 255f;
            map.ComboColours.Add(new Color(r, g, b, 1f));
        }

        private static void ParseEvent(Beatmap map, string line)
        {
            string[] p = line.Split(',');
            if (p.Length < 2) return;

            // Background: "0,0,\"bg.jpg\",0,0"
            if ((p[0] == "0" || p[0] == "Background") && p.Length >= 3)
            {
                map.BackgroundFile = p[2].Trim().Trim('"');
            }
            // Break period: "2,start,end" or "Break,start,end"
            else if ((p[0] == "2" || p[0] == "Break") && p.Length >= 3)
            {
                map.Breaks.Add(new BreakPeriod { Start = ParseInt(p[1]), End = ParseInt(p[2]) });
            }
        }

        private static void ParseTimingPoint(Beatmap map, string line)
        {
            string[] p = line.Split(',');
            if (p.Length < 2) return;

            var tp = new TimingPoint
            {
                Time = (int)ParseDouble(p[0]),
                BeatLength = ParseDouble(p[1]),
            };
            if (p.Length > 2) tp.Meter = ParseInt(p[2]);
            if (p.Length > 3) tp.SampleSet = ParseInt(p[3]);
            if (p.Length > 4) tp.SampleIndex = ParseInt(p[4]);
            if (p.Length > 5) tp.Volume = ParseInt(p[5]);
            tp.Uninherited = p.Length > 6 ? ParseInt(p[6]) != 0 : true;
            if (p.Length > 7) tp.Effects = ParseInt(p[7]);

            map.TimingPoints.Add(tp);
        }

        private static void ParseHitObject(Beatmap map, string line)
        {
            string[] p = line.Split(',');
            if (p.Length < 4) return;

            int x = ParseInt(p[0]);
            int y = ParseInt(p[1]);
            int time = ParseInt(p[2]);
            int rawType = ParseInt(p[3]);
            var type = (HitObjectType)rawType;
            var sound = p.Length > 4 ? (HitSoundType)ParseInt(p[4]) : HitSoundType.Normal;

            bool newCombo = (rawType & (int)HitObjectType.NewCombo) != 0;
            int comboSkip = (rawType & (int)HitObjectType.ComboSkipMask) >> 4;

            HitObject ho;

            if ((rawType & (int)HitObjectType.Slider) != 0)
            {
                ho = ParseSlider(p, x, y);
            }
            else if ((rawType & (int)HitObjectType.Spinner) != 0)
            {
                int end = p.Length > 5 ? ParseInt(p[5]) : time;
                ho = new Spinner { SpinnerEndTime = end };
            }
            else
            {
                ho = new HitCircle();
            }

            ho.Position = new Vector2(x, y);
            ho.StartTime = time;
            ho.Type = type;
            ho.HitSound = sound;
            ho.IsNewCombo = newCombo;
            ho.ComboColourSkip = comboSkip;

            map.HitObjects.Add(ho);
        }

        private static Slider ParseSlider(string[] p, int x, int y)
        {
            var slider = new Slider();
            slider.ControlPoints.Add(new Vector2(x, y));

            // p[5] = curveType|x:y|x:y|...
            if (p.Length > 5)
            {
                string[] curve = p[5].Split('|');
                slider.CurveType = ParseCurveType(curve[0]);
                for (int i = 1; i < curve.Length; i++)
                {
                    string[] xy = curve[i].Split(':');
                    if (xy.Length == 2)
                        slider.ControlPoints.Add(new Vector2(ParseInt(xy[0]), ParseInt(xy[1])));
                }
            }

            slider.Slides = p.Length > 6 ? Math.Max(1, ParseInt(p[6])) : 1;
            slider.PixelLength = p.Length > 7 ? ParseDouble(p[7]) : 0;

            // p[8] = edge hit sounds "2|0|2"
            if (p.Length > 8)
            {
                foreach (string s in p[8].Split('|'))
                    if (int.TryParse(s, out int v)) slider.EdgeSounds.Add((HitSoundType)v);
            }

            return slider;
        }

        private static SliderCurveType ParseCurveType(string s)
        {
            switch (s)
            {
                case "L": return SliderCurveType.Linear;
                case "P": return SliderCurveType.PerfectCircle;
                case "C": return SliderCurveType.Catmull;
                case "B":
                default: return SliderCurveType.Bezier;
            }
        }

        // --- primitive parsing helpers (invariant culture, tolerant) ---

        private static int ParseInt(string s) =>
            int.TryParse(s.Trim(), NumberStyles.Integer, Inv, out int v) ? v : 0;

        private static float ParseFloat(string s) =>
            float.TryParse(s.Trim(), NumberStyles.Float, Inv, out float v) ? v : 0f;

        private static double ParseDouble(string s) =>
            double.TryParse(s.Trim(), NumberStyles.Float, Inv, out double v) ? v : 0.0;
    }
}
