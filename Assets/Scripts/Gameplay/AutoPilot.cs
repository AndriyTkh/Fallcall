using System.Collections.Generic;
using OsuUnity.Beatmaps;
using UnityEngine;

// INDEX: Autoplay driver — walks the map timeline and emits cursor position + tap state each frame.
namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Autoplay pilot (osu! "Auto" mod, simplified): walks the beatmap timeline and produces, each frame,
    /// where the cursor should sit and whether a tap is pressed/held — so <see cref="CursorController.SetAuto"/>
    /// can play the map hands-free (for testing and beatmap preview). Not a difficulty-accurate replay: it
    /// taps every circle / slider head dead-on at its start time, tracks the slider ball, and orbits
    /// spinners. Hit detection sees the same cursor state a human would (world position + press/hold), so no
    /// drawable knows the difference.
    ///
    /// Position is in osu! space, so it drives any projection — Sphere and Ortho2D both work (Falling is not
    /// wired yet). Camera aiming in Sphere is a separate cosmetic concern handled by
    /// <see cref="ViewModeController.AimAt"/>; the hit test itself only needs the cursor's world position.
    /// </summary>
    public sealed class AutoPilot
    {
        private static readonly Vector2 Centre = new Vector2(256f, 192f);

        // How long the cursor lingers on a circle after tapping it before it starts gliding to the next —
        // keeps it inside the circle on the actual press frame even at coarse frame rates / fast streams.
        private const double TapHoldMs = 60.0;

        private readonly List<HitObject> _objs;
        private int _pressIdx;   // next object head to tap
        private int _posIdx;     // last object whose StartTime <= time (for positioning)

        public AutoPilot(Beatmap map) { _objs = map != null ? map.HitObjects : null; }

        /// <summary>Cursor state for song time <paramref name="time"/> (ms). <paramref name="press"/> is a
        /// single-frame tap edge; <paramref name="held"/> stays true across a slider/spinner.</summary>
        public void Tick(double time, out Vector2 osu, out bool held, out bool press)
        {
            osu = Centre; held = false; press = false;
            if (_objs == null || _objs.Count == 0) return;

            // Fire one fresh tap the first frame we reach an object's start time. One per frame keeps
            // consecutive stream notes to successive frames (a few ms late — still inside the hit window).
            if (_pressIdx < _objs.Count && time >= _objs[_pressIdx].StartTime)
            {
                press = true;
                _pressIdx++;
            }

            osu = Position(time, out held);
        }

        private Vector2 Position(double time, out bool held)
        {
            held = false;
            while (_posIdx + 1 < _objs.Count && time >= _objs[_posIdx + 1].StartTime) _posIdx++;

            var c = _objs[_posIdx];
            if (time < c.StartTime) return c.Position;   // pre-roll: wait on the first note

            bool durational = c is Slider || c is Spinner;
            double activeEnd = durational ? c.EndTime : c.StartTime + TapHoldMs;
            if (time <= activeEnd)
            {
                if (c is Slider s) { held = true; return s.PositionAtTime((int)time); }
                if (c is Spinner) { held = true; return Orbit(time); }
                return c.Position;                        // hit circle (instant tap)
            }

            // Gap: glide from this object's end to the next object's start.
            if (_posIdx + 1 < _objs.Count)
            {
                var n = _objs[_posIdx + 1];
                double t0 = activeEnd, t1 = n.StartTime;
                float f = t1 > t0 ? Mathf.Clamp01((float)((time - t0) / (t1 - t0))) : 1f;
                f = f * f * (3f - 2f * f);                // smoothstep
                return Vector2.Lerp(c.EndPosition, n.Position, f);
            }
            return c.EndPosition;
        }

        // Orbit the spinner centre fast enough to clear any spin requirement.
        private static Vector2 Orbit(double time)
        {
            const float radius = 60f, speed = 0.02f;      // osu! px, rad/ms (~3.2 rev/s)
            float a = (float)time * speed;
            return Centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
    }
}
