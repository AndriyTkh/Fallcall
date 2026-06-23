using System;
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
        SpinnerBonus = 100
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

        private double _hpDrain;    // per-miss drain, scaled by HP setting
        private double _hpRecover;  // per-hit recovery

        public void Configure(float hpDrainRate)
        {
            // Higher HP -> harsher misses, smaller recovery. Tuned for a forgiving-but-real feel.
            _hpDrain = 0.05 + 0.02 * hpDrainRate;
            _hpRecover = 0.06 - 0.003 * hpDrainRate;
            if (_hpRecover < 0.01) _hpRecover = 0.01;
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
                if (affectsCombo) Combo = 0;
                ChangeHp(-_hpDrain);
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

                double recover = j == Judgement.Great ? _hpRecover
                               : j == Judgement.Ok ? _hpRecover * 0.5
                               : j == Judgement.Meh ? _hpRecover * 0.2
                               : _hpRecover * 0.1; // ticks/bonus
                ChangeHp(recover);
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
