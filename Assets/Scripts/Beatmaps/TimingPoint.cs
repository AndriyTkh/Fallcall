namespace OsuUnity.Beatmaps
{
    /// <summary>
    /// A single line from the [TimingPoints] section.
    /// Format: time,beatLength,meter,sampleSet,sampleIndex,volume,uninherited,effects
    /// </summary>
    public sealed class TimingPoint
    {
        public int Time;

        /// <summary>
        /// Raw beatLength value. For uninherited points this is milliseconds per beat (positive).
        /// For inherited points this is a negative "speed" value where SV = -100 / beatLength.
        /// </summary>
        public double BeatLength;

        public int Meter = 4;
        public int SampleSet;
        public int SampleIndex;
        public int Volume = 100;
        public bool Uninherited = true;
        public int Effects;

        /// <summary>Slider velocity multiplier contributed by this point.</summary>
        public double SpeedMultiplier
        {
            get
            {
                if (Uninherited) return 1.0;
                if (BeatLength >= 0) return 1.0;
                return 100.0 / -BeatLength;
            }
        }
    }
}
