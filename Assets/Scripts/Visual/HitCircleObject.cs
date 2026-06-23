using OsuUnity.Beatmaps;
using OsuUnity.Gameplay;
using UnityEngine;

namespace OsuUnity.Visual
{
    public sealed class HitCircleObject : DrawableHitObject
    {
        public int DepthOrder;

        private SpriteRenderer _body;
        private SpriteRenderer _border;
        private SpriteRenderer _approach;
        private TextMesh _number;
        private MeshRenderer _numberRenderer;

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

            Color combo = ComboColour();
            float dia = ctx.RadiusWorld * 2f;
            int b = DepthOrder * 10;

            _body = AddSprite(transform, Util.TextureFactory.Disc, combo, dia, b);
            _border = AddSprite(transform, Util.TextureFactory.Ring, Color.white, dia, b + 1);
            _approach = AddSprite(transform, Util.TextureFactory.Ring, combo, dia, b + 3);

            CreateNumber(ho.ComboNumber, b + 2, ctx.RadiusWorld);
            SetGroupAlpha(0f);
        }

        private Color ComboColour()
        {
            var colours = Ctx.Beatmap.ComboColours;
            if (colours.Count == 0) return new Color(0.9f, 0.4f, 0.5f);
            return colours[Object.ComboColour % colours.Count];
        }

        private void CreateNumber(int number, int order, float radiusWorld)
        {
            var go = new GameObject("ComboNumber");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, 0, -0.001f);

            _number = go.AddComponent<TextMesh>();
            _number.text = number.ToString();
            _number.anchor = TextAnchor.MiddleCenter;
            _number.alignment = TextAlignment.Center;
            _number.fontSize = 64;
            _number.color = Color.white;
            _number.font = VisualResources.NumberFont;
            _number.characterSize = radiusWorld * 0.045f;

            _numberRenderer = go.GetComponent<MeshRenderer>();
            _numberRenderer.sharedMaterial = _number.font.material;
            _numberRenderer.sortingOrder = order;
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
                Ctx.HitSounds.Play(Object.HitSound, Object.StartTime);

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
                _border.transform.localScale = _body.transform.localScale;
                SetGroupAlpha(1f - t);
            }

            if (t >= 1f) Finished = true;
        }

        private void SetGroupAlpha(float a)
        {
            SetAlpha(_body, a * 0.85f);
            SetAlpha(_border, a);
            if (_number != null)
            {
                Color c = _number.color;
                c.a = a;
                _number.color = c;
            }
        }
    }
}
