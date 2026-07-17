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
        public static Sprite CursorTrail => Unit(TextureFactory.Disc, "cursortrail");

        /// <summary>
        /// Frames of the guide arrow ("followpoint"), which osu! animates along each connection. Unit-sized
        /// (every frame spans exactly one world unit wide, native aspect preserved), so the on-screen width is
        /// identical across skins no matter the source art's pixel dimensions — the caller just scales by the
        /// desired size. Falls back to the single procedural arrow when the skin has no followpoint element.
        /// A skin's frame 0 is often a blank fade-in frame, so callers must cycle rather than draw one frame.
        /// Pass <paramref name="forceDefault"/> to skip the skin and always return the built-in arrow.
        /// </summary>
        public static List<Sprite> FollowPointFrames(bool forceDefault = false)
        {
            var frames = !forceDefault && Skin.Current != null ? Skin.Current.GetFrames("followpoint", glyph: false) : null;
            return frames != null && frames.Count > 0 ? frames : new List<Sprite> { TextureFactory.Arrow };
        }

        // Elements with no procedural equivalent: null means "skin absent, draw nothing extra".
        public static Sprite ReverseArrow => SkinOnly("reversearrow");
        public static Sprite SliderScorePoint => SkinOnly("sliderscorepoint");
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
