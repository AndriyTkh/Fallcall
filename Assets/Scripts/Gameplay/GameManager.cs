using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using OsuUnity.Beatmaps;
using OsuUnity.Skinning;
using OsuUnity.UI;
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
        private FollowPoints _followPoints;
        private AudioSource _music;
        private Camera _cam;
        private ViewModeController _viewMode;
        private BreakSkip _breaks;
        private AutoPilot _auto;   // non-null while autoplay drives the cursor (GameSettings.Autoplay)

        private readonly List<DrawableHitObject> _active = new List<DrawableHitObject>();
        private int _spawnIndex;
        private bool _running;
        private bool _finished;
        private bool _saved;       // this session's result already written to LocalScoreStore (double-submit guard)
        private bool _failed;      // HP hit 0 while No-Fail was off — session stopped, fail screen up
        private bool _started;
        private bool _paused;
        private GUIStyle _style, _bigStyle, _centerStyle;
        private Texture2D _combo67Tex;         // shown in place of the combo counter at exactly 67 combo
        private bool _combo67Loaded;           // load attempted (success or fail) — don't retry every frame

        public void StartGame(Beatmap map, AudioClip music, Texture2D background, Camera cam)
        {
            _map = map;
            _cam = cam != null ? cam : Camera.main;

            GameSettings.Load(Osu3DSettings.Find());

            // Built before BuildScene: the hit-sound player subscribes to OnComboBreak in there.
            _score = new ScoreProcessor();
            _score.Configure(map);

            // Reserve the hit-object sorting band for this map before anything that sits above it is
            // built — the cursor reads its order in Init, and the band's height scales with the map.
            Util.RenderOrder.BeginSession(map.HitObjects.Count);

            BuildScene(background);

            _music = gameObject.AddComponent<AudioSource>();
            _music.playOnAwake = false;
            _music.clip = music;
            _music.volume = GameSettings.MusicVolume;

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

            // Autoplay: hand the cursor over to the AutoPilot so the map plays itself (testing / preview).
            _auto = GameSettings.Autoplay ? new AutoPilot(map) : null;
            _cursor.Auto = _auto != null;

            _clock = new GameClock();
            _clock.Prepare(_music, map.General.AudioLeadIn);
            _clock.Start();

            // Intro + breaks: one overlay for every click-free gap (needs the clock's lead-in so the intro
            // overlay is up before song time 0).
            _breaks = new BreakSkip();
            _breaks.Build(map, _clock.LeadInMs);
            _breaks.OnSkip += SkipTo;

            _running = true;
            _started = true;
        }

        private void BuildScene(Texture2D background)
        {
            // Playfield root, wrapped onto a sphere chunk for the first-person 3D view.
            var pfGo = new GameObject("Playfield");
            _playfield = pfGo.AddComponent<Playfield>();

            // Tuning comes from GameSettings (seeded from an Osu3DSettings in the scene or built-in
            // defaults, then overridden by saved values and the settings overlay).
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
            // _score is rebuilt with the session, so this subscription never outlives its player.
            _score.OnComboBreak += _hitSounds.PlayComboBreak;

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
            dimGo.AddComponent<BackgroundDim>().Init(_cam, videoFar * 0.95f);

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

            // The settings overlay opens over gameplay (Ctrl+O, from anywhere). While it is up it owns the
            // keyboard: pause the session under it and read no keys at all, or every letter typed into its
            // search field (A/S/D = hit, R = restart, Space = skip) would also play the map.
            if (SettingsOverlay.IsOpen)
            {
                if (!_paused && !_finished && !_failed) TogglePause();
                return;
            }
            // Its Esc-to-close claims the press, but script execution order between the two Updates is
            // undefined — without this we'd read that same Esc and pause the map it just uncovered.
            if (UiInput.Consumed) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // Results and fail screens both send Esc to the menu (like their [Menu] button); mid-play
                // Esc pauses. R restarts from anywhere, including both end screens.
                if (_finished || _failed) { ExitToMenu(); return; }
                TogglePause();
                return;
            }
            if (Input.GetKeyDown(KeyCode.R)) { Restart(); return; }

            if (!_running || _paused) return;

            _clock.Update();
            double time = _clock.TimeMs;
            _breaks?.Tick(time);   // may seek via SkipTo, so re-read the clock before spawning
            time = _clock.TimeMs;
            _score.UpdateDrain(time);   // osu! passive HP drain (no-op in breaks / before first object / when paused)
            _video?.Tick(time);
            _viewMode?.TickView(time);   // Ortho2D dynamic click-group zoom (no-op in other modes)
            _followPoints?.Tick(time);   // fade/slide the guide arrows toward upcoming objects

            // Autoplay: drive the cursor (and, in Sphere, aim the camera) before the drawables tick, so they
            // read fresh cursor state this same frame.
            if (_auto != null)
            {
                _auto.Tick(time, out Vector2 autoOsu, out bool autoHeld, out bool autoPress);
                _cursor.SetAuto(autoOsu, autoHeld, autoPress);
                _viewMode?.AimAt(autoOsu);
            }

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

            // Fail: HP hit 0 during play. No-Fail (osu!'s NF mod) gates only this *reaction* — HP still
            // drained to 0, but the session keeps playing to the end and the fail screen never shows.
            if (_score.Failed && !GameSettings.NoFail && !_finished && !_failed)
            {
                _failed = true;
                _running = false;
                _clock.Pause();          // stops the music (mirrors pause) — the run is over
                _video?.SetPaused(true);
                SetLook(false);          // drop first-person look + unlock the mouse for the fail buttons
                return;
            }

            // End condition: all spawned, none active, audio done (or past last object).
            bool allSpawned = _spawnIndex >= _map.HitObjects.Count;
            if (allSpawned && _active.Count == 0 && !_finished)
            {
                if (_clock.Finished || time > LastObjectEnd() + 1500)
                {
                    _finished = true;
                    _running = false;
                    SaveResult();   // completed play → persist to local history (once per session)
                }
            }
        }

        // Jump the session forward to <paramref name="target"/> (BreakSkip's skip button/key). Only ever
        // called with a time inside a click-free gap, so nothing judgeable is skipped over — but the spawn
        // cursor still has to step past objects the jump lands after, or they'd spawn late en masse.
        private void SkipTo(double target)
        {
            if (!_running || _paused || _finished || target <= _clock.TimeMs) return;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Destroy(_active[i].gameObject);
                _active.RemoveAt(i);
            }
            while (_spawnIndex < _map.HitObjects.Count && _map.HitObjects[_spawnIndex].EndTime < target)
                _spawnIndex++;

            _clock.Seek(target);
            _video?.Tick(target);
        }

        // Persists this session's final result to the local score history. Only ever called from the
        // FINISH path (a completed play); the guard makes it idempotent so a re-entrant finish frame or a
        // future extra call can't double-write. Fail/restart/quit-before-finish never reach here.
        private void SaveResult()
        {
            if (_saved || _score == null || _map == null) return;
            _saved = true;

            LocalScoreStore.Submit(new ScoreRecord
            {
                MapKey = LocalScoreStore.KeyFor(_map),
                TimestampUtc = DateTime.UtcNow.ToString("o"),
                Score = _score.Score,
                Accuracy = _score.Accuracy,
                MaxCombo = _score.MaxCombo,
                Count300 = _score.Count300,
                Count100 = _score.Count100,
                Count50 = _score.Count50,
                CountMiss = _score.CountMiss,
                Rank = _score.RankString(),
                NoFail = GameSettings.NoFail,
                Autoplay = GameSettings.Autoplay,
            });
        }

        private int LastObjectEnd()
        {
            if (_map.HitObjects.Count == 0) return 0;
            return _map.HitObjects[_map.HitObjects.Count - 1].EndTime;
        }

        private void Spawn(HitObject ho, int index)
        {
            int order = Util.RenderOrder.HitObject(index, _map.HitObjects.Count);
            DrawableHitObject d;

            var go = new GameObject(ho.GetType().Name);
            go.transform.SetParent(_playfield.transform, false);

            switch (ho)
            {
                case Slider _:
                    var s = go.AddComponent<SliderObject>();
                    s.SortingBase = order;
                    d = s;
                    break;
                case Spinner _:
                    var sp = go.AddComponent<SpinnerObject>();
                    sp.SortingBase = order;
                    d = sp;
                    break;
                default:
                    var c = go.AddComponent<HitCircleObject>();
                    c.SortingBase = order;
                    d = c;
                    break;
            }

            d.Init(ho, _ctx);
            _active.Add(d);
        }

        private void ShowJudgement(Judgement j, Vector3 worldPos)
        {
            FloatingText.Spawn(j, worldPos, _ctx.RadiusWorld * 0.03f, Util.RenderOrder.Judgement, _cam);
        }

        private void TogglePause()
        {
            _paused = !_paused;
            if (_paused) { _clock.Pause(); SetLook(false); PauseMenu.Show(TogglePause, Restart, ExitToMenu); }
            else { GameSettings.Save(); _clock.Resume(); SetLook(true); PauseMenu.Hide(); }
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
            GameSettings.Save(); // persist any settings-overlay tuning before rebuilding with it
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
            PauseMenu.Hide();   // persists across sessions, so it never tears down with us — just close it
            foreach (var d in _active) if (d != null) Destroy(d.gameObject);
            _active.Clear();
            _running = false;
            _started = false;
            _finished = false;
            _saved = false;
            _failed = false;
            _paused = false;
            _spawnIndex = 0;
            _breaks = null;   // rebuilt per session; its OnSkip closes over this manager
            _auto = null;     // rebuilt per session from GameSettings.Autoplay

            DestroyIfExists("Playfield");
            DestroyIfExists("Cursor");
            DestroyIfExists("HitSounds");
            DestroyIfExists("VideoPlayback");
            _video = null;
            DestroyIfExists("BackgroundDim");
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

            // IMGUI paints after every canvas has composited, so the HUD cannot be put *under* the pause
            // menu or the settings overlay — it can only stand down while one of them is up. The overlay
            // is checked separately: it also opens over the results screen, which never pauses.
            if (_paused || SettingsOverlay.IsOpen) return;

            DrawHud();
            if (!_finished && !_failed) _breaks?.Draw(_clock.TimeMs);

            GUI.Label(new Rect(20, Screen.height - 52, 700, 24),
                "[A]/[S]/[D] or click to hit   •   [Space] skip   •   [R] restart   •   [Esc] pause   •   [Ctrl+O] settings", _style);

            if (_finished) DrawResults();
            if (_failed) DrawFail();
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

        // osu!-style fail screen: the whole view dims and "Failed" sits centred over the two routes
        // (Retry [R] / Menu [Esc]). Mirrors DrawResults' panel + button layout and reuses the same
        // session actions; keys are wired in Update (R restart, Esc menu) exactly as on results.
        private void DrawFail()
        {
            // Full-screen red-black wash — the run is over, so obstructing the playfield is fine here
            // (§1.1 only protects *active* play).
            GUI.color = new Color(0.25f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float bw = 460, bh = 220;
            var r = new Rect((Screen.width - bw) / 2, (Screen.height - bh) / 2, bw, bh);
            GUI.color = new Color(0, 0, 0, 0.8f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;

            var failStyle = new GUIStyle(_centerStyle) { fontSize = 46 };
            GUI.Label(new Rect(r.x, r.y + 36, bw, 60), "Failed", failStyle);

            if (GUI.Button(new Rect(r.x + 40, r.y + bh - 56, 180, 36), "Retry [R]")) Restart();
            if (GUI.Button(new Rect(r.x + bw - 220, r.y + bh - 56, 180, 36), "Menu [Esc]")) ExitToMenu();
        }
    }
}
