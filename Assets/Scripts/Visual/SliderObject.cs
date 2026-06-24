using System.Collections.Generic;
using OsuUnity.Beatmaps;
using OsuUnity.Gameplay;
using OsuUnity.Skinning;
using UnityEngine;

namespace OsuUnity.Visual
{
    public sealed class SliderObject : DrawableHitObject
    {
        public int DepthOrder;

        private Slider _slider;
        private double _spawnTime;

        // visuals
        private LineRenderer _body;
        private LineRenderer _border;
        private SpriteRenderer _headBody, _headOverlay, _approach, _follow, _tail;
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
            int b = DepthOrder * 10;

            BuildBody(combo, b - 5);

            _tail = AddSprite(transform, SkinSprites.HitCircle, combo * 0.8f, dia, b - 4);
            Place(_tail.transform, OsuAt(1.0));

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
            // Track colour: skin override if present, else combo-tinted.
            Color track = Skin.Current?.Config.SliderTrackOverride ?? combo;
            track.a = 0.55f;

            // Optional outline drawn underneath a slightly narrower track.
            Color? border = Skin.Current?.Config.SliderBorder;
            if (border.HasValue)
            {
                _border = NewLine("SliderBorder", order, Ctx.RadiusWorld * 2f);
                Color bc = border.Value; bc.a = 0.9f;
                _border.startColor = _border.endColor = bc;
            }

            _body = NewLine("SliderBody", order + 1, Ctx.RadiusWorld * 2f * (border.HasValue ? 0.82f : 1f));
            _body.startColor = _body.endColor = track;
        }

        private LineRenderer NewLine(string name, int order, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.alignment = LineAlignment.View;
            lr.numCapVertices = 8;
            lr.numCornerVertices = 4;
            lr.textureMode = LineTextureMode.Stretch;
            lr.material = Util.MaterialFactory.UnlitTransparent;
            lr.widthMultiplier = width;
            lr.sortingOrder = order;

            var pts = _slider.Path.Points;
            lr.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++)
                lr.SetPosition(i, Ctx.Playfield.ToWorld(Object.Position + pts[i]));
            return lr;
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
                    Ctx.CursorWithin(_headWorld, Ctx.RadiusWorld))
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

            // Tail.
            if (_tracking)
            {
                _nestedHit++;
                Ctx.Score.Apply(Judgement.SliderTick, affectsCombo: true, affectsAccuracy: false);
                PlayEdge(_slider.Slides, _slider.EndTime);
            }
            else
            {
                Ctx.Score.Apply(Judgement.Miss, affectsCombo: true, affectsAccuracy: false);
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
            SetAlpha(_tail, a * 0.6f);
            SetAlpha(_revHead, a);
            SetAlpha(_revTail, a);
            foreach (var dot in _tickDots) SetAlpha(dot, a);
            SetLineAlpha(_body, 0.55f * a);
            SetLineAlpha(_border, 0.9f * a);
            _number?.SetAlpha(_headHit ? 0f : a);   // number hidden once the head travels off
            if (_numberAnchor != null) _numberAnchor.gameObject.SetActive(!_headHit);
        }

        private static void SetLineAlpha(LineRenderer lr, float a)
        {
            if (lr == null) return;
            Color c = lr.startColor; c.a = a;
            lr.startColor = lr.endColor = c;
        }
    }
}
