using OsuUnity.Skinning;
using UnityEngine;

// INDEX: Draws the gameplay HUD (score / combo / accuracy fonts + scorebar health) from the osu! skin.
namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Renders the play HUD using the current osu! skin's dedicated elements — the <c>score</c> number
    /// font (digits plus <c>-comma</c>/<c>-dot</c>/<c>-percent</c>/<c>-x</c>), the combo font and the
    /// <c>scorebar-bg</c>/<c>scorebar-colour</c> health bar. Everything is drawn with IMGUI so it lives
    /// alongside the existing <see cref="GameManager"/> HUD. Each <c>Draw*</c> returns <c>false</c> when
    /// the skin lacks the element it needs, letting the caller fall back to plain <see cref="GUI"/> text.
    ///
    /// Layout matches osu!: score top-right, accuracy under it, combo bottom-left, health bar top-left.
    /// See https://osu.ppy.sh/wiki/en/Skinning/osu! — "Score", "Combo", "Health bar".
    /// </summary>
    internal static class HudSkin
    {
        /// <summary>True when the skin ships a score number font (so the skinned HUD is worth drawing).</summary>
        public static bool Available =>
            Skin.Current != null && Skin.Current.HasGlyphs(Skin.Current.Config.ScorePrefix);

        // ------------------------------------------------------------------ numbers

        /// <summary>
        /// Draw <paramref name="text"/> with the skin font <paramref name="prefix"/>, glyphs
        /// <paramref name="height"/> px tall. <paramref name="anchorX"/> is the left edge, or the right
        /// edge when <paramref name="rightAnchor"/>. Returns false (drawing nothing) if the font's '0'
        /// glyph is missing. Non-digit chars map to osu! glyph names (','→comma, '.'→dot, '%'→percent,
        /// 'x'→x) and are silently skipped when that glyph is absent.
        /// </summary>
        public static bool DrawFont(string prefix, string text, float anchorX, float topY, float height,
            float overlapLegacyPx, bool rightAnchor, Color tint)
        {
            var skin = Skin.Current;
            if (skin == null) return false;
            if (!skin.GetHudTexture(prefix + "-0", out var zero, out int zScale)) return false;

            float refH = zero.height / (float)zScale;          // reference glyph height, legacy px
            float k = height / Mathf.Max(1f, refH);            // legacy px -> screen px
            float overlap = overlapLegacyPx * k;

            // Measure total advance width so we can right-anchor.
            float total = 0f;
            int glyphs = 0;
            foreach (char c in text)
            {
                if (!skin.GetHudTexture(prefix + "-" + GlyphSuffix(c), out var t, out int s)) continue;
                total += (t.width / (float)s) * k;
                glyphs++;
            }
            if (glyphs > 0) total -= overlap * (glyphs - 1);

            float x = rightAnchor ? anchorX - total : anchorX;

            Color prev = GUI.color;
            GUI.color = tint;
            foreach (char c in text)
            {
                if (!skin.GetHudTexture(prefix + "-" + GlyphSuffix(c), out var t, out int s)) continue;
                float w = (t.width / (float)s) * k;
                GUI.DrawTexture(new Rect(x, topY, w, height), t, ScaleMode.StretchToFill, true);
                x += w - overlap;
            }
            GUI.color = prev;
            return true;
        }

        private static string GlyphSuffix(char c)
        {
            switch (c)
            {
                case ',': return "comma";
                case '.': return "dot";
                case '%': return "percent";
                case 'x': return "x";
                default: return c.ToString(); // digits
            }
        }

        // ------------------------------------------------------------------ health bar

        /// <summary>
        /// Draw the osu! health bar top-left: <c>scorebar-bg</c> with the <c>scorebar-colour</c> fill
        /// cropped to <paramref name="hp"/> (0..1). Width is scaled to <paramref name="targetWidth"/> px
        /// (aspect preserved). Returns false when <c>scorebar-bg</c> is absent.
        /// </summary>
        public static bool DrawHealthBar(float x, float y, float targetWidth, float hp)
        {
            var skin = Skin.Current;
            if (skin == null) return false;
            if (!skin.GetHudTexture("scorebar-bg", out var bg, out int bgScale)) return false;

            float bgWLegacy = bg.width / (float)bgScale;
            float scale = targetWidth / Mathf.Max(1f, bgWLegacy);   // legacy px -> screen px
            float bgH = (bg.height / (float)bgScale) * scale;
            GUI.DrawTexture(new Rect(x, y, targetWidth, bgH), bg, ScaleMode.StretchToFill, true);

            // The fill sits slightly inside the frame; osu! offsets it a few px. Draw it cropped to HP.
            Texture2D colour = FillTexture(skin, out int colScale);
            if (colour != null)
            {
                const float insetX = 3f, insetY = 3f; // legacy px, approximate frame inset
                float fullW = (colour.width / (float)colScale) * scale;
                float colH = (colour.height / (float)colScale) * scale;
                float w = fullW * Mathf.Clamp01(hp);
                GUI.DrawTextureWithTexCoords(
                    new Rect(x + insetX * scale, y + insetY * scale, w, colH),
                    colour, new Rect(0f, 0f, Mathf.Clamp01(hp), 1f), true);
            }
            return true;
        }

        // scorebar-colour may be un-numbered, animated (frame 0), or spelt the American way.
        private static Texture2D FillTexture(Skin skin, out int scale)
        {
            if (skin.GetHudTexture("scorebar-colour", out var t, out scale)) return t;
            if (skin.GetHudTexture("scorebar-colour-0", out t, out scale)) return t;
            if (skin.GetHudTexture("scorebar-color", out t, out scale)) return t;
            if (skin.GetHudTexture("scorebar-color-0", out t, out scale)) return t;
            scale = 1;
            return null;
        }
    }
}
