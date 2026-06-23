using OsuUnity.Beatmaps;
using OsuUnity.Gameplay;
using UnityEngine;

namespace OsuUnity.Visual
{
    public sealed class SpinnerObject : DrawableHitObject
    {
        public int DepthOrder;

        private Spinner _spinner;
        private Vector3 _centre;
        private SpriteRenderer _disc, _ring, _spin;
        private TextMesh _info;

        private double _required;       // required full rotations
        private double _accumulated;    // accumulated rotations
        private float _lastAngle;
        private bool _hasLast;
        private bool _resolved;
        private double _resolveTime;
        private double _spawnTime;

        public override void Init(HitObject ho, GameContext ctx)
        {
            base.Init(ho, ctx);
            _spinner = (Spinner)ho;
            _centre = ctx.Playfield.ToWorld(new Vector2(256, 192));
            _spawnTime = ho.StartTime - ctx.Preempt * 0.5;

            int b = DepthOrder * 10;
            float big = ctx.Playfield.OsuToWorldDistance(Playfield.Height * 0.45f) * 2f;

            _disc = AddSprite(transform, Util.TextureFactory.Disc, new Color(0.1f, 0.1f, 0.15f, 0.5f), big, b);
            _disc.transform.position = _centre;
            _ring = AddSprite(transform, Util.TextureFactory.Ring, Color.white, big, b + 1);
            _ring.transform.position = _centre;
            _spin = AddSprite(transform, Util.TextureFactory.Ring, new Color(1f, 0.8f, 0.2f), big * 0.5f, b + 2);
            _spin.transform.position = _centre;

            double seconds = (_spinner.EndTime - _spinner.StartTime) / 1000.0;
            _required = Mathf.Max(1f, (float)(DifficultyCalculator.SpinsPerSecond(ctx.Beatmap.Difficulty.OverallDifficulty) * seconds));

            CreateInfo(b + 3);
            SetGroupAlpha(0f);

            // Spinners never participate in note-lock ordering.
            HeadJudged = true;
        }

        private void CreateInfo(int order)
        {
            var go = new GameObject("SpinnerInfo");
            go.transform.SetParent(transform, false);
            go.transform.position = _centre + new Vector3(0, -Ctx.Playfield.OsuToWorldDistance(60), -0.001f);
            _info = go.AddComponent<TextMesh>();
            _info.anchor = TextAnchor.MiddleCenter;
            _info.alignment = TextAlignment.Center;
            _info.fontSize = 48;
            _info.color = Color.white;
            _info.font = VisualResources.NumberFont;
            _info.characterSize = Ctx.Playfield.OsuToWorldDistance(40) * 0.05f;
            _info.text = "SPIN!";
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _info.font.material;
            mr.sortingOrder = order;
        }

        public override void Tick(double time, bool isFront)
        {
            if (_resolved) { AnimateOut(time); return; }

            float fadeT = Mathf.Clamp01((float)((time - _spawnTime) / (Ctx.FadeIn * 0.5)));
            SetGroupAlpha(fadeT);

            if (time < _spinner.StartTime)
            {
                HeadJudged = true; // spinners never block the queue
                return;
            }

            HeadJudged = true;

            if (time <= _spinner.EndTime)
            {
                AccumulateSpin(time);
                float progress = Mathf.Clamp01((float)(_accumulated / _required));
                float scale = Mathf.Lerp(1f, 0.15f, progress);
                _ring.transform.localScale = _disc.transform.localScale * scale;
                // visualise spin
                _spin.transform.Rotate(0, 0, -200f * Time.deltaTime * (Ctx.Cursor.Held ? 2f : 1f));
                if (_info != null) _info.text = $"{(int)(progress * 100)}%";
            }
            else
            {
                Resolve(time);
            }
        }

        private void AccumulateSpin(double time)
        {
            Vector3 dir = Ctx.Cursor.WorldPosition - _centre;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            if (Ctx.Cursor.Held)
            {
                if (_hasLast)
                {
                    float delta = Mathf.DeltaAngle(_lastAngle, angle);
                    _accumulated += Mathf.Abs(delta) / 360.0;
                }
                _hasLast = true;
            }
            else
            {
                _hasLast = false;
            }
            _lastAngle = angle;

            // Bonus spins beyond the requirement.
            if (_accumulated > _required && Mathf.Abs(Mathf.DeltaAngle(_lastAngle, angle)) > 0)
            {
                // award bonus roughly once per extra rotation
            }
        }

        private void Resolve(double time)
        {
            _resolved = true;
            _resolveTime = time;

            double ratio = _accumulated / _required;
            Judgement result = ratio >= 1.0 ? Judgement.Great
                             : ratio >= 0.75 ? Judgement.Ok
                             : ratio >= 0.5 ? Judgement.Meh
                             : Judgement.Miss;

            Ctx.Score.Apply(result, affectsCombo: true, affectsAccuracy: true);
            Ctx.OnJudgement?.Invoke(result, _centre);
            if (result != Judgement.Miss) Ctx.HitSounds.Play(Object.HitSound, _spinner.EndTime);
        }

        private void AnimateOut(double time)
        {
            float t = Mathf.Clamp01((float)((time - _resolveTime) / 200.0));
            SetGroupAlpha(1f - t);
            if (t >= 1f) Finished = true;
        }

        private void SetGroupAlpha(float a)
        {
            SetAlpha(_disc, a * 0.5f);
            SetAlpha(_ring, a);
            SetAlpha(_spin, a);
            if (_info != null) { Color c = _info.color; c.a = a; _info.color = c; }
        }
    }
}
