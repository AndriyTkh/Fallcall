using System.Collections.Generic;
using OsuUnity.Util;
using UnityEngine;

namespace OsuUnity.Skinning
{
    /// <summary>
    /// Resolves a gameplay element to the current skin's sprite, falling back to the procedural
    /// <see cref="TextureFactory"/> art when no skin is active or the element is missing. Every sprite
    /// returned spans one world unit, so callers keep scaling by the desired diameter.
    /// </summary>
    public static class SkinSprites
    {
        public static Sprite HitCircle => Unit(TextureFactory.Disc, "hitcircle");
        public static Sprite HitCircleOverlay => Unit(TextureFactory.Ring, "hitcircleoverlay");
        public static Sprite ApproachCircle => Unit(TextureFactory.Ring, "approachcircle");
        public static Sprite SliderFollow => Unit(TextureFactory.SoftRing, "sliderfollowcircle");
        public static Sprite SliderBall => Unit(TextureFactory.Disc, "sliderb0", "sliderb");
        public static Sprite Cursor => Unit(TextureFactory.Disc, "cursor");

        // Elements with no procedural equivalent: null means "skin absent, draw nothing extra".
        public static Sprite ReverseArrow => SkinOnly("reversearrow");
        public static Sprite SliderScorePoint => SkinOnly("sliderscorepoint");
        public static Sprite CursorTrail => SkinOnly("cursortrail");
        public static Sprite SpinnerCircle => SkinOnly("spinner-circle");
        public static Sprite SpinnerApproach => SkinOnly("spinner-approachcircle");
        public static Sprite SpinnerBackground => SkinGlyph("spinner-background");
        public static Sprite SpinnerClear => SkinGlyph("spinner-clear");

        /// <summary>
        /// Animated hit-result frames for a judgement (hit300/hit100/hit50/hit0). Aspect-preserving
        /// (these elements are wide), so callers scale uniformly by height. Empty list to fall back.
        /// </summary>
        public static List<Sprite> HitResultFrames(string name) =>
            Skin.Current != null ? Skin.Current.GetFrames(name, glyph: true) : new List<Sprite>();

        private static Sprite Unit(Sprite fallback, params string[] names)
        {
            var s = Skin.Current != null ? Skin.Current.GetUnit(names) : null;
            return s != null ? s : fallback;
        }

        /// <summary>One-world-unit skin sprite, or null when no skin / element is present.</summary>
        private static Sprite SkinOnly(string name) => Skin.Current?.GetUnit(name);

        /// <summary>Aspect-preserving (legacy-px sized) skin sprite, or null when absent.</summary>
        private static Sprite SkinGlyph(string name) => Skin.Current?.GetGlyph(name);
    }
}
