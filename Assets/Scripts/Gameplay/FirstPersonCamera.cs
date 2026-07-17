using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>
    /// First-person mouse-look for the sphere-chunk view. The camera sits at the sphere centre and only
    /// rotates — moving the mouse yaws/pitches the view so the player looks around the playfield chunk.
    /// Baseline (STRUCTURE §2): <b>free horizontal look</b> and a <b>vertical clamp of ±90°</b>, so you
    /// can face any direction around the sphere but never flip over the poles. Aiming is done by looking:
    /// the cursor rides the screen centre (see <see cref="CursorController"/> in curved mode), so wherever
    /// you look is where you hit.
    ///
    /// When <see cref="FreeHorizontal"/> is off, yaw is instead clamped to the chunk's horizontal extent
    /// (plus a margin) from <see cref="Init"/>, keeping the whole playfield reachable without free-look.
    /// </summary>
    public sealed class FirstPersonCamera : MonoBehaviour
    {
        /// <summary>Degrees of rotation per unit of mouse movement.</summary>
        public float Sensitivity = 3f;

        /// <summary>Autoplay: ignore the mouse and let <see cref="ViewModeController.AimAt"/> drive the
        /// camera rotation toward the notes instead.</summary>
        public bool Auto;

        /// <summary>
        /// Free horizontal look (STRUCTURE §2 baseline): yaw is unbounded so the player can turn all the
        /// way around the sphere. Off = yaw clamps to the chunk's horizontal half-extent (+margin).
        /// </summary>
        public bool FreeHorizontal = true;

        /// <summary>Hard vertical ceiling in degrees (STRUCTURE §2: clamp to ±90°, never past the poles).</summary>
        public float MaxPitch = 90f;

        /// <summary>Extra degrees of look range past the playfield edge, so edge notes sit comfortably
        /// (only used when <see cref="FreeHorizontal"/> is off).</summary>
        public float YawMargin = 4f;

        private float _maxYaw = 60f;
        private float _yaw;
        private float _pitch;
        private Quaternion _baseRot = Quaternion.identity;

        /// <summary>
        /// Configure the look limits. <paramref name="baseRotation"/> is the "dead-ahead" orientation
        /// (the playfield's rotation); <paramref name="halfYawDegrees"/> is the chunk's horizontal
        /// half-extent (used only when <see cref="FreeHorizontal"/> is off). <paramref name="halfPitchDegrees"/>
        /// is kept for API compatibility; the vertical clamp is the spec's ±<see cref="MaxPitch"/>.
        /// </summary>
        public void Init(Quaternion baseRotation, float halfYawDegrees, float halfPitchDegrees)
        {
            _baseRot = baseRotation;
            _maxYaw = halfYawDegrees + YawMargin;
            _yaw = 0f;
            _pitch = 0f;
            Apply();
        }

        private void OnEnable()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            if (Auto) return;   // autoplay drives rotation via ViewModeController.AimAt

            _yaw += Input.GetAxisRaw("Mouse X") * Sensitivity;
            _pitch -= Input.GetAxisRaw("Mouse Y") * Sensitivity; // screen-space: up is negative pitch

            if (FreeHorizontal)
                _yaw = Mathf.Repeat(_yaw + 180f, 360f) - 180f;    // wrap to (-180,180]; no clamp
            else
                _yaw = Mathf.Clamp(_yaw, -_maxYaw, _maxYaw);

            _pitch = Mathf.Clamp(_pitch, -MaxPitch, MaxPitch);
            Apply();
        }

        // Yaw about the playfield up axis, pitch about local right; position stays at the centre.
        private void Apply() => transform.rotation = _baseRot * Quaternion.Euler(_pitch, _yaw, 0f);
    }
}
