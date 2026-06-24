using OsuUnity.Beatmaps;
using OsuUnity.Gameplay;
using OsuUnity.Skinning;
using UnityEngine;

namespace OsuUnity.Visual
{
    public sealed class HitCircleObject : DrawableHitObject
    {
        public int DepthOrder;

        private SpriteRenderer _body;
        private SpriteRenderer _overlay;
        private SpriteRenderer _approach;
        private SkinNumber _number;

        private double _spawnTime;
        private bool _resolved;
        private Judgement _result;
        private double _resolveTime;
        private Vector3 _worldPos;

        public override void Init(HitObject ho, GameContext ctx)
        {
            base.Init(ho, ctx);
            _worldPos = ctx.Playfield.ToWorld(ho.Position);
            transform.position = _worldPos;
            _spawnTime = ho.StartTime - ctx.Preempt;

            Color combo = Ctx.ComboColour(Object.ComboColour);
            float dia = ctx.RadiusWorld * 2f;
            int b = DepthOrder * 10;

            // osu! layering: hit circle (combo-tinted), overlay (untinted) and the combo number, with
            // the overlay either above or below the number per HitCircleOverlayAboveNumber.
            bool overlayAbove = Skin.Current?.Config.HitCircleOverlayAboveNumber ?? true;
            int numberOrder = overlayAbove ? b + 1 : b + 2;
            int overlayOrder = overlayAbove ? b + 2 : b + 1;

            _body = AddSprite(transform, SkinSprites.HitCircle, combo, dia, b);
            _overlay = AddSprite(transform, SkinSprites.HitCircleOverlay, Color.white, dia, overlayOrder);
            _approach = AddSprite(transform, SkinSprites.ApproachCircle, combo, dia, b + 3);

            _number = new SkinNumber();
            _number.Build(transform, ho.ComboNumber, ctx.RadiusWorld * 0.8f, numberOrder, Color.white);
            SetGroupAlpha(0f);
        }

        public override void Tick(double time, bool isFront)
        {
            if (_resolved)
            {
                AnimateResolved(time);
                return;
            }

            // Fade in.
            float fadeT = Mathf.Clamp01((float)((time - _spawnTime) / Ctx.FadeIn));
            SetGroupAlpha(fadeT);

            // Approach circle shrinks from ~4x to 1x as the hit time nears.
            double untilHit = Object.StartTime - time;
            float approachScale = 1f + 3f * Mathf.Clamp01((float)(untilHit / Ctx.Preempt));
            _approach.transform.localScale = Vector3.one * (Ctx.RadiusWorld * 2f * approachScale);
            SetAlpha(_approach, fadeT * 0.9f);

            // Input / miss handling.
            double delta = time - Object.StartTime;

            if (isFront && Ctx.Cursor.PressedThisFrame)
            {
                if (Mathf.Abs((float)delta) <= (float)Ctx.Hit50 &&
                    Ctx.CursorWithin(_worldPos, Ctx.RadiusWorld))
                {
                    Resolve(Ctx.JudgeTiming(Mathf.Abs((float)delta)), time);
                    return;
                }
                // else: too early / cursor off the circle -> press ignored, not consumed.
            }

            if (delta > Ctx.Hit50)
                Resolve(Judgement.Miss, time);
        }

        private void Resolve(Judgement j, double time)
        {
            _resolved = true;
            HeadJudged = true;
            _result = j;
            _resolveTime = time;

            Ctx.Score.Apply(j);
            Ctx.OnJudgement?.Invoke(j, _worldPos);

            if (j != Judgement.Miss)
                Ctx.HitSounds.Play(Object.HitSound, Object.StartTime,
                    Object.SampleBank, Object.AdditionBank, Object.CustomSampleIndex, Object.SampleVolume);

            if (_approach != null) _approach.enabled = false;
        }

        private void AnimateResolved(double time)
        {
            float t = Mathf.Clamp01((float)((time - _resolveTime) / 180.0));

            if (_result == Judgement.Miss)
            {
                // Fade and drift slightly.
                SetGroupAlpha(1f - t);
            }
            else
            {
                float scale = 1f + 0.4f * t;
                _body.transform.localScale = Vector3.one * (Ctx.RadiusWorld * 2f * scale);
                _overlay.transform.localScale = _body.transform.localScale;
                SetGroupAlpha(1f - t);
            }

            if (t >= 1f) Finished = true;
        }

        private void SetGroupAlpha(float a)
        {
            SetAlpha(_body, a * 0.85f);
            SetAlpha(_overlay, a);
            _number?.SetAlpha(a);
        }
    }
}
