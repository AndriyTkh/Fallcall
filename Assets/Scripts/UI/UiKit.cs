using System;
using OsuUnity.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// INDEX: Fallcall shared UI widget kit — runtime-built uGUI+TMP factory (canvas, panel, button, toggle, range slider, search field, section header, list row) with hover/press/keyboard-focus states baked in. Every menu draws from this (U1).
namespace OsuUnity.UI
{
    /// <summary>
    /// The reusable widget factory every Fallcall menu builds from (U1 keystone). Runtime-built
    /// uGUI + TMP (no scene wiring / prefab assets — same auto-spawn convention as
    /// <see cref="SongSelectUI"/>), styled entirely from <see cref="UiTheme"/>. Each interactive
    /// widget ships with the mandated hover + keyboard-focus + press states (docs/UI-DESIGN §1.2/§1.5)
    /// so U2–U4 assemble screens without re-inventing styling. Screen-space only — never world math (§3).
    /// </summary>
    public static class UiKit
    {
        // ------------------------------------------------------------------ canvas / roots

        /// <summary>Make sure a keyboard-capable EventSystem exists (shared across all UI surfaces).</summary>
        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
        }

        /// <summary>
        /// A Screen-Space-Overlay canvas wired to "Scale With Screen Size" and a live
        /// <see cref="UiScaler"/> (so <see cref="GameSettings.UiScale"/> applies without a restart).
        /// One canvas per surface, per the UI-tech decision in PLAN U1.
        /// </summary>
        public static Canvas CreateCanvas(string name, int sortOrder = 0)
        {
            EnsureEventSystem();
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<UiScaler>();
            return canvas;
        }

        // ------------------------------------------------------------------ surfaces

        /// <summary>Full-screen dark scrim (behind text over artwork, §1.2). Stretches to its parent.</summary>
        public static Image Scrim(Transform parent)
        {
            var img = NewRect("Scrim", parent).gameObject.AddComponent<Image>();
            Stretch(img.rectTransform);
            img.color = UiTheme.Scrim;
            return img;
        }

        /// <summary>A rounded panel body (overlay/dialog surface). Caller positions <c>img.rectTransform</c>.</summary>
        public static Image Panel(Transform parent, string name = "Panel")
            => RoundedImage(parent, name, UiTheme.Surface, UiTheme.RadiusLG);

        /// <summary>A rounded, tintable <see cref="Image"/> (9-sliced so the radius holds at any size).</summary>
        public static Image RoundedImage(Transform parent, string name, Color color, int radius)
        {
            var img = NewRect(name, parent).gameObject.AddComponent<Image>();
            img.sprite = UiTheme.RoundedRect(radius);
            img.type = Image.Type.Sliced;
            img.color = color;
            return img;
        }

        /// <summary>A 1px horizontal separator line.</summary>
        public static Image Divider(Transform parent)
        {
            var img = NewRect("Divider", parent).gameObject.AddComponent<Image>();
            img.color = UiTheme.Divider;
            img.raycastTarget = false;
            return img;
        }

        // ------------------------------------------------------------------ text

        /// <summary>A TMP label at a typography role. Fills its parent by default; re-anchor as needed.</summary>
        public static TMP_Text Label(Transform parent, string text,
            UiTheme.Text role = UiTheme.Text.Body,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft,
            Color? color = null)
        {
            var r = NewRect("Label", parent);
            Stretch(r);
            var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = UiTheme.Font;
            t.fontSize = UiTheme.Size(role);
            t.alignment = align;
            t.color = color ?? UiTheme.TextPrimary;
            t.text = text;
            t.raycastTarget = false;
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Truncate;
            return t;
        }

        // ------------------------------------------------------------------ button

        /// <summary>
        /// A button carrying hover/press/focus states. <paramref name="primary"/> uses the accent fill,
        /// otherwise a raised surface. The returned <see cref="Button"/> sits on the outer container, so
        /// <c>btn.transform</c> is what you position (add it to a layout group or anchor it directly).
        /// </summary>
        public static Button Button(Transform parent, string text, Action onClick,
            bool primary = false, UiTheme.Text role = UiTheme.Text.Body)
        {
            var container = NewRect("Button", parent);
            var le = container.gameObject.AddComponent<LayoutElement>();
            le.minHeight = UiTheme.ControlHeight;
            le.preferredHeight = UiTheme.ControlHeight;

            var focus = FocusRingChild(container, UiTheme.RadiusMD);

            var fill = NewRect("Fill", container);
            Stretch(fill, UiTheme.FocusRingWidth, UiTheme.FocusRingWidth);
            var bg = fill.gameObject.AddComponent<Image>();
            bg.sprite = UiTheme.RoundedRect(UiTheme.RadiusMD);
            bg.type = Image.Type.Sliced;

            Color normal = primary ? UiTheme.Accent : UiTheme.SurfaceRaised;
            Color hover = primary ? UiTheme.AccentHover : UiTheme.SurfaceHover;
            Color active = primary ? UiTheme.AccentActive : UiTheme.SurfaceActive;
            bg.color = normal;

            var lbl = Label(fill, text, role, TextAlignmentOptions.Center,
                            primary ? UiTheme.OnAccent : UiTheme.TextPrimary);
            Stretch(lbl.rectTransform, UiTheme.SpaceMD, 0);

            var btn = container.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition = Selectable.Transition.None;   // visuals owned by UiInteractive
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var inter = container.gameObject.AddComponent<UiInteractive>();
            inter.Configure(bg, focus, normal, hover, active);
            return btn;
        }

        // ------------------------------------------------------------------ toggle

        /// <summary>A labelled on/off toggle with the same hover/focus affordances as buttons.</summary>
        public static Toggle Toggle(Transform parent, string text, bool value, Action<bool> onChanged)
        {
            var row = NewRect("Toggle", parent);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.minHeight = UiTheme.ControlHeight;
            le.preferredHeight = UiTheme.ControlHeight;

            // transparent full-row catcher so a click anywhere on the row toggles (events bubble up)
            var catcher = NewRect("Catcher", row);
            Stretch(catcher);
            catcher.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);

            // check-box (fixed square, vertically centred at the left edge)
            var box = NewRect("Box", row);
            box.anchorMin = new Vector2(0f, 0.5f);
            box.anchorMax = new Vector2(0f, 0.5f);
            box.pivot = new Vector2(0f, 0.5f);
            box.sizeDelta = new Vector2(24f, 24f);
            box.anchoredPosition = Vector2.zero;

            var focus = FocusRingChild(box, UiTheme.RadiusSM);
            var fill = NewRect("Fill", box);
            Stretch(fill, UiTheme.FocusRingWidth, UiTheme.FocusRingWidth);
            var boxBg = fill.gameObject.AddComponent<Image>();
            boxBg.sprite = UiTheme.RoundedRect(UiTheme.RadiusSM);
            boxBg.type = Image.Type.Sliced;
            boxBg.color = UiTheme.SurfaceRaised;

            var check = NewRect("Check", fill);
            Stretch(check, 4f, 4f);
            var checkImg = check.gameObject.AddComponent<Image>();
            checkImg.sprite = UiTheme.RoundedRect(UiTheme.RadiusSM);
            checkImg.type = Image.Type.Sliced;
            checkImg.color = UiTheme.Accent;
            checkImg.raycastTarget = false;

            var lbl = Label(row, text, UiTheme.Text.Body, TextAlignmentOptions.Left);
            var lr = lbl.rectTransform;
            lr.anchorMin = new Vector2(0f, 0f);
            lr.anchorMax = new Vector2(1f, 1f);
            lr.offsetMin = new Vector2(34f, 0f);
            lr.offsetMax = new Vector2(0f, 0f);

            var toggle = row.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = boxBg;
            toggle.graphic = checkImg;
            toggle.transition = Selectable.Transition.None;
            toggle.isOn = value;
            if (onChanged != null) toggle.onValueChanged.AddListener(v => onChanged(v));

            var inter = row.gameObject.AddComponent<UiInteractive>();
            inter.Configure(boxBg, focus, UiTheme.SurfaceRaised, UiTheme.SurfaceHover, UiTheme.SurfaceActive);
            return toggle;
        }

        // ------------------------------------------------------------------ range slider

        /// <summary>
        /// A range slider with a numeric readout, per-control reset icon, and keyboard stepping
        /// (arrow keys move the handle while focused) — the settings-row primitive (UI-DESIGN §2.1).
        /// </summary>
        public static UiRangeSlider Slider(Transform parent, float min, float max, float value,
            float defaultValue, Action<float> onChanged, string format = "0.##")
        {
            var row = NewRect("SliderRow", parent);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.minHeight = UiTheme.ControlHeight;
            le.preferredHeight = UiTheme.ControlHeight;

            // reset icon (far right)
            var reset = Button(row, "↺", null, false, UiTheme.Text.Body); // ↺
            var resetRect = (RectTransform)reset.transform;
            reset.GetComponent<LayoutElement>().ignoreLayout = true;
            Anchor(resetRect, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-28f, 4f), new Vector2(0f, -4f));

            // numeric readout (before the reset icon)
            var readout = Label(row, value.ToString(format), UiTheme.Text.Label,
                                TextAlignmentOptions.Right, UiTheme.TextSecondary);
            Anchor(readout.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-96f, 0f), new Vector2(-34f, 0f));

            // slider fills the remaining width on the left
            var sliderGO = NewRect("Slider", row);
            Anchor(sliderGO, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(-104f, 0f));
            var slider = sliderGO.gameObject.AddComponent<Slider>();

            var focus = FocusRingChild(sliderGO, UiTheme.RadiusSM);

            // transparent full-height catcher so dragging anywhere in the row moves the handle
            var catcher = NewRect("Catcher", sliderGO);
            Stretch(catcher);
            catcher.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);

            var bg = RoundedImage(sliderGO, "Background", UiTheme.Track, UiTheme.RadiusSM);
            Anchor(bg.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -4f), new Vector2(0f, 4f));

            var fillArea = NewRect("Fill Area", sliderGO);
            Anchor(fillArea, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(10f, -4f), new Vector2(-10f, 4f));
            var fill = RoundedImage(fillArea, "Fill", UiTheme.Accent, UiTheme.RadiusSM);
            var fr = fill.rectTransform;
            fr.anchorMin = new Vector2(0f, 0f);
            fr.anchorMax = new Vector2(1f, 1f);
            fr.offsetMin = Vector2.zero;
            fr.offsetMax = Vector2.zero;
            fill.raycastTarget = false;

            var handleArea = NewRect("Handle Slide Area", sliderGO);
            Anchor(handleArea, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(10f, 0f), new Vector2(-10f, 0f));
            var handle = RoundedImage(handleArea, "Handle", UiTheme.TextPrimary, UiTheme.RadiusLG);
            handle.rectTransform.sizeDelta = new Vector2(20f, 20f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.transition = Selectable.Transition.None;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(Mathf.Clamp(value, min, max));

            var inter = sliderGO.gameObject.AddComponent<UiInteractive>();
            inter.Configure(handle, focus, UiTheme.TextPrimary, UiTheme.AccentHover, UiTheme.Accent);

            var urs = row.gameObject.AddComponent<UiRangeSlider>();
            urs.Init(slider, readout, defaultValue, format, onChanged);
            reset.onClick.AddListener(() => urs.ResetToDefault());
            return urs;
        }

        // ------------------------------------------------------------------ search field

        /// <summary>A rounded search/text input (TMP) with placeholder + hover/focus states.</summary>
        public static TMP_InputField SearchField(Transform parent, string placeholder, Action<string> onChanged)
        {
            var container = NewRect("SearchField", parent);
            var le = container.gameObject.AddComponent<LayoutElement>();
            le.minHeight = UiTheme.ControlHeight;
            le.preferredHeight = UiTheme.ControlHeight;

            var focus = FocusRingChild(container, UiTheme.RadiusMD);
            var fill = NewRect("Fill", container);
            Stretch(fill, UiTheme.FocusRingWidth, UiTheme.FocusRingWidth);
            var bg = fill.gameObject.AddComponent<Image>();
            bg.sprite = UiTheme.RoundedRect(UiTheme.RadiusMD);
            bg.type = Image.Type.Sliced;
            bg.color = UiTheme.SurfaceRaised;

            var textArea = NewRect("TextArea", fill);
            Stretch(textArea, UiTheme.SpaceMD, UiTheme.SpaceXS);
            textArea.gameObject.AddComponent<RectMask2D>();

            var placeholderText = Label(textArea, placeholder, UiTheme.Text.Body,
                                        TextAlignmentOptions.Left, UiTheme.TextSecondary);
            Stretch(placeholderText.rectTransform);
            var inputText = Label(textArea, "", UiTheme.Text.Body, TextAlignmentOptions.Left, UiTheme.TextPrimary);
            Stretch(inputText.rectTransform);

            var field = fill.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = bg;
            field.transition = Selectable.Transition.None;
            field.textViewport = textArea;
            field.textComponent = (TMP_Text)inputText;
            field.placeholder = placeholderText;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.customCaretColor = true;
            field.caretColor = UiTheme.TextPrimary;
            field.selectionColor = UiTheme.WithAlpha(UiTheme.Accent, 0.4f);
            if (onChanged != null) field.onValueChanged.AddListener(v => onChanged(v));

            var inter = fill.gameObject.AddComponent<UiInteractive>();
            inter.Configure(bg, focus, UiTheme.SurfaceRaised, UiTheme.SurfaceHover, UiTheme.SurfaceActive);
            return field;
        }

        // ------------------------------------------------------------------ section header

        /// <summary>A section title with an underline divider — groups settings/menus (§1.6 hierarchy).</summary>
        public static RectTransform SectionHeader(Transform parent, string text)
        {
            var c = NewRect("Section", parent);
            var le = c.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 34f;
            le.preferredHeight = 34f;

            var lbl = Label(c, text, UiTheme.Text.Heading, TextAlignmentOptions.BottomLeft, UiTheme.TextPrimary);
            Anchor(lbl.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 6f), new Vector2(0f, 0f));

            var div = Divider(c);
            Anchor(div.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 1f));
            return c;
        }

        // ------------------------------------------------------------------ list row / card

        /// <summary>
        /// A full-width selectable row/card (list item). Returns the clickable <see cref="Button"/>; use
        /// <paramref name="content"/> (already inset) to place labels/art. Same hover/focus states as buttons.
        /// </summary>
        public static Button Row(Transform parent, float height, Action onClick, out RectTransform content)
        {
            var container = NewRect("Row", parent);
            var le = container.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            var focus = FocusRingChild(container, UiTheme.RadiusMD);
            var fill = NewRect("Fill", container);
            Stretch(fill, UiTheme.FocusRingWidth, UiTheme.FocusRingWidth);
            var bg = fill.gameObject.AddComponent<Image>();
            bg.sprite = UiTheme.RoundedRect(UiTheme.RadiusMD);
            bg.type = Image.Type.Sliced;
            bg.color = UiTheme.SurfaceRaised;

            content = NewRect("Content", fill);
            Stretch(content, UiTheme.SpaceMD, UiTheme.SpaceSM);
            // let clicks fall through the content to the row's Button (bubbles up)
            var cimg = content.gameObject.AddComponent<Image>();
            cimg.color = new Color(0, 0, 0, 0);
            cimg.raycastTarget = false;

            var btn = container.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition = Selectable.Transition.None;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var inter = container.gameObject.AddComponent<UiInteractive>();
            inter.Configure(bg, focus, UiTheme.SurfaceRaised, UiTheme.SurfaceHover, UiTheme.SurfaceActive);
            return btn;
        }

        // ------------------------------------------------------------------ focus ring

        // A rounded child placed BEHIND the widget's fill and slightly larger, so its coloured edge
        // reads as an outline. Disabled until the widget gains keyboard focus (UiInteractive drives it).
        private static Image FocusRingChild(Transform container, int radius)
        {
            var r = NewRect("FocusRing", container);
            Stretch(r);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.RoundedRect(radius + Mathf.CeilToInt(UiTheme.FocusRingWidth));
            img.type = Image.Type.Sliced;
            img.color = UiTheme.Focus;
            img.raycastTarget = false;
            img.enabled = false;
            return img;
        }

        // ------------------------------------------------------------------ rect helpers (mirror SongSelectUI)

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        public static void Stretch(RectTransform r, float insetX = 0, float insetY = 0)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(insetX, insetY);
            r.offsetMax = new Vector2(-insetX, -insetY);
        }

        public static void Anchor(RectTransform r, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = offsetMin;
            r.offsetMax = offsetMax;
        }
    }

    // ====================================================================== runtime components

    /// <summary>
    /// Applies <see cref="GameSettings.UiScale"/> live to a <see cref="CanvasScaler"/> by scaling its
    /// reference resolution (bigger UiScale → smaller reference → larger UI). Lets menus honour the
    /// player's UI-scale setting without a restart (UI-DESIGN §1.5 accessibility).
    /// </summary>
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class UiScaler : MonoBehaviour
    {
        public Vector2 baseReference = new Vector2(1920f, 1080f);
        private CanvasScaler _scaler;
        private float _applied = -1f;

        private void Awake() => _scaler = GetComponent<CanvasScaler>();
        private void OnEnable() => Apply(true);
        private void Update() => Apply(false);

        private void Apply(bool force)
        {
            float s = Mathf.Clamp(GameSettings.UiScale, 0.5f, 2f);
            if (!force && Mathf.Approximately(s, _applied)) return;
            _applied = s;
            if (_scaler != null) _scaler.referenceResolution = baseReference / s;
        }
    }

    /// <summary>
    /// Drives a widget's hover / press / keyboard-focus visuals (the states UI-DESIGN §1.2/§1.5 mandate
    /// on everything interactive). Tints a target graphic and shows a focus ring on selection; state is
    /// re-read from <see cref="UiTheme"/> on every change so a live palette swap (U2) is picked up.
    /// </summary>
    public sealed class UiInteractive : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        private Graphic _target;
        private Graphic _focusRing;
        private Color _normal, _hover, _active;
        private bool _hovered, _pressed, _selected;

        public void Configure(Graphic target, Graphic focusRing, Color normal, Color hover, Color active)
        {
            _target = target;
            _focusRing = focusRing;
            _normal = normal;
            _hover = hover;
            _active = active;
            Refresh();
        }

        public void OnPointerEnter(PointerEventData e) { _hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData e) { _hovered = false; _pressed = false; Refresh(); }
        public void OnPointerDown(PointerEventData e) { _pressed = true; Refresh(); }
        public void OnPointerUp(PointerEventData e) { _pressed = false; Refresh(); }
        public void OnSelect(BaseEventData e) { _selected = true; Refresh(); }
        public void OnDeselect(BaseEventData e) { _selected = false; Refresh(); }

        private void Refresh()
        {
            if (_target != null)
                _target.color = _pressed ? _active : (_hovered ? _hover : _normal);
            if (_focusRing != null)
                _focusRing.enabled = _selected;
        }
    }

    /// <summary>
    /// A range slider bundling a uGUI <see cref="Slider"/> with a numeric readout and reset-to-default.
    /// Built by <see cref="UiKit.Slider"/>; the underlying Slider supplies keyboard stepping on focus.
    /// </summary>
    public sealed class UiRangeSlider : MonoBehaviour
    {
        public Slider slider;
        public TMP_Text readout;
        public float defaultValue;
        public string format = "0.##";
        private Action<float> _onChanged;

        public void Init(Slider s, TMP_Text ro, float def, string fmt, Action<float> onChanged)
        {
            slider = s;
            readout = ro;
            defaultValue = def;
            format = fmt;
            _onChanged = onChanged;
            slider.onValueChanged.AddListener(OnChanged);
            UpdateReadout(slider.value);
        }

        private void OnChanged(float v)
        {
            UpdateReadout(v);
            _onChanged?.Invoke(v);
        }

        private void UpdateReadout(float v)
        {
            if (readout != null) readout.text = v.ToString(format);
        }

        /// <summary>Restore this control's default value (fires the change callback).</summary>
        public void ResetToDefault() => slider.value = defaultValue;

        /// <summary>Set the value from external state without firing the change callback.</summary>
        public void SetValueWithoutNotify(float v)
        {
            slider.SetValueWithoutNotify(v);
            UpdateReadout(v);
        }
    }
}
