using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Maps osu! playfield coordinates (512x384, origin top-left, y down) onto a plane in 3D world
    /// space. Because everything hangs off this transform, the whole playfield can later be rotated
    /// or otherwise projected for "3D osu" experiments without touching gameplay logic.
    /// </summary>
    public sealed class Playfield : MonoBehaviour
    {
        public const float Width = 512f;
        public const float Height = 384f;

        /// <summary>World units per osu! pixel.</summary>
        public float PixelScale = 0.01f;

        /// <summary>Convert an osu! coordinate to a world position on the playfield plane.</summary>
        public Vector3 ToWorld(Vector2 osu, float depth = 0f)
        {
            // Centre the playfield and flip Y (osu y grows downward).
            Vector3 local = new Vector3(
                (osu.x - Width * 0.5f) * PixelScale,
                -(osu.y - Height * 0.5f) * PixelScale,
                depth);
            return transform.TransformPoint(local);
        }

        /// <summary>Convert a world position back into osu! coordinates.</summary>
        public Vector2 ToOsu(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            return new Vector2(
                local.x / PixelScale + Width * 0.5f,
                -local.y / PixelScale + Height * 0.5f);
        }

        /// <summary>Plane the playfield lies on, in world space (for cursor raycasting).</summary>
        public Plane WorldPlane => new Plane(-transform.forward, transform.position);

        public float OsuToWorldDistance(double osuPixels) => (float)osuPixels * PixelScale;
    }
}
