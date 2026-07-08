using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Maps osu! playfield coordinates (512x384, origin top-left, y down) into 3D world space.
    ///
    /// Two modes:
    ///  • Flat   — the classic 2D plane on this transform's local XY (orthographic camera).
    ///  • Curved — the playfield is wrapped onto a <b>sphere chunk</b> centred on this transform. A
    ///             first-person (perspective) camera sits at the sphere centre and looks out at the
    ///             chunk, so the player stands inside the playfield and looks around it.
    ///
    /// The sphere projection is pure math (trig), no raycasts: an osu x maps to a yaw angle around the
    /// vertical axis, an osu y maps to a pitch angle, and the surface point is placed at the configured
    /// radius (<see cref="ProjectionDistance"/>). The playfield spans <see cref="ChunkHDegrees"/> ×
    /// <see cref="ChunkVDegrees"/> of the sphere (≈120°×90° by default — see STRUCTURE §2/§3a). Because
    /// every drawable already routes its positions through <see cref="ToWorld"/>, the whole game curves
    /// with no gameplay changes — drawables only additionally rotate their sprites to face the camera
    /// via <see cref="OrientationAt"/>.
    ///
    /// "Radius as a parameter": a larger radius pushes the projection surface out (a bigger warped
    /// screen) but does NOT change the chunk's angular size — the playfield always covers the same
    /// ChunkH×ChunkV degrees, so at large radius seeing the whole chunk means moving the camera.
    /// </summary>
    public sealed class Playfield : MonoBehaviour
    {
        public const float Width = 512f;
        public const float Height = 384f;

        /// <summary>World units per osu! pixel (controls circle size in flat mode / on-surface scale).</summary>
        public float PixelScale = 0.01f;

        // ----------------------------------------------------------------- 3D projection

        /// <summary>Wrap the playfield onto a sphere chunk and view it in first person.</summary>
        public bool Curved = false;

        /// <summary>
        /// Sphere radius in world units = distance from the camera/centre to the projected surface.
        /// This is the "projection distance": larger pushes the surface away (a bigger warped screen)
        /// without changing the chunk's angular size.
        /// </summary>
        public float ProjectionDistance = 3f;

        /// <summary>
        /// Horizontal chunk span: degrees of the sphere the full playfield width (512) is wrapped across.
        /// ≈120° by default. Negative values mirror the playfield horizontally.
        /// </summary>
        public float ChunkHDegrees = 120f;

        /// <summary>
        /// Vertical chunk span: degrees of the sphere the full playfield height (384) is wrapped across.
        /// ≈90° by default. (120×90 keeps roughly round circles, matching osu!'s 4:3 field.)
        /// </summary>
        public float ChunkVDegrees = 90f;

        /// <summary>A chunk span can't be zero (degenerate projection); clamp to a small floor.</summary>
        private float SafeH => Mathf.Abs(ChunkHDegrees) < 1f ? Mathf.Sign(ChunkHDegrees == 0f ? 1f : ChunkHDegrees) : ChunkHDegrees;
        private float SafeV => Mathf.Abs(ChunkVDegrees) < 1f ? 1f : ChunkVDegrees;

        /// <summary>Convert an osu! coordinate to a world position (flat plane or sphere surface).</summary>
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

        /// <summary>The sphere-surface point for an osu coordinate, in this transform's local space.</summary>
        private Vector3 LocalPoint(Vector2 osu, float depth)
        {
            // yaw about the up axis (+ = right), pitch (+ = up, so flip osu y). Equirectangular wrap.
            float yaw = (osu.x - Width * 0.5f) / Width * SafeH * Mathf.Deg2Rad;
            float pitch = -(osu.y - Height * 0.5f) / Height * SafeV * Mathf.Deg2Rad;
            float r = ProjectionDistance - depth;                     // depth pushes toward the centre
            float cp = Mathf.Cos(pitch);
            return new Vector3(r * cp * Mathf.Sin(yaw), r * Mathf.Sin(pitch), r * cp * Mathf.Cos(yaw));
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

            Vector3 dir = local.normalized;
            float yaw = Mathf.Atan2(dir.x, dir.z);                    // angle from +Z about up
            float pitch = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f));
            float x = yaw / (SafeH * Mathf.Deg2Rad) * Width + Width * 0.5f;
            float y = -pitch / (SafeV * Mathf.Deg2Rad) * Height + Height * 0.5f;
            return new Vector2(x, y);
        }

        /// <summary>
        /// Rotation that makes a sprite face the camera (which sits at the sphere centre). The sprite's
        /// front points straight at the centre in full 3D — so it billboards toward the camera position
        /// (full camera-align). Because the camera only rotates (never leaves the centre), this is stable
        /// per-object and needs no per-frame update. Identity in flat mode.
        /// </summary>
        public Quaternion OrientationAt(Vector2 osu)
        {
            if (!Curved) return transform.rotation;
            // Direction from the camera (centre) out to the surface point; sprite's back faces this.
            Vector3 dirOut = LocalPoint(osu, 0f).normalized;
            return transform.rotation * Quaternion.LookRotation(dirOut, Vector3.up);
        }

        public Quaternion OrientationAt(float osuX) => OrientationAt(new Vector2(osuX, Height * 0.5f));

        /// <summary>
        /// Intersect a ray with the projection sphere and return the outer surface point (the one in
        /// front of a camera at the centre). Pure quadratic — no Physics raycast. Used by the cursor to
        /// find where the player is looking. Assumes <paramref name="ray"/>.direction is normalised.
        /// </summary>
        public bool RaycastSurface(Ray ray, out Vector3 hit)
        {
            hit = default;
            Vector3 o = ray.origin - transform.position;
            float R = ProjectionDistance;
            float b = Vector3.Dot(o, ray.direction);
            float c = Vector3.Dot(o, o) - R * R;
            float disc = b * b - c;
            if (disc < 0f) return false;
            float t = -b + Mathf.Sqrt(disc);                          // far root: surface in front of centre
            if (t <= 0f) return false;
            hit = ray.origin + t * ray.direction;
            return true;
        }

        /// <summary>Half the horizontal angular extent of the chunk (degrees from centre to an edge).</summary>
        public float HalfArcDegrees => Curved ? 0.5f * Mathf.Abs(ChunkHDegrees) : 0f;

        /// <summary>Half the vertical angular extent of the chunk (degrees from centre to top/bottom).</summary>
        public float HalfPitchDegrees => Curved ? 0.5f * Mathf.Abs(ChunkVDegrees) : 0f;

        /// <summary>Plane the (flat) playfield lies on, in world space (for cursor raycasting).</summary>
        public Plane WorldPlane => new Plane(-transform.forward, transform.position);

        public float OsuToWorldDistance(double osuPixels) => (float)osuPixels * PixelScale;
    }
}
