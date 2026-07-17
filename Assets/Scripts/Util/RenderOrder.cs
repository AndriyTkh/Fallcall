using UnityEngine;

// INDEX: The one place the whole draw stack is defined — render queues, sorting orders and canvas orders for every band from the skybox up to the settings overlay.
namespace OsuUnity.Util
{
    /// <summary>
    /// The single definition of Fallcall's draw order. Every renderer that cares about what it sits
    /// above or below reads its band from here — the numbers used to live scattered across seven files
    /// with comments asserting invariants nothing enforced (and one of them was inverted).
    ///
    /// <para><b>How Unity resolves this.</b> Renderers sort by <c>material.renderQueue</c> first; within
    /// one queue by sorting layer, then <c>sortingOrder</c>, then distance. Screen-Space-Overlay canvases
    /// composite after the camera has finished entirely (so no queue/order here can reach them — they
    /// sort among themselves by <c>Canvas.sortingOrder</c>), and IMGUI (<c>OnGUI</c>) paints after that.
    /// That last rule is why the pause menu and settings are canvases: IMGUI cannot be put underneath
    /// them, only hidden (see <c>GameManager.OnGUI</c>).</para>
    ///
    /// <para><b>The stack, bottom to top:</b></para>
    /// <list type="number">
    ///   <item>skybox — camera <c>clearFlags</c>, no queue of its own</item>
    ///   <item>video backdrop — queue <see cref="VideoBackdropQueue"/></item>
    ///   <item>background dim — <see cref="BackgroundDim"/></item>
    ///   <item>follow points — <see cref="FollowPoints"/></item>
    ///   <item>hit objects (slider body/border, circles, numbers, approach circles) — <see cref="HitObject"/></item>
    ///   <item>cursor trail, then cursor — <see cref="CursorTrail"/> / <see cref="Cursor"/></item>
    ///   <item>judgement text — <see cref="Judgement"/></item>
    ///   <item>UI canvases — <see cref="CanvasScreen"/> … <see cref="CanvasSettings"/></item>
    ///   <item>HUD IMGUI — always last; nothing can draw over it, so it hides instead</item>
    /// </list>
    ///
    /// Bands 3–7 all share the transparent queue (3000): the dim and follow-point sprites use
    /// <c>Sprites/Default</c>, the slider tubes <c>Shaders/Resources/SliderBody.shader</c>
    /// (<c>Queue="Transparent"</c>). Only <c>sortingOrder</c> separates them, never distance — the dim
    /// quad is the farthest thing in that queue yet must still draw first, which back-to-front distance
    /// sorting would get right and *any* explicit order on a sibling would silently break.
    /// </summary>
    public static class RenderOrder
    {
        // ------------------------------------------------------------------ render queues

        /// <summary>
        /// Video backdrop: after the skybox + opaque geometry (a Background-queue quad with ZWrite off
        /// gets overwritten by the skybox pass and vanishes), before the transparent gameplay layer.
        /// </summary>
        public const int VideoBackdropQueue = 2501; // RenderQueue.GeometryLast (2500) + 1

        /// <summary>The queue every band from <see cref="BackgroundDim"/> up to <see cref="Judgement"/> shares.</summary>
        public const int TransparentQueue = 3000;

        // ------------------------------------------------------------------ sorting orders (within TransparentQueue)

        /// <summary>Background dim — the floor of the transparent queue: it darkens the video and skybox
        /// behind it and nothing in gameplay.</summary>
        public const int BackgroundDim = -200;

        /// <summary>Follow-point guide arrows — above the dim, below everything clickable.</summary>
        public const int FollowPoints = -100;

        /// <summary>Sorting-order headroom reserved per hit object for its own sub-elements (slider border →
        /// body → tick dots → head → number/follow → approach circle). Objects sit this far apart, and each
        /// spends its slots around its own base (the widest today is the slider's -5…+3), so the stride is
        /// what keeps one object's border from sinking under its neighbour's approach circle.</summary>
        public const int HitObjectStride = 10;

        private static int _hitObjectTop;

        /// <summary>
        /// Reserve the hit-object band for a session of <paramref name="hitObjectCount"/> objects. Must be
        /// called before anything above the band (cursor, judgement text) is built, since the band's height
        /// scales with the map: a 2000-object map alone reaches order 20000.
        /// </summary>
        public static void BeginSession(int hitObjectCount)
            => _hitObjectTop = (Mathf.Max(0, hitObjectCount) + 1) * HitObjectStride;

        /// <summary>
        /// The base order for the object at <paramref name="index"/> of <paramref name="hitObjectCount"/>.
        /// Earlier objects render on top (osu!'s stacking), so the order counts down with the index; the
        /// object's own parts offset themselves from this within <see cref="HitObjectStride"/>.
        /// </summary>
        public static int HitObject(int index, int hitObjectCount)
            => (hitObjectCount - index) * HitObjectStride;

        /// <summary>Cursor trail — just under the cursor, above every hit object.</summary>
        public static int CursorTrail => _hitObjectTop + 100;

        /// <summary>The cursor — never occluded by gameplay.</summary>
        public static int Cursor => _hitObjectTop + 110;

        /// <summary>Floating judgement text — the top of the world-space stack.</summary>
        public static int Judgement => _hitObjectTop + 200;

        // ------------------------------------------------------------------ canvas sorting orders

        /// <summary>Full-screen content screens (song select, map browser) — the base UI layer.</summary>
        public const int CanvasScreen = 0;

        /// <summary>The main menu.</summary>
        public const int CanvasMainScreen = 100;

        /// <summary>The persistent navigation toolbar — over the screen it navigates.</summary>
        public const int CanvasNavBar = 200;

        /// <summary>The pause menu — over gameplay and the toolbar.</summary>
        public const int CanvasPauseMenu = 300;

        /// <summary>The settings overlay — opens from anywhere, so it outranks every other surface.</summary>
        public const int CanvasSettings = 400;
    }
}
