using System.Collections.Generic;
using OsuUnity.Gameplay;
using OsuUnity.Skinning;
using UnityEngine;

namespace OsuUnity.Visual
{
    /// <summary>A short-lived judgement result (skin animation or 3D text) that pops and fades upward.</summary>
    public sealed class FloatingText : MonoBehaviour
    {
        private TextMesh _text;
        private SpriteRenderer _sprite;
        private List<Sprite> _frames;
        private float _frameDuration;
        private float _age;
        private float _life = 0.6f;
        private Vector3 _drift;

        public static void Spawn(Judgement j, Vector3 worldPos, float size, int order, Camera cam)
        {
            var go = new GameObject("Judgement");
            go.transform.position = worldPos;
            var ft = go.AddComponent<FloatingText>();
            ft.Setup(j, size, order, cam);
        }

        private void Setup(Judgement j, float size, int order, Camera cam)
        {
            _drift = Vector3.up * size * 8f;
            if (cam != null) transform.rotation = cam.transform.rotation;

            List<Sprite> frames = SkinSprites.HitResultFrames(ResultSpriteName(j));
            if (frames.Count > 0)
            {
                _frames = frames;
                Sprite first = frames[0];
                _sprite = gameObject.AddComponent<SpriteRenderer>();
                _sprite.sprite = first;
                _sprite.sortingOrder = order;
                // Scale uniformly to a target height (preserving the sprite's aspect ratio).
                float legacyHeight = first.rect.height / first.pixelsPerUnit;
                transform.localScale = Vector3.one * (size * 23f / Mathf.Max(0.0001f, legacyHeight));

                // Play the frames once at the skin's rate (default 60 fps), holding the last frame
                // while the result fades. Stretch life so a long animation isn't cut short.
                float fps = Skin.Current != null && Skin.Current.Config.AnimationFramerate > 0f
                    ? Skin.Current.Config.AnimationFramerate : 60f;
                _frameDuration = 1f / fps;
                if (frames.Count > 1)
                    _life = Mathf.Max(_life, frames.Count * _frameDuration);
                return;
            }

            _text = gameObject.AddComponent<TextMesh>();
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.fontSize = 48;
            _text.font = VisualResources.NumberFont;
            _text.characterSize = size;
            _text.GetComponent<MeshRenderer>().sharedMaterial = _text.font.material;
            _text.GetComponent<MeshRenderer>().sortingOrder = order;

            switch (j)
            {
                case Judgement.Great: _text.text = "300"; _text.color = new Color(0.4f, 0.8f, 1f); break;
                case Judgement.Ok: _text.text = "100"; _text.color = new Color(0.5f, 1f, 0.5f); break;
                case Judgement.Meh: _text.text = "50"; _text.color = new Color(1f, 0.9f, 0.4f); break;
                default: _text.text = "X"; _text.color = new Color(1f, 0.3f, 0.3f); break;
            }
        }

        private static string ResultSpriteName(Judgement j)
        {
            switch (j)
            {
                case Judgement.Great: return "hit300";
                case Judgement.Ok: return "hit100";
                case Judgement.Meh: return "hit50";
                default: return "hit0";
            }
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = _age / _life;
            transform.position += _drift * Time.deltaTime;

            // Advance the result animation (plays once, holds the final frame).
            if (_frames != null && _frames.Count > 1 && _sprite != null)
            {
                int idx = Mathf.Min(_frames.Count - 1, (int)(_age / _frameDuration));
                _sprite.sprite = _frames[idx];
            }

            float a = Mathf.Clamp01(1f - t);
            if (_text != null) { Color c = _text.color; c.a = a; _text.color = c; }
            if (_sprite != null) { Color c = _sprite.color; c.a = a; _sprite.color = c; }
            if (_age >= _life) Destroy(gameObject);
        }
    }
}
