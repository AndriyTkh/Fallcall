using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Maps osu! playfield coordinates (512x384, origin top-left, y down) into 3D world space.
    ///
    /// Two modes:
    ///  • Flat   — the classic 2D plane on this transform's local XY (orthographic camera).
    ///  • Curved — the playfield is wrapped onto the inner wall of a vertical cylinder whose axis
    ///             runs through this transform. A first-person (perspective) camera sits on that axis
    ///             looking down +Z, so the player stands inside the playfield and looks at the wall.
    ///
    /// The cylinder projection is pure math (trig), no raycasts: an osu x maps to an angle around the
    /// axis, an osu y maps to a height up the wall, and the surface point is placed at the configured
    /// radius. Because every drawable already routes its positions through <see cref="ToWorld"/>, the
    /// whole game curves with no gameplay changes — drawables only additionally rotate their sprites
    /// flat against the wall via <see cref="OrientationAt"/>.
    /// </summary>
    public sealed class Playfield : MonoBehaviour
    {
        public const float Width = 512f;
        public const float Height = 384f;

        /// <summary>World units per osu! pixel (controls circle / vertical scale on the wall).</summary>
        public float PixelScale = 0.01f;

        // ----------------------------------------------------------------- 3D projection

        /// <summary>Wrap the playfield onto a cylinder wall and view it in first person.</summary>
        public bool Curved = false;

        /// <summary>
        /// Cylinder radius in world units = distance from the camera/axis to the projected wall.
        /// This is the "projection distance": larger pushes the wall away (flatter, less curve).
        /// </summary>
        public float ProjectionDistance = 3f;

        /// <summary>
        /// Horizontal stretch: the angle (degrees) the full playfield width (512) is spread across the
        /// cylinder. e.g. 120 wraps the playfield onto a third of the cylinder. 0 = "natural" arc-length
        /// wrapping where on-wall horizontal scale matches <see cref="PixelScale"/> (round circles).
        /// Negative values mirror the playfield horizontally. Increasing the magnitude past natural
        /// spreads the pattern wider around the player.
        /// </summary>
        public float ArcDegrees = 0f;

        /// <summary>True when an explicit arc stretch is set (0 means natural arc-length wrap).</summary>
        private bool ExplicitArc => Mathf.Abs(ArcDegrees) > Mathf.Epsilon;

        /// <summary>Angle (radians) around the axis for the given osu x (0 = dead ahead, +Z).</summary>
        private float AngleAt(float osuX)
        {
            float dx = osuX - Width * 0.5f;
            if (ExplicitArc)
                return (dx / Width) * ArcDegrees * Mathf.Deg2Rad; // explicit stretch (negative = mirrored)
            return dx * PixelScale / ProjectionDistance;           // natural arc-length wrap
        }

        /// <summary>Convert an osu! coordinate to a world position (flat plane or cylinder wall).</summary>
        public Vector3 ToWorld(Vector2 osu, float depth = 0f)
        {
            if (!Curved)
            {
                // Centre the playfield and flip Y (osu y grows downward).
                Vector3 flat = new Vector3(
                    (osu.x - Width * 0.5f) * PixelScale,
                    -(osu.y - Height * 0.5f) * PixelScale,
                    depth);
                return transform.TransformPoint(flat);
            }

            return transform.TransformPoint(LocalPoint(osu, depth));
        }

        /// <summary>The cylinder-wall surface point for an osu coordinate, in this transform's local space.</summary>
        private Vector3 LocalPoint(Vector2 osu, float depth)
        {
            float theta = AngleAt(osu.x);
            float height = -(osu.y - Height * 0.5f) * PixelScale;   // up the wall (flip osu y)
            float r = ProjectionDistance - depth;                   // depth pushes toward the axis
            return new Vector3(r * Mathf.Sin(theta), height, r * Mathf.Cos(theta));
        }

        /// <summary>Convert a world position back into osu! coordinates.</summary>
        public Vector2 ToOsu(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            if (!Curved)
            {
                return new Vector2(
                    local.x / PixelScale + Width * 0.5f,
                    -local.y / PixelScale + Height * 0.5f);
            }

            float theta = Mathf.Atan2(local.x, local.z);            // angle from +Z
            float x;
            if (ExplicitArc)
                x = theta / (ArcDegrees * Mathf.Deg2Rad) * Width + Width * 0.5f;
            else
                x = theta * ProjectionDistance / PixelScale + Width * 0.5f;
            float y = -local.y / PixelScale + Height * 0.5f;
            return new Vector2(x, y);
        }

        /// <summary>
        /// Rotation that makes a sprite face the camera (which sits on the cylinder axis). The sprite's
        /// front points straight at the axis in full 3D — so it billboards toward the camera position
        /// rather than lying tangent to the wall. Because the camera only rotates (never leaves the
        /// axis), this is stable per-object and needs no per-frame update. Identity in flat mode.
        /// </summary>
        public Quaternion OrientationAt(Vector2 osu)
        {
            if (!Curved) return transform.rotation;
            // Direction from the camera (axis origin) out to the surface point; sprite's back faces this.
            Vector3 dirOut = LocalPoint(osu, 0f).normalized;
            return transform.rotation * Quaternion.LookRotation(dirOut, Vector3.up);
        }

        public Quaternion OrientationAt(float osuX) => OrientationAt(new Vector2(osuX, Height * 0.5f));

        /// <summary>Half the horizontal angular extent of the wall (degrees from centre to an edge).</summary>
        public float HalfArcDegrees
        {
            get
            {
                if (!Curved) return 0f;
                float theta = ExplicitArc
                    ? 0.5f * Mathf.Abs(ArcDegrees) * Mathf.Deg2Rad   // magnitude (sign only mirrors)
                    : (Width * 0.5f) * PixelScale / ProjectionDistance;
                return theta * Mathf.Rad2Deg;
            }
        }

        /// <summary>Half the vertical angular extent of the wall (degrees from centre to top/bottom).</summary>
        public float HalfPitchDegrees =>
            Curved ? Mathf.Atan((Height * 0.5f * PixelScale) / ProjectionDistance) * Mathf.Rad2Deg : 0f;

        /// <summary>Plane the (flat) playfield lies on, in world space (for cursor raycasting).</summary>
        public Plane WorldPlane => new Plane(-transform.forward, transform.position);

        public float OsuToWorldDistance(double osuPixels) => (float)osuPixels * PixelScale;
    }
}
