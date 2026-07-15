using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// INDEX: Map browser chrome — builds the browse screen (results column left; search+filters top-right; map detail with the reserved preview slot bottom-right) and hands back every widget reference. Construction only, no behaviour (U6).
namespace OsuUnity.UI
{
    /// <summary>
    /// Everything the browse screen <i>looks like</i>, and nothing it <i>does</i>. Built once from the U1
    /// <see cref="UiKit"/> (runtime uGUI + TMP, no scene wiring) and handed back as a bag of widget
    /// references that <see cref="MapBrowser"/> drives.
    ///
    /// <para><b>Layout B</b> (PLAN U6): one scrolling map column on the left; the right splits into search +
    /// filters on top and the map detail below. The detail's top band is <see cref="PreviewSlot"/> — it shows
    /// the set cover today and is the node the autoplay-preview panel mounts into next pass, so that block
    /// re-parents one node instead of re-cutting the screen.</para>
    /// </summary>
    public sealed class MapBrowserView
    {
        /// <summary>Which range a filter slider drives (the view is stateless — the screen owns the values).</summary>
        public enum Filter { Stars, Length, Bpm }

        /// <summary>What the chrome calls back into. Set by the screen before <see cref="Build"/>.</summary>
        public sealed class Callbacks
        {
            public Action<string> SearchChanged;
            public Action SortClicked;
            public Action FiltersReset;
            public Action PrimaryClicked;
            public Action<Filter, bool, float> FilterChanged;   // (which, isMin, value)
        }

        public GameObject Root;
        public RawImage Backdrop;          // full-screen dimmed cover art
        public RawImage Cover;             // the selected set's cover, inside PreviewSlot
        public RectTransform PreviewSlot;  // reserved: the autoplay-preview panel mounts here next pass

        public TMP_InputField SearchField;
        public TMP_Text SortLabel, ResultsStatus, DetailTitle, DetailMeta, ActionStatus, Hint;
        public Button PrimaryButton;
        public ScrollRect ResultsScroll, DiffScroll;
        public UiListView ResultList, DiffList;

        private readonly UiRangeSlider[,] _filters = new UiRangeSlider[3, 2];   // [Filter, isMin ? 0 : 1]

        private const float ColumnSplit = 0.42f;   // left results column ends here
        private const float Margin = 40f;
        private const float TopBand = 86f;         // title strip height
        private const float SearchPanelH = 356f;   // search + sort + the three filter pairs

        public static MapBrowserView Build(Callbacks cb, GameObject setRowPrefab, GameObject diffRowPrefab)
        {
            var v = new MapBrowserView();
            var canvas = UiKit.CreateCanvas("MapBrowserCanvas");
            v.Root = canvas.gameObject;
            var root = (RectTransform)v.Root.transform;

            // Cover art behind everything + scrim, so titles stay readable over any artwork (§1.2).
            v.Backdrop = UiKit.NewRect("Backdrop", root).gameObject.AddComponent<RawImage>();
            UiKit.Stretch(v.Backdrop.rectTransform);
            v.Backdrop.color = new Color(1, 1, 1, 0);
            v.Backdrop.raycastTarget = false;
            UiKit.Scrim(root).raycastTarget = false;

            var title = UiKit.Label(root, "Browse maps", UiTheme.Text.Title, TextAlignmentOptions.Left);
            UiKit.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                         new Vector2(Margin, -74), new Vector2(-Margin, -28));

            v.BuildResults(root, setRowPrefab);
            v.BuildSearchPanel(root, cb);
            v.BuildDetail(root, cb, diffRowPrefab);

            v.Hint = UiKit.Label(root, BrowseText.Hint, UiTheme.Text.Caption, TextAlignmentOptions.Left, UiTheme.TextSecondary);
            UiKit.Anchor(v.Hint.rectTransform, new Vector2(0, 0), new Vector2(1, 0),
                         new Vector2(Margin, 12), new Vector2(-Margin, 36));

            v.Root.SetActive(false);
            return v;
        }

        /// <summary>Push a filter value into its slider without firing the change callback (external reset).</summary>
        public void SetFilter(Filter which, bool isMin, float value)
            => _filters[(int)which, isMin ? 0 : 1]?.SetValueWithoutNotify(value);

        // ------------------------------------------------------------------ left column

        private void BuildResults(RectTransform root, GameObject rowPrefab)
        {
            var panel = UiKit.Panel(root, "ResultsPanel");
            UiKit.Anchor(panel.rectTransform, new Vector2(0, 0), new Vector2(ColumnSplit, 1),
                         new Vector2(Margin, Margin), new Vector2(-UiTheme.SpaceSM, -TopBand));

            ResultList = MapBrowserRows.ScrollList(panel.rectTransform, rowPrefab,
                                                   MapBrowserRows.DefaultSetRow, out var scroll);
            ResultsScroll = scroll;

            // Status sits over the list (empty / searching / failed) rather than replacing it, so the panel
            // never collapses and the player keeps their place (§1.3 certainty).
            ResultsStatus = UiKit.Label(panel.rectTransform, "", UiTheme.Text.Body,
                                        TextAlignmentOptions.TopLeft, UiTheme.TextSecondary);
            UiKit.Stretch(ResultsStatus.rectTransform, 20, 20);
        }

        // ------------------------------------------------------------------ top-right: search + filters

        private void BuildSearchPanel(RectTransform root, Callbacks cb)
        {
            var panel = UiKit.Panel(root, "SearchPanel");
            UiKit.Anchor(panel.rectTransform, new Vector2(ColumnSplit, 1), new Vector2(1, 1),
                         new Vector2(UiTheme.SpaceSM, -(TopBand + SearchPanelH)), new Vector2(-Margin, -TopBand));

            var col = UiKit.NewRect("Col", panel.rectTransform);
            UiKit.Stretch(col, UiTheme.SpaceLG, UiTheme.SpaceMD);
            var vlg = col.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = UiTheme.SpaceXS;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;

            SearchField = UiKit.SearchField(col, "Search the mirrors — artist / title / creator", cb.SearchChanged);

            // sort + reset on one row
            var actions = UiKit.NewRect("Actions", col);
            actions.gameObject.AddComponent<LayoutElement>().preferredHeight = UiTheme.ControlHeight;
            var hlg = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = UiTheme.SpaceSM;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;

            var sortBtn = UiKit.Button(actions, "", cb.SortClicked);
            SortLabel = sortBtn.GetComponentInChildren<TMP_Text>();
            UiKit.Button(actions, "Reset filters", cb.FiltersReset);

            AddFilter(col, cb, Filter.Stars, "Stars", BrowseFilters.StarLo, BrowseFilters.StarHi, "0.#");
            AddFilter(col, cb, Filter.Length, "Length (s)", BrowseFilters.LenLo, BrowseFilters.LenHi, "0");
            AddFilter(col, cb, Filter.Bpm, "BPM", BrowseFilters.BpmLo, BrowseFilters.BpmHi, "0");
        }

        // One labelled min/max pair, the two sliders side by side so all three filters fit above the detail
        // panel without a scroll (the whole point of putting them here rather than behind a Filters toggle).
        private void AddFilter(Transform parent, Callbacks cb, Filter which, string label, float lo, float hi, string fmt)
        {
            var head = UiKit.Label(parent, label, UiTheme.Text.Label, TextAlignmentOptions.Left);
            head.rectTransform.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            var row = UiKit.NewRect(label + "Row", parent);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = UiTheme.ControlHeight;
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = UiTheme.SpaceMD;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;

            _filters[(int)which, 0] = UiKit.Slider(row, lo, hi, lo, lo, v => cb.FilterChanged(which, true, v), fmt);
            _filters[(int)which, 1] = UiKit.Slider(row, lo, hi, hi, hi, v => cb.FilterChanged(which, false, v), fmt);
        }

        // ------------------------------------------------------------------ bottom-right: map detail

        private void BuildDetail(RectTransform root, Callbacks cb, GameObject rowPrefab)
        {
            var panel = UiKit.Panel(root, "DetailPanel");
            UiKit.Anchor(panel.rectTransform, new Vector2(ColumnSplit, 0), new Vector2(1, 1),
                         new Vector2(UiTheme.SpaceSM, Margin),
                         new Vector2(-Margin, -(TopBand + SearchPanelH + UiTheme.SpaceSM)));

            // Preview slot — cover art now, autoplay panel next pass (see the class summary).
            PreviewSlot = UiKit.NewRect("PreviewSlot", panel.rectTransform);
            UiKit.Anchor(PreviewSlot, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -212), new Vector2(-12, -12));
            var slotBg = UiKit.RoundedImage(PreviewSlot, "Frame", UiTheme.SurfaceRaised, UiTheme.RadiusMD);
            UiKit.Stretch(slotBg.rectTransform);
            slotBg.raycastTarget = false;

            Cover = UiKit.NewRect("Cover", PreviewSlot).gameObject.AddComponent<RawImage>();
            UiKit.Stretch(Cover.rectTransform, 2, 2);
            Cover.color = new Color(1, 1, 1, 0);
            Cover.raycastTarget = false;

            DetailTitle = UiKit.Label(panel.rectTransform, "Select a map", UiTheme.Text.Heading, TextAlignmentOptions.TopLeft);
            UiKit.Anchor(DetailTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -278), new Vector2(-16, -220));

            DetailMeta = UiKit.Label(panel.rectTransform, "", UiTheme.Text.Label, TextAlignmentOptions.TopLeft, UiTheme.TextSecondary);
            UiKit.Anchor(DetailMeta.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -330), new Vector2(-16, -282));

            var listHost = UiKit.NewRect("DiffList", panel.rectTransform);
            UiKit.Anchor(listHost, new Vector2(0, 0), new Vector2(1, 1), new Vector2(12, 96), new Vector2(-12, -334));
            DiffList = MapBrowserRows.ScrollList(listHost, rowPrefab, MapBrowserRows.DefaultDiffRow, out var scroll);
            DiffScroll = scroll;

            ActionStatus = UiKit.Label(panel.rectTransform, "", UiTheme.Text.Caption, TextAlignmentOptions.Left, UiTheme.TextSecondary);
            UiKit.Anchor(ActionStatus.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(16, 72), new Vector2(-16, 92));

            PrimaryButton = UiKit.Button(panel.rectTransform, "⬇  Download", cb.PrimaryClicked, primary: true, role: UiTheme.Text.Heading);
            var pr = (RectTransform)PrimaryButton.transform;
            PrimaryButton.GetComponent<LayoutElement>().ignoreLayout = true;
            UiKit.Anchor(pr, new Vector2(0, 0), new Vector2(1, 0), new Vector2(12, 12), new Vector2(-12, 64));
            PrimaryButton.gameObject.SetActive(false);
        }
    }
}
