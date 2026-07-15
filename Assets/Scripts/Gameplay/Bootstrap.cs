using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using OsuUnity.Beatmaps;
using OsuUnity.Skinning;
using OsuUnity.UI;
using OsuUnity.Util;
using UnityEngine;

// INDEX: Entry point — loads skin, scans BeatmapLibrary, drives SongSelectUI, then hands off to GameManager.
namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Entry point. Loads a skin, scans <see cref="BeatmapLibrary"/>, shows the osu!lazer-style
    /// song select (<see cref="SongSelectUI"/>), loads the chosen difficulty's audio/background,
    /// then hands off to <see cref="GameManager"/>. Spawns itself automatically on play so no
    /// scene wiring is required — just press Play.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        private enum State { Scanning, Menu, SongSelect, Loading, Playing }
        private State _state = State.Scanning;

        /// <summary>
        /// Seam for the U2 settings overlay: when that block lands, its overlay assigns this hook and the
        /// Settings route opens it. Until then the route shows a toast (no dead button, §1.3). Kept here
        /// so U2 can wire in without editing <see cref="Bootstrap"/> (which only U3 owns).
        /// </summary>
        public static Action OpenSettingsHook;

        private string _statusText = "Scanning for beatmaps...";
        private GUIStyle _label;
        private GameManager _game;
        private SongSelectUI _songSelect;
        private MainScreen _main;
        private NavBar _nav;
        private bool _hasBeatmaps;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            if (FindObjectOfType<Bootstrap>() != null) return;
            var go = new GameObject("OsuBootstrap");
            DontDestroyOnLoad(go);
            go.AddComponent<Bootstrap>();
        }

        private void Start()
        {
            // Prefer a developer-authored SongSelectUI placed in the scene (editor pivot); otherwise
            // auto-spawn one so "press Play, no wiring" still holds with zero scene setup.
            _songSelect = FindObjectOfType<SongSelectUI>();
            if (_songSelect == null) _songSelect = gameObject.AddComponent<SongSelectUI>();
            _songSelect.PlaySelected += Select;
            _songSelect.Hide();

            _main = gameObject.AddComponent<MainScreen>();
            _main.Navigate += OnNavigate;

            _nav = gameObject.AddComponent<NavBar>();
            _nav.Navigate += OnNavigate;
            _nav.SetSuppressed(true);   // hidden until the menu is up

            StartCoroutine(Scan());
        }

        private IEnumerator Scan()
        {
            _state = State.Scanning;
            yield return null;

            LoadSkin();
            yield return null;

            List<BeatmapSetInfo> sets = BeatmapLibrary.Scan();
            _songSelect.Populate(sets);
            _hasBeatmaps = sets.Count > 0;
            ShowMain();
        }

        // ----------------------------------------------------------------- navigation shell (U3)

        private void ShowMain()
        {
            _state = State.Menu;
            _songSelect.Hide();
            _main.SetHasBeatmaps(_hasBeatmaps);
            _main.Show();
            _nav.SetSuppressed(false);
        }

        private void ShowSongSelect()
        {
            _state = State.SongSelect;
            _main.Hide();
            _songSelect.Show();
            _nav.SetSuppressed(false);
        }

        // Routes raised by both the main screen and the toolbar (docs/UI-DESIGN §1.4 — same routes,
        // three ways). This is the only place that decides what a route does.
        private void OnNavigate(MenuRoute route)
        {
            switch (route)
            {
                case MenuRoute.Home:
                    if (_state == State.SongSelect || _state == State.Menu) ShowMain();
                    break;
                case MenuRoute.Play:
                    if (_hasBeatmaps && (_state == State.Menu || _state == State.SongSelect)) ShowSongSelect();
                    else if (!_hasBeatmaps) _nav.Toast("No beatmaps yet — use Browse to download some.");
                    break;
                case MenuRoute.Browse:
                    // v1: Browse routes into song select (download-by-id lives there); online mirror
                    // search is U5, which shares the same card UI.
                    if (_state == State.Menu || _state == State.SongSelect) ShowSongSelect();
                    break;
                case MenuRoute.Settings:
                    if (OpenSettingsHook != null) OpenSettingsHook();
                    else _nav.Toast("Settings overlay arrives in U2  ·  Ctrl+O");
                    break;
                case MenuRoute.Quit:
                    Quit();
                    break;
            }
        }

        private void Update()
        {
            // Esc backs out of song select to the main screen (§1.4 — Esc = back, everywhere).
            if (_state == State.SongSelect && !UiInput.Typing && Input.GetKeyDown(KeyCode.Escape))
                ShowMain();
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Select(BeatmapSetInfo set, BeatmapDifficultyInfo diff)
        {
            Debug.Log($"[Bootstrap] PlaySelected: {set.SetName} [{diff.Version}] ({diff.OsuPath})");
            _state = State.Loading;
            _songSelect.Hide();
            _nav.SetSuppressed(true);   // never obstruct the playfield during load/play (§1.1)
            _statusText = "Loading " + diff.Version + " ...";
            StartCoroutine(LoadAndPlay(diff));
        }

        private IEnumerator LoadAndPlay(BeatmapDifficultyInfo diff)
        {
            Beatmap map;
            try
            {
                map = BeatmapParser.ParseFile(diff.OsuPath);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Bootstrap] Failed to parse '{diff.OsuPath}': {e}");
                _statusText = "Failed to parse this beatmap.\n[Esc] to go back.";
                ShowSongSelect();
                yield break;
            }

            AudioClip clip = null;
            if (!string.IsNullOrEmpty(map.General.AudioFilename))
            {
                string audioPath = Path.Combine(map.Directory, map.General.AudioFilename);
                yield return AssetLoader.LoadAudio(audioPath, c => clip = c);
            }
            if (clip == null)
            {
                _statusText = "Failed to load audio.\nIf this is an .mp3, try converting it to .ogg.\n[Esc] to go back.";
                ShowSongSelect();
                yield break;
            }

            Texture2D bg = null;
            if (!string.IsNullOrEmpty(map.BackgroundFile))
            {
                string bgPath = Path.Combine(map.Directory, map.BackgroundFile);
                yield return AssetLoader.LoadTexture(bgPath, t => bg = t);
            }

            var go = new GameObject("GameManager");
            _game = go.AddComponent<GameManager>();
            _game.OnExitToMenu += BackToMenu;
            _game.StartGame(map, clip, bg, Camera.main);
            _state = State.Playing;
        }

        private void BackToMenu()
        {
            if (_game != null) Destroy(_game.gameObject);
            _game = null;
            _statusText = "";
            var sets = BeatmapLibrary.Scan();
            _hasBeatmaps = sets.Count > 0;
            _songSelect.Populate(sets);
            ShowSongSelect();   // land back at song select for quick retry; Esc → main
        }

        // ----------------------------------------------------------------- skin

        private void LoadSkin()
        {
            if (Skin.Current != null) return; // already loaded this session

            // A loose folder containing skin.ini wins (lets users drop an unpacked skin in).
            string folder = FindSkinFolder();

            // Otherwise extract the first .osk archive (a renamed .zip) we can find.
            if (folder == null)
            {
                string osk = FindFirst("*.osk");
                if (osk != null) folder = ArchiveExtractor.Extract(osk, "osu_skins");
            }

            if (folder != null) Skin.Current = Skin.Load(folder);
        }

        private static string FindSkinFolder()
        {
            foreach (string root in CandidateRoots())
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                try
                {
                    var inis = Directory.GetFiles(root, "skin.ini", SearchOption.AllDirectories);
                    if (inis.Length > 0)
                    {
                        System.Array.Sort(inis);
                        return Path.GetDirectoryName(inis[0]);
                    }
                }
                catch { /* skip unreadable roots */ }
            }
            return null;
        }

        private static string FindFirst(string pattern)
        {
            foreach (string root in CandidateRoots())
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                try
                {
                    var files = Directory.GetFiles(root, pattern, SearchOption.AllDirectories);
                    if (files.Length > 0) { System.Array.Sort(files); return files[0]; }
                }
                catch { /* skip unreadable roots */ }
            }
            return null;
        }

        private static IEnumerable<string> CandidateRoots()
        {
            yield return Application.persistentDataPath;
            yield return Application.streamingAssetsPath;
            yield return Application.dataPath;                              // Assets/ in the editor
            yield return Directory.GetParent(Application.dataPath)?.FullName; // project root
        }

        // ----------------------------------------------------------------- GUI (scanning/loading overlay only)

        private void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            _label.normal.textColor = Color.white;
        }

        private void OnGUI()
        {
            if (_state != State.Scanning && _state != State.Loading) return;
            EnsureStyles();

            GUI.color = new Color(0, 0, 0, 0.85f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(40, 30, Screen.width - 80, 44), "osu! 3D", _label);
            GUI.Label(new Rect(40, 130, Screen.width - 80, 200), _statusText, _label);
        }
    }
}
