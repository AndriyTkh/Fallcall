using System.Collections;
using System.Collections.Generic;
using System.IO;
using OsuUnity.Beatmaps;
using UnityEngine;

namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Entry point. Finds .osz beatmaps, shows a difficulty picker, loads the chosen map's audio and
    /// background, then hands off to <see cref="GameManager"/>. Spawns itself automatically on play
    /// so no scene wiring is required — just press Play.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        private enum State { Scanning, Menu, Loading, Playing }
        private State _state = State.Scanning;

        private readonly List<BeatmapEntry> _entries = new List<BeatmapEntry>();
        private string _setName = "";
        private string _statusText = "Scanning for beatmaps...";
        private Vector2 _scroll;
        private GUIStyle _title, _label, _button;
        private GameManager _game;

        private struct BeatmapEntry
        {
            public string Path;
            public string Artist;
            public string Title;
            public string Version;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            if (FindObjectOfType<Bootstrap>() != null) return;
            var go = new GameObject("OsuBootstrap");
            DontDestroyOnLoad(go);
            go.AddComponent<Bootstrap>();
        }

        private void Start() => StartCoroutine(Scan());

        private IEnumerator Scan()
        {
            _state = State.Scanning;
            yield return null;

            string osz = FindFirstOsz();
            if (osz == null)
            {
                _statusText = "No .osz found.\nPlace a beatmap in the Assets folder, StreamingAssets,\n" +
                              "or the persistent data folder, then press Play again.";
                _state = State.Menu;
                yield break;
            }

            _setName = Path.GetFileNameWithoutExtension(osz);
            _statusText = "Extracting " + _setName + " ...";
            yield return null;

            string folder = OszImporter.Extract(osz);
            foreach (string osuPath in OszImporter.FindOsuFiles(folder))
            {
                var meta = QuickMeta(osuPath);
                if (meta.Mode != 0) continue; // standard mode only for now
                _entries.Add(new BeatmapEntry
                {
                    Path = osuPath,
                    Artist = meta.Artist,
                    Title = meta.Title,
                    Version = meta.Version
                });
            }

            _statusText = _entries.Count == 0 ? "No osu!standard difficulties found in this set." : "";
            _state = State.Menu;
        }

        // --- lightweight header scan for the menu (avoids fully processing every difficulty) ---
        private struct Meta { public string Artist, Title, Version; public int Mode; }

        private Meta QuickMeta(string path)
        {
            var m = new Meta { Artist = "", Title = "", Version = Path.GetFileNameWithoutExtension(path) };
            try
            {
                foreach (string raw in File.ReadLines(path))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("Title:")) m.Title = line.Substring(6).Trim();
                    else if (line.StartsWith("Artist:")) m.Artist = line.Substring(7).Trim();
                    else if (line.StartsWith("Version:")) m.Version = line.Substring(8).Trim();
                    else if (line.StartsWith("Mode:")) int.TryParse(line.Substring(5).Trim(), out m.Mode);
                    else if (line.StartsWith("[HitObjects]")) break; // headers are all above this
                }
            }
            catch { /* ignore, fall back to filename */ }
            return m;
        }

        private void Select(BeatmapEntry entry)
        {
            _state = State.Loading;
            _statusText = "Loading " + entry.Version + " ...";
            StartCoroutine(LoadAndPlay(entry));
        }

        private IEnumerator LoadAndPlay(BeatmapEntry entry)
        {
            Beatmap map = BeatmapParser.ParseFile(entry.Path);

            AudioClip clip = null;
            if (!string.IsNullOrEmpty(map.General.AudioFilename))
            {
                string audioPath = Path.Combine(map.Directory, map.General.AudioFilename);
                yield return AssetLoader.LoadAudio(audioPath, c => clip = c);
            }
            if (clip == null)
            {
                _statusText = "Failed to load audio.\nIf this is an .mp3, try converting it to .ogg.\n[Esc] to go back.";
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
            _state = State.Menu;
        }

        // ----------------------------------------------------------------- discovery

        private static string FindFirstOsz()
        {
            foreach (string root in CandidateRoots())
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                try
                {
                    var files = Directory.GetFiles(root, "*.osz", SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        System.Array.Sort(files);
                        return files[0];
                    }
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

        // ----------------------------------------------------------------- GUI

        private void EnsureStyles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold };
            _title.normal.textColor = Color.white;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            _label.normal.textColor = Color.white;
            _button = new GUIStyle(GUI.skin.button) { fontSize = 18, alignment = TextAnchor.MiddleLeft };
        }

        private void OnGUI()
        {
            if (_state == State.Playing) return;
            EnsureStyles();

            GUI.color = new Color(0, 0, 0, 0.85f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(40, 30, Screen.width - 80, 44), "osu! 3D — Song Select", _title);

            if (!string.IsNullOrEmpty(_setName))
                GUI.Label(new Rect(40, 78, Screen.width - 80, 26), "Set: " + _setName, _label);

            if (_state == State.Scanning || _state == State.Loading || _entries.Count == 0)
            {
                GUI.Label(new Rect(40, 130, Screen.width - 80, 200), _statusText, _label);
                return;
            }

            var view = new Rect(40, 120, Screen.width - 80, Screen.height - 170);
            var content = new Rect(0, 0, view.width - 24, _entries.Count * 56);
            _scroll = GUI.BeginScrollView(view, _scroll, content);
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                string label = $"  {e.Artist} - {e.Title}\n  [{e.Version}]";
                if (GUI.Button(new Rect(0, i * 56, content.width, 50), label, _button))
                    Select(e);
            }
            GUI.EndScrollView();
        }
    }
}
