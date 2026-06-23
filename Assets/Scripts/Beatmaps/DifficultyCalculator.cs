using UnityEngine;

namespace OsuUnity.Beatmaps
{
    /// <summary>
    /// Converts the abstract difficulty settings (CS / AR / OD / HP) into concrete gameplay values,
    /// and processes a beatmap (combo colours, slider timing) after parsing.
    /// </summary>
    public static class DifficultyCalculator
    {
        /// <summary>Standard osu! difficulty interpolation around the value 5.</summary>
        public static double DifficultyRange(double value, double min, double mid, double max)
        {
            if (value > 5) return mid + (max - mid) * (value - 5) / 5;
            if (value < 5) return mid - (mid - min) * (5 - value) / 5;
            return mid;
        }

        /// <summary>Circle radius in osu! pixels.</summary>
        public static double CircleRadius(double cs) => 54.4 - 4.48 * cs;

        /// <summary>Time (ms) a hit object is visible before its hit time.</summary>
        public static double Preempt(double ar) => DifficultyRange(ar, 1800, 1200, 450);

        /// <summary>Fade-in duration (ms) of an approaching object.</summary>
        public static double FadeIn(double ar) => DifficultyRange(ar, 1200, 800, 300);

        // Hit windows (ms) measured from the perfect hit time.
        public static double Hit300Window(double od) => 80 - 6 * od;
        public static double Hit100Window(double od) => 140 - 8 * od;
        public static double Hit50Window(double od) => 200 - 10 * od;

        /// <summary>Required full spins for a spinner (osu! classic approximation, spins per second by OD).</summary>
        public static double SpinsPerSecond(double od) => DifficultyRange(od, 3, 5, 7.5);

        /// <summary>
        /// Fills in combo colours/numbers and computes slider velocity, duration and tick times.
        /// Call once after parsing.
        /// </summary>
        public static void Process(Beatmap map)
        {
            ProcessCombos(map);
            ProcessSliders(map);
        }

        private static void ProcessCombos(Beatmap map)
        {
            int colourCount = Mathf.Max(1, map.ComboColours.Count);
            int colour = 0;
            int number = 0;
            bool first = true;

            foreach (var ho in map.HitObjects)
            {
                bool startsCombo = first || ho.IsNewCombo || ho is Spinner;

                if (startsCombo)
                {
                    if (!first)
                    {
                        int skip = ho.IsNewCombo ? ho.ComboColourSkip : 0;
                        colour = (colour + 1 + skip) % colourCount;
                    }
                    number = 1;
                }
                else
                {
                    number++;
                }

                ho.ComboColour = colour;
                ho.ComboNumber = number;
                first = false;
            }
        }

        private static void ProcessSliders(Beatmap map)
        {
            foreach (var ho in map.HitObjects)
            {
                if (!(ho is Slider slider)) continue;

                slider.Path = new SliderPath(slider.CurveType, slider.ControlPoints, slider.PixelLength);

                var uninherited = map.GetUninheritedTimingPointAt(slider.StartTime);
                var active = map.GetTimingPointAt(slider.StartTime);

                double beatLength = uninherited != null && uninherited.BeatLength > 0
                    ? uninherited.BeatLength
                    : 500; // safe default (120 BPM)
                double sv = active != null ? active.SpeedMultiplier : 1.0;

                // osu! pixels per beat at SV 1x = 100 * SliderMultiplier.
                double pxPerBeat = 100.0 * map.Difficulty.SliderMultiplier * sv;
                slider.Velocity = pxPerBeat / beatLength;                 // px per ms
                slider.SpanDuration = slider.Path.Length / slider.Velocity;
                slider.Duration = Mathf.Max(1, (int)(slider.SpanDuration * slider.Slides));

                // Ticks: one every (beatLength / tickRate) ms within each span, excluding the edges.
                slider.TickTimes.Clear();
                double tickInterval = beatLength / System.Math.Max(0.1, map.Difficulty.SliderTickRate);
                if (tickInterval > 10 && slider.SpanDuration > tickInterval)
                {
                    for (int span = 0; span < slider.Slides; span++)
                    {
                        double spanStart = slider.StartTime + span * slider.SpanDuration;
                        for (double t = tickInterval; t < slider.SpanDuration - 10; t += tickInterval)
                        {
                            slider.TickTimes.Add((int)(spanStart + t));
                        }
                    }
                }
            }
        }
    }
}
