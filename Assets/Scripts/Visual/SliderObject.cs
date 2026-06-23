using System.Collections.Generic;
using OsuUnity.Beatmaps;
using OsuUnity.Gameplay;
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
        private SpriteRenderer _headBody, _headBorder, _approach, _ball, _follow, _tail;
        private TextMesh _number;
        private Vector3 _headWorld;

        // state
        private bool _headJudged, _headHit, _tracking, _finalized;
        private double _resolveTime;
        private int _nestedTotal, _nestedHit;
        private int _nextTick, _nextRepeat;

        public override void Init(HitObject ho, GameContext ctx)
        {
            base.Init(ho, ctx);
            _slider = (Slider)ho;
            _headWorld = ctx.Playfield.ToWorld(ho.Position);
            transform.position = Vector3.zero;
            _spawnTime = ho.StartTime - ctx.Preempt;

            Color combo = ComboColour();
            float dia = ctx.RadiusWorld * 2f;
            int b = DepthOrder * 10;

            BuildBody(combo, b - 5);

            _tail = AddSprite(transform, Util.TextureFactory.Disc, combo * 0.8f, dia,
                b - 4);
            _tail.transform.position = WorldAt(1.0);

            _headBody = AddSprite(transform, Util.TextureFactory.Disc, combo, dia, b);
            _headBody.transform.position = _headWorld;
            _headBorder = AddSprite(transform, Util.TextureFactory.Ring, Color.white, dia, b + 1);
            _headBorder.transform.position = _headWorld;
            _approach = AddSprite(transform, Util.TextureFactory.Ring, combo, dia, b + 3);
            _approach.transform.position = _headWorld;

            _ball = AddSprite(transform, Util.TextureFactory.Disc, combo * 0.6f, dia * 0.9f, b + 4);
            _follow = AddSprite(transform, Util.TextureFactory.SoftRing, new Color(1, 1, 1, 0.5f),
                ctx.FollowRadiusWorld * 2f, b + 2);
            _ball.enabled = false;
            _follow.enabled = false;

            CreateNumber(ho.ComboNumber, b + 2);

            _nestedTotal = 1 + _slider.TickTimes.Count + (_slider.Slides - 1) + 1; // head + ticks + repeats + tail
            SetGroupAlpha(0f);
        }

        private Color ComboColour()
        {
            var colours = Ctx.Beatmap.ComboColours;
            if (colours.Count == 0) return new Color(0.4f, 0.6f, 0.9f);
            return colours[Object.ComboColour % colours.Count];
        }

        private void BuildBody(Color combo, int order)
        {
            var go = new GameObject("SliderBody");
            go.transform.SetParent(transform, false);
            _body = go.AddComponent<LineRenderer>();
            _body.useWorldSpace = true;
            _body.alignment = LineAlignment.View;
            _body.numCapVertices = 8;
            _body.numCornerVertices = 4;
            _body.textureMode = LineTextureMode.Stretch;
            _body.material = Util.MaterialFactory.UnlitTransparent;
            _body.widthMultiplier = Ctx.RadiusWorld * 2f;
            _body.sortingOrder = order;

            var pts = _slider.Path.Points;
            _body.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++)
                _body.SetPosition(i, Ctx.Playfield.ToWorld(Object.Position + pts[i]));

            Color body = combo;
            body.a = 0.45f;
            _body.startColor = _body.endColor = body;
        }

        private Vector3 WorldAt(double progress) =>
            Ctx.Playfield.ToWorld(Object.Position + _slider.Path.PositionAt(progress));

        private void CreateNumber(int number, int order)
        {
            var go = new GameObject("ComboNumber");
            go.transform.SetParent(transform, false);
            go.transform.position = _headWorld + new Vector3(0, 0, -0.001f);
            _number = go.AddComponent<TextMesh>();
            _number.text = number.ToString();
            _number.anchor = TextAnchor.MiddleCenter;
            _number.alignment = TextAlignment.Center;
            _number.fontSize = 64;
            _number.color = Color.white;
            _number.font = VisualResources.NumberFont;
            _number.characterSize = Ctx.RadiusWorld * 0.045f;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _number.font.material;
            mr.sortingOrder = order;
        }

        public override void Tick(double time, bool isFront)
        {
            if (_finalized) { AnimateOut(time); return; }

            // Fade in.
            float fadeT = Mathf.Clamp01((float)((time - _spawnTime) / Ctx.FadeIn));
            SetGroupAlpha(fadeT);

            HandleHead(time, isFront, fadeT);

            if (time >= _slider.StartTime && time <= _slider.EndTime)
                UpdateSliding(time);

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
                _tracking = true;
                _nestedHit++;
                Ctx.Score.Apply(Judgement.SliderTick, affectsCombo: true, affectsAccuracy: false);
                Ctx.HitSounds.Play(EdgeSound(0), _slider.StartTime);
            }
            else
            {
                Ctx.Score.Apply(Judgement.Miss, affectsCombo: true, affectsAccuracy: false);
            }
        }

        private void UpdateSliding(double time)
        {
            Vector3 ballPos = Ctx.Playfield.ToWorld(_slider.PositionAtTime((int)time));
            _ball.enabled = true;
            _follow.enabled = true;
            _ball.transform.position = ballPos;
            _follow.transform.position = ballPos;

            _tracking = Ctx.Cursor.Held && Ctx.CursorWithin(ballPos, Ctx.FollowRadiusWorld);
            _follow.transform.localScale = Vector3.one *
                (Ctx.FollowRadiusWorld * 2f * (_tracking ? 1f : 0.8f));
            SetAlpha(_follow, _tracking ? 0.7f : 0.25f);

            // Slider ticks.
            while (_nextTick < _slider.TickTimes.Count && _slider.TickTimes[_nextTick] <= time)
            {
                if (_tracking)
                {
                    _nestedHit++;
                    Ctx.Score.Apply(Judgement.SliderTick, affectsCombo: true, affectsAccuracy: false);
                    Ctx.HitSounds.PlayTick();
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
                    Ctx.HitSounds.Play(EdgeSound(_nextRepeat + 1), (int)repeatTime);
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
                Ctx.HitSounds.Play(EdgeSound(_slider.Slides), _slider.EndTime);
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
            _ball.enabled = false;
            _follow.enabled = false;
        }

        private HitSoundType EdgeSound(int index)
        {
            if (_slider.EdgeSounds != null && index < _slider.EdgeSounds.Count)
                return _slider.EdgeSounds[index];
            return Object.HitSound;
        }

        private void AnimateOut(double time)
        {
            float t = Mathf.Clamp01((float)((time - _resolveTime) / 220.0));
            SetGroupAlpha(1f - t);
            if (_body != null)
            {
                Color c = _body.startColor; c.a = 0.45f * (1f - t);
                _body.startColor = _body.endColor = c;
            }
            if (t >= 1f) Finished = true;
        }

        private void SetGroupAlpha(float a)
        {
            SetAlpha(_headBody, a * 0.85f);
            SetAlpha(_headBorder, a);
            SetAlpha(_tail, a * 0.6f);
            if (_body != null)
            {
                Color c = _body.startColor; c.a = 0.45f * a;
                _body.startColor = _body.endColor = c;
            }
            if (_number != null)
            {
                Color c = _number.color; c.a = a; _number.color = c;
            }
        }
    }
}
