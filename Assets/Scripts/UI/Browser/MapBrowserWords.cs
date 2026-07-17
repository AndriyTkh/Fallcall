using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// INDEX: Inline word-filter row for the map browser — a fixed heading followed by clickable words (osu! listing style), single- or multi-select, that highlight the active choice. Construction + selection state only (U6).
namespace OsuUnity.UI
{
    /// <summary>
    /// The osu! beatmap-listing filter row rendered as Fallcall chrome (UI-DESIGN §2.3): a fixed-width
    /// heading (<i>Category</i> / <i>Extra</i> / <i>Sort by</i>) followed by a run of clickable words. One
    /// word is the active choice — pressing a word selects it. <b>Single-select</b> rows (Category, Sort)
    /// keep exactly one lit; <b>multi-select</b> rows (Extra) toggle each word independently. Pure widget:
    /// it reports the change through a callback and never touches browse state (that lives in
    /// <see cref="MapBrowser"/>).
    /// </summary>
    public static class MapBrowserWords
    {
        private const float RowHeight = 26f;
        private const float HeadWidth = 118f;   // heading column, so the words align across all three rows

        /// <summary>
        /// Build one filter row under <paramref name="parent"/> (expects a vertical layout). <paramref name="multi"/>
        /// makes the words independent toggles; otherwise they are one-of. <paramref name="onChange"/> gets
        /// <c>(index, isOn)</c> — for single-select <c>isOn</c> is always <c>true</c> on the newly chosen word.
        /// </summary>
        public static UiWordRow Build(Transform parent, string heading, string[] words, bool multi,
            Action<int, bool> onChange)
        {
            var row = UiKit.NewRect("WordRow_" + heading, parent);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.minHeight = RowHeight;
            le.preferredHeight = RowHeight;

            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = UiTheme.SpaceMD;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            MakeHeading(row, heading);

            var comp = row.gameObject.AddComponent<UiWordRow>();
            var list = new UiWord[words.Length];
            for (int i = 0; i < words.Length; i++)
            {
                int idx = i;
                list[i] = MakeWord(row, words[i], () => comp.Click(idx));
            }
            comp.Init(list, multi, onChange);
            return comp;
        }

        private static void MakeHeading(Transform parent, string text)
        {
            var go = UiKit.NewRect("Head", parent);
            var le = go.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = HeadWidth;
            le.minWidth = HeadWidth;
            le.flexibleWidth = 0;

            var t = go.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = UiTheme.Font;
            t.fontSize = UiTheme.Size(UiTheme.Text.Label);
            t.color = UiTheme.TextSecondary;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.text = text;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
        }

        private static UiWord MakeWord(Transform parent, string word, Action onClick)
        {
            var go = UiKit.NewRect("Word", parent);
            var t = go.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = UiTheme.Font;
            t.fontSize = UiTheme.Size(UiTheme.Text.Label);
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.text = word;
            t.raycastTarget = true;   // the word itself is the click target
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;

            // Size the cell to the word (measured unconstrained). +6 leaves room for the bold weight the
            // selected state adds, so it never clips or nudges its neighbours when it lights up.
            float w = Mathf.Ceil(t.GetPreferredValues(word).x) + 6f;
            var le = go.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.minWidth = w;
            le.flexibleWidth = 0;

            var uw = go.gameObject.AddComponent<UiWord>();
            uw.Init(t, onClick);
            return uw;
        }
    }

    /// <summary>One clickable word: secondary by default, primary on hover, accent + bold when selected
    /// (never hue alone — the weight change keeps it legible in greyscale, UI-DESIGN §1.2).</summary>
    public sealed class UiWord : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private TMP_Text _label;
        private Action _onClick;
        private bool _selected, _hovered;

        public void Init(TMP_Text label, Action onClick)
        {
            _label = label;
            _onClick = onClick;
            Refresh();
        }

        public void SetSelected(bool value)
        {
            if (_selected == value) return;
            _selected = value;
            Refresh();
        }

        public void OnPointerEnter(PointerEventData e) { _hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData e) { _hovered = false; Refresh(); }
        public void OnPointerClick(PointerEventData e) => _onClick?.Invoke();

        private void Refresh()
        {
            if (_label == null) return;
            _label.color = _selected ? UiTheme.Accent : (_hovered ? UiTheme.TextPrimary : UiTheme.TextSecondary);
            _label.fontStyle = _selected ? FontStyles.Bold : FontStyles.Normal;
        }
    }

    /// <summary>Owns a word row's selection: one-of for single-select, per-word for multi-select. Selection
    /// can be set silently (no callback) so <see cref="MapBrowser"/> can seed defaults on build.</summary>
    public sealed class UiWordRow : MonoBehaviour
    {
        private UiWord[] _words = Array.Empty<UiWord>();
        private bool _multi;
        private bool[] _on = Array.Empty<bool>();
        private Action<int, bool> _onChange;

        public void Init(UiWord[] words, bool multi, Action<int, bool> onChange)
        {
            _words = words;
            _multi = multi;
            _onChange = onChange;
            _on = new bool[words.Length];
        }

        /// <summary>A word was pressed — updates the highlight, then reports the change.</summary>
        public void Click(int i)
        {
            if (_multi)
            {
                _on[i] = !_on[i];
                _words[i].SetSelected(_on[i]);
                _onChange?.Invoke(i, _on[i]);
            }
            else
            {
                Highlight(i);
                _onChange?.Invoke(i, true);
            }
        }

        /// <summary>Light a single-select row's choice without firing the callback (seed a default).</summary>
        public void SelectSilent(int i) => Highlight(i);

        /// <summary>Set one multi-select word's state without firing the callback.</summary>
        public void SetOnSilent(int i, bool on)
        {
            _on[i] = on;
            _words[i].SetSelected(on);
        }

        private void Highlight(int i)
        {
            for (int k = 0; k < _words.Length; k++)
                _words[k].SetSelected(k == i);
        }
    }
}
