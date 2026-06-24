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
        [Tooltip("Wrap the playfield onto a cylinder and view it in first person. Off = classic 2D plane.")]
        public bool Curved = true;

        [Tooltip("World units per osu! pixel. Controls overall circle / playfield scale on the wall.")]
        [Range(0.002f, 0.05f)]
        public float PixelScale = 0.01f;

        [Tooltip("Cylinder radius = distance from the player to the wall. Smaller = more wrap-around curve.")]
        [Range(0.5f, 12f)]
        public float ProjectionDistance = 3f;

        [Tooltip("Horizontal stretch: degrees of cylinder the full playfield width spans. " +
                 "0 = natural round wrap; e.g. 120 spreads the playfield across a third of the cylinder. " +
                 "Negative values mirror the playfield horizontally.")]
        [Range(-300f, 300f)]
        public float ArcDegrees = 0f;

        [Tooltip("First-person mouse-look speed (degrees per unit of mouse movement).")]
        [Range(0.5f, 10f)]
        public float LookSensitivity = 3f;

        /// <summary>Find the active settings in the scene, or null to use defaults.</summary>
        public static Osu3DSettings Find() => FindObjectOfType<Osu3DSettings>();

        /// <summary>Copy these values onto the playfield.</summary>
        public void ApplyTo(Playfield pf)
        {
            pf.PixelScale = PixelScale;
            pf.Curved = Curved;
            pf.ProjectionDistance = ProjectionDistance;
            pf.ArcDegrees = ArcDegrees;
        }
    }
}
