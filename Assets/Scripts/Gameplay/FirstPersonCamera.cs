using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>
    /// First-person mouse-look for the cylinder view. The camera sits on the cylinder axis and only
    /// rotates — moving the mouse yaws/pitches the view so the player looks around the inside of the
    /// playfield wall. Yaw/pitch are clamped to the wall's angular extent (plus a small margin) so the
    /// notes always stay reachable. Aiming is done by looking: the cursor rides the screen centre
    /// (see <see cref="CursorController"/> in curved mode), so wherever you look is where you hit.
    /// </summary>
    public sealed class FirstPersonCamera : MonoBehaviour
    {
        /// <summary>Degrees of rotation per unit of mouse movement.</summary>
        public float Sensitivity = 3f;

        /// <summary>Extra degrees of look range past the playfield edge, so edge notes sit comfortably.</summary>
        public float YawMargin = 4f;
        public float PitchMargin = 4f;

        private float _maxYaw = 60f;
        private float _maxPitch = 35f;
        private float _yaw;
        private float _pitch;
        private Quaternion _baseRot = Quaternion.identity;

        /// <summary>
        /// Configure the look limits. <paramref name="baseRotation"/> is the "dead-ahead" orientation
        /// (the playfield's rotation); limits are the wall's half-extents in degrees.
        /// </summary>
        public void Init(Quaternion baseRotation, float halfYawDegrees, float halfPitchDegrees)
        {
            _baseRot = baseRotation;
            _maxYaw = halfYawDegrees + YawMargin;
            _maxPitch = halfPitchDegrees + PitchMargin;
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
            _yaw += Input.GetAxisRaw("Mouse X") * Sensitivity;
            _pitch -= Input.GetAxisRaw("Mouse Y") * Sensitivity; // screen-space: up is negative pitch
            _yaw = Mathf.Clamp(_yaw, -_maxYaw, _maxYaw);
            _pitch = Mathf.Clamp(_pitch, -_maxPitch, _maxPitch);
            Apply();
        }

        // Yaw about the playfield up axis, pitch about local right; position stays on the axis.
        private void Apply() => transform.rotation = _baseRot * Quaternion.Euler(_pitch, _yaw, 0f);
    }
}
