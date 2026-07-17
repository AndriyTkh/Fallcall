using System.Collections.Generic;
using OsuUnity.Beatmaps;
using OsuUnity.Gameplay;
using OsuUnity.Skinning;
using UnityEngine;

// INDEX: Drawable slider — body/border line, head, follow circle, ticks, reverse arrows, ball tracking.
namespace OsuUnity.Visual
{
    public sealed class SliderObject : DrawableHitObject
    {
        /// <summary>This object's sorting-order slot, from <see cref="Util.RenderOrder.HitObject"/>.
        /// The parts below sit at -5…+3 around it, inside <see cref="Util.RenderOrder.HitObjectStride"/>.</summary>
        public int SortingBase;

        private Slider _slider;
        private double _spawnTime;

        // visuals
        private MeshRenderer _body;
        private MeshRenderer _border;
        private Material _bodyMat, _borderMat;      // per-slider instances we own + tint each frame
        private Color _trackColor, _borderColor;    // rgb; alpha applied per frame via _Color
        private const int JoinSegments = 10;        // round-join fan resolution at each path vertex
        private SpriteRenderer _headBody, _headOverlay, _approach, _follow;
        private SpriteRenderer _revHead, _revTail;          // reverse arrows (skin only)
        private readonly List<SpriteRenderer> _tickDots = new List<SpriteRenderer>(); // sliderscorepoints
        private SkinNumber _number;
        private Transform _numberAnchor;
        private Vector3 _headWorld;

        // state
        private bool _headJudged, _headHit, _tracking, _finalized;
        private double _resolveTime;
        private double _headHitTime;
        private const double InflateDuration = 150.0;       // ms, one-shot click pop
        private int _nestedTotal, _nestedHit;
        private int _nextTick, _nextRepeat;

        public override void Init(HitObject ho, GameContext ctx)
        {
            base.Init(ho, ctx);
            _slider = (Slider)ho;
            _headWorld = ctx.Playfield.ToWorld(ho.Position);
            transform.position = Vector3.zero;
            _spawnTime = ho.StartTime - ctx.Preempt;

            Color combo = Ctx.ComboColour(Object.ComboColour);
            float dia = ctx.RadiusWorld * 2f;
            int b = SortingBase;

            BuildBody(combo, b - 5);

            _headBody = AddSprite(transform, SkinSprites.HitCircle, combo, dia, b);
            Place(_headBody.transform, Object.Position);
            _headOverlay = AddSprite(transform, SkinSprites.HitCircleOverlay, Color.white, dia, b + 1);
            Place(_headOverlay.transform, Object.Position);
            _approach = AddSprite(transform, SkinSprites.ApproachCircle, combo, dia, b + 3);
            Place(_approach.transform, Object.Position);

            _follow = AddSprite(transform, SkinSprites.SliderFollow, new Color(1, 1, 1, 0.5f),
                ctx.FollowRadiusWorld * 2f, b + 2);
            _follow.enabled = false;

            CreateNumber(ho.ComboNumber, b + 2);
            BuildScorePoints(b - 3);
            BuildReverseArrows(combo, dia, b + 1);

            _nestedTotal = 1 + _slider.TickTimes.Count + (_slider.Slides - 1) + 1; // head + ticks + repeats + tail
            SetGroupAlpha(0f);
        }

        /// <summary>Static dots marking each slider tick, collected (hidden) as the ball passes. Skin only.</summary>
        private void BuildScorePoints(int order)
        {
            if (Skin.Current == null || !Skin.Current.Has("sliderscorepoint")) return;
            float dotDia = Ctx.RadiusWorld * 0.5f;
            for (int i = 0; i < _slider.TickTimes.Count; i++)
            {
                Vector2 osu = _slider.PositionAtTime((int)_slider.TickTimes[i]);
                var dot = AddSprite(transform, SkinSprites.SliderScorePoint, Color.white, dotDia, order);
                Place(dot.transform, osu);
                _tickDots.Add(dot);
            }
        }

        /// <summary>
        /// Reverse arrows on the slider ends for repeats. Each points along the direction the ball
        /// travels after it bounces off that end. Only the end of the next pending bounce is shown.
        /// Skin only (procedural sliders never drew these).
        /// </summary>
        private void BuildReverseArrows(Color combo, float dia, int order)
        {
            if (_slider.Slides <= 1 || Skin.Current == null || !Skin.Current.Has("reversearrow")) return;

            // Tail arrow: after a bounce at the end, the ball heads back toward the head.
            _revTail = AddSprite(transform, SkinSprites.ReverseArrow, Color.white, dia, order);
            PlaceArrow(_revTail.transform, OsuAt(1.0), OsuAt(0.97) - OsuAt(1.0));
            _revTail.enabled = false;

            // Head arrow: after a bounce at the head, the ball heads back toward the end.
            _revHead = AddSprite(transform, SkinSprites.ReverseArrow, Color.white, dia, order);
            PlaceArrow(_revHead.transform, Object.Position, OsuAt(0.03) - Object.Position);
            _revHead.enabled = false;
        }

        /// <summary>Place a reverse arrow at an osu point, oriented on the wall and aimed along an
        /// osu-space travel direction (osu y is down, so its sign flips to the wall's up axis).</summary>
        private void PlaceArrow(Transform t, Vector2 osuPos, Vector2 osuDir)
        {
            t.position = Ctx.Playfield.ToWorld(osuPos);
            float angle = Mathf.Atan2(-osuDir.y, osuDir.x) * Mathf.Rad2Deg;
            t.rotation = Ctx.Playfield.OrientationAt(osuPos) * Quaternion.Euler(0, 0, angle);
        }

        private void BuildBody(Color combo, int order)
        {
            // Track colour: skin override if present, else combo-tinted. RGB only; alpha is applied
            // per frame through the material's _Color tint (see SetGroupAlpha).
            _trackColor = Skin.Current?.Config.SliderTrackOverride ?? combo;

            // Optional outline drawn underneath a slightly narrower track.
            Color? border = Skin.Current?.Config.SliderBorder;
            float radius = Ctx.RadiusWorld;
            if (border.HasValue)
            {
                _borderColor = border.Value;
                _borderMat = Util.MaterialFactory.CreateSliderBorder();
                _border = BuildTube("SliderBorder", order, radius, _borderMat);
            }

            _bodyMat = Util.MaterialFactory.CreateSliderBody();
            _body = BuildTube("SliderBody", order + 1, radius * (border.HasValue ? 0.82f : 1f), _bodyMat);
        }

        /// <summary>
        /// Build the slider body as a proper generated mesh: a quad ribbon along the sampled path plus a
        /// round triangle-fan join at every vertex. Unlike a LineRenderer this stays smooth at any turn
        /// angle — including bezier cusps (repeated control points) where the old ribbon pinched. Overlap
        /// from the joins is harmless: the stencil shader paints each pixel once.
        /// </summary>
        private MeshRenderer BuildTube(string name, int order, float halfWidth, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.sortingOrder = order;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mf.mesh = BuildTubeMesh(halfWidth);
            return mr;
        }

        private Mesh BuildTubeMesh(float halfWidth)
        {
            var pts = _slider.Path.Points;
            int n = pts.Count;

            // Per-point world position, wall normal (out of the wall), and side offset (in the wall plane,
            // perpendicular to travel). Projection lives entirely in Playfield.ToWorld / OrientationAt.
            var world = new Vector3[n];
            var wallN = new Vector3[n];
            var sideU = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 osu = Object.Position + pts[i];
                world[i] = Ctx.Playfield.ToWorld(osu);
                wallN[i] = Ctx.Playfield.OrientationAt(osu) * Vector3.forward;
            }
            for (int i = 0; i < n; i++)
            {
                Vector3 a = world[Mathf.Max(i - 1, 0)];
                Vector3 b = world[Mathf.Min(i + 1, n - 1)];
                Vector3 side = Vector3.Cross(wallN[i], b - a);
                sideU[i] = side.sqrMagnitude > 1e-10f ? side.normalized
                         : Vector3.Cross(wallN[i], Vector3.up).normalized;
            }

            var verts = new List<Vector3>(n * (JoinSegments + 3));
            var tris = new List<int>(n * (JoinSegments + 6) * 3);

            // Ribbon: left/right edge per point, two triangles per segment.
            for (int i = 0; i < n; i++)
            {
                verts.Add(world[i] - sideU[i] * halfWidth); // left  = 2i
                verts.Add(world[i] + sideU[i] * halfWidth); // right = 2i+1
            }
            for (int i = 0; i < n - 1; i++)
            {
                int l0 = 2 * i, r0 = 2 * i + 1, l1 = 2 * (i + 1), r1 = 2 * (i + 1) + 1;
                tris.Add(l0); tris.Add(l1); tris.Add(r0);
                tris.Add(r0); tris.Add(l1); tris.Add(r1);
            }

            // Round join / cap: a filled disc at every vertex fills the inner pinch of a sharp turn and
            // rounds the two ends. Discs overlap the ribbon and each other — the stencil shader dedupes.
            for (int i = 0; i < n; i++)
            {
                Vector3 u = sideU[i] * halfWidth;
                Vector3 v = Vector3.Cross(wallN[i], sideU[i]).normalized * halfWidth;
                int centre = verts.Count;
                verts.Add(world[i]);
                int first = verts.Count;
                for (int s = 0; s < JoinSegments; s++)
                {
                    float ang = s / (float)JoinSegments * Mathf.PI * 2f;
                    verts.Add(world[i] + u * Mathf.Cos(ang) + v * Mathf.Sin(ang));
                }
                for (int s = 0; s < JoinSegments; s++)
                {
                    tris.Add(centre);
                    tris.Add(first + s);
                    tris.Add(first + (s + 1) % JoinSegments);
                }
            }

            var mesh = new Mesh { name = "SliderTube" };
            if (verts.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private Vector3 WorldAt(double progress) => Ctx.Playfield.ToWorld(OsuAt(progress));

        private Vector2 OsuAt(double progress) => Object.Position + _slider.Path.PositionAt(progress);

        /// <summary>Place a sprite on the wall at an osu coordinate and lay it flat against it (3D).</summary>
        private void Place(Transform t, Vector2 osu)
        {
            t.position = Ctx.Playfield.ToWorld(osu);
            t.rotation = Ctx.Playfield.OrientationAt(osu);
        }

        private void CreateNumber(int number, int order)
        {
            var anchor = new GameObject("NumberAnchor");
            anchor.transform.SetParent(transform, false);
            Place(anchor.transform, Object.Position);
            _numberAnchor = anchor.transform;
            _number = new SkinNumber();
            _number.Build(anchor.transform, number, Ctx.RadiusWorld * 0.8f, order, Color.white);
        }

        public override void Tick(double time, bool isFront)
        {
            if (_finalized) { AnimateOut(time); return; }

            // Fade in.
            float fadeT = Mathf.Clamp01((float)((time - _spawnTime) / Ctx.FadeIn));
            SetGroupAlpha(fadeT);

            HandleHead(time, isFront, fadeT);
            UpdateReverseArrows();

            if (time >= _slider.StartTime && time <= _slider.EndTime)
                UpdateSliding(time);

            if (_headHit) UpdateHeadAnim(time);

            if (time >= _slider.EndTime)
                Finalize(time);
        }

        private void HandleHead(double time, bool isFront, float fadeT)
        {
            if (_headJudged)
            {
                if (_approach != null) _approach.enabled = false;
                return;
            }

            double untilHit = _slider.StartTime - time;
            float approachScale = 1f + 3f * Mathf.Clamp01((float)(untilHit / Ctx.Preempt));
            _approach.transform.localScale = Vector3.one * (Ctx.RadiusWorld * 2f * approachScale);
            SetAlpha(_approach, fadeT * 0.9f);

            double delta = time - _slider.StartTime;
            if (isFront && Ctx.Cursor.PressedThisFrame)
            {
                if (Mathf.Abs((float)delta) <= (float)Ctx.Hit50 &&
                    Ctx.CursorWithin(_headWorld, Ctx.RadiusWorld, Ctx.CursorHitboxWorld))
                {
                    HeadResult(true, time);
                    return;
                }
            }

            if (delta > Ctx.Hit50)
                HeadResult(false, time);
        }

        private void HeadResult(bool hit, double time)
        {
            _headJudged = true;
            HeadJudged = true;
            _approach.enabled = false;

            if (hit)
            {
                _headHit = true;
                _headHitTime = time;
                _tracking = true;
                _nestedHit++;
                Ctx.Score.Apply(Judgement.SliderTick, affectsCombo: true, affectsAccuracy: false);
                PlayEdge(0, _slider.StartTime);
            }
            else
            {
                Ctx.Score.Apply(Judgement.Miss, affectsCombo: true, affectsAccuracy: false);
            }
        }

        private void UpdateSliding(double time)
        {
            Vector2 ballOsu = _slider.PositionAtTime((int)time);
            Vector3 ballPos = Ctx.Playfield.ToWorld(ballOsu);
            Quaternion ballRot = Ctx.Playfield.OrientationAt(ballOsu);

            // The head circle itself travels the slider — no static copy left behind at the start.
            _headBody.transform.SetPositionAndRotation(ballPos, ballRot);
            _headOverlay.transform.SetPositionAndRotation(ballPos, ballRot);
            if (_numberAnchor != null) _numberAnchor.SetPositionAndRotation(ballPos, ballRot);

            _follow.enabled = true;
            _follow.transform.SetPositionAndRotation(ballPos, ballRot);

            _tracking = Ctx.Cursor.Held && Ctx.CursorWithin(ballPos, Ctx.FollowRadiusWorld);
            _follow.transform.localScale = Vector3.one *
                (Ctx.FollowRadiusWorld * 2f * (_tracking ? 1f : 0.8f));
            SetAlpha(_follow, _tracking ? 0.7f : 0.25f);

            // Slider ticks.
            while (_nextTick < _slider.TickTimes.Count && _slider.TickTimes[_nextTick] <= time)
            {
                if (_nextTick < _tickDots.Count) _tickDots[_nextTick].enabled = false; // collected
                if (_tracking)
                {
                    _nestedHit++;
                    Ctx.Score.Apply(Judgement.SliderTick, affectsCombo: true, affectsAccuracy: false);
                    Ctx.HitSounds.PlayTick(_slider.TickTimes[_nextTick], Object.SampleBank,
                        Object.CustomSampleIndex, Object.SampleVolume);
                }
                else
                {
                    Ctx.Score.Apply(Judgement.Miss, affectsCombo: true, affectsAccuracy: false);
                }
                _nextTick++;
            }

            // Repeat edges (span boundaries 1..Slides-1).
            while (_nextRepeat < _slider.Slides - 1)
            {
                double repeatTime = _slider.StartTime + (_nextRepeat + 1) * _slider.SpanDuration;
                if (repeatTime > time) break;
                if (_tracking)
                {
                    _nestedHit++;
                    Ctx.Score.Apply(Judgement.SliderTick, affectsCombo: true, affectsAccuracy: false);
                    PlayEdge(_nextRepeat + 1, (int)repeatTime);
                }
                else
                {
                    Ctx.Score.Apply(Judgement.Miss, affectsCombo: true, affectsAccuracy: false);
                }
                _nextRepeat++;
            }
        }

        private void Finalize(double time)
        {
            // Resolve any nested elements we might have skipped (e.g. very short sliders).
            UpdateSliding(_slider.EndTime);

            // Tail. A tail miss forfeits only its own point — unlike ticks/repeats it never breaks combo
            // (osu!lazer's SliderTailCircle judges as a non-combo-breaking miss).
            if (_tracking)
            {
                _nestedHit++;
                Ctx.Score.Apply(Judgement.SliderTick, affectsCombo: true, affectsAccuracy: false);
                PlayEdge(_slider.Slides, _slider.EndTime);
            }
            else
            {
                Ctx.Score.Apply(Judgement.Miss, affectsCombo: false, affectsAccuracy: false);
            }

            // Overall accuracy judgement by fraction of nested objects collected.
            float frac = _nestedTotal > 0 ? _nestedHit / (float)_nestedTotal : 0f;
            Judgement result = frac >= 1f ? Judgement.Great
                             : frac > 0.5f ? Judgement.Ok
                             : frac > 0f ? Judgement.Meh
                             : Judgement.Miss;

            Ctx.Score.Apply(result, affectsCombo: false, affectsAccuracy: true);
            Ctx.OnJudgement?.Invoke(result, WorldAt(1.0));

            _finalized = true;
            _resolveTime = time;
            _follow.enabled = false;
            if (_revHead != null) _revHead.enabled = false;
            if (_revTail != null) _revTail.enabled = false;
        }

        /// <summary>Show the reverse arrow on whichever end the next pending bounce occurs at.</summary>
        private void UpdateReverseArrows()
        {
            if (_revHead == null && _revTail == null) return;
            bool remaining = (_slider.Slides - 1) - _nextRepeat > 0;
            bool bounceAtTail = (_nextRepeat % 2) == 0; // first bounce is at the far end
            if (_revTail != null) _revTail.enabled = remaining && bounceAtTail;
            if (_revHead != null) _revHead.enabled = remaining && !bounceAtTail;
        }

        private HitSoundType EdgeSound(int index)
        {
            if (_slider.EdgeSounds != null && index < _slider.EdgeSounds.Count)
                return _slider.EdgeSounds[index];
            return Object.HitSound;
        }

        /// <summary>Play a slider edge, using its own sample banks when the edgeSets field supplied them.</summary>
        private void PlayEdge(int index, int timeMs)
        {
            SampleBank normal = Object.SampleBank, addition = Object.AdditionBank;
            if (_slider.EdgeSampleSets != null && index < _slider.EdgeSampleSets.Count)
            {
                var es = _slider.EdgeSampleSets[index];
                if (es.Normal != SampleBank.Auto) normal = es.Normal;
                if (es.Addition != SampleBank.Auto) addition = es.Addition;
            }
            Ctx.HitSounds.Play(EdgeSound(index), timeMs, normal, addition,
                Object.CustomSampleIndex, Object.SampleVolume);
        }

        /// <summary>Head-circle scale once hit: a one-shot inflate "pop" on click (same feel as a hit circle).</summary>
        private void UpdateHeadAnim(double time)
        {
            float scale = 1f;

            float pop = Mathf.Clamp01((float)((time - _headHitTime) / InflateDuration));
            if (pop < 1f) scale += 0.25f * Mathf.Sin(pop * Mathf.PI);   // grow then settle

            float dia = Ctx.RadiusWorld * 2f * scale;
            _headBody.transform.localScale = Vector3.one * dia;
            _headOverlay.transform.localScale = Vector3.one * dia;
        }

        private void AnimateOut(double time)
        {
            float t = Mathf.Clamp01((float)((time - _resolveTime) / 220.0));
            SetGroupAlpha(1f - t);
            if (t >= 1f) Finished = true;
        }

        private void SetGroupAlpha(float a)
        {
            SetAlpha(_headBody, a * 0.85f);
            SetAlpha(_headOverlay, a);
            SetAlpha(_revHead, a);
            SetAlpha(_revTail, a);
            foreach (var dot in _tickDots) SetAlpha(dot, a);
            SetTubeAlpha(_bodyMat, _trackColor, 0.55f * a);
            SetTubeAlpha(_borderMat, _borderColor, 0.9f * a);
            _number?.SetAlpha(_headHit ? 0f : a);   // number hidden once the head travels off
            if (_numberAnchor != null) _numberAnchor.gameObject.SetActive(!_headHit);
        }

        /// <summary>Tint a tube material: combo/border rgb at the current fade alpha, via the shader _Color.</summary>
        private static void SetTubeAlpha(Material mat, Color rgb, float a)
        {
            if (mat == null) return;
            rgb.a = a;
            mat.SetColor("_Color", rgb);
        }

        private void OnDestroy()
        {
            // We own these per-slider material instances and generated meshes; free them.
            if (_bodyMat != null) Destroy(_bodyMat);
            if (_borderMat != null) Destroy(_borderMat);
            if (_body != null) { var m = _body.GetComponent<MeshFilter>(); if (m != null) Destroy(m.sharedMesh); }
            if (_border != null) { var m = _border.GetComponent<MeshFilter>(); if (m != null) Destroy(m.sharedMesh); }
        }
    }
}
