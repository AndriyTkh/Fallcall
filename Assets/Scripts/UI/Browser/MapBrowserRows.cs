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
    /// <para>The set row is <see cref="UiMapCard"/> — the art-led card song select lists too, rather than the
    /// near-copy of it that used to live here.</para>
    /// </summary>
    public static class MapBrowserRows
    {
        /// <summary>A map card: the set's cover art, with title + subtitle (creator · diff count · star span · ✓ owned) over it.</summary>
        public static UiRow DefaultSetRow(Transform parent) => UiMapCard.Build(parent);

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

        /// <summary>The row's left-edge selection accent — see <see cref="UiMapCard.SelectionMarker"/>.</summary>
        public static Image SelectionMarker(Transform content) => UiMapCard.SelectionMarker(content);

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
