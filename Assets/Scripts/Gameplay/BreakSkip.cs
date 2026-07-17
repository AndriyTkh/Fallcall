using System;
using System.Collections.Generic;
using OsuUnity.Beatmaps;
using UnityEngine;

// INDEX: Click-free gap detection (song intro + mid-map breaks) plus the skip overlay: bar, countdown, skip button.
namespace OsuUnity.Gameplay
{
    /// <summary>
    /// One feature for every stretch of a map with nothing to click. The song intro and a mid-map break
    /// are the same thing here — a gap between the end of one hit object and the start of the next — so
    /// they get one overlay instead of two: a draining bar, the countdown to the next click, and a skip
    /// button that seeks to <see cref="GameSettings.BreakSkipLeadMs"/> before that click.
    ///
    /// Gaps come from the hit objects themselves, not the map's declared [Events] break periods: those are
    /// optional, absent before the first object, and often don't line up with what a player experiences as
    /// dead time. A gap qualifies once it is at least <see cref="GameSettings.BreakMinGapMs"/> long; both
    /// tunables are read live, so the whole list is built once per session and filtered per frame.
    ///
    /// Drawing is screen-space IMGUI hugging the bottom edge (UI-DESIGN §3: the centre belongs to the
    /// playfield), and the overlay fades rather than pops (§1.1). <see cref="GameManager"/> owns the
    /// instance and performs the actual seek via <see cref="OnSkip"/>.
    /// </summary>
    public sealed class BreakSkip
    {
        /// <summary>Raised with the target song time (ms) when the player skips.</summary>
        public event Action<double> OnSkip;

        /// <summary>Gaps shorter than this are never tracked, whatever the setting says.</summary>
        private const double MinTrackedGapMs = 1000;

        private const float FadeMs = 250f;

        private struct Gap
        {
            public double Start;   // where the overlay appears (song start incl. lead-in, or the previous object's end)
            public double End;     // the next object's start time — the first click after the gap
            public double Length;  // click-free length in song time (excludes the lead-in), for the threshold test
        }

        private readonly List<Gap> _gaps = new List<Gap>();
        private int _visible = -1;   // gap index the overlay is showing, or -1
        private GUIStyle _label, _button;

        /// <summary>Collect every click-free gap in the map. <paramref name="leadInMs"/> is the clock's
        /// lead-in, so the intro overlay is up from the very first frame rather than popping in at 0.</summary>
        public void Build(Beatmap map, double leadInMs)
        {
            _gaps.Clear();
            _visible = -1;
            if (map == null) return;

            double prevEnd = 0;   // song start: the intro is just the gap before the first object
            bool intro = true;
            foreach (var ho in map.HitObjects)
            {
                double length = ho.StartTime - prevEnd;
                if (length >= MinTrackedGapMs)
                    _gaps.Add(new Gap
                    {
                        Start = intro ? -leadInMs : prevEnd,
                        End = ho.StartTime,
                        Length = length,
                    });
                intro = false;
                // Sliders/spinners can overlap the next object, so track the furthest end reached.
                prevEnd = Math.Max(prevEnd, ho.EndTime);
            }
        }

        /// <summary>Call once per running gameplay frame (not while paused or finished).</summary>
        public void Tick(double time)
        {
            _visible = ActiveGap(time);
            if (_visible < 0) return;
            if (GameSettings.GetBind("skip").DownThisFrame()) Skip(time);
        }

        /// <summary>Skip the gap the overlay is currently showing (no-op if there isn't one).</summary>
        public void Skip(double time)
        {
            if (_visible < 0) return;
            double target = SkipTarget(_gaps[_visible]);
            if (target <= time) return;
            OnSkip?.Invoke(target);
        }

        // The gap containing `time` and still worth showing, or -1. Gaps are ordered, so the scan stops at
        // the first one that hasn't started yet.
        private int ActiveGap(double time)
        {
            double minGap = Math.Max(MinTrackedGapMs, GameSettings.BreakMinGapMs);
            for (int i = 0; i < _gaps.Count; i++)
            {
                var g = _gaps[i];
                if (time < g.Start) break;
                if (g.Length < minGap) continue;
                if (time < SkipTarget(g)) return i;
            }
            return -1;
        }

        // Where a skip lands: the configured lead before the first click after the gap, never earlier than
        // the gap's own start (so a short gap can't seek backwards).
        private static double SkipTarget(Gap g)
        {
            double lead = Mathf.Clamp(GameSettings.BreakSkipLeadMs, 0f, 5000f);
            return Math.Max(g.Start, g.End - lead);
        }

        // ----------------------------------------------------------------- overlay

        /// <summary>Draw the overlay for the current gap. Call from OnGUI during gameplay.</summary>
        public void Draw(double time)
        {
            if (_visible < 0) return;
            var g = _gaps[_visible];
            double target = SkipTarget(g);
            double span = Math.Max(1.0, target - g.Start);

            // Fade in on arrival and out as the skip window closes — never a pop next to the playfield.
            float alpha = Mathf.Clamp01(Mathf.Min((float)(time - g.Start) / FadeMs, (float)(target - time) / FadeMs));
            if (alpha <= 0.001f) return;

            EnsureStyles();

            float s = Mathf.Max(0.1f, GameSettings.HudScale);
            float w = Mathf.Min(Screen.width * 0.42f, 520f * s);
            float x = (Screen.width - w) * 0.5f;
            float btnH = 34f * s, barH = 8f * s, textH = 24f * s;

            // Bottom-anchored (the centre belongs to the playfield), stacked above the controls hint line
            // at Screen.height - 52 so the two never overlap on a narrow window.
            float btnY = Screen.height - 88f - btnH;
            float barY = btnY - 10f * s - barH;
            float textY = barY - 6f * s - textH;

            // Scrim: the countdown sits over whatever the background art is, so readability isn't luck.
            var panel = new Rect(x - 14f * s, textY - 8f * s, w + 28f * s, (btnY + btnH) - textY + 16f * s);
            GUI.color = new Color(0f, 0f, 0f, 0.55f * alpha);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);

            double remainMs = Math.Max(0.0, g.End - time);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(x, textY, w, textH), $"Next note in {remainMs / 1000.0:0.0}s", _label);

            // Bar drains toward the skip point, so its width always reads as "time you still have".
            float fill = Mathf.Clamp01((float)((target - time) / span));
            GUI.color = new Color(1f, 1f, 1f, 0.18f * alpha);
            GUI.DrawTexture(new Rect(x, barY, w, barH), Texture2D.whiteTexture);
            GUI.color = new Color(0.55f, 0.72f, 1f, 0.9f * alpha);
            GUI.DrawTexture(new Rect(x, barY, w * fill, barH), Texture2D.whiteTexture);

            GUI.color = new Color(1f, 1f, 1f, alpha);
            string key = GameSettings.GetBind("skip").Display();
            if (GUI.Button(new Rect(x, btnY, w, btnH), $"Skip  [{key}]", _button))
                Skip(time);
            GUI.color = Color.white;
        }

        private void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _label.normal.textColor = Color.white;
            _button = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
        }
    }
}
