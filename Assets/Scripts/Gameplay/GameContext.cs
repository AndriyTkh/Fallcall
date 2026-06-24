using System;
using OsuUnity.Beatmaps;
using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>Shared state &amp; tuned values handed to every drawable hit object.</summary>
    public sealed class GameContext
    {
        public Beatmap Beatmap;
        public Playfield Playfield;
        public CursorController Cursor;
        public ScoreProcessor Score;
        public HitSoundPlayer HitSounds;
        public Transform ObjectRoot;

        // Derived difficulty values.
        public double RadiusOsu;
        public float RadiusWorld;
        public double Preempt;
        public double FadeIn;
        public double Hit300, Hit100, Hit50;

        /// <summary>Follow-circle radius for sliders (osu! uses ~2.4x the circle radius).</summary>
        public float FollowRadiusWorld => RadiusWorld * 2.4f;

        /// <summary>Raised when an object is judged so the HUD can pop a result up.</summary>
        public Action<Judgement, Vector3> OnJudgement;

        public void Configure(Beatmap map)
        {
            Beatmap = map;
            var d = map.Difficulty;
            RadiusOsu = DifficultyCalculator.CircleRadius(d.CircleSize);
            RadiusWorld = Playfield.OsuToWorldDistance(RadiusOsu);
            Preempt = DifficultyCalculator.Preempt(d.ApproachRate);
            FadeIn = DifficultyCalculator.FadeIn(d.ApproachRate);
            Hit300 = DifficultyCalculator.Hit300Window(d.OverallDifficulty);
            Hit100 = DifficultyCalculator.Hit100Window(d.OverallDifficulty);
            Hit50 = DifficultyCalculator.Hit50Window(d.OverallDifficulty);
        }

        /// <summary>Map an absolute timing error (ms) to a judgement, or Miss if outside all windows.</summary>
        public Judgement JudgeTiming(double absDelta)
        {
            if (absDelta <= Hit300) return Judgement.Great;
            if (absDelta <= Hit100) return Judgement.Ok;
            if (absDelta <= Hit50) return Judgement.Meh;
            return Judgement.Miss;
        }

        /// <summary>
        /// Combo colour for the given index: the beatmap's own palette if it has one, otherwise the
        /// active skin's palette, otherwise a built-in default.
        /// </summary>
        public Color ComboColour(int index)
        {
            var list = Beatmap.ComboColours.Count > 0
                ? Beatmap.ComboColours
                : Skinning.Skin.Current?.Config.ComboColours;
            if (list == null || list.Count == 0) return new Color(0.9f, 0.4f, 0.5f);
            return list[index % list.Count];
        }

        public bool CursorWithin(Vector3 worldCentre, float radiusWorld)
        {
            Vector3 c = Cursor.WorldPosition;
            // Compare on the playfield plane.
            return (c - worldCentre).sqrMagnitude <= radiusWorld * radiusWorld;
        }
    }
}
