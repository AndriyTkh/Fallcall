using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Projects the mouse onto the playfield plane and tracks osu!-style input. Three independent keys
    /// (A / S / D) plus the mouse buttons act as "tap" inputs; a press on any of them this frame counts
    /// as a fresh hit attempt. The keys don't combine — each one alone is a full tap.
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
        private bool _expand = true;

        // Cursor trail (skin only): a recycled pool of fading segments dropped along the path.
        private const int TrailCount = 24;
        private const float TrailLife = 0.18f;
        private SpriteRenderer[] _trail;
        private float[] _trailAge;
        private int _trailHead;
        private float _trailSpacing;
        private Vector3 _lastTrailPos;

        public void Init(Playfield playfield, Camera cam, float worldDiameter)
        {
            Playfield = playfield;
            Camera = cam;

            _sprite = gameObject.AddComponent<SpriteRenderer>();
            var skinCursor = Skinning.SkinSprites.Cursor;
            bool skinned = Skinning.Skin.Current != null && skinCursor != Util.TextureFactory.Disc;
            _sprite.sprite = skinCursor;
            // Skin cursors carry their own colour; only tint the procedural fallback disc.
            _sprite.color = skinned ? Color.white : new Color(1f, 0.55f, 0.7f, 0.95f);
            _sprite.sortingOrder = 10000;
            _expand = Skinning.Skin.Current?.Config.CursorExpand ?? true;
            _baseScale = worldDiameter;
            transform.localScale = Vector3.one * _baseScale;

            InitTrail();
        }

        private void InitTrail()
        {
            var trailSprite = Skinning.SkinSprites.CursorTrail;
            if (trailSprite == null) return; // no skin trail -> keep the bare cursor

            _trail = new SpriteRenderer[TrailCount];
            _trailAge = new float[TrailCount];
            _trailSpacing = _baseScale * 0.25f;
            for (int i = 0; i < TrailCount; i++)
            {
                var go = new GameObject("CursorTrail");
                go.transform.SetParent(transform.parent, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = trailSprite;
                sr.color = Color.white;
                sr.sortingOrder = 9999;            // just under the cursor (10000)
                go.transform.localScale = Vector3.one * _baseScale;
                sr.enabled = false;
                _trail[i] = sr;
                _trailAge[i] = TrailLife;
            }
        }

        private void UpdateTrail()
        {
            if (_trail == null) return;

            // Drop a fresh segment once the cursor has moved far enough.
            if ((WorldPosition - _lastTrailPos).sqrMagnitude > _trailSpacing * _trailSpacing)
            {
                _trailHead = (_trailHead + 1) % _trail.Length;
                var seg = _trail[_trailHead];
                seg.transform.position = WorldPosition + (-Camera.transform.forward) * 0.005f;
                seg.transform.localScale = Vector3.one * _baseScale;
                _trailAge[_trailHead] = 0f;
                _lastTrailPos = WorldPosition;
            }

            // Age and fade every segment.
            for (int i = 0; i < _trail.Length; i++)
            {
                _trailAge[i] += Time.deltaTime;
                float a = 1f - _trailAge[i] / TrailLife;
                var seg = _trail[i];
                if (a <= 0f) { if (seg.enabled) seg.enabled = false; continue; }
                seg.enabled = true;
                Color c = seg.color; c.a = a * 0.6f; seg.color = c;
            }
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

            Held =
                Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D) ||
                Input.GetMouseButton(0) || Input.GetMouseButton(1);

            PressedThisFrame =
                Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D) ||
                Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);

            // Small pulse on press for feedback (skipped when the skin disables CursorExpand).
            float target = _expand && (PressedThisFrame || Held) ? _baseScale * 0.85f : _baseScale;
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * target, 0.4f);

            UpdateTrail();
        }
    }
}
