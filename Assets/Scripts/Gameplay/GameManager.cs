using System;
using System.Collections;
using System.Collections.Generic;
using OsuUnity.Beatmaps;
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
        private AudioSource _music;
        private Camera _cam;

        private readonly List<DrawableHitObject> _active = new List<DrawableHitObject>();
        private int _spawnIndex;
        private bool _running;
        private bool _finished;
        private bool _started;
        private bool _paused;
        private GUIStyle _style, _bigStyle, _centerStyle, _menuLabel;

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

            _clock = new GameClock();
            _clock.Prepare(_music, map.General.AudioLeadIn);
            _clock.Start();

            _running = true;
            _started = true;
        }

        private void BuildScene(Texture2D background)
        {
            // Playfield root, wrapped onto a cylinder for the first-person 3D view.
            var pfGo = new GameObject("Playfield");
            _playfield = pfGo.AddComponent<Playfield>();

            // Tuning comes from GameSettings (seeded from an Osu3DSettings in the scene or built-in
            // defaults, then overridden by saved values and the pause-menu sliders).
            _playfield.PixelScale = GameSettings.PixelScale;
            _playfield.Curved = GameSettings.Curved;
            _playfield.ProjectionDistance = GameSettings.ProjectionDistance;
            _playfield.ArcDegrees = GameSettings.ArcDegrees;

            // Camera: perspective, sitting on the cylinder axis looking down +Z (first person).
            if (_cam == null) _cam = new GameObject("Main Camera").AddComponent<Camera>();
            _cam.orthographic = false;
            float halfH = Playfield.Height * 0.5f * _playfield.PixelScale;
            // Vertical FOV that frames the wall's height at the projection distance, with a little margin.
            _cam.fieldOfView =
                2f * Mathf.Atan(halfH * 1.15f / _playfield.ProjectionDistance) * Mathf.Rad2Deg;
            _cam.transform.position = _playfield.transform.position; // on the axis
            _cam.transform.rotation = _playfield.transform.rotation; // looking +Z toward the wall
            _cam.clearFlags = CameraClearFlags.Skybox;               // show the skybox, not a flat colour
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = Mathf.Max(1000f, _playfield.ProjectionDistance * 20f);

            // First-person mouse-look: the player stands on the axis and looks around the wall.
            var look = _cam.GetComponent<FirstPersonCamera>() ?? _cam.gameObject.AddComponent<FirstPersonCamera>();
            look.enabled = true;
            look.Sensitivity = GameSettings.LookSensitivity;
            look.Init(_playfield.transform.rotation, _playfield.HalfArcDegrees, _playfield.HalfPitchDegrees);

            // Cursor.
            var cursorGo = new GameObject("Cursor");
            _cursor = cursorGo.AddComponent<CursorController>();
            _cursor.Init(_playfield, _cam,
                _playfield.OsuToWorldDistance(DifficultyCalculator.CircleRadius(_map.Difficulty.CircleSize)) * 0.6f);

            // Hit sounds.
            var hsGo = new GameObject("HitSounds");
            hsGo.transform.SetParent(transform, false);
            _hitSounds = hsGo.AddComponent<HitSoundPlayer>();
            _hitSounds.Volume = GameSettings.HitSoundVolume;
            _hitSounds.Init(_map);
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
        }

        // Enable/disable first-person look; disabling also unlocks the mouse for the menu (see OnDisable).
        private void SetLook(bool on)
        {
            if (_cam == null) return;
            var look = _cam.GetComponent<FirstPersonCamera>();
            if (look != null) look.enabled = on;
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
            if (_music != null) { _music.Stop(); Destroy(_music); }

            // Drop first-person look so the mouse cursor unlocks for the menu.
            if (_cam != null)
            {
                var look = _cam.GetComponent<FirstPersonCamera>();
                if (look != null) Destroy(look);
            }
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

        private void OnGUI()
        {
            if (!_started || _ctx == null) return;
            EnsureStyles();

            GUI.Label(new Rect(20, 14, 400, 40), $"{_score.Score:n0}", _bigStyle);
            GUI.Label(new Rect(20, 60, 400, 30), $"{_score.Accuracy * 100:0.00}%", _style);
            GUI.Label(new Rect(Screen.width - 220, 14, 200, 40), $"{_score.Combo}x", _bigStyle);

            // HP bar.
            float w = Screen.width - 40;
            GUI.Box(new Rect(20, Screen.height - 26, w, 12), GUIContent.none);
            GUI.color = Color.Lerp(Color.red, Color.green, (float)_score.HP);
            GUI.DrawTexture(new Rect(20, Screen.height - 26, w * (float)_score.HP, 12), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(20, Screen.height - 52, 600, 24),
                "[A]/[S]/[D] or click to hit   •   [R] restart   •   [Esc] pause", _style);

            if (_finished) DrawResults();
            else if (_paused) DrawPauseMenu();
        }

        private void DrawPauseMenu()
        {
            // Dim the playfield.
            GUI.color = new Color(0, 0, 0, 0.75f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float bw = 460, bh = 560;
            var r = new Rect((Screen.width - bw) / 2, (Screen.height - bh) / 2, bw, bh);
            GUI.color = new Color(0, 0, 0, 0.85f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(r.x, r.y + 16, bw, 40), "Paused", _centerStyle);

            float x = r.x + 40, w = bw - 80, y = r.y + 70;

            // --- audio + input ---
            GameSettings.MusicVolume = Slider("Music volume", GameSettings.MusicVolume, 0f, 1f, "0%", 100f, x, w, ref y);
            GameSettings.HitSoundVolume = Slider("Hit sound volume", GameSettings.HitSoundVolume, 0f, 1f, "0%", 100f, x, w, ref y);
            GameSettings.LookSensitivity = Slider("Look sensitivity", GameSettings.LookSensitivity, 0.5f, 10f, "0.0", 1f, x, w, ref y);

            // Audio + sensitivity apply live.
            if (_music != null) _music.volume = GameSettings.MusicVolume;
            if (_hitSounds != null) _hitSounds.Volume = GameSettings.HitSoundVolume;
            var look = _cam != null ? _cam.GetComponent<FirstPersonCamera>() : null;
            if (look != null) look.Sensitivity = GameSettings.LookSensitivity;

            y += 8;
            GUI.Label(new Rect(x, y, w, 22), "Playfield (applied on restart)", _menuLabel); y += 26;
            GameSettings.Curved = GUI.Toggle(new Rect(x, y, w, 24), GameSettings.Curved, "  Curved (3D wall)"); y += 30;
            GameSettings.PixelScale = Slider("Scale", GameSettings.PixelScale, 0.002f, 0.05f, "0.000", 1f, x, w, ref y);
            GameSettings.ProjectionDistance = Slider("Distance", GameSettings.ProjectionDistance, 0.5f, 12f, "0.0", 1f, x, w, ref y);
            GameSettings.ArcDegrees = Slider("Arc°", GameSettings.ArcDegrees, -300f, 300f, "0", 1f, x, w, ref y);

            y += 4;
            if (GUI.Button(new Rect(x, y, w, 30), "Reset to defaults"))
            {
                GameSettings.Reset();
                // Apply live ones immediately so the menu reflects the reset.
                if (_music != null) _music.volume = GameSettings.MusicVolume;
                if (_hitSounds != null) _hitSounds.Volume = GameSettings.HitSoundVolume;
                if (look != null) look.Sensitivity = GameSettings.LookSensitivity;
            }

            // --- buttons ---
            float by = r.y + bh - 52;
            float third = (w - 16) / 3f;
            if (GUI.Button(new Rect(x, by, third, 36), "Resume")) TogglePause();
            if (GUI.Button(new Rect(x + third + 8, by, third, 36), "Restart")) Restart();
            if (GUI.Button(new Rect(x + 2 * (third + 8), by, third, 36), "Song Select")) ExitToMenu();
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
