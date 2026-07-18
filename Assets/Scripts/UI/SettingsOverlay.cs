using System;
using System.Collections.Generic;
using OsuUnity.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// INDEX: Fallcall settings overlay (U2) — slide-over panel openable anywhere (Ctrl+O), sidebar sections + search-with-highlight, live-applying sliders/toggles with per-setting + per-section reset, and a rebindable keybinds section with conflict detection. Backed by GameSettings; built from the U1 UiKit.
namespace OsuUnity.UI
{
    /// <summary>
    /// The global settings overlay (docs/UI-DESIGN §2.1). A slide-over panel that opens from anywhere
    /// via the <c>open_settings</c> keybind (default <b>Ctrl+O</b>) or the nav routes (it registers
    /// <see cref="Bootstrap.OpenSettingsHook"/>). Sidebar sections + search-as-you-type with match
    /// highlight; every control <b>live-applies</b> (pushes to the running session's audio/dim/camera and
    /// updates <see cref="GameSettings"/>) and has its own reset, plus a per-section reset. The Input
    /// section rebinds keys with conflict detection (never silently double-binds).
    ///
    /// Self-bootstraps (no scene wiring) and persists via <see cref="DontDestroyOnLoad"/>, so it stays
    /// out of <see cref="Bootstrap"/>'s file (owned by U3). Built entirely from the U1 <see cref="UiKit"/>.
    /// This is the <i>only</i> settings surface: the pause menu's IMGUI settings window is gone, and
    /// <see cref="PauseMenu"/> is three routes plus a pointer to the Ctrl+O shortcut.
    /// </summary>
    public sealed class SettingsOverlay : MonoBehaviour
    {
        private static readonly string[] Sections = { "Gameplay", "Visuals / Camera", "Audio", "Skin", "Input", "UI" };

        private static SettingsOverlay _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_instance != null) return;
            var go = new GameObject("SettingsOverlay");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SettingsOverlay>();
        }

        /// <summary>Open the overlay from anywhere (also wired to the menu's Settings route).</summary>
        public static void OpenStatic() { if (_instance != null) _instance.Open(); }

        /// <summary>
        /// True while the overlay is up. Surfaces underneath check this to stand down: the overlay owns
        /// the keyboard while open (it keeps a text field focused), and it outranks every other canvas,
        /// so anything below it must neither read keys nor paint over it (see <see cref="GameManager"/>).
        /// </summary>
        public static bool IsOpen => _instance != null && _instance._open;

        private sealed class Entry
        {
            public int Section;
            public GameObject Go;
            public TMP_Text Caption;
            public string Plain;
            public Action Reset;
            public Action SetFromModel;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly GameObject[] _headers = new GameObject[Sections.Length];

        private GameObject _root;
        private RectTransform _panel;
        private Image _scrim;
        private RectTransform _content;
        private ScrollRect _scroll;
        private TMP_InputField _searchInput;
        private TMP_Text _statusText;

        private int _active;
        private string _search = "";
        private bool _open;
        private float _slide;            // 0 = hidden, 1 = shown
        private float _panelWidth = 780f;
        private float _margin = 24f;

        private string _rebinding;       // action id currently capturing, or null
        private TMP_Text _rebindLabel;
        private static KeyCode[] _captureKeys;

        private void Awake()
        {
            _instance = this;
            GameSettings.Load(null);            // no-op if already loaded; guarantees keybinds populated
            Bootstrap.OpenSettingsHook = Open;  // menu "Settings" route opens us
            BuildCaptureKeys();
            Build();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (Bootstrap.OpenSettingsHook == (Action)Open) Bootstrap.OpenSettingsHook = null;
        }

        // ------------------------------------------------------------------ open / close

        public void Open()
        {
            _open = true;
            if (_root != null && !_root.activeSelf) _root.SetActive(true);
            GameSettings.Load(null);
            foreach (var e in _entries) e.SetFromModel?.Invoke();
            RefreshVisibility();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
            SetStatus("");
            FocusSearch();
        }

        public void Close()
        {
            _open = false;
            _rebinding = null;
            _rebindLabel = null;
            GameSettings.Save();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        public void Toggle() { if (_open) Close(); else Open(); }

        // ------------------------------------------------------------------ update loop

        private void Update()
        {
            // Global open shortcut (open-only; Esc / scrim / Close dismiss). Stands down while typing so
            // it never fires mid-search; harmless if the main screen also opens us the same frame.
            if (!_open && _rebinding == null && !UiInput.Typing && GameSettings.GetBind("open_settings").DownThisFrame())
                Open();

            if (_open)
            {
                if (_rebinding != null)
                {
                    HandleRebindCapture();
                }
                else if (Input.GetKeyDown(KeyCode.Escape))
                {
                    Close();
                    // Close() drops the text focus that was keeping the shortcuts below us down, so claim
                    // this Esc or the screen we just uncovered reads it too (main screen → Quit).
                    UiInput.Consume();
                }
                else
                {
                    // Keep a text field focused while open so the shared menu shortcuts (Esc→Quit,
                    // B→Browse, Ctrl+T…) stand down (they honour UiInput.Typing) and can't fire under us.
                    KeepTypingFocus();
                }
            }

            AnimateSlide();
        }

        private void KeepTypingFocus()
        {
            var es = EventSystem.current;
            if (es == null || _searchInput == null) return;
            var sel = es.currentSelectedGameObject;
            bool onInput = sel != null && sel.GetComponent<TMP_InputField>() != null;
            if (!onInput) es.SetSelectedGameObject(_searchInput.gameObject);
        }

        private void FocusSearch()
        {
            if (EventSystem.current != null && _searchInput != null)
                EventSystem.current.SetSelectedGameObject(_searchInput.gameObject);
        }

        private void AnimateSlide()
        {
            float target = _open ? 1f : 0f;
            float dur = Mathf.Max(0.0001f, UiTheme.DurSlow);
            _slide = Mathf.MoveTowards(_slide, target, Time.unscaledDeltaTime / dur);

            if (_slide <= 0f && !_open)
            {
                if (_root.activeSelf) _root.SetActive(false);
                return;
            }
            if (!_root.activeSelf) _root.SetActive(true);

            float e = UiTheme.Ease.Evaluate(_slide);
            float shownX = -_margin;
            float hiddenX = _panelWidth + 40f;
            _panel.anchoredPosition = new Vector2(Mathf.Lerp(hiddenX, shownX, e), 0f);
            if (_scrim != null) _scrim.color = UiTheme.WithAlpha(Color.black, 0.72f * e);
        }

        // ------------------------------------------------------------------ live-apply

        // The overlay is global, so it pushes the not-auto-live settings to whatever session objects
        // currently exist (audio/dim/camera). Settings whose consumers read GameSettings each frame
        // (HudScale, FollowPointScale, UiScale) apply on their own. Restart-only fields take effect on R.
        private void AfterChange()
        {
            var gm = FindObjectOfType<GameManager>();
            if (gm != null) { var a = gm.GetComponent<AudioSource>(); if (a != null) a.volume = GameSettings.MusicVolume; }
            var hs = FindObjectOfType<HitSoundPlayer>(); if (hs != null) hs.Volume = GameSettings.HitSoundVolume;
            var dim = FindObjectOfType<BackgroundDim>(); if (dim != null) dim.SetDim(GameSettings.BackgroundDim);
            var cam = FindObjectOfType<FirstPersonCamera>(); if (cam != null) cam.Sensitivity = GameSettings.LookSensitivity;
            GameSettings.RaiseChanged();
        }

        // ------------------------------------------------------------------ search / visibility

        private void RefreshVisibility()
        {
            bool searching = !string.IsNullOrEmpty(_search);
            string q = searching ? _search.ToLowerInvariant() : null;
            var anyVisible = new bool[Sections.Length];

            foreach (var e in _entries)
            {
                bool vis = searching ? e.Plain.ToLowerInvariant().Contains(q) : e.Section == _active;
                e.Go.SetActive(vis);
                if (vis)
                {
                    anyVisible[e.Section] = true;
                    Highlight(e, searching ? q : null);
                }
            }

            for (int i = 0; i < _headers.Length; i++)
                if (_headers[i] != null)
                    _headers[i].SetActive(anyVisible[i] && (searching || i == _active));
        }

        private void Highlight(Entry e, string q)
        {
            if (e.Caption == null) return;
            if (string.IsNullOrEmpty(q)) { e.Caption.text = e.Plain; return; }
            int i = e.Plain.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal);
            if (i < 0) { e.Caption.text = e.Plain; return; }
            string hex = ColorUtility.ToHtmlStringRGB(UiTheme.Focus);
            e.Caption.text = e.Plain.Substring(0, i)
                + $"<color=#{hex}>" + e.Plain.Substring(i, q.Length) + "</color>"
                + e.Plain.Substring(i + q.Length);
        }

        private void SelectSection(int s)
        {
            _active = s;
            _search = "";
            if (_searchInput != null) _searchInput.SetTextWithoutNotify("");
            RefreshVisibility();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        private void ResetSection()
        {
            foreach (var e in _entries) if (e.Section == _active) e.Reset?.Invoke();
            SetStatus($"Reset “{Sections[_active]}” to defaults");
        }

        // ------------------------------------------------------------------ keybind rebinding

        private static void BuildCaptureKeys()
        {
            if (_captureKeys != null) return;
            var list = new List<KeyCode>();
            foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
            {
                if (kc == KeyCode.None) continue;
                if (kc >= KeyCode.Mouse0 && kc <= KeyCode.Mouse6) continue;
                if (IsModifier(kc)) continue;
                list.Add(kc);
            }
            _captureKeys = list.ToArray();
        }

        private static bool IsModifier(KeyCode kc)
            => kc == KeyCode.LeftControl || kc == KeyCode.RightControl
            || kc == KeyCode.LeftShift || kc == KeyCode.RightShift
            || kc == KeyCode.LeftAlt || kc == KeyCode.RightAlt
            || kc == KeyCode.LeftCommand || kc == KeyCode.RightCommand;

        private void BeginRebind(string id, TMP_Text label)
        {
            _rebinding = id;
            _rebindLabel = label;
            if (label != null) label.text = "Press a key…";
            SetStatus("Press a key to bind  ·  Esc cancels");
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        private void HandleRebindCapture()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                RefreshBindLabel(_rebindLabel, _rebinding);
                SetStatus("Rebind cancelled");
                _rebinding = null; _rebindLabel = null;
                UiInput.Consume();   // cancelling a rebind must not also reach the menus below
                return;
            }
            if (!Input.anyKeyDown) return;

            for (int i = 0; i < _captureKeys.Length; i++)
            {
                var kc = _captureKeys[i];
                if (!Input.GetKeyDown(kc)) continue;

                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                var kb = new GameSettings.Keybind(kc, ctrl, shift, alt);

                string conflict = FindConflict(_rebinding, kb);
                if (conflict != null)
                {
                    // Never silently double-bind: reject and report (docs/UI-DESIGN §2.1).
                    RefreshBindLabel(_rebindLabel, _rebinding);
                    SetStatus($"“{kb.Display()}” already bound to {conflict}");
                }
                else
                {
                    GameSettings.Keybinds[_rebinding] = kb;
                    RefreshBindLabel(_rebindLabel, _rebinding);
                    SetStatus("Bound " + kb.Display());
                    AfterChange();
                }
                _rebinding = null; _rebindLabel = null;
                return;
            }
        }

        private static string FindConflict(string id, GameSettings.Keybind kb)
        {
            foreach (var d in GameSettings.KeybindDefs)
            {
                if (d.Id == id) continue;
                if (GameSettings.GetBind(d.Id).SameChord(kb)) return d.Label;
            }
            return null;
        }

        private void RefreshBindLabel(TMP_Text label, string id)
        {
            if (label != null) label.text = GameSettings.GetBind(id).Display();
        }

        private void SetStatus(string s) { if (_statusText != null) _statusText.text = s; }

        // ================================================================== construction

        private void Build()
        {
            var canvas = UiKit.CreateCanvas("SettingsOverlayCanvas", Util.RenderOrder.CanvasSettings);
            _root = canvas.gameObject;
            var rootRect = _root.GetComponent<RectTransform>();

            // click-away scrim (also the backdrop that fades with the slide)
            _scrim = UiKit.Scrim(rootRect);
            _scrim.color = UiTheme.WithAlpha(Color.black, 0f);
            var scrimBtn = _scrim.gameObject.AddComponent<Button>();
            scrimBtn.transition = Selectable.Transition.None;
            scrimBtn.onClick.AddListener(Close);

            // right-anchored slide panel
            var panelImg = UiKit.Panel(rootRect, "Panel");
            _panel = panelImg.rectTransform;
            _panel.anchorMin = new Vector2(1f, 0f);
            _panel.anchorMax = new Vector2(1f, 1f);
            _panel.pivot = new Vector2(1f, 0.5f);
            _panel.sizeDelta = new Vector2(_panelWidth, -2f * _margin);
            _panel.anchoredPosition = new Vector2(_panelWidth + 40f, 0f);

            // title + close
            var title = UiKit.Label(_panel, "Settings", UiTheme.Text.Title, TextAlignmentOptions.Left);
            UiKit.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -60f), new Vector2(-64f, -14f));
            var close = UiKit.Button(_panel, "✕", Close, false);
            var closeRect = (RectTransform)close.transform;
            close.GetComponent<LayoutElement>().ignoreLayout = true;
            UiKit.Anchor(closeRect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-52f, -52f), new Vector2(-16f, -16f));

            // sidebar (section selector)
            var sidebar = UiKit.NewRect("Sidebar", _panel);
            UiKit.Anchor(sidebar, new Vector2(0f, 0f), new Vector2(0.26f, 1f), new Vector2(16f, 66f), new Vector2(-8f, -68f));
            var svlg = sidebar.gameObject.AddComponent<VerticalLayoutGroup>();
            svlg.spacing = 6f;
            svlg.childForceExpandHeight = false;
            svlg.childControlHeight = true;
            svlg.childControlWidth = true;
            svlg.childForceExpandWidth = true;
            for (int s = 0; s < Sections.Length; s++)
            {
                int captured = s;
                UiKit.Button(sidebar, Sections[s], () => SelectSection(captured), false, UiTheme.Text.Label);
            }

            // search field (top of the content column)
            _searchInput = UiKit.SearchField(_panel, "Search settings…", v => { _search = v; RefreshVisibility(); });
            UiKit.Anchor((RectTransform)_searchInput.transform.parent, // the SearchField container
                new Vector2(0.26f, 1f), new Vector2(1f, 1f), new Vector2(8f, -108f), new Vector2(-16f, -66f));

            // scrolling content
            BuildScroll(_panel, out _content);
            UiKit.Anchor((RectTransform)_scroll.transform, new Vector2(0.26f, 0f), new Vector2(1f, 1f),
                new Vector2(8f, 64f), new Vector2(-16f, -116f));

            // footer: status + reset-section
            _statusText = UiKit.Label(_panel, "", UiTheme.Text.Label, TextAlignmentOptions.Left, UiTheme.TextSecondary);
            UiKit.Anchor(_statusText.rectTransform, new Vector2(0.26f, 0f), new Vector2(1f, 0f), new Vector2(8f, 16f), new Vector2(-150f, 52f));
            var resetSection = UiKit.Button(_panel, "Reset section", ResetSection, false, UiTheme.Text.Label);
            var rsRect = (RectTransform)resetSection.transform;
            resetSection.GetComponent<LayoutElement>().ignoreLayout = true;
            UiKit.Anchor(rsRect, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-146f, 14f), new Vector2(-16f, 50f));

            BuildSections();

            _root.SetActive(false);
        }

        private void BuildScroll(Transform parent, out RectTransform content)
        {
            var scrollGO = UiKit.NewRect("Scroll", parent);
            _scroll = scrollGO.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.scrollSensitivity = 26f;

            var viewport = UiKit.NewRect("Viewport", scrollGO);
            UiKit.Stretch(viewport);
            viewport.gameObject.AddComponent<Image>().color = Color.white;
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            content = UiKit.NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;

            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;

            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll.viewport = viewport;
            _scroll.content = content;
        }

        // ------------------------------------------------------------------ section content

        private void BuildSections()
        {
            // 0 — Gameplay
            Header(0);
            AddToggle(0, "Autoplay (map plays itself)", true, false, () => GameSettings.Autoplay, v => GameSettings.Autoplay = v);
            AddToggle(0, "No Fail (HP can't end the map)", false, false, () => GameSettings.NoFail, v => GameSettings.NoFail = v);
            AddSlider(0, "Follow-point size", false, 0.3f, 3f, 1f, () => GameSettings.FollowPointScale, v => GameSettings.FollowPointScale = v);
            AddToggle(0, "Follow points", true, true, () => GameSettings.ShowFollowPoints, v => GameSettings.ShowFollowPoints = v);
            AddToggle(0, "Default follow-point arrow", true, false, () => GameSettings.DefaultFollowPoint, v => GameSettings.DefaultFollowPoint = v);
            AddSlider(0, "Cursor size", true, 0.5f, 2f, 1f, () => GameSettings.CursorSize, v => GameSettings.CursorSize = v);
            AddSlider(0, "Cursor hitbox (osu! px)", true, 0f, 3f, 1f, () => GameSettings.CursorHitboxOsu, v => GameSettings.CursorHitboxOsu = v);
            AddToggle(0, "Cursor trail", false, true, () => GameSettings.CursorTrail, v => GameSettings.CursorTrail = v);
            AddSlider(0, "Cursor trail size", false, 0.2f, 2f, 1f, () => GameSettings.CursorTrailSize, v => GameSettings.CursorTrailSize = v);
            AddSlider(0, "Cursor trail length", false, 0.1f, 3f, 1f, () => GameSettings.CursorTrailLength, v => GameSettings.CursorTrailLength = v);
            AddSlider(0, "Skip: shortest gap shown (s)", false, 2f, 20f, 5f,
                      () => GameSettings.BreakMinGapMs / 1000f, v => GameSettings.BreakMinGapMs = v * 1000f, "0.0");
            AddSlider(0, "Skip: lead before next note (s)", false, 0.25f, 4f, 2f,
                      () => GameSettings.BreakSkipLeadMs / 1000f, v => GameSettings.BreakSkipLeadMs = v * 1000f, "0.00");
            AddMode(0);

            // 1 — Visuals / Camera
            Header(1);
            AddSlider(1, "Background dim", false, 0f, 1f, 0.3f, () => GameSettings.BackgroundDim, v => GameSettings.BackgroundDim = v, "0%");
            AddSlider(1, "HUD size", false, 0.4f, 2.5f, 1f, () => GameSettings.HudScale, v => GameSettings.HudScale = v);
            AddSlider(1, "Look sensitivity", false, 0.5f, 10f, 1.4f, () => GameSettings.LookSensitivity, v => GameSettings.LookSensitivity = v, "0.0");
            AddToggle(1, "Background video", true, true, () => GameSettings.EnableVideo, v => GameSettings.EnableVideo = v);
            AddSlider(1, "Projection distance", true, 1f, 8f, 3.5f, () => GameSettings.ProjectionDistance, v => GameSettings.ProjectionDistance = v, "0.0");
            AddSlider(1, "Playfield pixel scale", true, 0.005f, 0.03f, 0.0135f, () => GameSettings.PixelScale, v => GameSettings.PixelScale = v, "0.000");
            AddSlider(1, "Sphere chunk width°", true, 40f, 180f, 120f, () => GameSettings.ChunkHDegrees, v => GameSettings.ChunkHDegrees = v, "0");
            AddSlider(1, "Sphere chunk height°", true, 30f, 140f, 90f, () => GameSettings.ChunkVDegrees, v => GameSettings.ChunkVDegrees = v, "0");
            AddToggle(1, "Curved projection", true, true, () => GameSettings.Curved, v => GameSettings.Curved = v);
            AddSlider(1, "Falling radius", false, 3f, 15f, 7f, () => GameSettings.FallingRadius, v => GameSettings.FallingRadius = v, "0.0");
            AddSlider(1, "Falling max tilt°", false, 0f, 45f, 18f, () => GameSettings.FallingMaxTiltDeg, v => GameSettings.FallingMaxTiltDeg = v, "0");
            AddSlider(1, "Falling zoom", false, 0.5f, 1.5f, 0.9f, () => GameSettings.FallingZoom, v => GameSettings.FallingZoom = v);

            // Ortho2D dynamic click-group zoom. These were the pause menu's IMGUI sliders; the pause menu
            // is three routes now (docs/UI-DESIGN §1.3) and settings live only here.
            AddToggle(1, "Ortho2D: dynamic click-group zoom", false, true, () => GameSettings.OrthoZoom, v => GameSettings.OrthoZoom = v);
            AddSlider(1, "Ortho2D: camera smoothing", false, 0.02f, 1f, 0.22f, () => GameSettings.OrthoZoomSmooth, v => GameSettings.OrthoZoomSmooth = v);
            AddSlider(1, "Ortho2D: zoom margin", false, 0f, 6f, 1.6f, () => GameSettings.OrthoZoomMargin, v => GameSettings.OrthoZoomMargin = v, "0.0");
            AddSlider(1, "Ortho2D: pan overshoot", false, 0f, 0.6f, 0f, () => GameSettings.OrthoOvershoot, v => GameSettings.OrthoOvershoot = v);
            AddSlider(1, "Ortho2D: kiai snap", false, 0.1f, 1f, 0.5f, () => GameSettings.OrthoKiaiSmoothMul, v => GameSettings.OrthoKiaiSmoothMul = v);
            AddSlider(1, "Ortho2D: kiai zoom", false, 0.5f, 1f, 0.82f, () => GameSettings.OrthoKiaiZoomMul, v => GameSettings.OrthoKiaiZoomMul = v);
            AddSlider(1, "Ortho2D: lookahead (ms)", true, 0f, 1500f, 400f, () => GameSettings.OrthoLookaheadMs, v => GameSettings.OrthoLookaheadMs = v, "0");
            AddSlider(1, "Ortho2D: grouping aggressiveness", true, 0f, 1f, 0.3f, () => GameSettings.OrthoAggressiveness, v => GameSettings.OrthoAggressiveness = v);

            // 2 — Audio
            Header(2);
            AddSlider(2, "Music volume", false, 0f, 1f, 0.2f, () => GameSettings.MusicVolume, v => GameSettings.MusicVolume = v, "0%");
            AddSlider(2, "Hit-sound volume", false, 0f, 1f, 0.15f, () => GameSettings.HitSoundVolume, v => GameSettings.HitSoundVolume = v, "0%");

            // 3 — Skin
            Header(3);
            AddInfo(3, "Skins are auto-detected from your osu! skins and applied per session. Dedicated skin selection lands with the song-select work (U4).");

            // 4 — Input (rebindable)
            Header(4);
            AddInfo(4, "Rebind actions below. Gameplay keys are read directly by the game for now — rebinding is fully wired for the settings shortcut; other actions follow in a later pass.");
            foreach (var d in GameSettings.KeybindDefs) AddKeybind(4, d);

            // 5 — UI
            Header(5);
            AddSlider(5, "UI scale", false, 0.7f, 1.6f, 1f, () => GameSettings.UiScale, v => GameSettings.UiScale = v);
            AddInfo(5, "Editable UI theme colours are planned here (needs a colour-picker widget) — deferred so palette decisions are made against complete layouts.");
        }

        private void Header(int section)
        {
            var h = UiKit.SectionHeader(_content, Sections[section]);
            _headers[section] = h.gameObject;
        }

        // ------------------------------------------------------------------ row builders

        private Entry NewRow(int section, string label, out RectTransform host)
        {
            var row = UiKit.NewRect("Row", _content);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 46f;
            le.preferredHeight = 46f;

            var caption = UiKit.Label(row, label, UiTheme.Text.Body, TextAlignmentOptions.Left, UiTheme.TextSecondary);
            UiKit.Anchor(caption.rectTransform, new Vector2(0f, 0f), new Vector2(0.44f, 1f), new Vector2(6f, 0f), new Vector2(-8f, 0f));

            host = UiKit.NewRect("Control", row);
            UiKit.Anchor(host, new Vector2(0.44f, 0f), new Vector2(1f, 1f), new Vector2(0f, 4f), new Vector2(-4f, -4f));

            var e = new Entry { Section = section, Go = row.gameObject, Caption = caption, Plain = label };
            _entries.Add(e);
            return e;
        }

        // A control host split into a control area (left) and a small reset icon (right).
        private RectTransform SplitReset(RectTransform host, Action reset)
        {
            var inner = UiKit.NewRect("Inner", host);
            UiKit.Anchor(inner, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(-34f, 0f));
            var rb = UiKit.Button(host, "↺", reset, false);
            rb.GetComponent<LayoutElement>().ignoreLayout = true;
            UiKit.Anchor((RectTransform)rb.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-30f, 2f), new Vector2(0f, -2f));
            return inner;
        }

        private static void Fill(Component c, RectTransform host) => UiKit.Stretch((RectTransform)c.transform);

        private void AddSlider(int section, string label, bool restart, float min, float max, float def,
            Func<float> get, Action<float> set, string fmt = "0.##")
        {
            var e = NewRow(section, label + (restart ? "   (restart)" : ""), out var host);
            var slider = UiKit.Slider(host, min, max, Mathf.Clamp(get(), min, max), def, v => { set(v); AfterChange(); }, fmt);
            Fill(slider, host);
            e.Reset = () => slider.ResetToDefault();
            e.SetFromModel = () => slider.SetValueWithoutNotify(Mathf.Clamp(get(), min, max));
        }

        private void AddToggle(int section, string label, bool restart, bool def,
            Func<bool> get, Action<bool> set)
        {
            var e = NewRow(section, label + (restart ? "   (restart)" : ""), out var host);
            Toggle tg = null;
            var inner = SplitReset(host, () => { if (tg != null) tg.isOn = def; });
            tg = UiKit.Toggle(inner, "", get(), v => { set(v); AfterChange(); });
            Fill(tg, inner);
            e.Reset = () => { if (tg != null) tg.isOn = def; };
            e.SetFromModel = () => { if (tg != null) tg.SetIsOnWithoutNotify(get()); };
        }

        private void AddMode(int section)
        {
            var e = NewRow(section, "Start view mode   (restart)", out var host);
            Button btn = null; TMP_Text lbl = null;
            var inner = SplitReset(host, () =>
            {
                GameSettings.StartMode = ViewMode.Sphere;
                if (lbl != null) lbl.text = ModeName(GameSettings.StartMode);
                AfterChange();
            });
            btn = UiKit.Button(inner, ModeName(GameSettings.StartMode), () =>
            {
                GameSettings.StartMode = NextMode(GameSettings.StartMode);
                if (lbl != null) lbl.text = ModeName(GameSettings.StartMode);
                AfterChange();
            }, false);
            Fill(btn, inner);
            lbl = btn.GetComponentInChildren<TMP_Text>();
            e.Reset = () => { GameSettings.StartMode = ViewMode.Sphere; if (lbl != null) lbl.text = ModeName(GameSettings.StartMode); AfterChange(); };
            e.SetFromModel = () => { if (lbl != null) lbl.text = ModeName(GameSettings.StartMode); };
        }

        private void AddKeybind(int section, GameSettings.KeybindDef def)
        {
            var e = NewRow(section, def.Label, out var host);
            Button btn = null; TMP_Text lbl = null;
            var inner = SplitReset(host, () =>
            {
                GameSettings.Keybinds[def.Id] = def.Default;
                RefreshBindLabel(lbl, def.Id);
                AfterChange();
            });
            btn = UiKit.Button(inner, GameSettings.GetBind(def.Id).Display(), () => BeginRebind(def.Id, lbl), false);
            Fill(btn, inner);
            lbl = btn.GetComponentInChildren<TMP_Text>();
            e.Reset = () => { GameSettings.Keybinds[def.Id] = def.Default; RefreshBindLabel(lbl, def.Id); AfterChange(); };
            e.SetFromModel = () => RefreshBindLabel(lbl, def.Id);
        }

        private void AddInfo(int section, string text)
        {
            var row = UiKit.NewRect("Info", _content);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 52f;
            le.preferredHeight = 52f;
            var lbl = UiKit.Label(row, text, UiTheme.Text.Label, TextAlignmentOptions.TopLeft, UiTheme.TextSecondary);
            UiKit.Stretch(lbl.rectTransform, 6f, 4f);
            _entries.Add(new Entry { Section = section, Go = row.gameObject, Caption = lbl, Plain = text });
        }

        private static string ModeName(ViewMode m) => m switch
        {
            ViewMode.Sphere => "Sphere (3D)",
            ViewMode.Ortho2D => "Ortho 2D",
            ViewMode.Falling => "Falling",
            _ => m.ToString(),
        };

        private static ViewMode NextMode(ViewMode m) => m switch
        {
            ViewMode.Sphere => ViewMode.Ortho2D,
            ViewMode.Ortho2D => ViewMode.Falling,
            _ => ViewMode.Sphere,
        };
    }
}
