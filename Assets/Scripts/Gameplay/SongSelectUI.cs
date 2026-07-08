using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OsuUnity.Beatmaps;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// INDEX: osu!lazer-style song select UI (carousel, search/sort, difficulty panel, download-by-id), built at runtime via uGUI.
namespace OsuUnity.Gameplay
{
    /// <summary>
    /// osu!lazer-style song select: carousel of <see cref="BeatmapSetInfo"/> from
    /// <see cref="BeatmapLibrary"/>, search/sort, difficulty panel with background preview,
    /// and a "download by online id" box using <see cref="BeatmapLibrary.DownloadSet"/>.
    /// Built entirely at runtime (uGUI, no scene/prefab assets) so it fits the project's
    /// auto-spawn convention. <see cref="Bootstrap"/> owns scanning + starting the session;
    /// this class only browses and reports the chosen difficulty via <see cref="PlaySelected"/>.
    /// </summary>
    public sealed class SongSelectUI : MonoBehaviour
    {
        public event Action<BeatmapSetInfo, BeatmapDifficultyInfo> PlaySelected;

        private enum SortMode { Title, Artist, SetId }

        private List<BeatmapSetInfo> _allSets = new List<BeatmapSetInfo>();
        private BeatmapSetInfo _selectedSet;
        private string _search = "";
        private SortMode _sort = SortMode.Title;
        private Font _font;

        private GameObject _root;
        private RectTransform _listContent;
        private InputField _searchField;
        private Text _statusText;
        private RawImage _bgImage;
        private RectTransform _detailPanel;
        private Text _detailTitleText;
        private RectTransform _diffListContent;
        private InputField _downloadIdField;
        private Text _downloadStatusText;

        private void Awake()
        {
            // Built-in resource names differ across Unity versions ("Arial.ttf" pre-2022.2,
            // "LegacyRuntime.ttf" after) and can resolve to null; a dynamic OS font always works.
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                    ?? Resources.GetBuiltinResource<Font>("Arial.ttf")
                    ?? Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Liberation Sans", "Segoe UI", "Helvetica" }, 16);
            Build();
        }

        public void Populate(List<BeatmapSetInfo> sets)
        {
            _allSets = sets ?? new List<BeatmapSetInfo>();
            _selectedSet = null;
            RefreshList();
            ClearDetailPanel();
        }

        public void Show() => _root.SetActive(true);
        public void Hide() => _root.SetActive(false);

        // ------------------------------------------------------------- list filtering

        private void RefreshList()
        {
            IEnumerable<BeatmapSetInfo> filtered = _allSets;
            if (!string.IsNullOrEmpty(_search))
            {
                string q = _search.ToLowerInvariant();
                filtered = _allSets.Where(s =>
                    s.SetName.ToLowerInvariant().Contains(q) ||
                    s.Difficulties.Any(d => d.Artist.ToLowerInvariant().Contains(q) ||
                                             d.Title.ToLowerInvariant().Contains(q) ||
                                             d.Version.ToLowerInvariant().Contains(q)));
            }

            List<BeatmapSetInfo> sorted = _sort switch
            {
                SortMode.Artist => filtered.OrderBy(s => s.Difficulties.Count > 0 ? s.Difficulties[0].Artist : s.SetName, StringComparer.OrdinalIgnoreCase).ToList(),
                SortMode.SetId => filtered.OrderBy(s => s.OnlineSetId ?? int.MaxValue).ToList(),
                _ => filtered.OrderBy(s => s.Difficulties.Count > 0 ? s.Difficulties[0].Title : s.SetName, StringComparer.OrdinalIgnoreCase).ToList(),
            };

            foreach (Transform child in _listContent) Destroy(child.gameObject);

            _statusText.gameObject.SetActive(sorted.Count == 0);
            if (sorted.Count == 0)
            {
                _statusText.text = _allSets.Count == 0
                    ? "No beatmaps found.\nPlace .osz files under the Songs folder, or download one by id below."
                    : "No matches.";
            }

            foreach (var set in sorted)
                CreateSetRow(set);
        }

        private void CreateSetRow(BeatmapSetInfo set)
        {
            var first = set.Difficulties.Count > 0 ? set.Difficulties[0] : null;
            string headline = first != null ? $"{first.Artist} - {first.Title}" : set.SetName;

            var row = NewRect("SetRow", _listContent);
            var layout = row.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 56;
            layout.minHeight = 56;

            var btn = row.gameObject.AddComponent<Button>();
            var img = row.gameObject.AddComponent<Image>();
            img.color = new Color(1, 1, 1, 0.16f);
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => SelectSet(set));

            var label = NewText(row, headline + $"\n<size=12>{set.SetName} · {set.Difficulties.Count} diff(s)</size>", 16, TextAnchor.MiddleLeft);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(12, 0);
            label.rectTransform.offsetMax = new Vector2(-12, 0);
            label.raycastTarget = false;
        }

        // ------------------------------------------------------------- detail panel

        private void SelectSet(BeatmapSetInfo set)
        {
            _selectedSet = set;
            var first = set.Difficulties.Count > 0 ? set.Difficulties[0] : null;
            _detailTitleText.text = first != null
                ? $"{first.Artist} - {first.Title}\n<size=14>{set.SetName}</size>"
                : set.SetName;

            foreach (Transform child in _diffListContent) Destroy(child.gameObject);
            foreach (var diff in set.Difficulties)
            {
                var row = NewRect("DiffRow", _diffListContent);
                var layout = row.gameObject.AddComponent<LayoutElement>();
                layout.preferredHeight = 40;
                layout.minHeight = 40;
                var btn = row.gameObject.AddComponent<Button>();
                var img = row.gameObject.AddComponent<Image>();
                img.color = new Color(1, 1, 1, 0.2f);
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => PlaySelected?.Invoke(set, diff));

                var label = NewText(row, $"  {diff.Stars:0.00}★   [{diff.Version}]", 15, TextAnchor.MiddleLeft);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(8, 0);
                label.rectTransform.offsetMax = new Vector2(-8, 0);
                label.raycastTarget = false;
            }

            _bgImage.texture = null;
            _bgImage.color = new Color(1, 1, 1, 0);
            if (first != null) StartCoroutine(LoadBackgroundPreview(first));
        }

        private void ClearDetailPanel()
        {
            _detailTitleText.text = "Select a beatmap set";
            foreach (Transform child in _diffListContent) Destroy(child.gameObject);
            _bgImage.texture = null;
            _bgImage.color = new Color(1, 1, 1, 0);
        }

        private IEnumerator LoadBackgroundPreview(BeatmapDifficultyInfo diff)
        {
            Beatmap map;
            try { map = BeatmapParser.ParseFile(diff.OsuPath); }
            catch { yield break; }

            if (string.IsNullOrEmpty(map.BackgroundFile)) yield break;
            string path = Path.Combine(map.Directory, map.BackgroundFile);
            Texture2D tex = null;
            yield return AssetLoader.LoadTexture(path, t => tex = t);
            if (tex == null || _selectedSet == null) yield break;
            // still the selected set? (user may have clicked away while loading)
            if (map.Directory != null) { _bgImage.texture = tex; _bgImage.color = new Color(1, 1, 1, 0.35f); }
        }

        // ------------------------------------------------------------- download by id

        private void OnDownloadClicked()
        {
            if (!int.TryParse(_downloadIdField.text.Trim(), out int setId) || setId <= 0)
            {
                _downloadStatusText.text = "Enter a valid numeric set id.";
                return;
            }
            _downloadStatusText.text = "Downloading...";
            StartCoroutine(BeatmapLibrary.DownloadSet(setId,
                progress => _downloadStatusText.text = $"Downloading... {progress * 100f:0}%",
                result =>
                {
                    if (result == null)
                    {
                        _downloadStatusText.text = "Download failed (bad id or mirrors unreachable).";
                        return;
                    }
                    if (result.Difficulties.Count == 0)
                    {
                        _downloadStatusText.text = "Downloaded, but that set has no osu!standard difficulty.";
                        return;
                    }
                    _downloadStatusText.text = $"Downloaded: {result.SetName}";
                    _allSets.RemoveAll(s => string.Equals(s.OszPath, result.OszPath, StringComparison.OrdinalIgnoreCase));
                    _allSets.Add(result);
                    RefreshList();
                    SelectSet(result); // open the detail panel immediately so its difficulties are one click away
                }));
        }

        // ------------------------------------------------------------- uGUI construction

        private void Build()
        {
            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            _root = new GameObject("SongSelectCanvas");
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            var rootRect = _root.GetComponent<RectTransform>();

            // full-screen background art + dark scrim, behind everything
            _bgImage = NewRect("Background", rootRect).gameObject.AddComponent<RawImage>();
            Stretch(_bgImage.rectTransform);
            _bgImage.color = new Color(1, 1, 1, 0);
            var scrim = NewRect("Scrim", rootRect).gameObject.AddComponent<Image>();
            Stretch(scrim.rectTransform);
            scrim.color = new Color(0, 0, 0, 0.72f);

            var title = NewText(rootRect, "osu! 3D — Song Select", 30, TextAnchor.MiddleLeft, FontStyle.Bold);
            Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, -70), new Vector2(-40, -20));

            // search field
            var searchGO = NewRect("Search", rootRect);
            Anchor(searchGO, new Vector2(0, 1), new Vector2(0.6f, 1), new Vector2(40, -110), new Vector2(-10, -76));
            var searchImg = searchGO.gameObject.AddComponent<Image>();
            searchImg.color = new Color(1, 1, 1, 0.2f);
            _searchField = searchGO.gameObject.AddComponent<InputField>();
            var searchLabel = NewText(searchGO, "", 16, TextAnchor.MiddleLeft);
            Stretch(searchLabel.rectTransform, 10, 4);
            var searchPlaceholder = NewText(searchGO, "Search artist / title / difficulty...", 16, TextAnchor.MiddleLeft);
            searchPlaceholder.color = new Color(1, 1, 1, 0.4f);
            Stretch(searchPlaceholder.rectTransform, 10, 4);
            _searchField.textComponent = searchLabel;
            _searchField.placeholder = searchPlaceholder;
            _searchField.onValueChanged.AddListener(v => { _search = v; RefreshList(); });

            // sort buttons
            float sx = 0.6f;
            foreach (SortMode mode in Enum.GetValues(typeof(SortMode)))
            {
                var b = NewRect(mode + "Sort", rootRect);
                Anchor(b, new Vector2(sx, 1), new Vector2(sx + 0.133f, 1), new Vector2(0, -110), new Vector2(0, -76));
                var img = b.gameObject.AddComponent<Image>();
                img.color = new Color(1, 1, 1, 0.22f);
                var btn = b.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                var lbl = NewText(b, mode.ToString(), 14, TextAnchor.MiddleCenter);
                Stretch(lbl.rectTransform);
                lbl.raycastTarget = false;
                btn.onClick.AddListener(() => { _sort = mode; RefreshList(); });
                sx += 0.133f;
            }

            // carousel (left 58%)
            var scrollGO = NewRect("Carousel", rootRect);
            Anchor(scrollGO, new Vector2(0, 0), new Vector2(0.58f, 1), new Vector2(40, 110), new Vector2(-10, -122));
            var scrollRect = scrollGO.gameObject.AddComponent<ScrollRect>();
            scrollGO.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.12f);
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            var viewport = NewRect("Viewport", scrollGO);
            Stretch(viewport);
            viewport.gameObject.AddComponent<Image>().color = Color.white;
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            // A fresh RectTransform keeps default offsets; changing anchors leaves a stray
            // anchoredPosition/sizeDelta that pushes scroll content off the top (rows clipped
            // by the Mask). Pin it to the viewport's top edge, full width.
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            _listContent = content;

            _statusText = NewText(viewport, "Scanning for beatmaps...", 16, TextAnchor.UpperLeft);
            Stretch(_statusText.rectTransform, 16, 16);

            // detail panel (right 40%)
            _detailPanel = NewRect("Detail", rootRect);
            Anchor(_detailPanel, new Vector2(0.6f, 0), new Vector2(1, 1), new Vector2(0, 110), new Vector2(-40, -122));
            _detailPanel.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.12f);
            _detailTitleText = NewText(_detailPanel, "Select a beatmap set", 20, TextAnchor.UpperLeft, FontStyle.Bold);
            Anchor(_detailTitleText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, -80), new Vector2(-14, -10));

            var diffScrollGO = NewRect("DiffScroll", _detailPanel);
            Anchor(diffScrollGO, new Vector2(0, 0), new Vector2(1, 1), new Vector2(10, 10), new Vector2(-10, -90));
            var diffScroll = diffScrollGO.gameObject.AddComponent<ScrollRect>();
            diffScroll.horizontal = false;
            var diffViewport = NewRect("Viewport", diffScrollGO);
            Stretch(diffViewport);
            diffViewport.gameObject.AddComponent<Image>().color = Color.white;
            diffViewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var diffContent = NewRect("Content", diffViewport);
            diffContent.anchorMin = new Vector2(0, 1);
            diffContent.anchorMax = new Vector2(1, 1);
            diffContent.pivot = new Vector2(0.5f, 1);
            diffContent.sizeDelta = Vector2.zero;
            diffContent.anchoredPosition = Vector2.zero;
            var diffVlg = diffContent.gameObject.AddComponent<VerticalLayoutGroup>();
            diffVlg.spacing = 3;
            diffVlg.childForceExpandHeight = false;
            diffVlg.childControlHeight = true;
            diffVlg.childControlWidth = true;
            var diffFitter = diffContent.gameObject.AddComponent<ContentSizeFitter>();
            diffFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            diffScroll.viewport = diffViewport;
            diffScroll.content = diffContent;
            _diffListContent = diffContent;

            // download-by-id bar
            var idGO = NewRect("DownloadId", rootRect);
            Anchor(idGO, new Vector2(0, 0), new Vector2(0.2f, 0), new Vector2(40, 20), new Vector2(-10, 60));
            idGO.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.2f);
            _downloadIdField = idGO.gameObject.AddComponent<InputField>();
            _downloadIdField.contentType = InputField.ContentType.IntegerNumber;
            var idLabel = NewText(idGO, "", 15, TextAnchor.MiddleLeft);
            Stretch(idLabel.rectTransform, 8, 2);
            var idPlaceholder = NewText(idGO, "Beatmapset id...", 15, TextAnchor.MiddleLeft);
            idPlaceholder.color = new Color(1, 1, 1, 0.4f);
            Stretch(idPlaceholder.rectTransform, 8, 2);
            _downloadIdField.textComponent = idLabel;
            _downloadIdField.placeholder = idPlaceholder;

            var dlBtnGO = NewRect("DownloadBtn", rootRect);
            Anchor(dlBtnGO, new Vector2(0.2f, 0), new Vector2(0.3f, 0), new Vector2(0, 20), new Vector2(-10, 60));
            var dlImg = dlBtnGO.gameObject.AddComponent<Image>();
            dlImg.color = new Color(1, 1, 1, 0.28f);
            var dlBtn = dlBtnGO.gameObject.AddComponent<Button>();
            dlBtn.targetGraphic = dlImg;
            var dlLabel = NewText(dlBtnGO, "Download", 15, TextAnchor.MiddleCenter);
            Stretch(dlLabel.rectTransform);
            dlLabel.raycastTarget = false;
            dlBtn.onClick.AddListener(OnDownloadClicked);

            _downloadStatusText = NewText(rootRect, "", 14, TextAnchor.MiddleLeft);
            Anchor(_downloadStatusText.rectTransform, new Vector2(0.3f, 0), new Vector2(1, 0), new Vector2(10, 20), new Vector2(-40, 60));

            Hide();
        }

        // ------------------------------------------------------------- small UI helpers

        private RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private Text NewText(Transform parent, string text, int size, TextAnchor anchor, FontStyle style = FontStyle.Normal)
        {
            var rect = NewRect("Text", parent);
            // Default to filling the parent. A bare RectTransform starts at a small default size, so a label
            // whose caller forgets to anchor/stretch it would otherwise render in a tiny mispositioned box
            // (text clipped mostly off-screen). Callers that want a specific frame still override this after.
            Stretch(rect);
            var t = rect.gameObject.AddComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = anchor;
            t.color = Color.white;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private static void Stretch(RectTransform r, float insetX = 0, float insetY = 0)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(insetX, insetY);
            r.offsetMax = new Vector2(-insetX, -insetY);
        }

        private static void Anchor(RectTransform r, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = offsetMin;
            r.offsetMax = offsetMax;
        }
    }
}
