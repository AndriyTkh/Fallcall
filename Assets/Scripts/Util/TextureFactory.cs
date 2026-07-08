using UnityEngine;

namespace OsuUnity.Util
{
    /// <summary>
    /// Generates the simple circle / ring sprites used by the gameplay so the project needs no
    /// imported art assets. Sprites are created at 1 world unit diameter (pixelsPerUnit == size).
    /// </summary>
    public static class TextureFactory
    {
        private static Sprite _disc;
        private static Sprite _ring;
        private static Sprite _softRing;
        private static Sprite _arrow;

        /// <summary>Solid filled circle with a soft antialiased edge.</summary>
        public static Sprite Disc => _disc != null ? _disc : (_disc = BuildDisc(256));

        /// <summary>Thin hollow ring (used for the hit circle border and approach circle).</summary>
        public static Sprite Ring => _ring != null ? _ring : (_ring = BuildRing(256, 0.80f, 0.97f));

        /// <summary>Wider ring used for slider follow circle visuals.</summary>
        public static Sprite SoftRing => _softRing != null ? _softRing : (_softRing = BuildRing(256, 0.62f, 0.97f));

        /// <summary>Right-pointing chevron arrow used as the follow-point fallback (skin "followpoint").</summary>
        public static Sprite Arrow => _arrow != null ? _arrow : (_arrow = BuildArrow(128));

        private static Sprite BuildDisc(int size)
        {
            var tex = NewTexture(size);
            float r = size * 0.5f;
            float edge = 1.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r;
                float dy = y + 0.5f - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((r - d) / edge);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            return ToSprite(tex);
        }

        private static Sprite BuildRing(int size, float innerFrac, float outerFrac)
        {
            var tex = NewTexture(size);
            float r = size * 0.5f;
            float inner = r * innerFrac;
            float outer = r * outerFrac;
            float edge = 1.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r;
                float dy = y + 0.5f - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float aOuter = Mathf.Clamp01((outer - d) / edge);
                float aInner = Mathf.Clamp01((d - inner) / edge);
                float a = Mathf.Min(aOuter, aInner);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            return ToSprite(tex);
        }

        /// <summary>A ">" chevron whose tip points along +X (so callers roll it toward the next object).</summary>
        private static Sprite BuildArrow(int size)
        {
            var tex = NewTexture(size);
            // Normalised space centred on the texture, +x right, +y up (symmetric about y).
            Vector2 tip = new Vector2(0.34f, 0f);
            Vector2 armTop = new Vector2(-0.26f, 0.40f);
            Vector2 armBot = new Vector2(-0.26f, -0.40f);
            const float thick = 0.13f;
            const float edge = 0.02f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f);
                float d = Mathf.Min(DistToSegment(p, tip, armTop), DistToSegment(p, tip, armBot));
                float a = Mathf.Clamp01((thick - d) / edge);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            return ToSprite(tex);
        }

        private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab));
            return Vector2.Distance(p, a + t * ab);
        }

        private static Texture2D NewTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            return tex;
        }

        private static Sprite ToSprite(Texture2D tex)
        {
            // pixelsPerUnit == texture size -> sprite spans exactly 1 world unit.
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), tex.width);
        }
    }
}
