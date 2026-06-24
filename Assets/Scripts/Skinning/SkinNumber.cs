using System.Collections.Generic;
using OsuUnity.Visual;
using UnityEngine;

namespace OsuUnity.Skinning
{
    /// <summary>
    /// Draws a run of digits using a skin's number font (e.g. the hit-circle combo number), laying
    /// glyphs out horizontally with the configured overlap. When no skin font is available it falls
    /// back to a single <see cref="TextMesh"/> so combo numbers always render.
    /// </summary>
    public sealed class SkinNumber
    {
        private readonly List<SpriteRenderer> _digits = new List<SpriteRenderer>();
        private TextMesh _text;

        /// <summary>
        /// Build the number under <paramref name="parent"/>, centred, sized so glyphs are
        /// <paramref name="worldHeight"/> tall, at the given sorting order.
        /// </summary>
        public void Build(Transform parent, int number, float worldHeight, int sortingOrder, Color colour)
        {
            var skin = Skin.Current;
            if (skin != null && skin.HasGlyphs(skin.Config.HitCirclePrefix))
                BuildGlyphs(parent, skin, number, worldHeight, sortingOrder, colour);
            else
                BuildText(parent, number, worldHeight, sortingOrder, colour);
        }

        public void SetAlpha(float a)
        {
            foreach (var d in _digits)
            {
                if (d == null) continue;
                Color c = d.color; c.a = a; d.color = c;
            }
            if (_text != null) { Color c = _text.color; c.a = a; _text.color = c; }
        }

        private void BuildGlyphs(Transform parent, Skin skin, int number, float worldHeight,
            int sortingOrder, Color colour)
        {
            string prefix = skin.Config.HitCirclePrefix;
            string s = number.ToString();

            // Reference height (legacy px) keeps every glyph on a common scale factor.
            var refSprite = skin.GetGlyph(prefix + "-0");
            float refH = refSprite != null ? refSprite.rect.height / refSprite.pixelsPerUnit : 1f;
            float k = worldHeight / Mathf.Max(0.0001f, refH);
            float overlap = skin.Config.HitCircleOverlap; // legacy px

            // Measure total advance width (in legacy px) so we can centre the run.
            float totalW = 0f;
            var sprites = new Sprite[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                sprites[i] = skin.GetGlyph(prefix + "-" + s[i]);
                float w = sprites[i] != null ? sprites[i].rect.width / sprites[i].pixelsPerUnit : 0f;
                totalW += w;
                if (i > 0) totalW -= overlap;
            }

            var container = new GameObject("ComboNumber");
            container.transform.SetParent(parent, false);
            container.transform.localPosition = new Vector3(0, 0, -0.001f);
            container.transform.localScale = Vector3.one * k;

            float x = -totalW * 0.5f; // left edge, in legacy px (container scale applies k)
            for (int i = 0; i < s.Length; i++)
            {
                var sprite = sprites[i];
                if (sprite == null) continue;
                float w = sprite.rect.width / sprite.pixelsPerUnit;

                var go = new GameObject("digit" + s[i]);
                go.transform.SetParent(container.transform, false);
                go.transform.localPosition = new Vector3(x + w * 0.5f, 0, 0);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.color = colour;
                sr.sortingOrder = sortingOrder;
                _digits.Add(sr);

                x += w - overlap;
            }
        }

        private void BuildText(Transform parent, int number, float worldHeight, int sortingOrder, Color colour)
        {
            var go = new GameObject("ComboNumber");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, 0, -0.001f);

            _text = go.AddComponent<TextMesh>();
            _text.text = number.ToString();
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.fontSize = 64;
            _text.color = colour;
            _text.font = VisualResources.NumberFont;
            _text.characterSize = worldHeight * 0.055f;

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _text.font.material;
            mr.sortingOrder = sortingOrder;
        }
    }
}
