using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Optional scene-level tuning for the 3D cylinder view. Drop this component on any GameObject in
    /// the scene and adjust it in the Inspector — <see cref="GameManager"/> reads it when it builds the
    /// playfield. If no instance exists, the built-in defaults below are used.
    ///
    /// Values are applied when a play session starts, so tweak then press <b>R</b> (restart) to see the
    /// change without leaving play mode.
    /// </summary>
    public sealed class Osu3DSettings : MonoBehaviour
    {
        [Tooltip("View mode a session opens in. [Tab] still cycles Sphere→Ortho2D→Falling live in-game; " +
                 "this just picks the starting one. Overrides Curved for mode selection.")]
        public ViewMode StartMode = ViewMode.Sphere;

        [Tooltip("Wrap the playfield onto a sphere chunk and view it in first person. Off = classic 2D plane.")]
        public bool Curved = true;

        [Tooltip("World units per osu! pixel. Controls overall circle / playfield scale in flat mode.")]
        [Range(0.002f, 0.05f)]
        public float PixelScale = 0.0135f;

        [Tooltip("Sphere radius = distance from the player to the projection surface. Larger = bigger " +
                 "warped screen (chunk angular size is unchanged; you move the camera to see it all).")]
        [Range(0.5f, 12f)]
        public float ProjectionDistance = 3.5f;

        [Tooltip("Horizontal chunk span: degrees of the sphere the full playfield width wraps across " +
                 "(≈120°). Negative values mirror the playfield horizontally.")]
        [Range(-300f, 300f)]
        public float ChunkHDegrees = 120f;

        [Tooltip("Vertical chunk span: degrees of the sphere the full playfield height wraps across " +
                 "(≈90°). 120×90 keeps roughly round circles.")]
        [Range(20f, 180f)]
        public float ChunkVDegrees = 90f;

        [Tooltip("First-person mouse-look speed (degrees per unit of mouse movement).")]
        [Range(0.5f, 10f)]
        public float LookSensitivity = 1.4f;

        [Header("Follow points (guide arrows between consecutive in-combo objects)")]
        [Tooltip("Draw the fading arrow line pointing from each object to the next (applied on restart).")]
        public bool ShowFollowPoints = true;

        [Tooltip("Size of each guide arrow relative to the circle radius (applied live).")]
        [Range(0.3f, 3f)]
        public float FollowPointScale = 1f;

        [Header("2D-ortho dynamic zoom (Ortho2D view mode — press Tab)")]
        [Tooltip("Pan+zoom the orthographic camera to frame the upcoming click group (follow streams, " +
                 "zoom into spinners). Off = static full-field framing.")]
        public bool OrthoZoom = true;

        [Tooltip("Lead-in before the first click group's first note (ms). Later groups reveal as soon as the " +
                 "previous group's last note is hit, using the whole pause to reframe (applied live).")]
        [Range(0f, 1500f)]
        public float OrthoZoomLeadMs = 350f;

        [Tooltip("Pan+zoom smoothing time in seconds (SmoothDamp). Higher = lazier camera (applied live).")]
        [Range(0.02f, 1f)]
        public float OrthoZoomSmooth = 0.22f;

        [Tooltip("Padding kept around a framed group, in circle radii (applied live).")]
        [Range(0f, 6f)]
        public float OrthoZoomMargin = 1.6f;

        [Tooltip("Grouping aggressiveness. 0 = calm, big groups (Target/Max counts below). 1 = hyperactive: " +
                 "the camera cuts to every native click-group in the map. Scales the counts toward tiny " +
                 "(applied on restart).")]
        [Range(0f, 1f)]
        public float OrthoAggressiveness = 0.3f;

        [Tooltip("Notes per group at aggressiveness 0. Aggressiveness shrinks this toward 1. Groups float " +
                 "around this size, bigger on denser/harder maps (applied on restart).")]
        [Range(4, 40)]
        public int OrthoGroupTargetCount = 16;

        [Tooltip("Hard cap on a group's note count at aggressiveness 0 — forces a cut even with no pause " +
                 "(e.g. mid-stream). Aggressiveness shrinks this toward 4 (applied on restart).")]
        [Range(6, 60)]
        public int OrthoGroupMaxCount = 28;

        [Tooltip("Once a group is at its target size, the first pause (ms) at least this long ends it — the " +
                 "camera reframes during that pause, giving sightread time (applied on restart).")]
        [Range(40f, 600f)]
        public float OrthoGroupBreakGapMs = 160f;

        [Tooltip("A pause (ms) longer than this always ends a group, whatever its size — a section break " +
                 "(applied on restart).")]
        [Range(300f, 3000f)]
        public float OrthoGroupGapMs = 900f;

        [Tooltip("Max object-to-object time gap (ms) still counted as a stream (applied on restart).")]
        [Range(40f, 400f)]
        public float OrthoStreamGapMs = 130f;

        [Tooltip("Max object-to-object spacing (osu! px) still counted as a stream (applied on restart).")]
        [Range(20f, 400f)]
        public float OrthoStreamSpacingOsu = 130f;

        [Tooltip("Kiai (hyper) time: multiply the pan+zoom smoothing time — <1 = snappier camera, shorter " +
                 "transition cooldown, going all-out (applied live).")]
        [Range(0.1f, 1f)]
        public float OrthoKiaiSmoothMul = 0.5f;

        [Tooltip("Kiai (hyper) time: multiply the framed ortho size — <1 = tighter, punchier frame " +
                 "(applied live).")]
        [Range(0.5f, 1f)]
        public float OrthoKiaiZoomMul = 0.82f;

        [Header("Falling view mode (perspective camera above the flat plane — press Tab)")]
        [Tooltip("Camera sphere radius above the plane, in world units. Larger = higher / more overhead.")]
        [Range(2f, 20f)]
        public float FallingRadius = 7f;

        [Tooltip("Max camera tilt toward the mouse at the screen edge (degrees). 0 = straight down.")]
        [Range(0f, 60f)]
        public float FallingMaxTiltDeg = 18f;

        [Tooltip("Framing: the flat plane fills this fraction of the view height (~0.9 = 90%).")]
        [Range(0.4f, 1.5f)]
        public float FallingZoom = 0.9f;

        [Tooltip("Handheld-camera smoothing time in seconds (SmoothDamp).")]
        [Range(0.02f, 1f)]
        public float FallingSmooth = 0.15f;

        /// <summary>Find the active settings in the scene, or null to use defaults.</summary>
        public static Osu3DSettings Find() => FindObjectOfType<Osu3DSettings>();

        /// <summary>Copy these values onto the playfield.</summary>
        public void ApplyTo(Playfield pf)
        {
            pf.PixelScale = PixelScale;
            pf.Curved = Curved;
            pf.ProjectionDistance = ProjectionDistance;
            pf.ChunkHDegrees = ChunkHDegrees;
            pf.ChunkVDegrees = ChunkVDegrees;
        }
    }
}
