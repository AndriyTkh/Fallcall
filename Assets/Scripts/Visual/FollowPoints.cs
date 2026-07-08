using System.Collections.Generic;
using OsuUnity.Beatmaps;
using OsuUnity.Gameplay;
using OsuUnity.Skinning;
using UnityEngine;

// INDEX: Follow points — the fading line of arrows guiding the eye from one object to the next.
namespace OsuUnity.Visual
{
    /// <summary>
    /// osu! "follow points": a staggered line of arrows connecting each hit object to the next
    /// <b>within the same combo</b>, guiding the eye — most useful when the jump is large (STRUCTURE §4,
    /// "lines/arrows pointing toward the next clicks when they're far enough apart"). Points slide + fade
    /// in ahead of the upcoming object and fade out as it is reached, matching osu!'s FollowPointConnection.
    ///
    /// Every point is placed through <see cref="Playfield.ToWorld"/> and billboarded via
    /// <see cref="Playfield.OrientationAt"/> — then rolled about the view axis so the arrow points along the
    /// on-surface direction to the next object — so the guide curves and faces the camera with the rest of
    /// the scene, and follows any mid-map view/projection change for free (positions are recomputed each frame).
    /// </summary>
    public sealed class FollowPoints : MonoBehaviour
    {
        // osu! constants (osu!lazer FollowPointConnection).
        private const float SpacingOsu = 32f;      // gap between points along the line, osu! px
        private const double PreemptMs = 800.0;    // each point fades in this long before it is "reached"
        private const double FadeInMs = 400.0;     // fade + slide-in duration of a point
        private const double FadeOutMs = 400.0;    // fade-out duration once passed
        private const float StartPad = 1.5f;       // first point sits Spacing*1.5 from the start object

        private GameContext _ctx;
        private Playfield _pf;
        private Sprite _sprite;
        private List<Conn> _conns;

        private struct Pt
        {
            public Vector2 FromOsu;      // slide-in start (osu px)
            public Vector2 ToOsu;        // rest position (osu px)
            public Vector2 DirOsu;       // connection direction (osu space), for the arrow's roll
            public double FadeInTime;    // ms
            public double FadeOutTime;   // ms
        }

        private sealed class Conn
        {
            public List<Pt> Points;
            public double ActivateTime;
            public double DeactivateTime;
            public SpriteRenderer[] Renderers; // null while inactive
        }

        public void Init(GameContext ctx)
        {
            _ctx = ctx;
            _pf = ctx.Playfield;
            _sprite = SkinSprites.FollowPoint;

            _conns = new List<Conn>();
            var objs = ctx.Beatmap.HitObjects;
            for (int i = 0; i < objs.Count - 1; i++)
            {
                HitObject a = objs[i], b = objs[i + 1];
                if (a is Spinner || b is Spinner) continue; // spinners break the chain
                if (b.IsNewCombo) continue;                 // follow points stay within a combo
                var c = Build(a, b);
                if (c != null) _conns.Add(c);
            }
        }

        // Lay out one connection's points along the line from a's end to b's start (osu! geometry).
        private Conn Build(HitObject a, HitObject b)
        {
            Vector2 start = a.EndPosition;
            Vector2 end = b.Position;
            Vector2 delta = end - start;
            float dist = delta.magnitude;
            if (dist < SpacingOsu * (StartPad + 1f)) return null; // too close -> no guide

            double startTime = a.EndTime;
            double duration = b.StartTime - startTime;
            if (duration <= 0) return null;

            Vector2 dir = delta / dist;
            var pts = new List<Pt>();
            for (float d = SpacingOsu * StartPad; d < dist - SpacingOsu; d += SpacingOsu)
            {
                float frac = d / dist;
                double fadeOut = startTime + frac * duration;
                pts.Add(new Pt
                {
                    FromOsu = start + (frac - 0.1f) * delta,
                    ToOsu = start + frac * delta,
                    DirOsu = dir,
                    FadeOutTime = fadeOut,
                    FadeInTime = fadeOut - PreemptMs,
                });
            }
            if (pts.Count == 0) return null;

            return new Conn
            {
                Points = pts,
                ActivateTime = pts[0].FadeInTime,
                DeactivateTime = pts[pts.Count - 1].FadeOutTime + FadeOutMs,
            };
        }

        public void Tick(double time)
        {
            if (_conns == null) return;
            for (int i = 0; i < _conns.Count; i++)
            {
                var c = _conns[i];
                bool visible = time >= c.ActivateTime && time <= c.DeactivateTime;
                if (visible && c.Renderers == null) Activate(c);
                else if (!visible && c.Renderers != null) Deactivate(c);
                if (c.Renderers != null) UpdateConn(c, time);
            }
        }

        private void Activate(Conn c)
        {
            c.Renderers = new SpriteRenderer[c.Points.Count];
            for (int i = 0; i < c.Points.Count; i++)
            {
                var go = new GameObject("FollowPoint");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _sprite;
                sr.color = new Color(1, 1, 1, 0);
                sr.sortingOrder = -50; // behind hit objects (which use positive DepthOrder*10)
                c.Renderers[i] = sr;
            }
        }

        private void Deactivate(Conn c)
        {
            foreach (var sr in c.Renderers)
                if (sr != null) Destroy(sr.gameObject);
            c.Renderers = null;
        }

        private void UpdateConn(Conn c, double time)
        {
            float dia = _ctx.RadiusWorld * 0.8f * Mathf.Max(0.1f, GameSettings.FollowPointScale);
            for (int i = 0; i < c.Points.Count; i++)
            {
                Pt p = c.Points[i];
                float alpha;
                Vector2 posOsu;
                if (time < p.FadeInTime) { alpha = 0f; posOsu = p.FromOsu; }
                else if (time < p.FadeInTime + FadeInMs)
                {
                    float t = (float)((time - p.FadeInTime) / FadeInMs);
                    alpha = t;
                    posOsu = Vector2.Lerp(p.FromOsu, p.ToOsu, t);
                }
                else if (time < p.FadeOutTime) { alpha = 1f; posOsu = p.ToOsu; }
                else
                {
                    float t = (float)((time - p.FadeOutTime) / FadeOutMs);
                    alpha = Mathf.Clamp01(1f - t);
                    posOsu = p.ToOsu;
                }
                Place(c.Renderers[i], posOsu, p.DirOsu, alpha, dia);
            }
        }

        // World-place a point, billboard it, then roll about the view axis so the arrow points along the
        // connection's on-surface direction toward the next object.
        private void Place(SpriteRenderer sr, Vector2 posOsu, Vector2 dirOsu, float alpha, float dia)
        {
            if (sr == null) return;

            Vector3 world = _pf.ToWorld(posOsu);
            Quaternion face = _pf.OrientationAt(posOsu);

            // Direction along the guide in world space, projected into the sprite's (billboard) plane.
            Vector3 wDir = _pf.ToWorld(posOsu + dirOsu * SpacingOsu) - world;
            Vector3 fwd = face * Vector3.forward, right = face * Vector3.right, up = face * Vector3.up;
            Vector3 proj = wDir - Vector3.Dot(wDir, fwd) * fwd;
            float ang = Mathf.Atan2(Vector3.Dot(proj, up), Vector3.Dot(proj, right)) * Mathf.Rad2Deg;

            sr.transform.SetPositionAndRotation(world, face * Quaternion.AngleAxis(ang, Vector3.forward));
            sr.transform.localScale = Vector3.one * dia;

            Color col = sr.color;
            col.a = alpha * 0.7f; // guide sits subtly under the gameplay
            sr.color = col;
        }
    }
}
