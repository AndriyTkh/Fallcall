using System;
using System.Collections.Generic;
using OsuUnity.Beatmaps;

// INDEX: Faithful port of osu!lazer DrainingHealthProcessor — per-judgement HP graph + continuous passive drain.
namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Faithful reimplementation of osu!lazer's <c>DrainingHealthProcessor</c> (osu! ruleset).
    ///
    /// Two parts, both matching lazer:
    ///  1. <b>HP graph</b> — each judgement changes health by a fixed amount scaled off
    ///     <see cref="DEFAULT_MAX_HEALTH_INCREASE"/> (0.05). See <see cref="HealthIncreaseFor"/>.
    ///  2. <b>Continuous drain</b> — health drains at a constant <see cref="DrainRate"/> (health per ms)
    ///     during active gameplay, paused during breaks and before the first object. The rate is picked
    ///     by a binary search (<see cref="ComputeDrainRate"/>) so a <i>perfect</i> play bottoms out at
    ///     <see cref="targetMinimumHealth"/> — the HP-drain-rate target curve (0.99 / 0.9 / 0.4 at HP 0/5/10).
    ///
    /// This object owns the drain <i>algorithm</i> only; the live 0..1 health value and the fail flag stay
    /// on <see cref="ScoreProcessor"/>, which drives this via <c>HealthIncreaseFor</c> + <c>DrainDelta</c>.
    ///
    /// APPROXIMATIONS vs. true lazer (documented, all minor / conservative):
    ///  - <b>Calibration nesting.</b> Lazer feeds <c>ComputeDrainRate</c> the exact per-nested-object
    ///    judgement stream of a simulated perfect play (slider head/ticks/repeats/tail, spinner ticks).
    ///    Our <see cref="Beatmap"/> model only exposes top-level objects with Start/End times, so the
    ///    calibration sim uses one Great at each object's StartTime plus a second increase at a slider/
    ///    spinner's EndTime. Ticks are omitted. Effect: the computed rate is marginally more forgiving on
    ///    tick-dense maps (fewer recovery events between drain windows), never harsher. The runtime HP
    ///    graph itself (part 1) is exact for whatever judgements the gameplay code actually fires.
    ///  - <b>Nested-miss mapping.</b> We infer LargeTick vs. full-circle from the judgement flags the
    ///    existing gameplay funnels in (a nested miss arrives as <c>Miss</c> with affectsAccuracy=false),
    ///    rather than distinct HitResult types.
    ///  - <b>Seek guard.</b> A per-frame drain delta larger than <see cref="MaxDrainStepMs"/> is treated as
    ///    a clock discontinuity (BreakSkip seek / lag spike) and skipped, so a seek across a gap can't dump
    ///    a huge one-frame drain. Lazer clamps within the frame instead; we can't see sub-frame time.
    /// </summary>
    public sealed class HealthProcessor
    {
        /// <summary>osu!lazer <c>Judgement.DEFAULT_MAX_HEALTH_INCREASE</c>. All HP-graph amounts scale off this.</summary>
        private const double DEFAULT_MAX_HEALTH_INCREASE = 0.05;

        // Minimum-health targets of the HP-drain-rate curve (lazer constants).
        private const double min_health_target = 0.99; // at HP 0
        private const double mid_health_target = 0.9;  // at HP 5
        private const double max_health_target = 0.4;  // at HP 10
        private const double minimum_health_error = 0.01;

        /// <summary>A per-frame drain step longer than this (ms) is treated as a seek/stall and ignored.</summary>
        private const double MaxDrainStepMs = 250.0;

        /// <summary>Drain as a proportion of total health per millisecond (lazer's DrainRate).</summary>
        public double DrainRate { get; private set; }

        /// <summary>Song time (ms) at/after which drain applies — the first object's start time.</summary>
        public double DrainStartTime { get; private set; }

        /// <summary>Song time (ms) at which drain stops — the last object's end time.</summary>
        public double GameplayEndTime { get; private set; }

        private double _targetMinimumHealth;

        // No-drain windows [start,end] in song time: from the last object before a break to the first after it.
        private readonly List<(double start, double end)> _noDrainPeriods = new List<(double, double)>();

        // Perfect-play calibration stream (time, amount), used only by ComputeDrainRate.
        private readonly struct HealthIncrease
        {
            public readonly double Time;
            public readonly double Amount;
            public HealthIncrease(double time, double amount) { Time = time; Amount = amount; }
        }

        private readonly List<HealthIncrease> _healthIncreases = new List<HealthIncrease>();
        private readonly List<(double start, double end)> _breaks = new List<(double, double)>();

        /// <param name="drainLenience">0 = default drain, 0.5 = half, 1 = no drain (matches lazer's DrainLenience).</param>
        public void Configure(Beatmap map, double drainLenience = 0)
        {
            drainLenience = Math.Clamp(drainLenience, 0, 1);
            _healthIncreases.Clear();
            _breaks.Clear();
            _noDrainPeriods.Clear();
            DrainRate = 0;

            var objs = map.HitObjects;
            if (objs == null || objs.Count == 0)
            {
                DrainStartTime = 0;
                GameplayEndTime = 0;
                return;
            }

            DrainStartTime = objs[0].StartTime;
            GameplayEndTime = objs[objs.Count - 1].EndTime;

            // Break list as (start,end), sorted by end — ComputeDrainRate walks it in order.
            foreach (var b in map.Breaks) _breaks.Add((b.Start, b.End));
            _breaks.Sort((a, c) => a.end.CompareTo(c.end));

            // No-drain runtime windows: lazer pauses drain from the last object ending before a break to the
            // first object starting after it (not the raw break markers).
            foreach (var b in map.Breaks)
            {
                double lastEnd = double.MinValue;
                double firstStart = double.MaxValue;
                for (int i = 0; i < objs.Count; i++)
                {
                    int e = objs[i].EndTime;
                    if (e <= b.Start && e > lastEnd) lastEnd = e;
                    int s = objs[i].StartTime;
                    if (s >= b.End && s < firstStart) firstStart = s;
                }
                if (lastEnd == double.MinValue) lastEnd = b.Start;
                if (firstStart == double.MaxValue) firstStart = b.End;
                _noDrainPeriods.Add((lastEnd, firstStart));
            }

            // Minimum-health target from the HP-drain-rate curve, plus the lenience give-back (lazer).
            _targetMinimumHealth = DifficultyCalculator.DifficultyRange(
                map.Difficulty.HPDrainRate, min_health_target, mid_health_target, max_health_target);
            _targetMinimumHealth += drainLenience * (1 - _targetMinimumHealth);
            _targetMinimumHealth = Math.Clamp(_targetMinimumHealth, 0, 1);

            // Build the perfect-play calibration stream (see APPROXIMATIONS): a Great at each object's start,
            // and a large-tick-equivalent increase at a slider/spinner's end.
            double greatInc = HealthIncreaseFor(Judgement.Great, affectsAccuracy: true);
            foreach (var ho in objs)
            {
                _healthIncreases.Add(new HealthIncrease(ho.StartTime, greatInc));
                if (ho.EndTime > ho.StartTime)
                    _healthIncreases.Add(new HealthIncrease(ho.EndTime, DEFAULT_MAX_HEALTH_INCREASE));
            }
            _healthIncreases.Sort((a, c) => a.Time.CompareTo(c.Time));

            DrainRate = drainLenience >= 1 ? 0 : ComputeDrainRate();
        }

        /// <summary>
        /// Health delta for a judgement, matching lazer's <c>Judgement.HealthIncreaseFor</c> (base osu! table).
        /// The gameplay code funnels nested slider/spinner results through the same <see cref="Judgement"/>
        /// enum, so we disambiguate with the accuracy flag: a nested (tick/repeat) result carries
        /// <paramref name="affectsAccuracy"/> = false, a full circle carries true.
        /// </summary>
        public double HealthIncreaseFor(Judgement j, bool affectsAccuracy)
        {
            switch (j)
            {
                case Judgement.Great: return DEFAULT_MAX_HEALTH_INCREASE;         // +0.05
                case Judgement.Ok:    return DEFAULT_MAX_HEALTH_INCREASE * 0.5;   // +0.025
                case Judgement.Meh:   return DEFAULT_MAX_HEALTH_INCREASE * 0.05;  // +0.0025
                // Nested hit (slider tick / repeat / tail) = LargeTickHit.
                case Judgement.SliderTick: return DEFAULT_MAX_HEALTH_INCREASE;    // +0.05
                // Spinner bonus spin = LargeBonus (bonus never affects the fail check; see ScoreProcessor).
                case Judgement.SpinnerBonus: return DEFAULT_MAX_HEALTH_INCREASE;  // +0.05
                case Judgement.Miss:
                    // Full-circle miss = Miss (-0.10); nested miss (tick/repeat) = LargeTickMiss (-0.05).
                    return affectsAccuracy ? -DEFAULT_MAX_HEALTH_INCREASE * 2 : -DEFAULT_MAX_HEALTH_INCREASE;
                default: return 0;
            }
        }

        /// <summary>True while <paramref name="time"/> sits inside a break's no-drain window.</summary>
        public bool InNoDrainPeriod(double time)
        {
            for (int i = 0; i < _noDrainPeriods.Count; i++)
                if (time > _noDrainPeriods[i].start && time < _noDrainPeriods[i].end) return true;
            return false;
        }

        /// <summary>
        /// Passive health lost between <paramref name="prevTime"/> and <paramref name="curTime"/> (song ms).
        /// Mirrors lazer's <c>Update</c>: clamp both ends to [DrainStartTime, GameplayEndTime] and drain the
        /// clamped span at <see cref="DrainRate"/>. Returns 0 during breaks / before the first object, and
        /// swallows clock discontinuities (see <see cref="MaxDrainStepMs"/>). Non-negative.
        /// </summary>
        public double DrainDelta(double prevTime, double curTime)
        {
            if (DrainRate <= 0) return 0;
            if (curTime <= prevTime) return 0;
            if (InNoDrainPeriod(curTime)) return 0;

            double last = Math.Clamp(prevTime, DrainStartTime, GameplayEndTime);
            double cur = Math.Clamp(curTime, DrainStartTime, GameplayEndTime);
            double span = cur - last;
            if (span <= 0 || span > MaxDrainStepMs) return 0; // out of window or a seek/stall

            return DrainRate * span;
        }

        // Binary search for the per-ms drain rate whose perfect-play minimum health hits the target.
        // Verbatim port of osu!lazer DrainingHealthProcessor.ComputeDrainRate.
        private double ComputeDrainRate()
        {
            if (_healthIncreases.Count <= 1) return 0;

            int adjustment = 1;
            double result = 1;

            // Converges within ~30 iterations; the int `adjustment` overflowing to negative is the loop's
            // safety stop (matches lazer — do not "fix" the overflow).
            while (adjustment > 0)
            {
                double currentHealth = 1;
                double lowestHealth = 1;
                int currentBreak = 0;

                for (int i = 0; i < _healthIncreases.Count; i++)
                {
                    double currentTime = _healthIncreases[i].Time;
                    double lastTime = i > 0 ? _healthIncreases[i - 1].Time : DrainStartTime;

                    while (currentBreak < _breaks.Count && _breaks[currentBreak].end <= currentTime)
                    {
                        // A break between two objects means no drain for the whole gap between them.
                        lastTime = currentTime;
                        currentBreak++;
                    }

                    currentHealth -= (currentTime - lastTime) * result;
                    lowestHealth = Math.Min(lowestHealth, currentHealth);
                    currentHealth = Math.Min(1, currentHealth + _healthIncreases[i].Amount);

                    if (lowestHealth < 0) break; // rate is definitely too harsh
                }

                if (Math.Abs(lowestHealth - _targetMinimumHealth) <= minimum_health_error) break;

                adjustment *= 2;
                result += 1.0 / adjustment * Math.Sign(lowestHealth - _targetMinimumHealth);
            }

            return result;
        }
    }
}
