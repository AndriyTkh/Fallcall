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

        /// <summary>Solid filled circle with a soft antialiased edge.</summary>
        public static Sprite Disc => _disc != null ? _disc : (_disc = BuildDisc(256));

        /// <summary>Thin hollow ring (used for the hit circle border and approach circle).</summary>
        public static Sprite Ring => _ring != null ? _ring : (_ring = BuildRing(256, 0.80f, 0.97f));

        /// <summary>Wider ring used for slider follow circle visuals.</summary>
        public static Sprite SoftRing => _softRing != null ? _softRing : (_softRing = BuildRing(256, 0.62f, 0.97f));

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
