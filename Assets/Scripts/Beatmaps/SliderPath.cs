using System;
using System.Collections.Generic;
using UnityEngine;

namespace OsuUnity.Beatmaps
{
    /// <summary>
    /// Computes the geometry of a slider from its control points and resamples it to a fixed
    /// pixel length. Positions returned are relative to the slider's start position (osu! pixels).
    /// </summary>
    public sealed class SliderPath
    {
        private readonly List<Vector2> _points = new List<Vector2>();      // densely sampled polyline
        private readonly List<double> _cumulative = new List<double>();    // cumulative arc length per point

        /// <summary>Total (clamped) length of the path in osu! pixels.</summary>
        public double Length { get; private set; }

        public IReadOnlyList<Vector2> Points => _points;

        public SliderPath(SliderCurveType type, IReadOnlyList<Vector2> controlPoints, double pixelLength)
        {
            // All control points come in as absolute osu! coordinates; make them relative to the head.
            Vector2 origin = controlPoints.Count > 0 ? controlPoints[0] : Vector2.zero;
            var relative = new List<Vector2>(controlPoints.Count);
            foreach (var c in controlPoints) relative.Add(c - origin);

            List<Vector2> raw = CalculateRaw(type, relative);
            BuildClamped(raw, pixelLength);
        }

        // ------------------------------------------------------------------ public sampling

        /// <summary>Position at progress 0..1 along the (clamped) path.</summary>
        public Vector2 PositionAt(double progress)
        {
            progress = Math.Clamp(progress, 0.0, 1.0);
            return PositionAtDistance(progress * Length);
        }

        /// <summary>Position at an absolute arc-length distance along the path.</summary>
        public Vector2 PositionAtDistance(double distance)
        {
            if (_points.Count == 0) return Vector2.zero;
            if (_points.Count == 1) return _points[0];
            distance = Math.Clamp(distance, 0.0, Length);

            // Binary search for the segment that contains this distance.
            int lo = 0, hi = _cumulative.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_cumulative[mid] < distance) lo = mid + 1;
                else hi = mid;
            }

            int i = Mathf.Max(1, lo);
            double segLen = _cumulative[i] - _cumulative[i - 1];
            double t = segLen <= 0 ? 0 : (distance - _cumulative[i - 1]) / segLen;
            return Vector2.LerpUnclamped(_points[i - 1], _points[i], (float)t);
        }

        // ------------------------------------------------------------------ construction

        private void BuildClamped(List<Vector2> raw, double pixelLength)
        {
            if (raw.Count == 0)
            {
                _points.Add(Vector2.zero);
                _cumulative.Add(0);
                Length = 0;
                return;
            }

            _points.Add(raw[0]);
            _cumulative.Add(0);
            double total = 0;

            for (int i = 1; i < raw.Count; i++)
            {
                double d = Vector2.Distance(raw[i - 1], raw[i]);
                if (d <= 1e-7) continue; // skip duplicate points

                if (pixelLength > 0 && total + d >= pixelLength)
                {
                    // Truncate exactly at pixelLength.
                    double remaining = pixelLength - total;
                    Vector2 dir = (raw[i] - raw[i - 1]).normalized;
                    Vector2 end = raw[i - 1] + dir * (float)remaining;
                    _points.Add(end);
                    _cumulative.Add(pixelLength);
                    Length = pixelLength;
                    return;
                }

                total += d;
                _points.Add(raw[i]);
                _cumulative.Add(total);
            }

            // The raw geometry was shorter than the authored pixel length: extend along the last direction.
            if (pixelLength > 0 && total < pixelLength && _points.Count >= 2)
            {
                Vector2 a = _points[_points.Count - 2];
                Vector2 b = _points[_points.Count - 1];
                Vector2 dir = (b - a).sqrMagnitude > 1e-9f ? (b - a).normalized : Vector2.right;
                Vector2 end = b + dir * (float)(pixelLength - total);
                _points.Add(end);
                _cumulative.Add(pixelLength);
                Length = pixelLength;
            }
            else
            {
                Length = total;
            }
        }

        private static List<Vector2> CalculateRaw(SliderCurveType type, List<Vector2> cp)
        {
            switch (type)
            {
                case SliderCurveType.Linear:
                    return new List<Vector2>(cp);

                case SliderCurveType.PerfectCircle:
                    if (cp.Count != 3) goto case SliderCurveType.Bezier;
                    return CirclePath(cp[0], cp[1], cp[2]);

                case SliderCurveType.Catmull:
                    return CatmullPath(cp);

                case SliderCurveType.Bezier:
                default:
                    return BezierPath(cp);
            }
        }

        // ------------------------------------------------------------------ bezier

        private static List<Vector2> BezierPath(List<Vector2> cp)
        {
            var result = new List<Vector2>();
            if (cp.Count == 0) return result;

            // osu! encodes multiple bezier segments in a single point list by repeating the
            // point where one segment ends and the next begins.
            var segment = new List<Vector2> { cp[0] };
            for (int i = 1; i < cp.Count; i++)
            {
                bool repeated = cp[i] == cp[i - 1];
                bool last = i == cp.Count - 1;

                if (repeated)
                {
                    FlattenBezier(segment, result);
                    segment = new List<Vector2>();
                }

                segment.Add(cp[i]);

                if (last) FlattenBezier(segment, result);
            }
            return result;
        }

        private static void FlattenBezier(List<Vector2> control, List<Vector2> output)
        {
            int n = control.Count;
            if (n == 0) return;
            if (n == 1) { Append(output, control[0]); return; }

            // Sample count scales with the rough length so long curves stay smooth.
            double rough = 0;
            for (int i = 1; i < n; i++) rough += Vector2.Distance(control[i - 1], control[i]);
            int steps = Mathf.Clamp((int)(rough / 5.0) + n * 2, 12, 1000);

            for (int s = 0; s <= steps; s++)
            {
                double t = (double)s / steps;
                Append(output, DeCasteljau(control, t));
            }
        }

        [System.ThreadStatic] private static Vector2[] _deCasteljauBuffer;

        private static Vector2 DeCasteljau(List<Vector2> pts, double t)
        {
            int n = pts.Count;
            if (_deCasteljauBuffer == null || _deCasteljauBuffer.Length < n)
                _deCasteljauBuffer = new Vector2[Mathf.Max(n, 64)];
            var buf = _deCasteljauBuffer;

            for (int i = 0; i < n; i++) buf[i] = pts[i];
            for (int k = 1; k < n; k++)
                for (int i = 0; i < n - k; i++)
                    buf[i] = Vector2.LerpUnclamped(buf[i], buf[i + 1], (float)t);
            return buf[0];
        }

        // ------------------------------------------------------------------ perfect circle

        private static List<Vector2> CirclePath(Vector2 a, Vector2 b, Vector2 c)
        {
            // Circumcentre of the triangle a,b,c.
            float aSq = b.sqrMagnitude - a.sqrMagnitude;
            float bSq = c.sqrMagnitude - a.sqrMagnitude;

            float det = 2 * ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
            if (Mathf.Abs(det) < 1e-5f)
            {
                // Collinear: fall back to a straight line.
                return new List<Vector2> { a, b, c };
            }

            float cx = ((c.y - a.y) * aSq - (b.y - a.y) * bSq) / det;
            float cy = ((b.x - a.x) * bSq - (c.x - a.x) * aSq) / det;
            Vector2 centre = new Vector2(a.x + cx, a.y + cy);
            float radius = Vector2.Distance(centre, a);

            double angA = Math.Atan2(a.y - centre.y, a.x - centre.x);
            double angB = Math.Atan2(b.y - centre.y, b.x - centre.x);
            double angC = Math.Atan2(c.y - centre.y, c.x - centre.x);

            // Decide arc direction so that b lies between a and c.
            // Normalise to go from angA toward angC, possibly the long way, keeping b on the path.
            if (!IsBetween(angA, angB, angC))
            {
                // go the other way
                if (angC < angA) angC += 2 * Math.PI;
                else angC -= 2 * Math.PI;
            }

            var pts = new List<Vector2>();
            int steps = Mathf.Clamp((int)(Math.Abs(angC - angA) * radius / 4.0) + 8, 16, 2000);
            for (int i = 0; i <= steps; i++)
            {
                double ang = angA + (angC - angA) * (i / (double)steps);
                pts.Add(centre + new Vector2((float)Math.Cos(ang), (float)Math.Sin(ang)) * radius);
            }
            return pts;
        }

        private static bool IsBetween(double a, double b, double c)
        {
            // Is b on the short arc from a to c (going the direct way)?
            double TwoPi = 2 * Math.PI;
            double ab = Mod(b - a, TwoPi);
            double ac = Mod(c - a, TwoPi);
            return ab <= ac;
        }

        private static double Mod(double x, double m)
        {
            double r = x % m;
            return r < 0 ? r + m : r;
        }

        // ------------------------------------------------------------------ catmull-rom

        private static List<Vector2> CatmullPath(List<Vector2> cp)
        {
            var result = new List<Vector2>();
            if (cp.Count == 0) return result;
            if (cp.Count == 1) { result.Add(cp[0]); return result; }

            for (int i = 0; i < cp.Count - 1; i++)
            {
                Vector2 p0 = i > 0 ? cp[i - 1] : cp[i];
                Vector2 p1 = cp[i];
                Vector2 p2 = cp[i + 1];
                Vector2 p3 = i < cp.Count - 2 ? cp[i + 2] : p2 + (p2 - p1);

                const int steps = 24;
                for (int s = 0; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    Append(result, CatmullPoint(p0, p1, p2, p3, t));
                }
            }
            return result;
        }

        private static Vector2 CatmullPoint(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        // ------------------------------------------------------------------ helpers

        private static void Append(List<Vector2> list, Vector2 p)
        {
            if (list.Count == 0 || (list[list.Count - 1] - p).sqrMagnitude > 1e-6f)
                list.Add(p);
        }
    }
}
