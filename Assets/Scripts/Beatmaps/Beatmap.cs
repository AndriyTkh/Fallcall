using System.Collections.Generic;
using UnityEngine;

namespace OsuUnity.Beatmaps
{
    public sealed class GeneralSection
    {
        public string AudioFilename;
        public int AudioLeadIn;
        public int PreviewTime = -1;
        public string SampleSet = "Normal";
        public double StackLeniency = 0.7;
        public int Mode; // 0 = osu!standard
    }

    public sealed class MetadataSection
    {
        public string Title = "";
        public string TitleUnicode = "";
        public string Artist = "";
        public string ArtistUnicode = "";
        public string Creator = "";
        public string Version = ""; // difficulty name
        public string Source = "";
        public string[] Tags = new string[0];
        public int BeatmapID;
        public int BeatmapSetID;
    }

    public sealed class DifficultySection
    {
        public float HPDrainRate = 5;
        public float CircleSize = 5;
        public float OverallDifficulty = 5;
        public float ApproachRate = 5;
        public double SliderMultiplier = 1.4;
        public double SliderTickRate = 1;
    }

    public struct BreakPeriod
    {
        public int Start;
        public int End;
    }

    public sealed class Beatmap
    {
        public int FormatVersion = 14;

        public GeneralSection General = new GeneralSection();
        public MetadataSection Metadata = new MetadataSection();
        public DifficultySection Difficulty = new DifficultySection();

        public List<Color> ComboColours = new List<Color>();
        public List<TimingPoint> TimingPoints = new List<TimingPoint>();
        public List<HitObject> HitObjects = new List<HitObject>();
        public List<BreakPeriod> Breaks = new List<BreakPeriod>();

        public string BackgroundFile;

        /// <summary>Absolute filesystem path of the .osu file this beatmap came from (if any).</summary>
        public string SourcePath;

        /// <summary>Directory that holds the audio / background / hit sounds.</summary>
        public string Directory;

        /// <summary>Last uninherited timing point at or before <paramref name="time"/>.</summary>
        public TimingPoint GetUninheritedTimingPointAt(int time)
        {
            TimingPoint result = null;
            foreach (var tp in TimingPoints)
            {
                if (!tp.Uninherited) continue;
                if (tp.Time <= time || result == null) result = tp;
                if (tp.Time > time) break;
            }
            return result;
        }

        /// <summary>Active timing point (inherited or not) at or before <paramref name="time"/>.</summary>
        public TimingPoint GetTimingPointAt(int time)
        {
            TimingPoint result = null;
            foreach (var tp in TimingPoints)
            {
                if (tp.Time <= time || result == null) result = tp;
                if (tp.Time > time) break;
            }
            return result;
        }

        public string DisplayTitle =>
            string.IsNullOrEmpty(Metadata.Title) ? "(unknown)" : Metadata.Title;
    }
}
