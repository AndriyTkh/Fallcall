using System;
using OsuUnity.Beatmaps;
using UnityEngine;

namespace OsuUnity.Gameplay
{
    public enum Judgement
    {
        Miss = 0,
        Meh = 50,
        Ok = 100,
        Great = 300,
        // Slider/spinner sub-results that don't carry the full circle accuracy weight:
        SliderTick = 10,
        SpinnerBonus = 1000
    }

    /// <summary>Tracks score, combo, accuracy and HP. Accuracy uses the standard osu! weighting.</summary>
    public sealed class ScoreProcessor
    {
        public int Count300;
        public int Count100;
        public int Count50;
        public int CountMiss;

        public int Combo;
        public int MaxCombo;
        public long Score;

        public double HP = 1.0; // 0..1
        public bool Failed;

        /// <summary>
        /// Raised when a combo worth announcing is lost, i.e. when the combo-break sound should play.
        /// osu!lazer only plays it past <see cref="ComboBreakThreshold"/> (see ComboEffects), so small
        /// combos break silently.
        /// </summary>
        public event Action OnComboBreak;

        /// <summary>Combo must exceed this to make a break audible (osu!lazer ComboEffects).</summary>
        public const int ComboBreakThreshold = 20;

        // osu!lazer HP model: fixed per-judgement graph + continuous passive drain, calibrated so a
        // perfect play bottoms out near the HP-drain-rate target. See HealthProcessor.
        private readonly HealthProcessor _health = new HealthProcessor();
        private double _lastDrainTime;   // last song time (ms) UpdateDrain saw
        private bool _drainStarted;      // set once song time reaches the first object

        public void Configure(Beatmap map)
        {
            _health.Configure(map);
            _lastDrainTime = _health.DrainStartTime;
            _drainStarted = false;
        }

        /// <summary>
        /// Per-frame passive drain. GameManager calls this each active frame with the current song time.
        /// No-op before the first object, during breaks, and while paused (the caller skips paused frames).
        /// Sets <see cref="Failed"/> if health reaches 0.
        /// </summary>
        public void UpdateDrain(double timeMs)
        {
            if (Failed) return;

            // Hold the clock at the drain-start time until the first object, so no drain accrues in the intro.
            if (!_drainStarted)
            {
                if (timeMs < _health.DrainStartTime) { _lastDrainTime = _health.DrainStartTime; return; }
                _drainStarted = true;
                _lastDrainTime = _health.DrainStartTime;
            }

            double drop = _health.DrainDelta(_lastDrainTime, timeMs);
            _lastDrainTime = timeMs;
            if (drop > 0) ChangeHp(-drop);
        }

        /// <summary>Total number of accuracy-bearing hits seen so far.</summary>
        public int TotalHits => Count300 + Count100 + Count50 + CountMiss;

        public double Accuracy
        {
            get
            {
                int total = TotalHits;
                if (total == 0) return 1.0;
                double points = Count300 * 300.0 + Count100 * 100.0 + Count50 * 50.0;
                return points / (total * 300.0);
            }
        }

        /// <summary>Apply a judgement. <paramref name="affectsCombo"/> false for spinner bonus etc.</summary>
        public void Apply(Judgement j, bool affectsCombo = true, bool affectsAccuracy = true)
        {
            if (affectsAccuracy)
            {
                switch (j)
                {
                    case Judgement.Great: Count300++; break;
                    case Judgement.Ok: Count100++; break;
                    case Judgement.Meh: Count50++; break;
                    case Judgement.Miss: CountMiss++; break;
                }
            }

            int baseValue = (int)j;

            if (j == Judgement.Miss)
            {
                if (affectsCombo)
                {
                    bool audible = Combo > ComboBreakThreshold;
                    Combo = 0;
                    if (audible) OnComboBreak?.Invoke();
                }
                // HP graph: full-circle miss vs. nested (tick/repeat) miss is inferred from affectsAccuracy.
                ChangeHp(_health.HealthIncreaseFor(Judgement.Miss, affectsAccuracy));
            }
            else
            {
                if (affectsCombo)
                {
                    Combo++;
                    if (Combo > MaxCombo) MaxCombo = Combo;
                }
                // osu!-style combo scaling: base + base * combo * difficulty / 25.
                long comboBonus = (long)(baseValue * Math.Max(0, Combo - 1) * 0.04);
                Score += baseValue + comboBonus;

                ChangeHp(_health.HealthIncreaseFor(j, affectsAccuracy));
            }
        }

        private void ChangeHp(double delta)
        {
            HP = Math.Clamp(HP + delta, 0.0, 1.0);
            if (HP <= 0.0) Failed = true;
        }

        public string RankString()
        {
            double acc = Accuracy;
            bool noMiss = CountMiss == 0;
            if (acc >= 1.0) return "SS";
            if (acc > 0.9333 && noMiss) return "S";
            if (acc > 0.9333) return "A";
            if (acc > 0.8666) return "A";
            if (acc > 0.80) return "B";
            if (acc > 0.70) return "C";
            return "D";
        }
    }
}
