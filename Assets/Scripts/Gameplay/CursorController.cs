using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Projects the mouse onto the playfield plane and tracks osu!-style input. Two keys (Z / X) plus
    /// the mouse buttons act as "tap" inputs; a press this frame counts as a fresh hit attempt.
    /// </summary>
    public sealed class CursorController : MonoBehaviour
    {
        public Playfield Playfield;
        public Camera Camera;

        public Vector3 WorldPosition { get; private set; }
        public Vector2 OsuPosition { get; private set; }

        /// <summary>True on the frame a tap key/button went down.</summary>
        public bool PressedThisFrame { get; private set; }

        /// <summary>True while any tap key/button is held.</summary>
        public bool Held { get; private set; }

        private SpriteRenderer _sprite;
        private float _baseScale;

        public void Init(Playfield playfield, Camera cam, float worldDiameter)
        {
            Playfield = playfield;
            Camera = cam;

            _sprite = gameObject.AddComponent<SpriteRenderer>();
            _sprite.sprite = Util.TextureFactory.Disc;
            _sprite.color = new Color(1f, 0.55f, 0.7f, 0.95f);
            _sprite.sortingOrder = 10000;
            _baseScale = worldDiameter;
            transform.localScale = Vector3.one * _baseScale;
        }

        private void Update()
        {
            if (Camera == null || Playfield == null) return;

            // Raycast the mouse onto the playfield plane.
            Ray ray = Camera.ScreenPointToRay(Input.mousePosition);
            Plane plane = Playfield.WorldPlane;
            if (plane.Raycast(ray, out float enter))
            {
                WorldPosition = ray.GetPoint(enter);
                OsuPosition = Playfield.ToOsu(WorldPosition);
                transform.position = WorldPosition + (-Camera.transform.forward) * 0.01f;
            }

            bool z = Input.GetKey(KeyCode.Z);
            bool x = Input.GetKey(KeyCode.X);
            bool m0 = Input.GetMouseButton(0);
            bool m1 = Input.GetMouseButton(1);
            Held = z || x || m0 || m1;

            PressedThisFrame =
                Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.X) ||
                Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);

            // Small pulse on press for feedback.
            float target = PressedThisFrame || Held ? _baseScale * 0.85f : _baseScale;
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * target, 0.4f);
        }
    }
}
