using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using OsuUnity.Beatmaps;
using OsuUnity.Skinning;
using OsuUnity.Visual;
using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Drives a single play session: builds the playfield, spawns hit objects in time with the audio,
    /// resolves input (with osu! note-lock ordering) and renders the HUD.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        public event Action OnExitToMenu;

        private Beatmap _map;
        private GameContext _ctx;
        private GameClock _clock;
        private ScoreProcessor _score;
        private Playfield _playfield;
        private CursorController _cursor;
        private HitSoundPlayer _hitSounds;
        private VideoPlayback _video;
        private BackgroundDim _dim;
        private FollowPoints _followPoints;
        private AudioSource _music;
        private Camera _cam;
        private ViewModeController _viewMode;

        private readonly List<DrawableHitObject> _active = new List<DrawableHitObject>();
        private int _spawnIndex;
        private bool _running;
        private bool _finished;
        private bool _started;
        private bool _paused;
        private GUIStyle _style, _bigStyle, _centerStyle, _menuLabel;
        private Texture2D _combo67Tex;         // shown in place of the combo counter at exactly 67 combo
        private bool _combo67Loaded;           // load attempted (success or fail) — don't retry every frame
        private static readonly string[] _modeNames = { "Sphere", "2D Ortho", "Falling" };
        private Vector2 _pauseScroll;          // pause-menu scroll offset
        private float _pauseContentH = 1200f;  // measured content height (last frame) for the scroll view

        public void StartGame(Beatmap map, AudioClip music, Texture2D background, Camera cam)
        {
            _map = map;
            _cam = cam != null ? cam : Camera.main;

            GameSettings.Load(Osu3DSettings.Find());

            BuildScene(background);

            _music = gameObject.AddComponent<AudioSource>();
            _music.playOnAwake = false;
            _music.clip = music;
            _music.volume = GameSettings.MusicVolume;

            _score = new ScoreProcessor();
            _score.Configure(map.Difficulty.HPDrainRate);

            _ctx = new GameContext
            {
                Playfield = _playfield,
                Cursor = _cursor,
                Score = _score,
                HitSounds = _hitSounds,
                ObjectRoot = _playfield.transform,
                OnJudgement = ShowJudgement,
            };
            _ctx.Configure(map);
            _followPoints?.Init(_ctx); // guide-arrow line between objects (needs the configured context)

            _clock = new GameClock();
            _clock.Prepare(_music, map.General.AudioLeadIn);
            _clock.Start();

            _running = true;
            _started = true;
        }

        private void BuildScene(Texture2D background)
        {
            // Playfield root, wrapped onto a sphere chunk for the first-person 3D view.
            var pfGo = new GameObject("Playfield");
            _playfield = pfGo.AddComponent<Playfield>();

            // Tuning comes from GameSettings (seeded from an Osu3DSettings in the scene or built-in
            // defaults, then overridden by saved values and the pause-menu sliders).
            _playfield.PixelScale = GameSettings.PixelScale;
            _playfield.Curved = GameSettings.Curved;
            _playfield.ProjectionDistance = GameSettings.ProjectionDistance;
            _playfield.ChunkHDegrees = GameSettings.ChunkHDegrees;
            _playfield.ChunkVDegrees = GameSettings.ChunkVDegrees;

            // Camera basics (mode-agnostic); ViewModeController owns per-mode config below.
            if (_cam == null) _cam = new GameObject("Main Camera").AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.Skybox;               // show the skybox, not a flat colour
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = Mathf.Max(1000f, _playfield.ProjectionDistance * 20f);

            // View modes (Sphere / 2D-ortho / Falling), switchable mid-map with [Tab]. Initial mode comes
            // from the Curved setting; the controller configures the camera, projection and first-person look.
            _viewMode = _cam.GetComponent<ViewModeController>() ?? _cam.gameObject.AddComponent<ViewModeController>();
            _viewMode.Init(_playfield, _cam, _active, GameSettings.StartMode, _map);

            // Cursor.
            var cursorGo = new GameObject("Cursor");
            _cursor = cursorGo.AddComponent<CursorController>();
            _cursor.Init(_playfield, _cam,
                _playfield.OsuToWorldDistance(DifficultyCalculator.CircleRadius(_map.Difficulty.CircleSize)) *
                0.6f * GameSettings.CursorSize);

            // Hit sounds.
            var hsGo = new GameObject("HitSounds");
            hsGo.transform.SetParent(transform, false);
            _hitSounds = hsGo.AddComponent<HitSoundPlayer>();
            _hitSounds.Volume = GameSettings.HitSoundVolume;
            _hitSounds.Init(_map);

            // Background video (osu! "Video" event), if the map has one and it's enabled. Backdrop quad
            // sits well beyond the gameplay chunk radius but inside the far clip plane; the camera's FOV
            // for the active view mode is already set above, so the quad sizes correctly against it.
            float videoFar = Mathf.Max(_playfield.ProjectionDistance * 15f, 50f);
            if (GameSettings.EnableVideo && !string.IsNullOrEmpty(_map.VideoFile))
            {
                string videoPath = Path.Combine(_map.Directory, _map.VideoFile);
                if (File.Exists(videoPath))
                {
                    var videoGo = new GameObject("VideoPlayback");
                    _video = videoGo.AddComponent<VideoPlayback>();
                    _video.Init(videoPath, _map.VideoOffset, _cam, videoFar);
                }
            }

            // Background dim quad: sits just in front of the video backdrop / skybox but beyond gameplay,
            // so it darkens everything behind the hit objects (osu! "background dim"). Always present; alpha
            // 0 disables the draw. Distance stays under videoFar so it is never occluded by the video quad.
            var dimGo = new GameObject("BackgroundDim");
            _dim = dimGo.AddComponent<BackgroundDim>();
            _dim.Init(_cam, videoFar * 0.95f);

            // Follow points (guide arrows between consecutive in-combo objects). Child of the playfield so
            // it rides along with any view-mode transform; Init runs later once the context is configured.
            if (GameSettings.ShowFollowPoints)
            {
                var fpGo = new GameObject("FollowPoints");
                fpGo.transform.SetParent(_playfield.transform, false);
                _followPoints = fpGo.AddComponent<FollowPoints>();
            }
        }

        private void Update()
        {
            if (!_started) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_finished) { ExitToMenu(); return; }
                TogglePause();
                return;
            }
            if (Input.GetKeyDown(KeyCode.R)) { Restart(); return; }

            if (!_running || _paused) return;

            _clock.Update();
            double time = _clock.TimeMs;
            _video?.Tick(time);
            _viewMode?.TickView(time);   // Ortho2D dynamic click-group zoom (no-op in other modes)
            _followPoints?.Tick(time);   // fade/slide the guide arrows toward upcoming objects

            // Spawn objects entering their preempt window.
            while (_spawnIndex < _map.HitObjects.Count &&
                   time >= _map.HitObjects[_spawnIndex].StartTime - _ctx.Preempt)
            {
                Spawn(_map.HitObjects[_spawnIndex], _spawnIndex);
                _spawnIndex++;
            }

            // Determine the front-most un-judged object (note lock).
            DrawableHitObject front = null;
            for (int i = 0; i < _active.Count; i++)
            {
                var d = _active[i];
                if (d.HeadJudged) continue;
                if (front == null || d.Object.StartTime < front.Object.StartTime)
                    front = d;
            }

            // Tick + cull.
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var d = _active[i];
                d.Tick(time, d == front);
                if (d.Finished)
                {
                    Destroy(d.gameObject);
                    _active.RemoveAt(i);
                }
            }

            // End condition: all spawned, none active, audio done (or past last object).
            bool allSpawned = _spawnIndex >= _map.HitObjects.Count;
            if (allSpawned && _active.Count == 0 && !_finished)
            {
                if (_clock.Finished || time > LastObjectEnd() + 1500)
                {
                    _finished = true;
                    _running = false;
                }
            }
        }

        private int LastObjectEnd()
        {
            if (_map.HitObjects.Count == 0) return 0;
            return _map.HitObjects[_map.HitObjects.Count - 1].EndTime;
        }

        private void Spawn(HitObject ho, int index)
        {
            int depth = _map.HitObjects.Count - index; // earlier objects render on top
            DrawableHitObject d;

            var go = new GameObject(ho.GetType().Name);
            go.transform.SetParent(_playfield.transform, false);

            switch (ho)
            {
                case Slider _:
                    var s = go.AddComponent<SliderObject>();
                    s.DepthOrder = depth;
                    d = s;
                    break;
                case Spinner _:
                    var sp = go.AddComponent<SpinnerObject>();
                    sp.DepthOrder = depth;
                    d = sp;
                    break;
                default:
                    var c = go.AddComponent<HitCircleObject>();
                    c.DepthOrder = depth;
                    d = c;
                    break;
            }

            d.Init(ho, _ctx);
            _active.Add(d);
        }

        private void ShowJudgement(Judgement j, Vector3 worldPos)
        {
            FloatingText.Spawn(j, worldPos, _ctx.RadiusWorld * 0.03f, 20000, _cam);
        }

        private void TogglePause()
        {
            _paused = !_paused;
            if (_paused) { _clock.Pause(); SetLook(false); }
            else { GameSettings.Save(); _clock.Resume(); SetLook(true); }
            _video?.SetPaused(_paused);
        }

        // Pause/resume the active view mode: pausing drops first-person look and unlocks the mouse for the
        // menu; resuming restores the current mode's look state (see ViewModeController.SetPaused).
        private void SetLook(bool on)
        {
            if (_viewMode != null) _viewMode.SetPaused(!on);
        }

        private void ExitToMenu()
        {
            GameSettings.Save();
            Cleanup();
            OnExitToMenu?.Invoke();
        }

        private void Restart()
        {
            GameSettings.Save(); // persist any pause-menu tuning before rebuilding with it
            var map = _map;
            var clip = _music != null ? _music.clip : null;
            Texture2D bg = null;
            var bgGo = GameObject.Find("Background");
            if (bgGo != null) bg = bgGo.GetComponent<SpriteRenderer>().sprite.texture;

            Cleanup();
            StartGame(map, clip, bg, _cam);
        }

        private void Cleanup()
        {
            foreach (var d in _active) if (d != null) Destroy(d.gameObject);
            _active.Clear();
            _running = false;
            _started = false;
            _finished = false;
            _paused = false;
            _spawnIndex = 0;

            DestroyIfExists("Playfield");
            DestroyIfExists("Cursor");
            DestroyIfExists("HitSounds");
            DestroyIfExists("VideoPlayback");
            _video = null;
            DestroyIfExists("BackgroundDim");
            _dim = null;
            DestroyIfExists("FollowPoints");
            _followPoints = null;
            if (_music != null) { _music.Stop(); Destroy(_music); }

            // Drop the view-mode controller + first-person look so the mouse cursor unlocks for the menu.
            // DestroyImmediate (not Destroy): these live on the persistent camera and are re-created this
            // same frame by StartGame's `GetComponent<>() ?? AddComponent<>()`. Deferred Destroy would let
            // that GetComponent hand back the doomed old components (?? sees them as non-null), which then
            // get culled at end of frame — leaving the camera with no look component (frozen/locked cursor).
            if (_cam != null)
            {
                var vm = _cam.GetComponent<ViewModeController>();
                if (vm != null) DestroyImmediate(vm);
                var look = _cam.GetComponent<FirstPersonCamera>();
                if (look != null) DestroyImmediate(look);
            }
            _viewMode = null;
        }

        private static void DestroyIfExists(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) Destroy(go);
        }

        // ----------------------------------------------------------------- HUD

        private void EnsureStyles()
        {
            if (_style != null) return;
            _style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            _style.normal.textColor = Color.white;
            _bigStyle = new GUIStyle(_style) { fontSize = 40 };
            _centerStyle = new GUIStyle(_style) { fontSize = 30, alignment = TextAnchor.MiddleCenter };
            _menuLabel = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            _menuLabel.normal.textColor = Color.white;
        }

        // Lazily loads Assets/Images/images.png (the "67" combo art). Returns null if the file is missing.
        private Texture2D Combo67Texture()
        {
            if (_combo67Loaded) return _combo67Tex;
            _combo67Loaded = true;

            string file = Path.Combine(Application.dataPath, "Images", "images.png");
            if (File.Exists(file))
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                if (tex.LoadImage(File.ReadAllBytes(file))) { tex.filterMode = FilterMode.Bilinear; _combo67Tex = tex; }
                else Destroy(tex);
            }
            return _combo67Tex;
        }

        private void OnGUI()
        {
            if (!_started || _ctx == null) return;
            EnsureStyles();

            DrawHud();

            GUI.Label(new Rect(20, Screen.height - 52, 600, 24),
                "[A]/[S]/[D] or click to hit   •   [R] restart   •   [Esc] pause", _style);

            if (_finished) DrawResults();
            else if (_paused) DrawPauseMenu();
        }

        // Draws score / accuracy / combo / health. Uses the skin's dedicated HUD fonts + scorebar when
        // present (osu! layout: score & accuracy top-right, combo bottom-left, health top-left); falls
        // back to plain IMGUI text with the original layout otherwise.
        private void DrawHud()
        {
            float s = Mathf.Max(0.1f, GameSettings.HudScale);
            const float margin = 16f;

            if (HudSkin.Available)
            {
                var cfg = Skin.Current.Config;
                HudSkin.DrawFont(cfg.ScorePrefix, _score.Score.ToString("#,0"),
                    Screen.width - margin, margin, 42f * s, cfg.ScoreOverlap, true, Color.white);
                HudSkin.DrawFont(cfg.ScorePrefix, (_score.Accuracy * 100.0).ToString("0.00") + "%",
                    Screen.width - margin, margin + 50f * s, 24f * s, cfg.ScoreOverlap, true, Color.white);
                if (!HudSkin.DrawHealthBar(margin, margin, Screen.width * 0.4f * s, (float)_score.HP))
                    DrawSimpleHealthBar(margin, margin, Screen.width * 0.4f * s, 12f);

                // Combo bottom-left; the "67" easter egg still wins when it fires.
                float comboH = 44f * s, comboY = Screen.height - margin - comboH;
                if (!DrawCombo67(margin, comboY, comboH, leftAnchor: true) &&
                    !HudSkin.DrawFont(cfg.ComboPrefix, _score.Combo.ToString() + "x",
                        margin, comboY, comboH, cfg.ComboOverlap, false, Color.white))
                {
                    GUI.Label(new Rect(margin, comboY, 200, comboH), $"{_score.Combo}x", _bigStyle);
                }
                return;
            }

            // ---- plain-text fallback (no skin HUD font), osu!standard layout ----
            var rightScore = new GUIStyle(_bigStyle) { alignment = TextAnchor.UpperRight };
            var rightAcc = new GUIStyle(_style) { alignment = TextAnchor.UpperRight };
            GUI.Label(new Rect(Screen.width - 420 - margin, margin, 420, 44), $"{_score.Score:n0}", rightScore);
            GUI.Label(new Rect(Screen.width - 420 - margin, margin + 46, 420, 30), $"{_score.Accuracy * 100:0.00}%", rightAcc);

            float fbComboY = Screen.height - margin - 44f;
            if (!DrawCombo67(margin, fbComboY, 44f, leftAnchor: true))
                GUI.Label(new Rect(margin, fbComboY, 200, 44), $"{_score.Combo}x", _bigStyle);

            DrawSimpleHealthBar(margin, margin, Screen.width * 0.4f * s, 12f);
        }

        // Plain red→green HP bar for when no scorebar skin element is available.
        private void DrawSimpleHealthBar(float x, float y, float width, float height)
        {
            GUI.Box(new Rect(x, y, width, height), GUIContent.none);
            GUI.color = Color.Lerp(Color.red, Color.green, (float)_score.HP);
            GUI.DrawTexture(new Rect(x, y, width * (float)_score.HP, height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // Draws the "67" combo art (Assets/Images/images.png) if the combo is exactly 67 and the art
        // loads; returns whether it drew. <paramref name="anchorX"/> is the left or right edge of the slot.
        private bool DrawCombo67(float anchorX, float y, float height, bool leftAnchor)
        {
            if (_score.Combo != 67 || Combo67Texture() == null) return false;
            var tex = _combo67Tex;
            float w67 = height * tex.width / tex.height;
            float x = leftAnchor ? anchorX : anchorX - w67;
            GUI.DrawTexture(new Rect(x, y, w67, height), tex, ScaleMode.ScaleToFit);
            return true;
        }

        private void DrawPauseMenu()
        {
            // Dim the playfield.
            GUI.color = new Color(0, 0, 0, 0.75f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Fixed chrome heights; the middle content scrolls so the window never exceeds the screen.
            const float headerH = 56f, footerH = 64f, pad = 8f;
            float bw = 460;
            float bh = Mathf.Min(headerH + _pauseContentH + footerH, Screen.height - 40f);
            var r = new Rect((Screen.width - bw) / 2, (Screen.height - bh) / 2, bw, bh);
            GUI.color = new Color(0, 0, 0, 0.85f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(r.x, r.y + 16, bw, 40), "Paused", _centerStyle);

            var look = _cam != null ? _cam.GetComponent<FirstPersonCamera>() : null;

            // --- scrollable content ---
            var viewport = new Rect(r.x + pad, r.y + headerH, bw - 2 * pad, bh - headerH - footerH);
            // Reserve the scrollbar width so content never sits under it.
            var content = new Rect(0, 0, viewport.width - 18f, _pauseContentH);
            _pauseScroll = GUI.BeginScrollView(viewport, _pauseScroll, content);

            float x = 24, w = content.width - 48, y = 6;

            // --- audio + input ---
            // fmt "0%" already scales by 100 for display, so displayMul stays 1 (else it double-scales).
            GameSettings.MusicVolume = Slider("Music volume", GameSettings.MusicVolume, 0f, 1f, "0%", 1f, x, w, ref y);
            GameSettings.HitSoundVolume = Slider("Hit sound volume", GameSettings.HitSoundVolume, 0f, 1f, "0%", 1f, x, w, ref y);
            GameSettings.LookSensitivity = Slider("Look sensitivity", GameSettings.LookSensitivity, 0.5f, 10f, "0.0", 1f, x, w, ref y);

            // Audio + sensitivity apply live.
            if (_music != null) _music.volume = GameSettings.MusicVolume;
            if (_hitSounds != null) _hitSounds.Volume = GameSettings.HitSoundVolume;
            if (look != null) look.Sensitivity = GameSettings.LookSensitivity;

            y += 8;
            GUI.Label(new Rect(x, y, w, 22), "Visibility", _menuLabel); y += 26;
            GameSettings.BackgroundDim = Slider("Background dim", GameSettings.BackgroundDim, 0f, 1f, "0%", 1f, x, w, ref y);
            _dim?.SetDim(GameSettings.BackgroundDim); // applies live to video + skybox + far scene
            GameSettings.ShowFollowPoints = GUI.Toggle(new Rect(x, y, w, 24), GameSettings.ShowFollowPoints, "  Follow points (restart)"); y += 30;
            GameSettings.FollowPointScale = Slider("Follow point size", GameSettings.FollowPointScale, 0.3f, 3f, "0.00", 1f, x, w, ref y);
            GameSettings.HudScale = Slider("HUD size", GameSettings.HudScale, 0.4f, 2.5f, "0.00", 1f, x, w, ref y);

            y += 8;
            GUI.Label(new Rect(x, y, w, 22), "View mode — live · also [Tab] to cycle", _menuLabel); y += 26;
            int curMode = _viewMode != null ? (int)_viewMode.Mode : (int)GameSettings.StartMode;
            int selMode = GUI.Toolbar(new Rect(x, y, w, 26), curMode, _modeNames);
            if (selMode != curMode)
            {
                GameSettings.StartMode = (ViewMode)selMode;                 // remembered for the next session
                GameSettings.Curved = selMode == (int)ViewMode.Sphere;      // keep the playfield seed consistent
                if (_viewMode != null)
                {
                    _viewMode.SetMode((ViewMode)selMode);                   // apply immediately, mid-map
                    _viewMode.SetPaused(true);                              // keep look off + mouse free for the menu
                }
            }
            y += 30;

            y += 8;
            GUI.Label(new Rect(x, y, w, 22), "Playfield (applied on restart)", _menuLabel); y += 26;
            GameSettings.PixelScale = Slider("Scale", GameSettings.PixelScale, 0.002f, 0.05f, "0.000", 1f, x, w, ref y);
            GameSettings.ProjectionDistance = Slider("Radius", GameSettings.ProjectionDistance, 0.5f, 12f, "0.0", 1f, x, w, ref y);
            GameSettings.ChunkHDegrees = Slider("Chunk H°", GameSettings.ChunkHDegrees, -300f, 300f, "0", 1f, x, w, ref y);
            GameSettings.ChunkVDegrees = Slider("Chunk V°", GameSettings.ChunkVDegrees, 20f, 180f, "0", 1f, x, w, ref y);

            y += 8;
            GUI.Label(new Rect(x, y, w, 22), "Cursor (applied on restart)", _menuLabel); y += 26;
            GameSettings.CursorSize = Slider("Cursor size", GameSettings.CursorSize, 0.5f, 3f, "0.00", 1f, x, w, ref y);
            GameSettings.CursorHitboxOsu = Slider("Cursor hitbox (0 = faithful)", GameSettings.CursorHitboxOsu, 0f, 30f, "0", 1f, x, w, ref y);

            y += 8;
            GUI.Label(new Rect(x, y, w, 22), "Video (applied on restart)", _menuLabel); y += 26;
            GameSettings.EnableVideo = GUI.Toggle(new Rect(x, y, w, 24), GameSettings.EnableVideo, "  Play background video"); y += 30;

            y += 8;
            GUI.Label(new Rect(x, y, w, 22), "2D zoom — Ortho mode / [Tab]", _menuLabel); y += 26;
            GameSettings.OrthoZoom = GUI.Toggle(new Rect(x, y, w, 24), GameSettings.OrthoZoom, "  Dynamic click-group zoom (live)"); y += 30;
            GameSettings.OrthoZoomSmooth = Slider("Camera smoothing (live)", GameSettings.OrthoZoomSmooth, 0.02f, 1f, "0.00", 1f, x, w, ref y);
            GameSettings.OrthoZoomMargin = Slider("Zoom margin (live)", GameSettings.OrthoZoomMargin, 0f, 6f, "0.0", 1f, x, w, ref y);
            GameSettings.OrthoOvershoot = Slider("Pan overshoot (live)", GameSettings.OrthoOvershoot, 0f, 0.6f, "0.00", 1f, x, w, ref y);
            GameSettings.OrthoLookaheadMs = Slider("Lookahead (ms, restart)", GameSettings.OrthoLookaheadMs, 0f, 1500f, "0", 1f, x, w, ref y);
            GameSettings.OrthoAggressiveness = Slider("Grouping aggressiveness (restart)", GameSettings.OrthoAggressiveness, 0f, 1f, "0%", 1f, x, w, ref y);
            GameSettings.OrthoKiaiSmoothMul = Slider("Kiai snap (live)", GameSettings.OrthoKiaiSmoothMul, 0.1f, 1f, "0.00", 1f, x, w, ref y);
            GameSettings.OrthoKiaiZoomMul = Slider("Kiai zoom (live)", GameSettings.OrthoKiaiZoomMul, 0.5f, 1f, "0.00", 1f, x, w, ref y);

            y += 4;
            if (GUI.Button(new Rect(x, y, w, 30), "Reset to defaults"))
            {
                GameSettings.Reset();
                // Apply live ones immediately so the menu reflects the reset.
                if (_music != null) _music.volume = GameSettings.MusicVolume;
                if (_hitSounds != null) _hitSounds.Volume = GameSettings.HitSoundVolume;
                if (look != null) look.Sensitivity = GameSettings.LookSensitivity;
                _dim?.SetDim(GameSettings.BackgroundDim);
            }
            y += 38;

            _pauseContentH = y;   // remember for next frame's scroll-view sizing
            GUI.EndScrollView();

            // --- buttons (fixed footer, below the scroll view) ---
            float bx = r.x + pad + 16, bwn = bw - 2 * (pad + 16);
            float by = r.y + bh - footerH + 14;
            float third = (bwn - 16) / 3f;
            if (GUI.Button(new Rect(bx, by, third, 36), "Resume")) TogglePause();
            if (GUI.Button(new Rect(bx + third + 8, by, third, 36), "Restart")) Restart();
            if (GUI.Button(new Rect(bx + 2 * (third + 8), by, third, 36), "Song Select")) ExitToMenu();
        }

        // Labeled horizontal slider; returns the new value. Advances y by one row.
        private float Slider(string label, float value, float min, float max,
                             string fmt, float displayMul, float x, float w, ref float y)
        {
            GUI.Label(new Rect(x, y, w - 70, 22), label, _menuLabel);
            GUI.Label(new Rect(x + w - 70, y, 70, 22), (value * displayMul).ToString(fmt), _menuLabel);
            y += 22;
            value = GUI.HorizontalSlider(new Rect(x, y + 4, w, 18), value, min, max);
            y += 30;
            return value;
        }

        private void DrawResults()
        {
            float bw = 460, bh = 320;
            var r = new Rect((Screen.width - bw) / 2, (Screen.height - bh) / 2, bw, bh);
            GUI.color = new Color(0, 0, 0, 0.8f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(r.x, r.y + 16, bw, 40), "Results", _centerStyle);
            float y = r.y + 70;
            void Line(string s) { GUI.Label(new Rect(r.x + 40, y, bw - 80, 28), s, _style); y += 32; }
            Line($"Rank:      {_score.RankString()}");
            Line($"Score:     {_score.Score:n0}");
            Line($"Accuracy:  {_score.Accuracy * 100:0.00}%");
            Line($"Max Combo: {_score.MaxCombo}x");
            Line($"300 / 100 / 50 / X:");
            Line($"   {_score.Count300} / {_score.Count100} / {_score.Count50} / {_score.CountMiss}");

            if (GUI.Button(new Rect(r.x + 40, r.y + bh - 56, 180, 36), "Retry [R]")) Restart();
            if (GUI.Button(new Rect(r.x + bw - 220, r.y + bh - 56, 180, 36), "Menu [Esc]")) ExitToMenu();
        }
    }
}
