using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OsuUnity.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// INDEX: The map browser screen — owns browse state (results → filter/sort → selection), drives the chrome, search, and media, and turns Enter into either a mirror download+auto-import or a play request. Built at runtime, keyboard-first (U6).
namespace OsuUnity.UI
{
    /// <summary>
    /// The browse screen (docs/UI-DESIGN §2.3): mirror search results on the left, search + filters and the
    /// selected map's detail on the right. Behaviour only — <see cref="MapBrowserView"/> owns the chrome,
    /// <see cref="MapBrowserSearch"/> the queries, <see cref="MapBrowserMedia"/> the demo + cover, and
    /// <see cref="BrowseSet"/> / <see cref="BrowseFilters"/> / <see cref="BrowseText"/> the data and strings.
    /// This class is the only place that holds browse <i>state</i>.
    ///
    /// <para>Keyboard-first, mirroring <see cref="SongSelectUI"/>: type to search without focusing the field,
    /// ↑↓ moves map, ←→ moves difficulty, Enter is the primary action. Esc is <see cref="Bootstrap"/>'s.</para>
    ///
    /// <para>Owns no library: <see cref="Populate"/> feeds it the imported sets so results can show the ✓
    /// marker, and an import is reported back through <see cref="SetImported"/> rather than re-scanning here.</para>
    /// </summary>
    public sealed class MapBrowser : MonoBehaviour
    {
        /// <summary>Raised when the player picks an already-imported set — the host routes them to song select.</summary>
        public event Action<int> PlayRequested;

        /// <summary>Raised after a download auto-imports, so the host can refresh its library view.</summary>
        public event Action<BeatmapSetInfo> SetImported;

        // The canvas this screen builds. Also the scope of its editor preview: SongSelectUI tags its preview
        // canvas the same way, so a teardown that matched on the tag alone would clear the other screen too.
        private const string CanvasName = "MapBrowserCanvas";

        // ------------------------------------------------------------- state
        private readonly List<BrowseSet> _results = new List<BrowseSet>();   // raw, in mirror order
        private List<BrowseSet> _shown = new List<BrowseSet>();              // after filters + sort
        private readonly HashSet<int> _library = new HashSet<int>();         // imported online set ids (✓)

        private readonly BrowseFilters _filters = new BrowseFilters();
        private BrowseSort _sort = BrowseSort.Relevance;
        private string _search = "";
        private int _index = -1;      // selected set within _shown
        private int _diffIndex = 0;   // selected difficulty within that set

        private MapBrowserView _view;
        private MapBrowserSearch _searcher;
        private MapBrowserMedia _media;
        private readonly List<Image> _setMarkers = new List<Image>();
        private readonly List<Image> _diffMarkers = new List<Image>();

        // ------------------------------------------------------------- editor authoring (developer overrides)
        // Optional styled row prefabs, same contract as SongSelectUI: null → the code default row is built, so
        // the screen works with zero wiring; assign a prefab carrying a UiRow and the screen only fills its
        // named slots and drives selection. See UiScaffold.cs.
        [Header("Editor authoring — optional row prefabs (null = code default)")]
        [SerializeField] private GameObject setRowPrefab;
        [SerializeField] private GameObject diffRowPrefab;

        private BrowseSet Current => (_index >= 0 && _index < _shown.Count) ? _shown[_index] : null;

        private void Awake()
        {
            DestroyEditorPreview();   // a leftover editor preview must not survive into play as a duplicate
            Build();
        }

        // Re-entrant by design: the editor preview rebuilds on demand, so the two helpers are get-or-added and
        // their events re-bound rather than stacked (a second subscription would fire every handler twice).
        private void Build()
        {
            _searcher = UiScaffold.Ensure<MapBrowserSearch>(gameObject);
            _searcher.IsInLibrary = id => _library.Contains(id);
            _searcher.Started -= OnSearchStarted;
            _searcher.Completed -= OnSearchCompleted;
            _searcher.Started += OnSearchStarted;
            _searcher.Completed += OnSearchCompleted;

            _media = UiScaffold.Ensure<MapBrowserMedia>(gameObject);

            _view = MapBrowserView.Build(new MapBrowserView.Callbacks
            {
                SearchChanged = OnSearchChanged,
                SortClicked = CycleSort,
                FiltersReset = ResetFilters,
                PrimaryClicked = PrimaryAction,
                FilterChanged = OnFilterChanged,
            }, setRowPrefab, diffRowPrefab);

            UpdateSortLabel();
        }

        // ------------------------------------------------------------- public API (the host drives these)

        /// <summary>Tell the browser which sets are already imported, so results can carry the ✓ marker.</summary>
        public void Populate(List<BeatmapSetInfo> sets)
        {
            _library.Clear();
            if (sets != null)
                foreach (var s in sets)
                    if (s.OnlineSetId.HasValue) _library.Add(s.OnlineSetId.Value);

            // Results already on screen may predate an import (e.g. downloaded from song select).
            foreach (var r in _results)
                if (r.Status != BeatmapDownloadStatus.Downloading && _library.Contains(r.Id))
                    r.Status = BeatmapDownloadStatus.Downloaded;
            if (_view != null && _view.Root.activeInHierarchy) { BuildSetRows(); Reselect(); }
        }

        public void Show()
        {
            _view.Root.SetActive(true);
            // First open lands on the mirror's default listing rather than an empty screen (§1.3 certainty).
            if (_results.Count == 0) _searcher.Query(_search);
            else StartMedia();
        }

        public void Hide()
        {
            _media.Stop();
            _searcher.Cancel();
            _view.Root.SetActive(false);
        }

        // ------------------------------------------------------------- search

        private void OnSearchChanged(string v)
        {
            _search = v ?? "";
            _searcher.Query(_search);
        }

        private void OnSearchStarted()
        {
            _view.ResultsStatus.gameObject.SetActive(true);
            _view.ResultsStatus.text = "Searching…";
        }

        private void OnSearchCompleted(List<BrowseSet> results)
        {
            _results.Clear();
            _media.Stop();

            if (results == null)
            {
                _shown = new List<BrowseSet>();
                BuildSetRows();
                ClearDetail();
                _view.ResultsStatus.gameObject.SetActive(true);
                _view.ResultsStatus.text = "Search failed — offline or mirrors unreachable.";
                return;
            }

            _results.AddRange(results);
            ApplyFilters(selectFirst: true);
        }

        // ------------------------------------------------------------- filters + sort

        private void OnFilterChanged(MapBrowserView.Filter which, bool isMin, float v)
        {
            switch (which)
            {
                case MapBrowserView.Filter.Stars: if (isMin) _filters.StarMin = v; else _filters.StarMax = v; break;
                case MapBrowserView.Filter.Length: if (isMin) _filters.LenMin = v; else _filters.LenMax = v; break;
                case MapBrowserView.Filter.Bpm: if (isMin) _filters.BpmMin = v; else _filters.BpmMax = v; break;
            }
            ApplyFilters(selectFirst: true);
        }

        private void ResetFilters()
        {
            _filters.Reset();
            _view.SetFilter(MapBrowserView.Filter.Stars, true, BrowseFilters.StarLo);
            _view.SetFilter(MapBrowserView.Filter.Stars, false, BrowseFilters.StarHi);
            _view.SetFilter(MapBrowserView.Filter.Length, true, BrowseFilters.LenLo);
            _view.SetFilter(MapBrowserView.Filter.Length, false, BrowseFilters.LenHi);
            _view.SetFilter(MapBrowserView.Filter.Bpm, true, BrowseFilters.BpmLo);
            _view.SetFilter(MapBrowserView.Filter.Bpm, false, BrowseFilters.BpmHi);
            ApplyFilters(selectFirst: true);
        }

        private void CycleSort()
        {
            _sort = (BrowseSort)(((int)_sort + 1) % Enum.GetValues(typeof(BrowseSort)).Length);
            UpdateSortLabel();
            ApplyFilters();
        }

        private void UpdateSortLabel()
        {
            if (_view?.SortLabel != null) _view.SortLabel.text = $"Sort: {_sort}";
        }

        // Filter + sort _results into _shown, keeping the current map selected when it survives. OrderBy is a
        // stable sort, so equal keys keep the mirror's own relevance order underneath.
        private void ApplyFilters(bool selectFirst = false)
        {
            var keep = selectFirst ? null : Current;

            IEnumerable<BrowseSet> q = _results.Where(_filters.Passes);
            _shown = _sort switch
            {
                BrowseSort.Stars => q.OrderBy(s => s.StarsHi).ToList(),
                BrowseSort.Length => q.OrderBy(s => s.Diffs[0].LengthSec).ToList(),
                BrowseSort.Bpm => q.OrderBy(s => s.Diffs[0].Bpm).ToList(),
                BrowseSort.Title => q.OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase).ToList(),
                BrowseSort.Artist => q.OrderBy(s => s.Artist, StringComparer.OrdinalIgnoreCase).ToList(),
                _ => q.ToList(),   // Relevance — the mirror's own order
            };

            BuildSetRows();

            int idx = keep != null ? _shown.IndexOf(keep) : -1;
            if (idx < 0) idx = _shown.Count > 0 ? 0 : -1;
            SelectSet(idx);

            _view.ResultsStatus.gameObject.SetActive(_shown.Count == 0);
            if (_shown.Count == 0)
                _view.ResultsStatus.text = _results.Count == 0
                    ? "No results. Try a different search."
                    : "No matches for the current filters.";
        }

        // ------------------------------------------------------------- rows

        private void BuildSetRows()
        {
            _view.ResultList.Clear();
            _setMarkers.Clear();

            for (int i = 0; i < _shown.Count; i++)
            {
                int rowIndex = i;
                var set = _shown[i];
                var row = _view.ResultList.CreateRow();
                if (row == null) continue;

                if (row.button != null)
                    row.button.onClick.AddListener(() =>
                    {
                        if (_index == rowIndex) PrimaryAction();
                        else SelectSet(rowIndex);
                    });

                if (row.title != null) row.title.text = BrowseText.SetTitle(set);
                if (row.subtitle != null) row.subtitle.text = BrowseText.SetSubtitle(set);
                if (row.marker != null) row.marker.enabled = false;

                _setMarkers.Add(row.marker);
            }
        }

        private void BuildDiffRows(BrowseSet set)
        {
            _view.DiffList.Clear();
            _diffMarkers.Clear();

            for (int i = 0; i < set.Diffs.Count; i++)
            {
                int diffIndex = i;
                var d = set.Diffs[i];
                var row = _view.DiffList.CreateRow();
                if (row == null) continue;

                if (row.button != null)
                    row.button.onClick.AddListener(() =>
                    {
                        if (_diffIndex == diffIndex) PrimaryAction();
                        else SelectDiff(diffIndex);
                    });

                if (row.title != null) row.title.text = BrowseText.DiffRow(d);
                if (row.marker != null) row.marker.enabled = false;

                _diffMarkers.Add(row.marker);
            }
        }

        // ------------------------------------------------------------- selection

        private void SelectSet(int index)
        {
            _index = Mathf.Clamp(index, -1, _shown.Count - 1);
            _diffIndex = 0;

            for (int i = 0; i < _setMarkers.Count; i++)
                if (_setMarkers[i] != null) _setMarkers[i].enabled = i == _index;

            var set = Current;
            if (set == null)
            {
                ClearDetail();
                _media.Stop();
                return;
            }

            _view.DetailTitle.text = BrowseText.DetailTitle(set);
            BuildDiffRows(set);
            // Open question on the board: the default-difficulty rule (proposal: highest-star, as a setting).
            // Until it's answered this stays on the easiest diff — Diffs are sorted easiest → hardest.
            SelectDiff(0);
            MapBrowserRows.ScrollTo(_view.ResultsScroll, _index, _shown.Count);
            StartMedia();
        }

        private void SelectDiff(int index)
        {
            var set = Current;
            if (set == null || set.Diffs.Count == 0) { _diffIndex = 0; _view.DetailMeta.text = ""; return; }

            _diffIndex = Mathf.Clamp(index, 0, set.Diffs.Count - 1);
            for (int i = 0; i < _diffMarkers.Count; i++)
                if (_diffMarkers[i] != null) _diffMarkers[i].enabled = i == _diffIndex;

            _view.DetailMeta.text = BrowseText.DiffMeta(set.Diffs[_diffIndex]);
            MapBrowserRows.ScrollTo(_view.DiffScroll, _diffIndex, set.Diffs.Count);
            UpdatePrimary();
            _view.PrimaryButton.gameObject.SetActive(true);
        }

        // Re-mark the current selection after a row rebuild that didn't change what's selected.
        private void Reselect()
        {
            for (int i = 0; i < _setMarkers.Count; i++)
                if (_setMarkers[i] != null) _setMarkers[i].enabled = i == _index;
            UpdatePrimary();
        }

        private void ClearDetail()
        {
            _view.DetailTitle.text = "Select a map";
            _view.DetailMeta.text = "";
            _view.DiffList.Clear();
            _diffMarkers.Clear();
            _view.PrimaryButton.gameObject.SetActive(false);
            SetCover(null);
        }

        private void UpdatePrimary()
        {
            var lbl = _view.PrimaryButton != null ? _view.PrimaryButton.GetComponentInChildren<TMP_Text>() : null;
            if (lbl != null) lbl.text = BrowseText.PrimaryLabel(Current);
        }

        // ------------------------------------------------------------- media (demo + cover)

        private void StartMedia()
        {
            var set = Current;
            if (set == null) { _media.Stop(); SetCover(null); return; }

            _media.Play(set.Id);
            int id = set.Id;
            SetCover(null);
            _media.LoadCover(id, tex =>
            {
                if (Current == null || Current.Id != id) return;   // player moved on while it loaded
                SetCover(tex);
            });
        }

        private void SetCover(Texture2D tex)
        {
            _view.Cover.texture = tex;
            _view.Cover.color = tex != null ? Color.white : new Color(1, 1, 1, 0);
            _view.Backdrop.texture = tex;
            _view.Backdrop.color = tex != null ? new Color(1, 1, 1, 0.30f) : new Color(1, 1, 1, 0);
        }

        // ------------------------------------------------------------- primary action

        // Enter / the primary button: download the selected set (auto-imports through the existing .osz
        // pipeline), or hand an already-imported set to the host to play.
        private void PrimaryAction()
        {
            var set = Current;
            if (set == null) return;

            if (set.Status == BeatmapDownloadStatus.Downloaded)
            {
                _media.Stop();
                PlayRequested?.Invoke(set.Id);
                return;
            }
            if (set.Status == BeatmapDownloadStatus.Downloading) return;

            set.Status = BeatmapDownloadStatus.Downloading;
            UpdatePrimary();
            _view.ActionStatus.text = $"Downloading {BrowseText.SetTitle(set)}…";
            StartCoroutine(DownloadRoutine(set));
        }

        private IEnumerator DownloadRoutine(BrowseSet set)
        {
            yield return BeatmapLibrary.DownloadSet(set.Id,
                p => _view.ActionStatus.text = $"Downloading {set.Title}… {p * 100f:0}%",
                result =>
                {
                    if (result == null || result.Difficulties.Count == 0)
                    {
                        set.Status = BeatmapDownloadStatus.Failed;
                        _view.ActionStatus.text = result == null
                            ? "Download failed (bad id or mirrors unreachable)."
                            : "Downloaded, but that set has no osu!standard difficulty.";
                        UpdatePrimary();
                        return;
                    }

                    set.Status = BeatmapDownloadStatus.Downloaded;
                    _library.Add(set.Id);
                    _view.ActionStatus.text = $"Added to library: {result.SetName}  ·  Enter to play";
                    SetImported?.Invoke(result);

                    // Re-render the rows so the ✓ shows, then restore the selection the rebuild dropped.
                    BuildSetRows();
                    Reselect();
                });
        }

        // ------------------------------------------------------------- keyboard flow

        private void Update()
        {
            if (_view == null || !_view.Root.activeInHierarchy) return;
            if (UiInput.Typing) return;   // the field owns input (incl. the caret) once it has focus

            // Type-to-search without focusing the field (§1.5), same as song select.
            bool changed = false;
            foreach (char c in Input.inputString)
            {
                if (c == '\b') { if (_search.Length > 0) { _search = _search.Substring(0, _search.Length - 1); changed = true; } }
                else if (c == '\n' || c == '\r') { /* Enter is the primary action, below */ }
                else if (!char.IsControl(c)) { _search += c; changed = true; }
            }
            if (changed)
            {
                _view.SearchField.SetTextWithoutNotify(_search);
                _searcher.Query(_search);
            }

            if (Input.GetKeyDown(KeyCode.DownArrow)) SelectSet(_index + 1);
            else if (Input.GetKeyDown(KeyCode.UpArrow)) SelectSet(_index - 1);
            else if (Input.GetKeyDown(KeyCode.RightArrow)) SelectDiff(_diffIndex + 1);
            else if (Input.GetKeyDown(KeyCode.LeftArrow)) SelectDiff(_diffIndex - 1);
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) PrimaryAction();
        }

        // ------------------------------------------------------------- editor authoring entry

        /// <summary>
        /// Build (or refresh) the browse screen in the editor with sample rows in both lists, so a developer
        /// can style the layout — and especially the row prefabs — in context. Right-click the component →
        /// this menu item. The preview is a throwaway: it is tagged and auto-removed on entering Play (the
        /// real screen builds fresh), and "Clear editor preview" removes it by hand.
        /// </summary>
        [ContextMenu("Fallcall/Build or Refresh editor preview")]
        private void EditorBuildPreview()
        {
            UiScaffold.EditAuthoring = true;
            try
            {
                DestroyEditorPreview();   // drop any stale preview (incl. after a script reload) → clean rebuild
                Build();
                _view.Root.SetActive(true);   // Build ends hidden; show it for authoring
                if (_view.Root.GetComponent<UiPlaceholder>() == null) _view.Root.AddComponent<UiPlaceholder>();

                _view.ResultList?.ShowPlaceholders(6, (i, r) =>
                {
                    if (r.title != null) r.title.text = $"Artist {i + 1} - Sample Track {i + 1}";
                    if (r.subtitle != null) r.subtitle.text = $"Mapper {i + 1}  ·  {i + 2} diffs  ·  {2.5f + i:0.##}★";
                    if (r.marker != null) r.marker.enabled = i == 0;
                });
                _view.DiffList?.ShowPlaceholders(4, (i, r) =>
                {
                    if (r.title != null) r.title.text = $"<b>{3.2f + i:0.00}★</b>  [Sample Diff {i + 1}]";
                    if (r.marker != null) r.marker.enabled = i == 0;
                });
                if (_view.DetailTitle != null) _view.DetailTitle.text = "Sample Artist - Sample Track";
                _view.PrimaryButton?.gameObject.SetActive(true);
            }
            finally { UiScaffold.EditAuthoring = false; }
        }

        [ContextMenu("Fallcall/Clear editor preview")]
        private void EditorClearPreview() => DestroyEditorPreview();

        // Remove this screen's editor-preview canvas (a UiPlaceholder tag on a Canvas named CanvasName).
        // The name check is load-bearing: SongSelectUI tags its own preview canvas the same way, so matching
        // on the tag alone would have the two screens tearing down each other's previews.
        private void DestroyEditorPreview()
        {
            foreach (var ph in FindObjectsOfType<UiPlaceholder>(true))
            {
                if (ph == null || ph.GetComponent<Canvas>() == null) continue;
                if (ph.gameObject.name != CanvasName) continue;
                if (Application.isPlaying) Destroy(ph.gameObject);
                else DestroyImmediate(ph.gameObject);
            }
            _view = null;   // force a fresh Build after a preview is cleared
        }
    }
}
