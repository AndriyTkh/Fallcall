using System;
using OsuUnity.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// INDEX: Fallcall persistent navigation toolbar — a toggleable top strip (Ctrl+T) giving the same routes as the main screen from anywhere, plus a shared MenuRoute enum, transient toast, and the UiInput typing-guard helper (U3).
namespace OsuUnity.UI
{
    /// <summary>A top-level UI destination shared by <see cref="MainScreen"/> and <see cref="NavBar"/>.</summary>
    public enum MenuRoute { Home, Play, Browse, Settings, Quit }

    /// <summary>
    /// Shared input helpers for menu shortcuts. <see cref="Typing"/> lets global single-key shortcuts
    /// (B, Enter, …) stand down while the player is typing in a text field, so they don't fire mid-search.
    /// <see cref="Consume"/>/<see cref="Consumed"/> stop one keypress from being acted on twice in the
    /// same frame by two different screens.
    /// </summary>
    public static class UiInput
    {
        /// <summary>True when a uGUI/TMP input field currently holds keyboard focus.</summary>
        public static bool Typing
        {
            get
            {
                var es = EventSystem.current;
                var sel = es != null ? es.currentSelectedGameObject : null;
                if (sel == null) return false;
                var tmp = sel.GetComponent<TMP_InputField>();
                if (tmp != null && tmp.isFocused) return true;
                var legacy = sel.GetComponent<InputField>();
                return legacy != null && legacy.isFocused;
            }
        }

        private static int _consumedFrame = -1;

        /// <summary>
        /// Claim this frame's keypress. A screen that acts on a key and, in doing so, reveals another
        /// screen must call this: <c>Input.GetKeyDown</c> stays true for the rest of the frame, so the
        /// screen just revealed would otherwise see the same press and act on it too (e.g. Esc backing
        /// out of browse to the main screen, which then read that same Esc as Quit).
        /// </summary>
        public static void Consume() => _consumedFrame = Time.frameCount;

        /// <summary>True when another screen already acted on this frame's keypress.</summary>
        public static bool Consumed => _consumedFrame == Time.frameCount;
    }

    /// <summary>
    /// The persistent navigation toolbar (docs/UI-DESIGN §1.4, §4): a thin strip pinned to the top of the
    /// screen giving the same routes as the <see cref="MainScreen"/> — Home / Play / Browse / Settings —
    /// so every feature is reachable from anywhere, not just the menu. Toggleable with <b>Ctrl+T</b>
    /// (lazer's toolbar pattern; the visual is Fallcall's own). Built from the U1 <see cref="UiKit"/>.
    /// Raises <see cref="Navigate"/>; <see cref="Bootstrap"/> owns what each route does and when the bar
    /// is <see cref="Suppressed"/> (hidden during gameplay so it never obstructs the playfield, §1.1).
    /// </summary>
    public sealed class NavBar : MonoBehaviour
    {
        /// <summary>Raised when a toolbar button is clicked.</summary>
        public event Action<MenuRoute> Navigate;

        private const float BarHeight = 44f;

        private GameObject _root;
        private CanvasGroup _group;
        private RectTransform _toast;
        private TMP_Text _toastText;
        private float _toastUntil = -1f;
        private bool _visible = true;

        /// <summary>When true the bar hides itself and ignores its toggle (set during gameplay/loading).</summary>
        public bool Suppressed { get; private set; }

        private void Awake() => Build();

        public void SetSuppressed(bool suppressed)
        {
            Suppressed = suppressed;
            ApplyVisibility();
        }

        /// <summary>Show/hide the bar (the Ctrl+T toggle and Bootstrap both drive this).</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            bool show = _visible && !Suppressed;
            if (_group != null)
            {
                _group.alpha = show ? 1f : 0f;
                _group.interactable = show;
                _group.blocksRaycasts = show;
            }
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            var canvas = UiKit.CreateCanvas("NavBar", Util.RenderOrder.CanvasNavBar);
            _root = canvas.gameObject;
            _group = _root.AddComponent<CanvasGroup>();

            var bar = UiKit.NewRect("Bar", _root.transform);
            UiKit.Anchor(bar, new Vector2(0f, 1f), new Vector2(1f, 1f),
                         new Vector2(0f, -BarHeight), new Vector2(0f, 0f));
            var barBg = bar.gameObject.AddComponent<Image>();
            barBg.color = UiTheme.Surface;

            var div = UiKit.Divider(bar);
            UiKit.Anchor(div.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                         new Vector2(0f, 0f), new Vector2(0f, 1f));

            // Route buttons, laid left-to-right from the left edge.
            var strip = UiKit.NewRect("Strip", bar);
            strip.anchorMin = new Vector2(0f, 0f);
            strip.anchorMax = new Vector2(1f, 1f);
            strip.offsetMin = new Vector2(UiTheme.SpaceSM, 6f);
            strip.offsetMax = new Vector2(-UiTheme.SpaceSM, -6f);
            var hlg = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = UiTheme.SpaceXS;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            AddButton(strip, "Fallcall", MenuRoute.Home);
            AddButton(strip, "Play", MenuRoute.Play);
            AddButton(strip, "Browse", MenuRoute.Browse);
            AddButton(strip, "Settings", MenuRoute.Settings);

            BuildToast(bar);
            ApplyVisibility();
        }

        private void AddButton(Transform strip, string label, MenuRoute route)
        {
            var btn = UiKit.Button(strip, label, () => Navigate?.Invoke(route),
                                   primary: false, UiTheme.Text.Label);
            var le = btn.GetComponent<LayoutElement>();
            le.minWidth = 84f;
            le.preferredWidth = 96f;
            le.minHeight = BarHeight - 12f;
            le.preferredHeight = BarHeight - 12f;
        }

        // A transient message pinned under the bar — used for routes that aren't wired yet (e.g. the
        // Settings overlay lands in U2) so the click still gives certain feedback (§1.3) instead of a
        // dead button. Fades out after a couple of seconds.
        private void BuildToast(Transform bar)
        {
            _toast = UiKit.NewRect("Toast", _root.transform);
            _toast.anchorMin = new Vector2(0.5f, 1f);
            _toast.anchorMax = new Vector2(0.5f, 1f);
            _toast.pivot = new Vector2(0.5f, 1f);
            _toast.sizeDelta = new Vector2(420f, 34f);
            _toast.anchoredPosition = new Vector2(0f, -(BarHeight + UiTheme.SpaceSM));
            var bg = _toast.gameObject.AddComponent<Image>();
            bg.sprite = UiTheme.RoundedRect(UiTheme.RadiusMD);
            bg.type = Image.Type.Sliced;
            bg.color = UiTheme.SurfaceRaised;
            _toastText = UiKit.Label(_toast, "", UiTheme.Text.Label,
                                     TextAlignmentOptions.Center, UiTheme.TextPrimary);
            UiKit.Stretch(_toastText.rectTransform, UiTheme.SpaceMD, 0f);
            _toast.gameObject.SetActive(false);
        }

        /// <summary>Flash a short status message under the toolbar (2.2 s).</summary>
        public void Toast(string message)
        {
            if (_toastText == null) return;
            _toastText.text = message;
            _toast.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + 2.2f;
        }

        // ------------------------------------------------------------------ shortcuts / lifetime

        private void Update()
        {
            if (_toastUntil > 0f && Time.unscaledTime >= _toastUntil)
            {
                _toast.gameObject.SetActive(false);
                _toastUntil = -1f;
            }

            if (Suppressed || UiInput.Typing) return;

            // Ctrl+T toggles the toolbar (lazer's toggle-toolbar shortcut; our binding).
            if (Input.GetKeyDown(KeyCode.T) &&
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
                SetVisible(!_visible);
        }
    }
}
