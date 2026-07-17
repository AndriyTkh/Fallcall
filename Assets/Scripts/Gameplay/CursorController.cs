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

        /// <summary>When set, the mouse/keys are ignored and <see cref="SetAuto"/> drives position + input
        /// instead (autoplay). <see cref="GameManager"/> feeds it each frame from the <see cref="AutoPilot"/>.</summary>
        public bool Auto;

        private SpriteRenderer _sprite;
        private float _baseScale;
        private bool _expand = true;

        // Cursor trail: a ring of fading segments dropped along the cursor's path. Size and length are
        // live settings, so the ring is sized for the longest trail the length slider allows and only the
        // first _trailCount entries are ever used (segments are built on first use, not up front).
        private const int TrailSegmentsAtUnitLength = 24;   // segments at CursorTrailLength = 1
        private const int TrailMaxSegments = 96;
        private const float TrailUnitLife = 0.18f;          // seconds a segment lives at length = 1
        private const float TrailSpacingFactor = 0.25f;     // gap between drops, in segment diameters
        private const float TrailAlpha = 0.6f;              // alpha of a freshly dropped segment
        private const float TrailEndScale = 0.55f;          // segment tapers to this by the end of its life
        private const float TrailSurfaceOffset = 0.005f;    // lift off the playfield so it can't z-fight

        private SpriteRenderer[] _trail;
        private float[] _trailAge;
        private int _trailHead;
        private int _trailCount;
        private Sprite _trailSprite;
        private Color _trailTint;
        private Transform _trailRoot;
        private Vector3 _lastTrailPos;
        private Vector2 _lastTrailOsu;
        private bool _trailSeeded;

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
            _sprite.sortingOrder = Util.RenderOrder.Cursor;
            _expand = Skinning.Skin.Current?.Config.CursorExpand ?? true;
            _baseScale = worldDiameter;
            transform.localScale = Vector3.one * _baseScale;

            InitTrail();
        }

        private void InitTrail()
        {
            _trailSprite = Skinning.SkinSprites.CursorTrail;
            // A skin's own trail carries its colour; the procedural fallback follows the cursor's tint.
            _trailTint = _trailSprite != Util.TextureFactory.Disc ? Color.white : _sprite.color;
            _trail = new SpriteRenderer[TrailMaxSegments];
            _trailAge = new float[TrailMaxSegments];
            // Segments own their world transform, so they hang off a root of their own rather than the
            // cursor (whose scale pulses) — and that root dies with this controller, so a restart can't
            // strand them in the scene.
            _trailRoot = new GameObject("CursorTrail").transform;
        }

        private void OnDestroy()
        {
            if (_trailRoot != null) Destroy(_trailRoot.gameObject);
        }

        /// <summary>The segment at <paramref name="i"/>, built on first use.</summary>
        private SpriteRenderer Segment(int i)
        {
            var seg = _trail[i];
            if (seg != null) return seg;

            var go = new GameObject("Segment");
            go.transform.SetParent(_trailRoot, false);
            seg = go.AddComponent<SpriteRenderer>();
            seg.sprite = _trailSprite;
            seg.color = _trailTint;
            // One sprite, one texture, one sorting order across the whole ring: it draws as one batch.
            seg.sortingOrder = Util.RenderOrder.CursorTrail;
            seg.enabled = false;
            _trail[i] = seg;
            return seg;
        }

        /// <summary>Resize the live ring, retiring any segments that fall outside it.</summary>
        private void SetTrailCount(int count)
        {
            // Kill the age too, not just the renderer: a segment left merely hidden would pop back at its
            // stale position the moment the length slider grows the ring past it again.
            for (int i = count; i < _trail.Length; i++)
            {
                if (_trail[i] != null) _trail[i].enabled = false;
                _trailAge[i] = float.MaxValue;
            }
            _trailCount = count;
            if (_trailHead >= count) _trailHead = 0;
        }

        private void UpdateTrail()
        {
            if (_trail == null) return;

            int want = GameSettings.CursorTrail
                ? Mathf.Clamp(Mathf.RoundToInt(TrailSegmentsAtUnitLength * GameSettings.CursorTrailLength),
                              2, TrailMaxSegments)
                : 0;
            if (want != _trailCount) SetTrailCount(want);
            if (_trailCount == 0) return;

            float scale = _baseScale * GameSettings.CursorTrailSize;
            float spacing = Mathf.Max(1e-4f, scale * TrailSpacingFactor);
            float life = Mathf.Max(0.01f, TrailUnitLife * GameSettings.CursorTrailLength);

            DropTrail(spacing, scale);
            AgeTrail(scale, life);
        }

        private void DropTrail(float spacing, float scale)
        {
            float dist = Vector3.Distance(WorldPosition, _lastTrailPos);
            if (dist < spacing) return;

            // A flick (or a camera whip in first person) crosses many segment-widths in a single frame, so
            // walk the gap instead of dropping one segment per frame and leaving a dotted line. The walk is
            // in osu! space so the segments follow the sphere rather than chording through it.
            //
            // Past a ring's worth of drops the older ones would be overwritten before they ever draw, so
            // the gap gets spread evenly over the whole ring instead: the trail stretches rather than
            // falling behind the cursor, and a genuine teleport (restart, view-mode switch) resolves in a
            // single frame instead of smearing for as long as it takes the walk to catch up.
            int want = Mathf.FloorToInt(dist / spacing);
            bool stretched = want > _trailCount;
            int steps = stretched ? _trailCount : want;

            Vector2 fromOsu = _lastTrailOsu;
            for (int s = 1; s <= steps; s++)
            {
                // Uncapped, the walk consumes whole spacings and leaves the remainder for next frame.
                float t = stretched ? (float)s / steps : s * spacing / dist;
                Vector2 osu = Vector2.Lerp(fromOsu, OsuPosition, t);
                _trailHead = (_trailHead + 1) % _trailCount;
                PlaceSegment(Segment(_trailHead), osu, scale);
                _trailAge[_trailHead] = 0f;
                _lastTrailOsu = osu;
                _lastTrailPos = Playfield.ToWorld(osu);
            }
        }

        private void PlaceSegment(SpriteRenderer seg, Vector2 osu, float scale)
        {
            Vector3 world = Playfield.ToWorld(osu);
            var t = seg.transform;
            if (Playfield.Curved)
            {
                t.rotation = Playfield.OrientationAt(osu);
                t.position = world - t.forward * TrailSurfaceOffset;
            }
            else
            {
                t.position = world + (-Camera.transform.forward) * TrailSurfaceOffset;
            }
            t.localScale = Vector3.one * scale;
        }

        /// <summary>Fade and taper every live segment; retire the ones past their life.</summary>
        private void AgeTrail(float scale, float life)
        {
            for (int i = 0; i < _trailCount; i++)
            {
                var seg = _trail[i];
                if (seg == null) continue;

                _trailAge[i] += Time.deltaTime;
                float k = 1f - _trailAge[i] / life;   // 1 = just dropped, 0 = dead
                if (k <= 0f) { if (seg.enabled) seg.enabled = false; continue; }

                seg.enabled = true;
                Color c = _trailTint;
                c.a = _trailTint.a * k * TrailAlpha;
                seg.color = c;
                seg.transform.localScale = Vector3.one * (scale * Mathf.Lerp(TrailEndScale, 1f, k));
            }
        }

        /// <summary>Autoplay hook: place the cursor at an osu! coordinate and drive its tap state directly,
        /// bypassing the mouse/keys. <paramref name="press"/> is a single-frame tap edge (read like a real
        /// <see cref="PressedThisFrame"/>); <paramref name="held"/> stays true across sliders/spinners. Called
        /// by <see cref="GameManager"/> before the drawables tick, so they read fresh state the same frame.</summary>
        public void SetAuto(Vector2 osu, bool held, bool press)
        {
            OsuPosition = osu;
            WorldPosition = Playfield.ToWorld(osu);
            if (Playfield.Curved)
            {
                transform.rotation = Playfield.OrientationAt(osu);
                transform.position = WorldPosition - transform.forward * 0.01f;
            }
            else
            {
                transform.position = WorldPosition + (-Camera.transform.forward) * 0.01f;
            }
            Held = held;
            PressedThisFrame = press;
        }

        private void Update()
        {
            if (Camera == null || Playfield == null) return;

            // In autoplay the position + tap state come from SetAuto (fed by GameManager); read no input.
            if (!Auto)
            {
                if (Playfield.Curved)
                {
                    // First person: aim is wherever the camera looks, so the cursor rides the screen centre.
                    Ray ray = Camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                    if (Playfield.RaycastSurface(ray, out Vector3 hit))
                    {
                        WorldPosition = hit;
                        OsuPosition = Playfield.ToOsu(hit);
                        // Sit just in front of the wall, laid flat against it.
                        transform.rotation = Playfield.OrientationAt(OsuPosition);
                        transform.position = hit - transform.forward * 0.01f;
                    }
                }
                else
                {
                    // Flat mode: project the mouse pointer onto the playfield plane.
                    Ray ray = Camera.ScreenPointToRay(Input.mousePosition);
                    Plane plane = Playfield.WorldPlane;
                    if (plane.Raycast(ray, out float enter))
                    {
                        WorldPosition = ray.GetPoint(enter);
                        OsuPosition = Playfield.ToOsu(WorldPosition);
                        transform.position = WorldPosition + (-Camera.transform.forward) * 0.01f;
                    }
                }

                Held =
                    Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D) ||
                    Input.GetMouseButton(0) || Input.GetMouseButton(1);

                PressedThisFrame =
                    Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D) ||
                    Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
            }

            // Small pulse on press for feedback (skipped when the skin disables CursorExpand).
            float target = _expand && (PressedThisFrame || Held) ? _baseScale * 0.85f : _baseScale;
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * target, 0.4f);

            // Anchor the trail to the first real cursor position; without this the first frame walks a
            // whole ring out from the origin.
            if (!_trailSeeded) { _lastTrailPos = WorldPosition; _lastTrailOsu = OsuPosition; _trailSeeded = true; }
            UpdateTrail();
        }
    }
}
