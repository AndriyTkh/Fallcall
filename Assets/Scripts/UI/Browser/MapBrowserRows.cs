using UnityEngine;
using UnityEngine.UI;
using TMPro;

// INDEX: Map browser list plumbing — the code-default map/difficulty rows (superseded by a developer's row prefab), the selection marker, and the scroll-content helpers the browser's two lists share (U6).
namespace OsuUnity.UI
{
    /// <summary>
    /// The row and scroll-list primitives the browser builds with. Split out of the screen so
    /// <see cref="MapBrowser"/> holds behaviour and <see cref="MapBrowserView"/> holds chrome, and neither
    /// carries row construction.
    ///
    /// <para>These rows are the <b>fallback</b> factories for <see cref="UiListView"/>: assign a
    /// <c>setRowPrefab</c>/<c>diffRowPrefab</c> carrying a <see cref="UiRow"/> and the developer's styled row
    /// wins — the screen only fills the named slots (see <c>UiScaffold.cs</c>).</para>
    ///
    /// <para>Deliberate near-duplicate of the equivalents in <c>SongSelectUI</c>: pulling them up into the
    /// shared <see cref="UiKit"/> touches a file this block doesn't own, so it is left as a follow-up rather
    /// than done silently.</para>
    /// </summary>
    public static class MapBrowserRows
    {
        /// <summary>A map row: cover-less title + subtitle (creator · diff count · star span · ✓ owned).</summary>
        public static UiRow DefaultSetRow(Transform parent)
        {
            var btn = UiKit.Row(parent, 58f, null, out var content);
            var marker = SelectionMarker(content);

            var title = UiKit.Label(content, "", UiTheme.Text.Body, TextAlignmentOptions.TopLeft);
            UiKit.Anchor(title.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 1), new Vector2(8, 0), new Vector2(0, 0));
            title.enableWordWrapping = false;
            title.overflowMode = TextOverflowModes.Ellipsis;

            var sub = UiKit.Label(content, "", UiTheme.Text.Caption, TextAlignmentOptions.BottomLeft, UiTheme.TextSecondary);
            UiKit.Anchor(sub.rectTransform, new Vector2(0, 0), new Vector2(1, 0.5f), new Vector2(8, 0), new Vector2(0, 0));
            sub.enableWordWrapping = false;
            sub.overflowMode = TextOverflowModes.Ellipsis;

            var row = btn.gameObject.AddComponent<UiRow>();
            row.button = btn;
            row.content = content;
            row.title = title;
            row.subtitle = sub;
            row.marker = marker;
            return row;
        }

        /// <summary>A difficulty row: one line of stars + name + glance metadata.</summary>
        public static UiRow DefaultDiffRow(Transform parent)
        {
            var btn = UiKit.Row(parent, 40f, null, out var content);
            var marker = SelectionMarker(content);

            var label = UiKit.Label(content, "", UiTheme.Text.Label, TextAlignmentOptions.Left);
            UiKit.Stretch(label.rectTransform, 8, 0);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;

            var row = btn.gameObject.AddComponent<UiRow>();
            row.button = btn;
            row.content = content;
            row.title = label;
            row.marker = marker;
            return row;
        }

        /// <summary>
        /// The thin accent bar marking persistent selection on a row's left edge. Separate from the row fill
        /// because <see cref="UiInteractive"/> re-tints that on hover — selection must survive the mouse
        /// passing over a different row. Disabled by default.
        /// </summary>
        public static Image SelectionMarker(Transform content)
        {
            var r = UiKit.NewRect("SelMarker", content);
            UiKit.Anchor(r, new Vector2(0, 0), new Vector2(0, 1), new Vector2(-6, 3), new Vector2(-2, -3));
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.RoundedRect(UiTheme.RadiusSM);
            img.type = Image.Type.Sliced;
            img.color = UiTheme.Accent;
            img.raycastTarget = false;
            img.enabled = false;
            return img;
        }

        /// <summary>
        /// A masked vertical <see cref="ScrollRect"/> filling <paramref name="parent"/>, returning the content
        /// node rows are parented to (already carrying a <see cref="UiListView"/> bound to the given factory
        /// + optional developer prefab).
        /// </summary>
        public static UiListView ScrollList(RectTransform parent, GameObject rowPrefab,
                                            System.Func<Transform, UiRow> fallback, out ScrollRect scroll)
        {
            var scrollGO = UiKit.NewRect("Scroll", parent);
            UiKit.Stretch(scrollGO, UiTheme.SpaceSM, UiTheme.SpaceSM);
            scroll = scrollGO.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 24f;

            var viewport = UiKit.NewRect("Viewport", scrollGO);
            UiKit.Stretch(viewport);
            viewport.gameObject.AddComponent<Image>().color = Color.white;
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var content = VerticalContent(viewport);
            scroll.viewport = viewport;
            scroll.content = content;

            var list = UiScaffold.Ensure<UiListView>(content.gameObject);
            list.rowPrefab = rowPrefab;
            list.fallbackFactory = fallback;
            return list;
        }

        /// <summary>A top-anchored, self-sizing vertical content node (the rows' parent).</summary>
        public static RectTransform VerticalContent(RectTransform viewport)
        {
            var content = UiKit.NewRect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;

            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = UiTheme.SpaceXS;
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return content;
        }

        /// <summary>Bring row <paramref name="index"/> of <paramref name="count"/> into view.</summary>
        public static void ScrollTo(ScrollRect sr, int index, int count)
        {
            if (sr == null) return;
            if (count <= 1) { sr.verticalNormalizedPosition = 1f; return; }
            sr.verticalNormalizedPosition = Mathf.Clamp01(1f - (float)index / (count - 1));
        }
    }
}
