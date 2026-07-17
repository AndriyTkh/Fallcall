using System;
using System.Collections.Generic;
using OsuUnity.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// INDEX: Fallcall main screen — title + Play/Browse/Settings/Quit entries, each showing its keyboard shortcut, with graceful first-run (no maps → Browse, not a dead Play). Routes via the NavBar-shared MenuRoute (U3).
namespace OsuUnity.UI
{
    /// <summary>
    /// The main menu surface (docs/UI-DESIGN §2.4). A full-screen, screen-space canvas built from the
    /// U1 <see cref="UiKit"/>: title, then the top-level entries — <b>Play</b>, <b>Browse</b>,
    /// <b>Settings</b>, <b>Quit</b> — each with its keyboard shortcut shown as a small hint beneath it
    /// (fixes osu!'s undiscoverable-shortcut weakness, §1.4). Fully keyboard-operable (arrows/Tab move
    /// focus, Enter confirms, plus the per-entry global shortcuts). On first run with no beatmaps, Play
    /// is disabled and Browse is emphasised so there is no dead end (§2.4). Owns no routing logic — it
    /// raises <see cref="Navigate"/>; <see cref="Bootstrap"/> decides what each route does.
    /// </summary>
    public sealed class MainScreen : MonoBehaviour
    {
        /// <summary>Raised when the player picks a top-level destination (click or shortcut).</summary>
        public event Action<MenuRoute> Navigate;

        private GameObject _root;
        private readonly List<Entry> _entries = new List<Entry>();
        private Entry _play;
        private Entry _browse;
        private TMP_Text _firstRunNote;
        private bool _hasBeatmaps = true;

        private struct Entry
        {
            public MenuRoute Route;
            public Button Button;
            public CanvasGroup Group;   // dims + disables the whole entry when unavailable
            public TMP_Text Hint;
        }

        private void Awake() => Build();

        public void Show()
        {
            _root.SetActive(true);
            // Give keyboard nav a starting point: the primary, available entry.
            var start = (_hasBeatmaps ? _play : _browse).Button;
            if (start != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(start.gameObject);
        }

        public void Hide() => _root.SetActive(false);
        public bool IsVisible => _root != null && _root.activeSelf;

        /// <summary>
        /// First-run handling (§2.4): with no imported beatmaps, disable Play and point the player at
        /// Browse instead of a dead button. Re-emphasises Play once maps exist.
        /// </summary>
        public void SetHasBeatmaps(bool has)
        {
            _hasBeatmaps = has;
            SetEntryEnabled(_play, has);
            SetPrimary(_play, has);
            SetPrimary(_browse, !has);
            if (_firstRunNote != null)
            {
                _firstRunNote.gameObject.SetActive(!has);
                _firstRunNote.text = "No beatmaps yet — press <b>Browse</b> to download some.";
            }
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            var canvas = UiKit.CreateCanvas("MainScreen", Util.RenderOrder.CanvasMainScreen);
            _root = canvas.gameObject;

            // Ambient backdrop (dimmed base colour; a beatmap image can layer in later — §2.4).
            var bg = UiKit.NewRect("Backdrop", _root.transform);
            UiKit.Stretch(bg);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.color = UiTheme.Background;

            // Title (top-left, large — the thing you came for is biggest, §1.6).
            var title = UiKit.Label(_root.transform, "FALLCALL", UiTheme.Text.Display,
                                    TextAlignmentOptions.BottomLeft, UiTheme.TextPrimary);
            UiKit.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                         new Vector2(UiTheme.SpaceXXL, -140f), new Vector2(-UiTheme.SpaceXXL, -80f));
            var subtitle = UiKit.Label(_root.transform, "osu! reimagined — falling through geometric space",
                                       UiTheme.Text.Body, TextAlignmentOptions.TopLeft, UiTheme.TextSecondary);
            UiKit.Anchor(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                         new Vector2(UiTheme.SpaceXXL, -172f), new Vector2(-UiTheme.SpaceXXL, -140f));

            // Entry column — left-anchored, vertically centred. (Screen centre stays clear per §3;
            // here there is no playfield, but a left rail matches the persistent-nav mental model.)
            var col = UiKit.NewRect("Entries", _root.transform);
            col.anchorMin = new Vector2(0f, 0.5f);
            col.anchorMax = new Vector2(0f, 0.5f);
            col.pivot = new Vector2(0f, 0.5f);
            col.sizeDelta = new Vector2(360f, 340f);
            col.anchoredPosition = new Vector2(UiTheme.SpaceXXL, -20f);
            var vlg = col.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = UiTheme.SpaceLG;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            AddEntry(col, "Play", "Enter", MenuRoute.Play, primary: true);
            AddEntry(col, "Browse", "B", MenuRoute.Browse, primary: false);
            AddEntry(col, "Settings", "Ctrl+O", MenuRoute.Settings, primary: false);
            AddEntry(col, "Quit", "Esc", MenuRoute.Quit, primary: false);
            _play = _entries[0];
            _browse = _entries[1];

            // First-run note (hidden unless SetHasBeatmaps(false)).
            _firstRunNote = UiKit.Label(_root.transform, "", UiTheme.Text.Label,
                                        TextAlignmentOptions.BottomLeft, UiTheme.Accent);
            UiKit.Anchor(_firstRunNote.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                         new Vector2(UiTheme.SpaceXXL, UiTheme.SpaceLG),
                         new Vector2(-UiTheme.SpaceXXL, UiTheme.SpaceLG + 24f));
            _firstRunNote.gameObject.SetActive(false);

            _root.SetActive(false);
        }

        private void AddEntry(Transform col, string label, string shortcut, MenuRoute route, bool primary)
        {
            // Container holds the button and its shortcut hint stacked vertically.
            var container = UiKit.NewRect("Entry_" + route, col);
            var group = container.gameObject.AddComponent<CanvasGroup>();
            var cle = container.gameObject.AddComponent<LayoutElement>();
            cle.preferredHeight = UiTheme.ControlHeight + 20f;
            cle.minHeight = UiTheme.ControlHeight + 20f;
            var cvlg = container.gameObject.AddComponent<VerticalLayoutGroup>();
            cvlg.spacing = 2f;
            cvlg.childControlWidth = true;
            cvlg.childControlHeight = true;
            cvlg.childForceExpandWidth = true;
            cvlg.childForceExpandHeight = false;

            var btn = UiKit.Button(container, label, () => Navigate?.Invoke(route),
                                   primary, UiTheme.Text.Heading);

            var hint = UiKit.Label(container, "[" + shortcut + "]", UiTheme.Text.Caption,
                                   TextAlignmentOptions.Left, UiTheme.TextSecondary);
            var hle = hint.gameObject.AddComponent<LayoutElement>();
            hle.preferredHeight = 16f;
            hle.minHeight = 16f;

            _entries.Add(new Entry { Route = route, Button = btn, Group = group, Hint = hint });
        }

        private void SetEntryEnabled(Entry e, bool enabled)
        {
            if (e.Group == null) return;
            e.Group.interactable = enabled;   // blocks clicks + keyboard activation
            e.Group.alpha = enabled ? 1f : 0.45f;   // visibly dimmed, not hidden (§1.2)
        }

        // Re-tint an entry's fill between the accent (primary) and raised-surface look. Kept in sync with
        // UiKit.Button's palette; the UiInteractive on the button re-reads these on the next hover/press.
        private void SetPrimary(Entry e, bool primary)
        {
            if (e.Button == null) return;
            var inter = e.Button.GetComponent<UiInteractive>();
            var bg = e.Button.targetGraphic as Image;
            if (bg != null)
                bg.color = primary ? UiTheme.Accent : UiTheme.SurfaceRaised;
            if (inter != null)
                inter.Configure(bg,
                    e.Button.transform.Find("FocusRing")?.GetComponent<Graphic>(),
                    primary ? UiTheme.Accent : UiTheme.SurfaceRaised,
                    primary ? UiTheme.AccentHover : UiTheme.SurfaceHover,
                    primary ? UiTheme.AccentActive : UiTheme.SurfaceActive);
            // Label colour that reads on the chosen fill.
            var lbl = e.Button.GetComponentInChildren<TMP_Text>();
            if (lbl != null) lbl.color = primary ? UiTheme.OnAccent : UiTheme.TextPrimary;
        }

        // ------------------------------------------------------------------ keyboard shortcuts

        private void Update()
        {
            if (!IsVisible || UiInput.Typing || UiInput.Consumed) return;

            if (_hasBeatmaps && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
                Navigate?.Invoke(MenuRoute.Play);
            else if (Input.GetKeyDown(KeyCode.B))
                Navigate?.Invoke(MenuRoute.Browse);
            else if (Input.GetKeyDown(KeyCode.O) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
                Navigate?.Invoke(MenuRoute.Settings);
            else if (Input.GetKeyDown(KeyCode.Escape))
                Navigate?.Invoke(MenuRoute.Quit);
        }
    }
}
