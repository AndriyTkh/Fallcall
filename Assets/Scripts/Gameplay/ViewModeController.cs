using System.Collections.Generic;
using OsuUnity.Beatmaps;
using OsuUnity.Visual;
using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>Camera / projection presentation modes, switchable mid-map (STRUCTURE §3).</summary>
    public enum ViewMode
    {
        /// <summary>Default 3D: playfield wrapped onto a sphere chunk, first-person look.</summary>
        Sphere,
        /// <summary>Classic flat osu! plane viewed with an orthographic camera, mouse-aimed.</summary>
        Ortho2D,
        /// <summary>Flat gameplay viewed by a perspective camera projected onto a sphere-cap above the
        /// plane, tilting toward the mouse — "you move above the screen" (STRUCTURE §3c).</summary>
        Falling,
    }

    // INDEX: Runtime view-mode switcher — configures camera+projection per ViewMode, toggles mid-map.
    /// <summary>
    /// Owns the camera + <see cref="Playfield"/> presentation and switches between <see cref="ViewMode"/>s
    /// at runtime (press <see cref="ToggleKey"/>). Each mode fully reconfigures the camera (perspective vs.
    /// orthographic, position, FOV/size) and the projection (<see cref="Playfield.Curved"/>) plus the
    /// first-person look component. Switching reprojects already-spawned objects so they follow the new
    /// projection (see <see cref="DrawableHitObject.Reproject"/>).
    ///
    /// This is the single seam for camera/projection modes — <see cref="GameManager"/> just creates it and
    /// hands over the camera/playfield/active-object list; it does not configure the camera itself.
    ///
    /// In <see cref="ViewMode.Ortho2D"/> the orthographic camera does <b>not</b> statically frame the whole
    /// field: a <see cref="OrthoZoom"/> pass (A.4) pans and zooms it to the upcoming <i>click group</i> —
    /// following streams, zooming into spinners, otherwise framing the next group of clicks. It only
    /// retargets between clicks (target is constant within a group, so the smoothing settles and holds),
    /// giving osu!-cursor-cam-style motion. All knobs are settings (<see cref="GameSettings"/>).
    ///
    /// <see cref="ViewMode.Falling"/> (A.5) keeps gameplay on the flat plane but views it with a
    /// perspective camera placed on a sphere-cap above the plane, looking at the plane centre and tilting
    /// toward the mouse's offset from screen centre — a handheld-overhead feel. Tab cycles all three modes.
    /// </summary>
    public sealed class ViewModeController : MonoBehaviour
    {
        public KeyCode ToggleKey = KeyCode.Tab;

        private Playfield _pf;
        private Camera _cam;
        private FirstPersonCamera _look;
        private List<DrawableHitObject> _active;
        private ViewMode _mode;
        private bool _paused;

        private readonly OrthoZoomer _ortho = new OrthoZoomer();
        private Vector3 _fallVel;   // SmoothDamp velocity for the Falling camera position

        public ViewMode Mode => _mode;

        /// <summary>Wire up the camera/playfield and apply the initial mode (no reproject on first apply).</summary>
        public void Init(Playfield pf, Camera cam, List<DrawableHitObject> active, ViewMode initial, Beatmap map)
        {
            _pf = pf;
            _cam = cam;
            _active = active;
            _look = cam.GetComponent<FirstPersonCamera>() ?? cam.gameObject.AddComponent<FirstPersonCamera>();
            _look.Sensitivity = GameSettings.LookSensitivity;
            _look.Auto = GameSettings.Autoplay;   // autoplay aims the camera at the notes (see AimAt)
            _ortho.Build(pf, cam, map);           // precompute click groups for the Ortho2D zoom pass
            Apply(initial, reproject: false);
        }

        private void Update()
        {
            if (_paused) return;
            if (Input.GetKeyDown(ToggleKey)) Cycle();
        }

        /// <summary>Advance to the next mode: Sphere → Ortho2D → Falling → Sphere.</summary>
        public void Cycle()
        {
            ViewMode next;
            switch (_mode)
            {
                case ViewMode.Sphere: next = ViewMode.Ortho2D; break;
                case ViewMode.Ortho2D: next = ViewMode.Falling; break;
                default: next = ViewMode.Sphere; break;
            }
            Apply(next, reproject: true);
        }

        public void SetMode(ViewMode m) => Apply(m, reproject: true);

        /// <summary>
        /// Per-frame view update, driven by <see cref="GameManager"/> with the current song time. Used by
        /// Ortho2D (dynamic click-group zoom) and Falling (mouse-tilt camera); Sphere is static so no-ops.
        /// </summary>
        public void TickView(double timeMs)
        {
            if (_paused) return;
            if (_mode == ViewMode.Ortho2D) _ortho.Tick(timeMs);
            else if (_mode == ViewMode.Falling) TickFalling();
        }

        /// <summary>
        /// Autoplay camera aim (Sphere only): rotate the first-person camera toward an osu! target so the
        /// notes the AutoPilot is hitting stay on screen. No-op in Ortho2D/Falling, where the camera is
        /// driven by their own passes and the cursor is placed directly on the flat plane. Smoothed so the
        /// view glides between notes rather than snapping.
        /// </summary>
        public void AimAt(Vector2 osu)
        {
            if (_mode != ViewMode.Sphere || _cam == null || _pf == null) return;
            Vector3 dir = _pf.ToWorld(osu) - _cam.transform.position;
            if (dir.sqrMagnitude < 1e-6f) return;
            Quaternion want = Quaternion.LookRotation(dir, _pf.transform.up);
            float k = 1f - Mathf.Exp(-12f * Time.deltaTime);   // frame-rate-independent smoothing
            _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, want, k);
        }

        /// <summary>Pause hook: while paused, drop first-person look (and unlock the mouse for menus);
        /// on resume, restore the current mode's look state without recentring the view.</summary>
        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (_look == null) return;
            if (paused) _look.enabled = false;
            else _look.enabled = UsesFirstPerson(_mode);
        }

        private static bool UsesFirstPerson(ViewMode m) => m == ViewMode.Sphere;

        private void Apply(ViewMode m, bool reproject)
        {
            _mode = m;
            switch (m)
            {
                case ViewMode.Ortho2D: ConfigureOrtho(); break;
                case ViewMode.Falling: ConfigureFalling(); break;
                case ViewMode.Sphere: ConfigureSphere(); break;
            }

            if (reproject && _active != null)
                for (int i = 0; i < _active.Count; i++)
                    if (_active[i] != null) _active[i].Reproject();
        }

        // Sphere: perspective camera at the sphere centre, FOV framing the vertical chunk, first-person look.
        private void ConfigureSphere()
        {
            _pf.Curved = true;
            _cam.orthographic = false;
            _cam.fieldOfView = Mathf.Clamp(Mathf.Abs(_pf.ChunkVDegrees) * 1.05f, 30f, 120f);
            _cam.transform.position = _pf.transform.position;
            _cam.transform.rotation = _pf.transform.rotation;
            _look.enabled = true;
            _look.Init(_pf.transform.rotation, _pf.HalfArcDegrees, _pf.HalfPitchDegrees);
        }

        // Ortho2D: flat plane framed by an orthographic camera looking straight at it; mouse-aimed cursor.
        // Snaps to the full-field frame; the per-frame OrthoZoomer then pans/zooms it to the click groups.
        private void ConfigureOrtho()
        {
            _pf.Curved = false;
            _look.enabled = false;                          // OnDisable unlocks the mouse for flat aiming
            _cam.orthographic = true;
            _cam.orthographicSize = _ortho.FullFieldSize;
            _cam.transform.rotation = _pf.transform.rotation;
            _cam.transform.position = _pf.transform.position - _pf.transform.forward * 10f; // behind, looking in
            _ortho.Reset();                                 // clear smoothing so the next frames ease from here
        }

        // Falling (STRUCTURE §3c): flat gameplay, perspective camera on a sphere-cap above the plane looking
        // at its centre. Static config below (projection + FOV + an overhead starting pose); TickFalling
        // tilts the camera toward the mouse each frame. Mouse is free (look disabled) for flat aiming.
        private void ConfigureFalling()
        {
            _pf.Curved = false;
            _look.enabled = false;                          // OnDisable unlocks the mouse for flat aiming
            _cam.orthographic = false;

            // Frame the plane to fill FallingZoom of the view height at radius R (perspective).
            float planeHalfH = Playfield.Height * 0.5f * _pf.PixelScale;
            float r = Mathf.Max(0.5f, GameSettings.FallingRadius);
            float zoom = Mathf.Max(0.05f, GameSettings.FallingZoom);
            _cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan2(planeHalfH / zoom, r) * Mathf.Rad2Deg, 10f, 120f);

            // Start straight overhead, looking down at the plane centre.
            Vector3 centre = _pf.transform.position;
            Vector3 up = -_pf.transform.forward;            // plane's viewer-side normal (the sphere pole)
            _cam.transform.position = centre + up * r;
            _cam.transform.rotation = Quaternion.LookRotation(-up, _pf.transform.up);
            _fallVel = Vector3.zero;
        }

        // Move the perspective camera along the sphere-cap toward the mouse's offset from screen centre:
        // the further the mouse from centre, the more the camera tilts (up to FallingMaxTiltDeg), leaning
        // the overhead view that way. Uses screen-space mouse (not the cursor's plane hit) so the camera
        // never chases its own projection. SmoothDamp'd for a handheld drift.
        private void TickFalling()
        {
            if (_pf == null || _cam == null) return;

            // Mouse offset from screen centre, normalised to [-1,1] per axis.
            Vector2 half = new Vector2(Mathf.Max(1f, Screen.width * 0.5f), Mathf.Max(1f, Screen.height * 0.5f));
            Vector2 sn = new Vector2((Input.mousePosition.x - half.x) / half.x, (Input.mousePosition.y - half.y) / half.y);
            float t = Mathf.Clamp01(sn.magnitude);

            Vector3 centre = _pf.transform.position;
            Vector3 pole = -_pf.transform.forward;          // sphere pole (straight above the plane)
            // In-plane tilt direction: screen right/up → plane right/up.
            Vector3 dir = _pf.transform.right * sn.x + _pf.transform.up * sn.y;
            if (dir.sqrMagnitude > 1e-6f) dir.Normalize(); else dir = _pf.transform.right;

            float r = Mathf.Max(0.5f, GameSettings.FallingRadius);
            float theta = t * GameSettings.FallingMaxTiltDeg * Mathf.Deg2Rad;
            Vector3 target = centre + r * (Mathf.Cos(theta) * pole + Mathf.Sin(theta) * dir);

            float smooth = Mathf.Max(0.02f, GameSettings.FallingSmooth);
            _cam.transform.position = Vector3.SmoothDamp(_cam.transform.position, target, ref _fallVel, smooth);
            _cam.transform.rotation = Quaternion.LookRotation(centre - _cam.transform.position, _pf.transform.up);
        }

        /// <summary>
        /// Dynamic orthographic camera driver for <see cref="ViewMode.Ortho2D"/> (A.4). Pre-partitions the
        /// beatmap into big <i>click groups</i> — not osu!'s short combos but a timing-driven partition that
        /// accumulates ~<see cref="GameSettings.OrthoGroupTargetCount"/> notes and then cuts at the next
        /// pause (so a group's actual size floats with map density; spinners stand alone). It classifies
        /// each group as Normal / Stream / Spinner and every frame pans+zooms the camera to frame the group
        /// that is currently being (or about to be) clicked:
        ///  • <b>Normal</b> — a fixed rect covering the whole group's hit geometry (full slider paths, not
        ///    just endpoints) plus every note coming within <see cref="GameSettings.OrthoLookaheadMs"/> after
        ///    it so upcoming targets are visible before the shift, padded by the circle radius. Constant while
        ///    clicked → the camera holds still; it re-frames in the gap before the next group.
        ///  • <b>Stream</b> — follows the cursor path along the stream, tightly zoomed.
        ///  • <b>Spinner</b> — a smooth zoom-in toward the centre over the spinner's duration.
        /// During <b>kiai</b> ("hyper") time each group's camera goes all-out — a shorter smoothing time
        /// (snappier, less transition cooldown) and a tighter, punchier frame (<see cref="GameSettings"/>
        /// Kiai muls).
        /// Motion is a <see cref="Vector3.SmoothDamp"/>/<see cref="Mathf.SmoothDamp"/> chase with a
        /// <b>constant</b> smoothing time (<see cref="GameSettings.OrthoZoomSmooth"/> alone) — deliberately
        /// never sped up to "catch up", which reads as a disorienting snap. Predictiveness comes from timing,
        /// not speed: the target retargets to a group the moment the previous group's last note is hit, so
        /// the lazy camera gets the whole gap as runway, and generous framing (lookahead + margin) keeps
        /// notes on screen even mid-glide. Snappier feel = a lower smoothing slider. An optional
        /// <see cref="GameSettings.OrthoOvershoot"/> (default 0) aims slightly past the target to shave lag
        /// without steady-state aim bias. All thresholds are <see cref="GameSettings"/> knobs; groups are
        /// rebuilt each session (grouping/lookahead apply on restart), framing/motion apply live.
        /// </summary>
        private sealed class OrthoZoomer
        {
            private enum Kind { Normal, Stream, Spinner }

            private struct Group
            {
                public int Start, End;      // ms
                public Kind Kind;
                public Rect Bounds;         // osu-space bbox of the group's own hit geometry (slider paths incl.)
                public Vector2 Center;      // osu-space bbox centre
                public Rect FrameBounds;    // Bounds grown to also include the next group's first note(s)
                public Vector2 FrameCenter; // FrameBounds centre (what the Normal frame targets)
                public float AvgSpacingOsu; // mean object-to-object distance (stream sizing)
                public int First, Last;     // indices into _objs (inclusive) for stream follow
                public bool Kiai;           // group falls in kiai ("hyper") time → all-out camera
            }

            private Playfield _pf;
            private Camera _cam;
            private Beatmap _map;
            private List<HitObject> _objs;
            private readonly List<Group> _groups = new List<Group>();
            private float _circleRadiusOsu;

            private Vector3 _posVel;
            private float _sizeVel;
            private bool _activeKiai;       // kiai state of the group framed this tick (drives smoothing)

            /// <summary>Full-field orthographic size (the static "see the whole plane" frame).</summary>
            public float FullFieldSize => Playfield.Height * 0.5f * (_pf != null ? _pf.PixelScale : 0.0135f) * 1.1f;

            public void Build(Playfield pf, Camera cam, Beatmap map)
            {
                _pf = pf;
                _cam = cam;
                _map = map;
                _groups.Clear();
                _objs = map != null ? map.HitObjects : null;
                _circleRadiusOsu = map != null
                    ? (float)DifficultyCalculator.CircleRadius(map.Difficulty.CircleSize)
                    : 32f;
                if (_objs == null || _objs.Count == 0) return;

                float streamGap = GameSettings.OrthoStreamGapMs;
                float streamSpace = GameSettings.OrthoStreamSpacingOsu;

                // Timing-driven partition (not osu!'s short combos). Accumulate notes until the group is
                // "full" (target count), then end it at the first real pause — the gap becomes the sightread
                // window where the camera reframes. A very long pause ends a group regardless of size
                // (section break); a hard count cap forces a cut through gapless streams; spinners stand
                // alone. Group size therefore floats with map density: denser/harder maps pack more notes
                // between pauses, so groups grow — exactly the map-dependent ~15-20 the design wants.
                // OrthoAggressiveness scales the counts: 0 = the configured (big) sizes; 1 = target 1 /
                // max 4 → the camera cuts to every native click-group (each little cluster).
                float aggr = Mathf.Clamp01(GameSettings.OrthoAggressiveness);
                int target = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(GameSettings.OrthoGroupTargetCount, 1f, aggr)));
                int maxCount = Mathf.Max(target, Mathf.RoundToInt(Mathf.Lerp(GameSettings.OrthoGroupMaxCount, 4f, aggr)));
                float breakGap = GameSettings.OrthoGroupBreakGapMs;
                float hardGap = GameSettings.OrthoGroupGapMs;

                int start = 0;
                for (int i = 1; i <= _objs.Count; i++)
                {
                    int count = i - start;                        // notes in the group if we cut after i-1
                    bool atEnd = i == _objs.Count;
                    // Spinner isolation: a spinner never shares a group with clicks.
                    bool spinnerEdge = !atEnd && (_objs[i] is Spinner || _objs[i - 1] is Spinner);
                    float gapAfter = atEnd ? float.MaxValue : _objs[i].StartTime - _objs[i - 1].EndTime;

                    bool cut = atEnd
                        || spinnerEdge
                        || count >= maxCount                      // hard cap (gapless stream)
                        || gapAfter > hardGap                     // section-break pause
                        || (count >= target && gapAfter >= breakGap); // full + a real pause to reframe in

                    if (!cut) continue;
                    _groups.Add(MakeGroup(start, i - 1, streamGap, streamSpace));
                    start = i;
                }

                // Lookahead: grow each group's frame to also include every note the player must click within
                // OrthoLookaheadMs after the group ends, so upcoming targets are already on screen before the
                // camera shifts to them. Time-based (not a fixed note count) so it adapts to density — on a
                // fast section it reaches further ahead in notes, on a slow one fewer. Stops at a spinner.
                float lookMs = Mathf.Max(0f, GameSettings.OrthoLookaheadMs);
                for (int gi = 0; gi < _groups.Count; gi++)
                {
                    Group g = _groups[gi];
                    if (lookMs <= 0f || g.Kind == Kind.Spinner) continue;

                    // Start from the current group's own bbox, then union in every note coming within the
                    // window. The frame stays CENTRED on the current group (g.Center) and only grows — so the
                    // notes being clicked now stay put/centred and the upcoming ones appear toward the edge
                    // they arrive from, rather than the centre sliding off onto the future notes.
                    float minX = g.Bounds.xMin, minY = g.Bounds.yMin, maxX = g.Bounds.xMax, maxY = g.Bounds.yMax;
                    float limit = g.End + lookMs;
                    bool grew = false;
                    for (int k = g.Last + 1; k < _objs.Count; k++)
                    {
                        var ho = _objs[k];
                        if (ho is Spinner || ho.StartTime > limit) break;
                        Vector2 p = ho.Position;
                        if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                        if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
                        grew = true;
                    }
                    if (grew)
                    {
                        Vector2 c = g.Center;                       // keep the current group centred
                        float halfX = Mathf.Max(c.x - minX, maxX - c.x);
                        float halfY = Mathf.Max(c.y - minY, maxY - c.y);
                        g.FrameBounds = Rect.MinMaxRect(c.x - halfX, c.y - halfY, c.x + halfX, c.y + halfY);
                        g.FrameCenter = c;
                        _groups[gi] = g;
                    }
                }
            }

            private Group MakeGroup(int first, int last, float streamGap, float streamSpace)
            {
                var g = new Group { First = first, Last = last, Start = _objs[first].StartTime, End = _objs[last].EndTime };

                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                void Extend(Vector2 p)
                {
                    if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
                }
                // A slider's body can bow far outside the head→tail segment, so frame its whole sampled
                // path, not just the endpoints — otherwise the curve gets clipped.
                void ExtendObject(HitObject ho)
                {
                    Extend(ho.Position);
                    Extend(ho.EndPosition);
                    if (ho is Slider sl && sl.Path != null)
                    {
                        var pts = sl.Path.Points;
                        for (int k = 0; k < pts.Count; k++) Extend(sl.Position + pts[k]);
                    }
                }

                bool hasSpinner = false;
                float spacingSum = 0f; int spacingCount = 0;
                bool stream = (last - first + 1) >= 5;
                for (int i = first; i <= last; i++)
                {
                    var ho = _objs[i];
                    if (ho is Spinner) hasSpinner = true;
                    ExtendObject(ho);
                    if (i > first)
                    {
                        float spacing = Vector2.Distance(_objs[i - 1].EndPosition, ho.Position);
                        int gap = ho.StartTime - _objs[i - 1].StartTime;
                        spacingSum += spacing; spacingCount++;
                        if (gap > streamGap || spacing > streamSpace) stream = false;
                    }
                }

                g.Bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
                g.Center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
                g.FrameBounds = g.Bounds;      // default; lookahead pass may grow it in Build
                g.FrameCenter = g.Center;
                g.AvgSpacingOsu = spacingCount > 0 ? spacingSum / spacingCount : 0f;
                g.Kind = hasSpinner ? Kind.Spinner : (stream ? Kind.Stream : Kind.Normal);
                g.Kiai = _map != null && _map.IsKiaiAt(g.Start);
                return g;
            }

            /// <summary>Snap the smoothing to the camera's current state (call when entering the mode).</summary>
            public void Reset() { _posVel = Vector3.zero; _sizeVel = 0f; }

            public void Tick(double timeMs)
            {
                if (_pf == null || _cam == null) return;

                float time = (float)timeMs;
                Vector2 centerOsu;
                float size;
                bool haveGroup;
                if (!GameSettings.OrthoZoom || _groups.Count == 0)
                {
                    centerOsu = new Vector2(Playfield.Width * 0.5f, Playfield.Height * 0.5f);
                    size = FullFieldSize;
                    _activeKiai = false;
                    haveGroup = false;
                }
                else
                {
                    Desired(time, out centerOsu, out size);   // also sets _activeKiai / _activeStart
                    haveGroup = true;
                }

                Vector3 targetPos = _pf.ToWorld(centerOsu, 0f) - _pf.transform.forward * 10f;

                // Smoothing is CONSTANT — the slider alone sets the camera's laziness, so the motion always
                // has the same, predictable, trackable character (never a speed-up/snap to "catch up", which
                // throws players off). Predictiveness comes from *when* the target changes, not how fast the
                // camera chases it: the target retargets to a group the instant the previous group's last
                // note is hit (see ActiveIndex), giving the lazy camera the whole gap of runway; generous
                // framing (lookahead + margin) keeps notes on screen even mid-glide. Want snappy? Lower the
                // smoothing slider. Kiai applies a constant (not dynamic) tighter multiplier.
                float smooth = Mathf.Max(0.02f, GameSettings.OrthoZoomSmooth);
                if (_activeKiai) smooth *= Mathf.Clamp(GameSettings.OrthoKiaiSmoothMul, 0.05f, 1f);
                smooth = Mathf.Max(0.02f, smooth);

                // Optional predictive lead: aim slightly past the target along the travel vector to shave
                // arrival lag. The term is proportional to the remaining distance so it vanishes at rest —
                // no steady-state aim bias while clicking. Default 0 (pure, maximally smooth SmoothDamp).
                Vector3 aim = targetPos;
                float overshoot = Mathf.Clamp(GameSettings.OrthoOvershoot, 0f, 0.6f);
                if (haveGroup && overshoot > 0f)
                    aim = targetPos + (targetPos - _cam.transform.position) * overshoot;

                _cam.transform.position = Vector3.SmoothDamp(_cam.transform.position, aim, ref _posVel, smooth);
                _cam.orthographicSize = Mathf.SmoothDamp(_cam.orthographicSize, size, ref _sizeVel, smooth);
            }

            private void Desired(float time, out Vector2 centerOsu, out float size)
            {
                Group g = _groups[ActiveIndex(time)];
                _activeKiai = g.Kiai;
                float ps = _pf.PixelScale;
                float circleWorld = _circleRadiusOsu * ps;
                float pad = circleWorld * GameSettings.OrthoZoomMargin;
                float minSize = circleWorld * 2.5f;
                float full = FullFieldSize;

                switch (g.Kind)
                {
                    case Kind.Spinner:
                        centerOsu = new Vector2(Playfield.Width * 0.5f, Playfield.Height * 0.5f);
                        float prog = g.End > g.Start ? Mathf.Clamp01((time - g.Start) / (g.End - g.Start)) : 1f;
                        prog = 1f - (1f - prog) * (1f - prog);            // ease-out
                        size = Mathf.Lerp(full, full * 0.5f, prog);
                        break;

                    case Kind.Stream:
                        centerOsu = FollowPos(g, time);
                        size = Mathf.Clamp(g.AvgSpacingOsu * ps * 1.6f + pad, minSize, full);
                        break;

                    default: // Normal — fixed rect over the whole group + the next group's first note(s).
                        centerOsu = g.FrameCenter;
                        float halfW = g.FrameBounds.width * 0.5f * ps + pad;
                        float halfH = g.FrameBounds.height * 0.5f * ps + pad;
                        float aspect = _cam.aspect > 0.01f ? _cam.aspect : 1.777f;
                        // Frame the group's OWN geometry — slider bodies/tails bow outside the 512x384
                        // playfield and lookahead pulls in the next group's notes, so the needed size
                        // routinely exceeds `full`. Clamping down to `full` clipped whatever spilled past
                        // the field edge (usually a slider tail); only cap far above it to bound pathology.
                        size = Mathf.Clamp(Mathf.Max(halfH, halfW / aspect), minSize, full * 2.5f);
                        break;
                }

                // Kiai ("hyper"): tighten the frame for a punchier, all-out look (never below minSize).
                if (g.Kiai) size = Mathf.Max(minSize, size * Mathf.Clamp(GameSettings.OrthoKiaiZoomMul, 0.3f, 1f));
            }

            // The group currently framed. A group is revealed the instant the previous group's last click
            // lands (its End) — the whole inter-group pause is then spent easing the camera onto the new
            // frame, i.e. maximum sightread time. The first group leads in OrthoZoomLeadMs before its first
            // click (there is no previous group to wait on).
            private int ActiveIndex(float time)
            {
                float lead = GameSettings.OrthoZoomLeadMs;
                int idx = 0;
                for (int i = 0; i < _groups.Count; i++)
                {
                    float reveal = i == 0 ? _groups[i].Start - lead : _groups[i - 1].End;
                    if (time >= reveal) idx = i;
                    else break;
                }
                return idx;
            }

            // Cursor path along a stream: lerp between consecutive objects by time (tail of the previous
            // to the head of the next), so the camera tracks where the cursor should be.
            private Vector2 FollowPos(Group g, float time)
            {
                if (time <= _objs[g.First].StartTime) return _objs[g.First].Position;
                for (int i = g.First; i < g.Last; i++)
                {
                    int t0 = _objs[i].StartTime, t1 = _objs[i + 1].StartTime;
                    if (time >= t0 && time <= t1)
                    {
                        float f = t1 > t0 ? (time - t0) / (t1 - t0) : 1f;
                        f = f * f * (3f - 2f * f);                        // smoothstep
                        return Vector2.Lerp(_objs[i].EndPosition, _objs[i + 1].Position, f);
                    }
                }
                return _objs[g.Last].EndPosition;
            }
        }
    }
}
