using System;
using OsuUnity.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// INDEX: The pause menu — Continue / Restart / Quit and nothing else. Settings live in the global overlay (Ctrl+O), not in here.
namespace OsuUnity.UI
{
    /// <summary>
    /// The pause menu: three routes — <b>Continue</b>, <b>Restart</b>, <b>Quit</b> — and nothing else
    /// (docs/UI-DESIGN §1.3 single-function clarity). Everything tunable moved to the global
    /// <see cref="SettingsOverlay"/>, which opens from anywhere including here (Ctrl+O) and draws over
    /// this menu — it owns the canvas band above us (<see cref="Util.RenderOrder"/>).
    ///
    /// A canvas rather than IMGUI on purpose: <c>OnGUI</c> paints after every canvas has composited, so
    /// an IMGUI pause menu can only ever sit <i>above</i> the settings overlay, never under it.
    ///
    /// Self-bootstraps and persists (<see cref="DontDestroyOnLoad"/>) like the settings overlay, so
    /// <see cref="GameManager"/> just calls <see cref="Show"/>/<see cref="Hide"/> and owns no lifetime.
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        private static PauseMenu _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_instance != null) return;
            var go = new GameObject("PauseMenu");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PauseMenu>();
        }

        /// <summary>True while the menu is on screen.</summary>
        public static bool IsOpen => _instance != null && _instance._root != null && _instance._root.activeSelf;

        /// <summary>Show the menu, routing its three buttons at the caller's session.</summary>
        public static void Show(Action onContinue, Action onRestart, Action onQuit)
        {
            if (_instance == null) return;
            _instance._onContinue = onContinue;
            _instance._onRestart = onRestart;
            _instance._onQuit = onQuit;
            _instance.ShowInstance();
        }

        /// <summary>Hide the menu (resume, restart, quit — and any session teardown).</summary>
        public static void Hide()
        {
            if (_instance == null || _instance._root == null) return;
            _instance._root.SetActive(false);
        }

        private GameObject _root;
        private Button _continueBtn;
        private Action _onContinue, _onRestart, _onQuit;

        private void Awake()
        {
            _instance = this;
            Build();
        }

        private void OnDestroy() { if (_instance == this) _instance = null; }

        private void ShowInstance()
        {
            _root.SetActive(true);
            // Keyboard-only operability (§1.5): land focus on the primary route.
            if (_continueBtn != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_continueBtn.gameObject);
        }

        // Each button hides the menu itself, so the callbacks stay pure session actions.
        private void Route(Action a)
        {
            Hide();
            a?.Invoke();
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            var canvas = UiKit.CreateCanvas("PauseMenuCanvas", Util.RenderOrder.CanvasPauseMenu);
            _root = canvas.gameObject;
            var rootRect = _root.GetComponent<RectTransform>();

            // Scrim: dims the playfield behind the menu. Not click-away — leaving a pause menu has to be
            // a deliberate choice (§1.1: never drop the player back into a running map by accident).
            UiKit.Scrim(rootRect);

            var panel = UiKit.Panel(rootRect, "Panel");
            var pr = panel.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.pivot = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(420f, 320f);
            pr.anchoredPosition = Vector2.zero;

            var title = UiKit.Label(pr, "Paused", UiTheme.Text.Title, TextAlignmentOptions.Center);
            UiKit.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                         new Vector2(UiTheme.SpaceXL, -68f), new Vector2(-UiTheme.SpaceXL, -UiTheme.SpaceXL));

            // The three routes, stacked. Order is by how often it is wanted: resume, retry, leave.
            var column = UiKit.NewRect("Routes", pr);
            UiKit.Anchor(column, new Vector2(0f, 0f), new Vector2(1f, 1f),
                         new Vector2(UiTheme.SpaceXL, 52f), new Vector2(-UiTheme.SpaceXL, -76f));
            var vlg = column.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = UiTheme.SpaceMD;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            _continueBtn = UiKit.Button(column, "Continue", () => Route(_onContinue), true);
            UiKit.Button(column, "Restart", () => Route(_onRestart), false);
            UiKit.Button(column, "Quit", () => Route(_onQuit), false);

            // Shortcut hints (§1.4 — the shortcut is shown, not hidden in a wiki).
            var hint = UiKit.Label(pr, "[Esc] continue   ·   [R] restart   ·   [Ctrl+O] settings",
                                   UiTheme.Text.Caption, TextAlignmentOptions.Center, UiTheme.TextSecondary);
            UiKit.Anchor(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                         new Vector2(UiTheme.SpaceMD, UiTheme.SpaceLG), new Vector2(-UiTheme.SpaceMD, 44f));

            _root.SetActive(false);
        }
    }
}
