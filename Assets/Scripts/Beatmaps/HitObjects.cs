using System.Collections.Generic;
using UnityEngine;

namespace OsuUnity.Beatmaps
{
    /// <summary>
    /// The "type" bitfield of a hit object line in a .osu file.
    /// See https://osu.ppy.sh/wiki/en/Client/File_formats/osu_%28file_format%29
    /// </summary>
    [System.Flags]
    public enum HitObjectType
    {
        Circle = 1 << 0,   // 1
        Slider = 1 << 1,   // 2
        NewCombo = 1 << 2, // 4
        Spinner = 1 << 3,  // 8
        // bits 4-6 encode how many combo colours to skip on a new combo
        ComboSkipMask = (1 << 4) | (1 << 5) | (1 << 6),
        ManiaHold = 1 << 7 // 128 (osu!mania only)
    }

    /// <summary>Hit sound additions bitfield.</summary>
    [System.Flags]
    public enum HitSoundType
    {
        Normal = 0,
        Whistle = 1 << 1, // 2
        Finish = 1 << 2,  // 4
        Clap = 1 << 3     // 8
    }

    /// <summary>osu! sample bank. Auto = inherit from the active timing point / beatmap default.</summary>
    public enum SampleBank { Auto = 0, Normal = 1, Soft = 2, Drum = 3 }

    /// <summary>Per-slider-edge sample banks parsed from the edgeSets field ("normal:addition").</summary>
    public struct EdgeSampleSet
    {
        public SampleBank Normal;
        public SampleBank Addition;
    }

    public enum SliderCurveType
    {
        Catmull,
        Bezier,
        Linear,
        PerfectCircle
    }

    /// <summary>Base class for every playable object in a beatmap.</summary>
    public abstract class HitObject
    {
        /// <summary>Position in osu! pixels. Origin top-left, y grows downward. Playfield is 512x384.</summary>
        public Vector2 Position;

        /// <summary>Start time in milliseconds.</summary>
        public int StartTime;

        public HitObjectType Type;
        public HitSoundType HitSound;

        // --- Hit sample addressing (from the trailing hitSample field). ---

        /// <summary>Bank for the normal sound (Auto = inherit the timing point / beatmap default).</summary>
        public SampleBank SampleBank;

        /// <summary>Bank for additions (whistle/finish/clap); Auto = follow the resolved normal bank.</summary>
        public SampleBank AdditionBank;

        /// <summary>Custom sample index override (0 = inherit the timing point's index).</summary>
        public int CustomSampleIndex;

        /// <summary>Per-object volume override 1-100 (0 = inherit the timing point's volume).</summary>
        public int SampleVolume;

        public bool IsNewCombo;
        public int ComboColourSkip;

        // Filled in by a post-parse pass.
        public int ComboNumber;   // 1-based index within the current combo
        public int ComboColour;   // index into Beatmap.ComboColours

        public virtual int EndTime => StartTime;

        /// <summary>The position the cursor should be at when the object ends (for stacking / follow).</summary>
        public virtual Vector2 EndPosition => Position;
    }

    public sealed class HitCircle : HitObject
    {
    }

    public sealed class Slider : HitObject
    {
        public SliderCurveType CurveType;

        /// <summary>Control points including the start position as the first element (osu! pixels).</summary>
        public List<Vector2> ControlPoints = new List<Vector2>();

        /// <summary>Number of slides ("repeats" column). 1 = single traversal head -> tail.</summary>
        public int Slides = 1;

        /// <summary>Pixel length of a single slide as authored in the beatmap.</summary>
        public double PixelLength;

        /// <summary>Hit sound for each slider edge (head, repeats..., tail). May be empty.</summary>
        public List<HitSoundType> EdgeSounds = new List<HitSoundType>();

        /// <summary>Sample banks for each slider edge, parallel to <see cref="EdgeSounds"/>. May be empty.</summary>
        public List<EdgeSampleSet> EdgeSampleSets = new List<EdgeSampleSet>();

        // --- Computed during difficulty processing ---

        /// <summary>Geometry of the slider, resampled to <see cref="PixelLength"/>.</summary>
        public SliderPath Path;

        /// <summary>Velocity in osu! pixels per millisecond.</summary>
        public double Velocity;

        /// <summary>Duration of a single slide in milliseconds.</summary>
        public double SpanDuration;

        /// <summary>Total duration (all slides) in milliseconds.</summary>
        public int Duration;

        /// <summary>Absolute times of every slider tick (excludes head, repeats and tail).</summary>
        public List<int> TickTimes = new List<int>();

        public override int EndTime => StartTime + Duration;

        public override Vector2 EndPosition
        {
            get
            {
                if (Path == null) return Position;
                // After an odd number of slides the cursor ends at the tail, otherwise at the head.
                bool endsAtTail = (Slides % 2) == 1;
                return Position + (endsAtTail ? Path.PositionAt(1.0) : Path.PositionAt(0.0));
            }
        }

        /// <summary>World/osu position along the slider for a given absolute time.</summary>
        public Vector2 PositionAtTime(int time)
        {
            if (Path == null) return Position;
            double elapsed = Mathf.Clamp(time - StartTime, 0, Duration);
            if (SpanDuration <= 0) return Position + Path.PositionAt(0);

            double spanProgress = (elapsed / SpanDuration);
            int span = (int)spanProgress;
            double t = spanProgress - span;
            if (span >= Slides) { span = Slides - 1; t = 1; }
            // Reverse direction on odd spans.
            if ((span & 1) == 1) t = 1 - t;
            return Position + Path.PositionAt(t);
        }
    }

    public sealed class Spinner : HitObject
    {
        public int SpinnerEndTime;
        public override int EndTime => SpinnerEndTime;
        // Spinners are always centred regardless of the stored position.
        public override Vector2 EndPosition => new Vector2(256, 192);
    }
}
