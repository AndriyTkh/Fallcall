using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using OsuUnity.Beatmaps;
using OsuUnity.UI;
using OsuUnity.Util;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

// INDEX: Song select on the U1 UI kit — Local/Online sources (mirror search + download→auto-import), set carousel + detail panel, audio preview (PreviewTime local / ppy preview online), star/length/BPM filters + sort (defaults to Date Added), type-to-search, full keyboard nav, glance metadata (CS/AR/OD/HP/len/BPM). Built at runtime via uGUI+TMP.
namespace OsuUnity.Gameplay
{
    /// <summary>
    /// Beatmap selection (docs/UI-DESIGN §2.2), rebuilt on the U1 <see cref="UiKit"/>: a carousel of
    /// <see cref="BeatmapSetInfo"/> from <see cref="BeatmapLibrary"/> on the left, a detail panel (background,
    /// difficulty list with glance metadata, Play) on the right. Selecting a set previews its audio from the
    /// map's <c>PreviewTime</c> (fallback ~40% in). Search-as-you-type (no field focus needed), star/length/BPM
    /// filters + sort, and a full keyboard-only flow (type → search, arrows → move, Enter → play; Esc → back is
    /// owned by <see cref="Bootstrap"/>). Built entirely at runtime (no scene wiring), same convention as before;
    /// <see cref="Bootstrap"/> owns scanning + launching and reports the choice via <see cref="PlaySelected"/>.
    ///
    /// <para>Metadata beyond stars (CS/AR/OD/HP, length, BPM, PreviewTime, audio/background paths) is not on
    /// <see cref="BeatmapDifficultyInfo"/>, so it is parsed lazily from the <c>.osu</c> and cached here
    /// (this block only owns <c>SongSelectUI.cs</c>; it does not touch <see cref="BeatmapLibrary"/>).</para>
    /// </summary>
    public sealed class SongSelectUI : MonoBehaviour
    {
        public event Action<BeatmapSetInfo, BeatmapDifficultyInfo> PlaySelected;

        private enum SortMode { DateAdded, Title, Artist, Stars, SetId }
        private enum Source { Local, Online }

        // ------------------------------------------------------------- state
        private Source _source = Source.Local;
        private List<BeatmapSetInfo> _allSets = new List<BeatmapSetInfo>();
        private List<BeatmapSetInfo> _filtered = new List<BeatmapSetInfo>();
        private int _setIndex = -1;     // selected set within _filtered
        private int _diffIndex = 0;     // selected difficulty within the selected set

        // Online source (mirror search results). Parallel to the local carousel, rendered into the same widgets.
        private sealed class OnlineDiff
        {
            public string Version;
            public double Stars, Cs, Ar, Od, Hp, Bpm;
            public int LengthSec;
        }
        private sealed class OnlineSet
        {
            public int Id;
            public string Artist, Title, Creator;
            public readonly List<OnlineDiff> Diffs = new List<OnlineDiff>();
            public BeatmapDownloadStatus Status = BeatmapDownloadStatus.NotDownloaded;
        }
        private readonly List<OnlineSet> _online = new List<OnlineSet>();
        private int _onlineIndex = -1;
        private int _onlineDiffIndex = 0;
        private Coroutine _searchCo, _onlinePreviewCo, _onlineBgCo;
        private int _searchSeq;   // guards against a stale search response overwriting a newer one

        private string _search = "";
        private SortMode _sort = SortMode.DateAdded;   // newest import first — how players usually look for a map

        // Filter bounds. A filter is "active" only when narrowed from its full span (so we avoid parsing
        // every map just to draw the default view). Stars come free from BeatmapDifficultyInfo; length/BPM
        // force a lazy parse (cached).
        private const float StarLo = 0f, StarHi = 10f, LenLo = 0f, LenHi = 600f, BpmLo = 0f, BpmHi = 400f;
        private float _starMin = StarLo, _starMax = StarHi;
        private float _lenMin = LenLo, _lenMax = LenHi;     // seconds
        private float _bpmMin = BpmLo, _bpmMax = BpmHi;

        private bool LenActive => _lenMin > LenLo + 0.5f || _lenMax < LenHi - 0.5f;
        private bool BpmActive => _bpmMin > BpmLo + 0.5f || _bpmMax < BpmHi - 0.5f;

        // ------------------------------------------------------------- widgets
        private GameObject _root;
        private RawImage _bgImage;
        private TMP_InputField _searchField;
        private TMP_Text _sortLabel;
        private TMP_Text _sourceLabel;
        private TMP_Text _hintLabel;
        private TMP_Text _statusText;
        private ScrollRect _carousel;
        private RectTransform _listContent;
        private GameObject _filterPanel;

        private TMP_Text _detailTitle;
        private RectTransform _diffListContent;
        private TMP_Text _detailMeta;
        private Button _playButton;

        private TMP_InputField _downloadIdField;
        private TMP_Text _downloadStatus;

        private readonly List<SetRow> _setRows = new List<SetRow>();
        private readonly List<DiffRow> _diffRows = new List<DiffRow>();

        // ------------------------------------------------------------- editor authoring (developer overrides)
        // Optional styled row prefabs. Leave null → the code-built default row (byte-identical to before)
        // is used, so the screen works with zero wiring. Assign a prefab (must carry a UiRow) to restyle
        // the beatmap / difficulty rows with custom art + effects; the screen only fills the UiRow slots
        // and drives selection, so the developer owns everything else on the prefab. See UiScaffold.cs.
        [Header("Editor authoring — optional row prefabs (null = code default)")]
        [SerializeField] private GameObject setRowPrefab;
        [SerializeField] private GameObject diffRowPrefab;
        private UiListView _setList;    // lives on _listContent (drives local + online set rows)
        private UiListView _diffList;   // lives on _diffListContent (drives local + online diff rows)

        // ------------------------------------------------------------- audio preview
        private AudioSource _audio;
        private Coroutine _previewCo;
        private string _previewPath;
        private AudioClip _previewClip;
        private Coroutine _bgCo;
        private Coroutine _artCo;  // walks the carousel filling in card art (see PrefetchSetArt)
        private bool _launching;   // one-shot guard so a play action can't fire PlaySelected twice

        // ------------------------------------------------------------- lazy .osu metadata cache
        private sealed class DiffMeta
        {
            public bool Parsed;
            public float Cs, Ar, Od, Hp;
            public int PreviewTime = -1;   // ms; -1 = none
            public int LengthMs;
            public double BpmMin, BpmMax;
            public string AudioPath;
            public string BackgroundPath;
        }
        private readonly Dictionary<string, DiffMeta> _metaCache = new Dictionary<string, DiffMeta>();

        private struct SetRow { public GameObject Go; public Image Marker; }
        private struct DiffRow { public GameObject Go; public Image Marker; }

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.loop = false;
            DestroyEditorPreview();   // a leftover editor preview must not survive into play as a duplicate
            Build();
        }

        // ------------------------------------------------------------- editor authoring entry

        /// <summary>
        /// Build (or refresh) the song-select screen in the editor and drop sample rows into both lists, so
        /// a developer can style the layout — and especially the row prefabs (<c>setRowPrefab</c> /
        /// <c>diffRowPrefab</c>) — in context. Right-click the component → this menu item. The preview is a
        /// throwaway: it is tagged and auto-removed when you enter Play (the real screen builds fresh), and
        /// "Clear editor preview" removes it by hand. Nothing here changes runtime behaviour.
        /// </summary>
        [ContextMenu("Fallcall/Build or Refresh editor preview")]
        private void EditorBuildPreview()
        {
            UiScaffold.EditAuthoring = true;
            try
            {
                DestroyEditorPreview();   // drop any stale preview (incl. after a script reload) → clean rebuild
                Build();
                _root.SetActive(true);                       // Build ends hidden; show it for authoring
                if (_root.GetComponent<UiPlaceholder>() == null) _root.AddComponent<UiPlaceholder>();

                _setList?.ShowPlaceholders(6, (i, r) =>
                {
                    if (r.title != null) r.title.text = $"Artist {i + 1} - Sample Track {i + 1}";
                    if (r.subtitle != null) r.subtitle.text = $"{i + 2} diffs  ·  {2.5f + i:0.##}★";
                    if (r.marker != null) r.marker.enabled = i == 0;
                });
                _diffList?.ShowPlaceholders(4, (i, r) =>
                {
                    if (r.title != null) r.title.text = $"<b>{3.2f + i:0.00}★</b>  [Sample Diff {i + 1}]";
                    if (r.marker != null) r.marker.enabled = i == 0;
                });
                if (_detailTitle != null) _detailTitle.text = "Sample Artist - Sample Track";
            }
            finally { UiScaffold.EditAuthoring = false; }
        }

        [ContextMenu("Fallcall/Clear editor preview")]
        private void EditorClearPreview() => DestroyEditorPreview();

        // Remove any editor-preview canvas (a UiPlaceholder tag sitting on a Canvas). Individual placeholder
        // rows live under it and go with it; real runtime rows are never tagged, so this never touches them.
        private void DestroyEditorPreview()
        {
            foreach (var ph in FindObjectsOfType<UiPlaceholder>(true))
            {
                if (ph == null || ph.GetComponent<Canvas>() == null) continue;
                if (Application.isPlaying) Destroy(ph.gameObject);
                else DestroyImmediate(ph.gameObject);
            }
            _root = null;   // force a fresh Build after a preview is cleared
        }

        // ------------------------------------------------------------- public API

        public void Populate(List<BeatmapSetInfo> sets)
        {
            _allSets = sets ?? new List<BeatmapSetInfo>();
            _launching = false;
            _source = Source.Local;
            UpdateSourceLabel();
            UpdateHint();
            RefreshList(selectFirst: true);
        }

        public void Show()
        {
            _launching = false;
            _root.SetActive(true);
            if (_source == Source.Local)
            {
                if (_setIndex >= 0 && _setIndex < _filtered.Count) StartPreview(RepDiff(_filtered[_setIndex]));
            }
            else if (_onlineIndex >= 0 && _onlineIndex < _online.Count)
            {
                StartOnlinePreview(_online[_onlineIndex]);
            }
        }

        public void Hide()
        {
            StopPreview();
            _root.SetActive(false);
        }

        /// <summary>
        /// Switch to Local and select the set with this online id — how the U6 map browser hands a map it has
        /// already imported over to be played. No-op when the id isn't in the library.
        /// </summary>
        public void FocusLocalSet(int onlineSetId) => JumpToLocal(onlineSetId);

        // ------------------------------------------------------------- filtering + sort

        private void RefreshList(bool selectFirst = false)
        {
            BeatmapSetInfo keep = (!selectFirst && _setIndex >= 0 && _setIndex < _filtered.Count) ? _filtered[_setIndex] : null;

            IEnumerable<BeatmapSetInfo> q = _allSets.Where(SetPasses);

            _filtered = _sort switch
            {
                // Newest import first, then title so a bulk import (one shared timestamp) still reads in order.
                SortMode.DateAdded => q.OrderByDescending(s => s.DateAddedUtc)
                                       .ThenBy(SetTitle, StringComparer.OrdinalIgnoreCase).ToList(),
                SortMode.Artist => q.OrderBy(SetArtist, StringComparer.OrdinalIgnoreCase).ToList(),
                SortMode.Stars => q.OrderBy(s => s.Difficulties.Count > 0 ? s.Difficulties.Max(d => d.Stars) : 0.0).ToList(),
                SortMode.SetId => q.OrderBy(s => s.OnlineSetId ?? int.MaxValue).ToList(),
                _ => q.OrderBy(SetTitle, StringComparer.OrdinalIgnoreCase).ToList(),
            };

            BuildSetRows();

            int idx = keep != null ? _filtered.IndexOf(keep) : -1;
            if (idx < 0) idx = _filtered.Count > 0 ? 0 : -1;
            SelectSet(idx, focusRow: false);

            _statusText.gameObject.SetActive(_filtered.Count == 0);
            if (_filtered.Count == 0)
                _statusText.text = _allSets.Count == 0
                    ? "No beatmaps found.\nDrop .osz files in the Songs folder, or download one by id below."
                    : "No matches for the current search / filters.";
        }

        private bool SetPasses(BeatmapSetInfo set)
        {
            if (!MatchesSearch(set)) return false;
            foreach (var d in set.Difficulties)
                if (DiffPassesFilters(d)) return true;
            return false;
        }

        private bool MatchesSearch(BeatmapSetInfo set)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            string term = _search.ToLowerInvariant();
            if (set.SetName.ToLowerInvariant().Contains(term)) return true;
            foreach (var d in set.Difficulties)
                if (d.Artist.ToLowerInvariant().Contains(term) ||
                    d.Title.ToLowerInvariant().Contains(term) ||
                    d.Version.ToLowerInvariant().Contains(term)) return true;
            return false;
        }

        private bool DiffPassesFilters(BeatmapDifficultyInfo d)
        {
            float sLo = Mathf.Min(_starMin, _starMax), sHi = Mathf.Max(_starMin, _starMax);
            if (d.Stars < sLo - 0.001f || d.Stars > sHi + 0.001f) return false;

            if (!LenActive && !BpmActive) return true;
            var m = Meta(d);
            if (!m.Parsed) return false;   // can't confirm length/BPM → exclude when those filters are on

            if (LenActive)
            {
                float sec = m.LengthMs / 1000f;
                if (sec < Mathf.Min(_lenMin, _lenMax) || sec > Mathf.Max(_lenMin, _lenMax)) return false;
            }
            if (BpmActive)
            {
                float lo = Mathf.Min(_bpmMin, _bpmMax), hi = Mathf.Max(_bpmMin, _bpmMax);
                if (m.BpmMax < lo || m.BpmMin > hi) return false;   // ranges must overlap
            }
            return true;
        }

        private static string SetTitle(BeatmapSetInfo s) => s.Difficulties.Count > 0 ? s.Difficulties[0].Title : s.SetName;
        private static string SetArtist(BeatmapSetInfo s) => s.Difficulties.Count > 0 ? s.Difficulties[0].Artist : s.SetName;

        // ------------------------------------------------------------- carousel rows

        private void BuildSetRows()
        {
            _setList.Clear();
            _setRows.Clear();

            var cards = new List<UiRow>(_filtered.Count);
            foreach (var set in _filtered)
            {
                var captured = set;
                int rowIndex = _setRows.Count;
                var row = _setList.CreateRow();
                if (row == null) continue;

                if (row.button != null)
                    row.button.onClick.AddListener(() =>
                    {
                        if (_setIndex == rowIndex) PlayCurrent();
                        else SelectSet(rowIndex, focusRow: true);
                    });

                var first = captured.Difficulties.Count > 0 ? captured.Difficulties[0] : null;
                if (row.title != null) row.title.text = first != null ? $"{first.Artist} - {first.Title}" : captured.SetName;
                if (row.subtitle != null) row.subtitle.text = SetSubtitle(captured);
                if (row.marker != null) row.marker.enabled = false;

                cards.Add(row);
                _setRows.Add(new SetRow { Go = row.gameObject, Marker = row.marker });
            }

            PrefetchSetArt(new List<BeatmapSetInfo>(_filtered), cards);
        }

        // Card art for the whole carousel, not just the selection — a library you can only recognise one map
        // at a time is a list of grey rectangles. Local art is the map's own background, whose path only
        // exists after the .osu is parsed, so this walks the list a few sets per frame instead of parsing the
        // entire library in the frame a filter changes. UiCoverCache handles the loads from there.
        private void PrefetchSetArt(List<BeatmapSetInfo> sets, List<UiRow> cards)
        {
            if (_artCo != null) StopCoroutine(_artCo);
            _artCo = StartCoroutine(SetArtRoutine(sets, cards));
        }

        private IEnumerator SetArtRoutine(List<BeatmapSetInfo> sets, List<UiRow> cards)
        {
            for (int i = 0; i < cards.Count && i < sets.Count; i++)
            {
                var card = cards[i];
                if (card == null) continue;   // the list was rebuilt under us; the new one has its own pass

                var first = sets[i].Difficulties.Count > 0 ? sets[i].Difficulties[0] : null;
                string bg = first != null ? Meta(first).BackgroundPath : null;
                UiMapCard.Bind(card, string.IsNullOrEmpty(bg) ? null : AssetLoader.ToFileUrl(bg));

                if ((i & 3) == 3) yield return null;   // ~4 .osu parses per frame
            }
            _artCo = null;
        }

        // The default code-built beatmap-set row: the shared UiMapCard, so the carousel and the browse screen
        // list maps as the same object. Used when no setRowPrefab is assigned, so the screen needs zero
        // wiring; a developer can instead assign a styled prefab (carrying a UiRow with these same slots) to
        // restyle cards without touching this script.
        private UiRow DefaultSetRow(Transform parent) => UiMapCard.Build(parent);

        private static string SetSubtitle(BeatmapSetInfo set)
        {
            int n = set.Difficulties.Count;
            if (n == 0) return set.SetName;
            double lo = set.Difficulties.Min(d => d.Stars), hi = set.Difficulties.Max(d => d.Stars);
            string stars = Mathf.Approximately((float)lo, (float)hi) ? $"{lo:0.##}★" : $"{lo:0.##}–{hi:0.##}★";
            return $"{n} diff{(n == 1 ? "" : "s")}  ·  {stars}";
        }

        // ------------------------------------------------------------- selection + detail

        private void SelectSet(int index, bool focusRow)
        {
            _setIndex = Mathf.Clamp(index, -1, _filtered.Count - 1);
            _diffIndex = 0;

            for (int i = 0; i < _setRows.Count; i++)
                if (_setRows[i].Marker != null) _setRows[i].Marker.enabled = i == _setIndex;

            if (_setIndex < 0)
            {
                ClearDetail();
                StopPreview();
                return;
            }

            var set = _filtered[_setIndex];
            var first = set.Difficulties.Count > 0 ? set.Difficulties[0] : null;
            _detailTitle.text = first != null ? $"{first.Artist} - {first.Title}\n<size=70%><color=#9DB0C6>{set.SetName}</color></size>" : set.SetName;

            BuildDiffRows(set);
            SelectDiff(0);
            ScrollTo(_carousel, _setIndex, _filtered.Count);
            LoadBackground(set);
            StartPreview(RepDiff(set));
        }

        private void BuildDiffRows(BeatmapSetInfo set)
        {
            _diffList.Clear();
            _diffRows.Clear();

            for (int i = 0; i < set.Difficulties.Count; i++)
            {
                var diff = set.Difficulties[i];
                int diffIndex = i;
                var row = _diffList.CreateRow();
                if (row == null) continue;

                if (row.button != null)
                    row.button.onClick.AddListener(() =>
                    {
                        if (_diffIndex == diffIndex) PlayCurrent();
                        else SelectDiff(diffIndex);
                    });

                if (row.title != null) row.title.text = DiffRowText(diff);
                if (row.marker != null) row.marker.enabled = false;

                _diffRows.Add(new DiffRow { Go = row.gameObject, Marker = row.marker });
            }
        }

        // The default code-built difficulty row (single label). Superseded by diffRowPrefab when assigned.
        private UiRow DefaultDiffRow(Transform parent)
        {
            var btn = UiKit.Row(parent, 46f, null, out var content);

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

        private string DiffRowText(BeatmapDifficultyInfo diff)
        {
            var m = Meta(diff);
            var sb = new StringBuilder();
            sb.Append($"<b>{diff.Stars:0.00}★</b>  [{diff.Version}]");
            if (m.Parsed)
            {
                sb.Append($"   <color=#9DB0C6>{FormatLength(m.LengthMs)} · {FormatBpm(m)}BPM · ");
                sb.Append($"CS{m.Cs:0.#} AR{m.Ar:0.#} OD{m.Od:0.#} HP{m.Hp:0.#}</color>");
            }
            return sb.ToString();
        }

        private void SelectDiff(int index)
        {
            if (_setIndex < 0) return;
            var set = _filtered[_setIndex];
            if (set.Difficulties.Count == 0) { _diffIndex = 0; _detailMeta.text = ""; return; }

            _diffIndex = Mathf.Clamp(index, 0, set.Difficulties.Count - 1);
            for (int i = 0; i < _diffRows.Count; i++)
                if (_diffRows[i].Marker != null) _diffRows[i].Marker.enabled = i == _diffIndex;

            var diff = set.Difficulties[_diffIndex];
            var m = Meta(diff);
            _detailMeta.text = m.Parsed
                ? $"<b>{diff.Stars:0.00}★</b>   {FormatLength(m.LengthMs)}   {FormatBpm(m)} BPM\nCS {m.Cs:0.#}   AR {m.Ar:0.#}   OD {m.Od:0.#}   HP {m.Hp:0.#}"
                : $"<b>{diff.Stars:0.00}★</b>   [{diff.Version}]";
            UpdatePlayButtonLabel();
            _playButton.gameObject.SetActive(true);
        }

        private void ClearDetail()
        {
            _detailTitle.text = "Select a beatmap";
            _detailMeta.text = "";
            _diffList.Clear();
            _diffRows.Clear();
            _playButton.gameObject.SetActive(false);
            SetBackground(null);
        }

        private void PlayCurrent()
        {
            if (_launching || _setIndex < 0 || _setIndex >= _filtered.Count) return;
            var set = _filtered[_setIndex];
            if (set.Difficulties.Count == 0) return;
            int di = Mathf.Clamp(_diffIndex, 0, set.Difficulties.Count - 1);
            _launching = true;
            StopPreview();
            PlaySelected?.Invoke(set, set.Difficulties[di]);
        }

        private static BeatmapDifficultyInfo RepDiff(BeatmapSetInfo set)
            => set.Difficulties.Count > 0 ? set.Difficulties[0] : null;

        // ------------------------------------------------------------- keyboard flow

        private void Update()
        {
            if (_root == null || !_root.activeInHierarchy) return;

            // Type-to-search without focusing the field (§2.2): route printable keys / backspace into the
            // search string while nothing else holds text focus. Once the player clicks the field, UiInput
            // .Typing is true and this stands down (the field then owns input, incl. the caret).
            if (!UiInput.Typing)
            {
                bool changed = false;
                foreach (char c in Input.inputString)
                {
                    if (c == '\b') { if (_search.Length > 0) { _search = _search.Substring(0, _search.Length - 1); changed = true; } }
                    else if (c == '\n' || c == '\r') { /* handled below as Enter */ }
                    else if (!char.IsControl(c)) { _search += c; changed = true; }
                }
                if (changed)
                {
                    _searchField.SetTextWithoutNotify(_search);
                    if (_source == Source.Local) RefreshList(selectFirst: true);
                    else ScheduleSearch();
                }

                if (Input.GetKeyDown(KeyCode.DownArrow)) MoveSet(1);
                else if (Input.GetKeyDown(KeyCode.UpArrow)) MoveSet(-1);
                else if (Input.GetKeyDown(KeyCode.RightArrow)) MoveDiff(1);
                else if (Input.GetKeyDown(KeyCode.LeftArrow)) MoveDiff(-1);
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) PrimaryAction();
            }
        }

        // ------------------------------------------------------------- audio preview

        private void StartPreview(BeatmapDifficultyInfo diff)
        {
            if (diff == null) { StopPreview(); return; }
            if (_previewCo != null) StopCoroutine(_previewCo);
            _previewCo = StartCoroutine(PreviewRoutine(diff));
        }

        private IEnumerator PreviewRoutine(BeatmapDifficultyInfo diff)
        {
            yield return new WaitForSeconds(0.22f);   // debounce while arrowing quickly through the list
            if (!_root.activeInHierarchy) yield break;

            var m = Meta(diff);
            if (string.IsNullOrEmpty(m.AudioPath)) { StopPreview(); yield break; }

            if (m.AudioPath != _previewPath || _previewClip == null)
            {
                AudioClip clip = null;
                yield return AssetLoader.LoadAudio(m.AudioPath, c => clip = c);
                if (clip == null || !_root.activeInHierarchy) { StopPreview(); yield break; }
                _previewClip = clip;
                _previewPath = m.AudioPath;
            }

            float t = m.PreviewTime >= 0 ? m.PreviewTime / 1000f : _previewClip.length * 0.4f;
            _audio.clip = _previewClip;
            _audio.time = Mathf.Clamp(t, 0f, Mathf.Max(0f, _previewClip.length - 0.1f));
            _audio.volume = GameSettings.MusicVolume;
            _audio.Play();
        }

        private void StopPreview()
        {
            if (_previewCo != null) { StopCoroutine(_previewCo); _previewCo = null; }
            if (_onlinePreviewCo != null) { StopCoroutine(_onlinePreviewCo); _onlinePreviewCo = null; }
            if (_audio != null && _audio.isPlaying) _audio.Stop();
        }

        // ------------------------------------------------------------- background preview

        private void LoadBackground(BeatmapSetInfo set)
        {
            SetBackground(null);
            if (_bgCo != null) StopCoroutine(_bgCo);
            var first = RepDiff(set);
            if (first != null) _bgCo = StartCoroutine(BackgroundRoutine(first));
        }

        private IEnumerator BackgroundRoutine(BeatmapDifficultyInfo diff)
        {
            var m = Meta(diff);
            if (string.IsNullOrEmpty(m.BackgroundPath)) yield break;
            Texture2D tex = null;
            yield return AssetLoader.LoadTexture(m.BackgroundPath, t => tex = t);
            // still on the same difficulty? (user may have moved on while it loaded)
            if (tex != null && _setIndex >= 0 && RepDiff(_filtered[_setIndex]) == diff) SetBackground(tex);
        }

        private void SetBackground(Texture2D tex)
        {
            _bgImage.texture = tex;
            _bgImage.color = tex != null ? new Color(1, 1, 1, 0.30f) : new Color(1, 1, 1, 0);
        }

        // ------------------------------------------------------------- lazy metadata

        private DiffMeta Meta(BeatmapDifficultyInfo diff)
        {
            if (diff == null) return new DiffMeta();
            if (_metaCache.TryGetValue(diff.OsuPath, out var cached)) return cached;

            var m = new DiffMeta();
            try
            {
                var map = BeatmapParser.ParseFile(diff.OsuPath);
                m.Cs = map.Difficulty.CircleSize;
                m.Ar = map.Difficulty.ApproachRate;
                m.Od = map.Difficulty.OverallDifficulty;
                m.Hp = map.Difficulty.HPDrainRate;
                m.PreviewTime = map.General.PreviewTime;

                int first = int.MaxValue, last = 0;
                foreach (var ho in map.HitObjects)
                {
                    if (ho.StartTime < first) first = ho.StartTime;
                    if (ho.EndTime > last) last = ho.EndTime;
                }
                m.LengthMs = (map.HitObjects.Count > 0 && last > first) ? last - first : 0;

                double bmin = double.MaxValue, bmax = 0;
                foreach (var tp in map.TimingPoints)
                {
                    if (!tp.Uninherited || tp.BeatLength <= 0) continue;
                    double bpm = 60000.0 / tp.BeatLength;
                    if (bpm < bmin) bmin = bpm;
                    if (bpm > bmax) bmax = bpm;
                }
                if (bmax > 0) { m.BpmMin = bmin; m.BpmMax = bmax; }

                if (!string.IsNullOrEmpty(map.General.AudioFilename))
                    m.AudioPath = Path.Combine(map.Directory ?? "", map.General.AudioFilename);
                if (!string.IsNullOrEmpty(map.BackgroundFile))
                    m.BackgroundPath = Path.Combine(map.Directory ?? "", map.BackgroundFile);

                m.Parsed = true;
            }
            catch { m.Parsed = false; }

            _metaCache[diff.OsuPath] = m;
            return m;
        }

        private static string FormatLength(int ms)
        {
            if (ms <= 0) return "--:--";
            int total = ms / 1000;
            return $"{total / 60}:{total % 60:00}";
        }

        private static string FormatBpm(DiffMeta m)
        {
            if (m.BpmMax <= 0) return "--";
            int lo = Mathf.RoundToInt((float)m.BpmMin), hi = Mathf.RoundToInt((float)m.BpmMax);
            return lo == hi ? lo.ToString() : $"{lo}–{hi}";
        }

        // ------------------------------------------------------------- download by id (kept from B; U5 extends)

        private void OnDownloadClicked()
        {
            if (!int.TryParse(_downloadIdField.text.Trim(), out int setId) || setId <= 0)
            {
                _downloadStatus.text = "Enter a valid numeric set id.";
                return;
            }
            _downloadStatus.text = "Downloading...";
            StartCoroutine(BeatmapLibrary.DownloadSet(setId,
                progress => _downloadStatus.text = $"Downloading... {progress * 100f:0}%",
                result =>
                {
                    if (result == null) { _downloadStatus.text = "Download failed (bad id or mirrors unreachable)."; return; }
                    if (result.Difficulties.Count == 0) { _downloadStatus.text = "Downloaded, but that set has no osu!standard difficulty."; return; }
                    _downloadStatus.text = $"Downloaded: {result.SetName}";
                    _allSets.RemoveAll(s => string.Equals(s.OszPath, result.OszPath, StringComparison.OrdinalIgnoreCase));
                    _allSets.Add(result);
                    RefreshList();
                    int idx = _filtered.IndexOf(result);
                    if (idx >= 0) SelectSet(idx, focusRow: true);
                }));
        }

        // ============================================================= source dispatch (Local ⇄ Online)

        // Keyboard + primary-button actions route through these so one set of shortcuts drives both sources.
        private void MoveSet(int delta)
        {
            if (_source == Source.Local) SelectSet(_setIndex + delta, focusRow: true);
            else SelectOnlineSet(_onlineIndex + delta, focusRow: true);
        }

        private void MoveDiff(int delta)
        {
            if (_source == Source.Local) SelectDiff(_diffIndex + delta);
            else SelectOnlineDiff(_onlineDiffIndex + delta);
        }

        private void PrimaryAction()
        {
            if (_source == Source.Local) PlayCurrent();
            else DownloadCurrentOnline();
        }

        private void OnSearchChanged(string v)
        {
            _search = v;
            if (_source == Source.Local) RefreshList(selectFirst: true);
            else ScheduleSearch();
        }

        private void ToggleSource() => SetSource(_source == Source.Local ? Source.Online : Source.Local);

        private void SetSource(Source s)
        {
            _source = s;
            StopPreview();
            _search = "";
            _searchField.SetTextWithoutNotify("");
            UpdateSourceLabel();
            UpdateHint();
            _downloadStatus.text = "";

            if (s == Source.Local)
            {
                RefreshList(selectFirst: true);
            }
            else
            {
                _online.Clear();
                _onlineIndex = -1;
                BuildOnlineRows();
                ClearDetail();
                ScheduleSearch();   // empty query → the mirror's default ranked listing
            }
        }

        private void UpdateSourceLabel()
        {
            if (_sourceLabel != null) _sourceLabel.text = _source == Source.Local ? "View: Local" : "View: Online";
        }

        private void UpdateHint()
        {
            if (_hintLabel == null) return;
            _hintLabel.text = _source == Source.Local
                ? "Type to search  ·  ↑↓ set  ·  ←→ difficulty  ·  Enter play  ·  Esc back"
                : "Type to search online  ·  ↑↓ set  ·  ←→ difficulty  ·  Enter download  ·  Esc back";
        }

        private void UpdatePlayButtonLabel()
        {
            var lbl = _playButton != null ? _playButton.GetComponentInChildren<TMP_Text>() : null;
            if (lbl == null) return;
            if (_source == Source.Local) { lbl.text = "▶  Play"; return; }
            var os = (_onlineIndex >= 0 && _onlineIndex < _online.Count) ? _online[_onlineIndex] : null;
            switch (os?.Status)
            {
                case BeatmapDownloadStatus.Downloading: lbl.text = "Downloading…"; break;
                case BeatmapDownloadStatus.Downloaded: lbl.text = "▶  Play (in library)"; break;
                default: lbl.text = "⬇  Download"; break;
            }
        }

        // ============================================================= online search + results

        private void ScheduleSearch()
        {
            if (_searchCo != null) StopCoroutine(_searchCo);
            _searchCo = StartCoroutine(SearchRoutine(_search));
        }

        private IEnumerator SearchRoutine(string query)
        {
            yield return new WaitForSeconds(0.45f);   // debounce typing before hitting the mirror
            if (_source != Source.Online) yield break;

            int seq = ++_searchSeq;
            _statusText.gameObject.SetActive(true);
            _statusText.text = "Searching…";

            List<BeatmapDownloader.OnlineBeatmapset> res = null;
            yield return BeatmapDownloader.Search(query, 0, r => res = r);
            if (seq != _searchSeq || _source != Source.Online) yield break;   // superseded / switched away

            if (res == null)
            {
                _online.Clear();
                BuildOnlineRows();
                ClearDetail();
                _statusText.gameObject.SetActive(true);
                _statusText.text = "Search failed — offline or mirrors unreachable.";
                yield break;
            }

            SetOnlineResults(res);
            BuildOnlineRows();
            _statusText.gameObject.SetActive(_online.Count == 0);
            if (_online.Count == 0) _statusText.text = "No online results. Try a different search.";
            SelectOnlineSet(_online.Count > 0 ? 0 : -1, focusRow: false);
        }

        private void SetOnlineResults(List<BeatmapDownloader.OnlineBeatmapset> sets)
        {
            _online.Clear();
            if (sets == null) return;
            foreach (var s in sets)
            {
                var os = new OnlineSet { Id = s.id, Artist = s.artist ?? "", Title = s.title ?? "", Creator = s.creator ?? "" };
                if (s.beatmaps != null)
                    foreach (var b in s.beatmaps)
                    {
                        if (b == null || b.mode_int != 0) continue;   // osu!standard only, matches the importer
                        os.Diffs.Add(new OnlineDiff
                        {
                            Version = b.version ?? "",
                            Stars = b.difficulty_rating,
                            Cs = b.cs, Ar = b.ar, Od = b.accuracy, Hp = b.drain, Bpm = b.bpm,
                            LengthSec = b.total_length,
                        });
                    }
                if (os.Diffs.Count == 0) continue;
                os.Diffs.Sort((a, b) => a.Stars.CompareTo(b.Stars));   // easiest → hardest, like the local panel
                if (_allSets.Exists(x => x.OnlineSetId == os.Id)) os.Status = BeatmapDownloadStatus.Downloaded;
                _online.Add(os);
            }
        }

        private void BuildOnlineRows()
        {
            _setList.Clear();
            _setRows.Clear();

            for (int i = 0; i < _online.Count; i++)
            {
                int rowIndex = i;
                var os = _online[i];
                var row = _setList.CreateRow();
                if (row == null) continue;

                if (row.button != null)
                    row.button.onClick.AddListener(() =>
                    {
                        if (_onlineIndex == rowIndex) DownloadCurrentOnline();
                        else SelectOnlineSet(rowIndex, focusRow: true);
                    });

                if (row.title != null) row.title.text = $"{os.Artist} - {os.Title}";
                if (row.subtitle != null) row.subtitle.text = OnlineSubtitle(os);
                if (row.marker != null) row.marker.enabled = false;

                // Online art is a URL, no .osu to parse first — bind the whole page straight away.
                UiMapCard.Bind(row, BeatmapDownloader.CardUrl(os.Id));

                _setRows.Add(new SetRow { Go = row.gameObject, Marker = row.marker });
            }
        }

        private static string OnlineSubtitle(OnlineSet s)
        {
            int n = s.Diffs.Count;
            double lo = s.Diffs[0].Stars, hi = s.Diffs[n - 1].Stars;   // sorted easiest → hardest
            string stars = Mathf.Approximately((float)lo, (float)hi) ? $"{lo:0.##}★" : $"{lo:0.##}–{hi:0.##}★";
            string owned = s.Status == BeatmapDownloadStatus.Downloaded ? "  ·  ✓ in library" : "";
            return $"{s.Creator}  ·  {n} diff{(n == 1 ? "" : "s")}  ·  {stars}{owned}";
        }

        private void SelectOnlineSet(int index, bool focusRow)
        {
            _onlineIndex = Mathf.Clamp(index, -1, _online.Count - 1);
            _onlineDiffIndex = 0;

            for (int i = 0; i < _setRows.Count; i++)
                if (_setRows[i].Marker != null) _setRows[i].Marker.enabled = i == _onlineIndex;

            if (_onlineIndex < 0)
            {
                ClearDetail();
                StopPreview();
                return;
            }

            var os = _online[_onlineIndex];
            _detailTitle.text = $"{os.Artist} - {os.Title}\n<size=70%><color=#9DB0C6>mapped by {os.Creator}</color></size>";

            BuildOnlineDiffRows(os);
            SelectOnlineDiff(0);
            ScrollTo(_carousel, _onlineIndex, _online.Count);
            LoadOnlineBackground(os);
            StartOnlinePreview(os);
        }

        private void BuildOnlineDiffRows(OnlineSet os)
        {
            _diffList.Clear();
            _diffRows.Clear();

            for (int i = 0; i < os.Diffs.Count; i++)
            {
                int diffIndex = i;
                var d = os.Diffs[i];
                var row = _diffList.CreateRow();
                if (row == null) continue;

                if (row.button != null)
                    row.button.onClick.AddListener(() =>
                    {
                        if (_onlineDiffIndex == diffIndex) DownloadCurrentOnline();
                        else SelectOnlineDiff(diffIndex);
                    });

                if (row.title != null) row.title.text = OnlineDiffRowText(d);
                if (row.marker != null) row.marker.enabled = false;

                _diffRows.Add(new DiffRow { Go = row.gameObject, Marker = row.marker });
            }
        }

        private static string OnlineDiffRowText(OnlineDiff d)
            => $"<b>{d.Stars:0.00}★</b>  [{d.Version}]   <color=#9DB0C6>{FormatSec(d.LengthSec)} · {d.Bpm:0}BPM · CS{d.Cs:0.#} AR{d.Ar:0.#} OD{d.Od:0.#} HP{d.Hp:0.#}</color>";

        private void SelectOnlineDiff(int index)
        {
            if (_onlineIndex < 0) return;
            var os = _online[_onlineIndex];
            if (os.Diffs.Count == 0) { _onlineDiffIndex = 0; _detailMeta.text = ""; return; }

            _onlineDiffIndex = Mathf.Clamp(index, 0, os.Diffs.Count - 1);
            for (int i = 0; i < _diffRows.Count; i++)
                if (_diffRows[i].Marker != null) _diffRows[i].Marker.enabled = i == _onlineDiffIndex;

            var d = os.Diffs[_onlineDiffIndex];
            _detailMeta.text = $"<b>{d.Stars:0.00}★</b>   {FormatSec(d.LengthSec)}   {d.Bpm:0} BPM\nCS {d.Cs:0.#}   AR {d.Ar:0.#}   OD {d.Od:0.#}   HP {d.Hp:0.#}";
            UpdatePlayButtonLabel();
            _playButton.gameObject.SetActive(true);
        }

        // Download the selected online set → auto-import via the existing .osz pipeline. Already-imported sets
        // jump straight to the Local tab so the player can play them.
        private void DownloadCurrentOnline()
        {
            if (_onlineIndex < 0 || _onlineIndex >= _online.Count) return;
            var os = _online[_onlineIndex];
            if (os.Status == BeatmapDownloadStatus.Downloaded) { JumpToLocal(os.Id); return; }
            if (os.Status == BeatmapDownloadStatus.Downloading) return;

            os.Status = BeatmapDownloadStatus.Downloading;
            UpdatePlayButtonLabel();
            _downloadStatus.text = $"Downloading {os.Artist} - {os.Title}…";

            StartCoroutine(BeatmapLibrary.DownloadSet(os.Id,
                progress => _downloadStatus.text = $"Downloading {os.Title}… {progress * 100f:0}%",
                result =>
                {
                    if (result == null || result.Difficulties.Count == 0)
                    {
                        os.Status = BeatmapDownloadStatus.Failed;
                        _downloadStatus.text = result == null
                            ? "Download failed (bad id or mirrors unreachable)."
                            : "Downloaded, but that set has no osu!standard difficulty.";
                        UpdatePlayButtonLabel();
                        return;
                    }

                    os.Status = BeatmapDownloadStatus.Downloaded;
                    _downloadStatus.text = $"Added to library: {result.SetName}";
                    _allSets.RemoveAll(s => string.Equals(s.OszPath, result.OszPath, StringComparison.OrdinalIgnoreCase));
                    _allSets.Add(result);

                    // reflect the ✓ in the row + button if we're still on this set/source
                    if (_source == Source.Online)
                    {
                        BuildOnlineRows();
                        for (int i = 0; i < _setRows.Count; i++)
                            if (_setRows[i].Marker != null) _setRows[i].Marker.enabled = i == _onlineIndex;
                        UpdatePlayButtonLabel();
                    }
                }));
        }

        private void JumpToLocal(int onlineId)
        {
            SetSource(Source.Local);
            int idx = _filtered.FindIndex(s => s.OnlineSetId == onlineId);
            if (idx >= 0) SelectSet(idx, focusRow: true);
        }

        // ------------------------------------------------------------- online preview (audio + cover)

        private void StartOnlinePreview(OnlineSet os)
        {
            StopPreview();
            if (os == null) return;
            _onlinePreviewCo = StartCoroutine(OnlinePreviewRoutine(os.Id));
        }

        private IEnumerator OnlinePreviewRoutine(int id)
        {
            yield return new WaitForSeconds(0.30f);   // debounce arrow-key scrubbing
            if (!_root.activeInHierarchy || _source != Source.Online) yield break;

            // Vorbis, not MPEG — b.ppy.sh serves Ogg under the .mp3 extension (docs/osu-api.md §1); the old
            // AudioType.MPEG made this a silent no-op.
            string previewUrl = BeatmapDownloader.PreviewUrl(id);
            using var req = UnityWebRequestMultimedia.GetAudioClip(previewUrl, AudioType.OGGVORBIS);
            if (req.downloadHandler is DownloadHandlerAudioClip dh) dh.streamAudio = true;
            ApiLog.Begin("preview", previewUrl);
            var sw = Stopwatch.StartNew();
            yield return req.SendWebRequest();
            ApiLog.End("preview", req, sw);

            if (req.result != UnityWebRequest.Result.Success || !_root.activeInHierarchy || _source != Source.Online) yield break;
            if (_onlineIndex < 0 || _onlineIndex >= _online.Count || _online[_onlineIndex].Id != id) yield break;   // moved on

            var clip = DownloadHandlerAudioClip.GetContent(req);
            if (clip == null) yield break;
            _audio.clip = clip;
            _audio.time = 0f;
            _audio.volume = GameSettings.MusicVolume;
            _audio.Play();
        }

        private void LoadOnlineBackground(OnlineSet os)
        {
            SetBackground(null);
            if (_onlineBgCo != null) StopCoroutine(_onlineBgCo);
            if (os != null) _onlineBgCo = StartCoroutine(OnlineBackgroundRoutine(os.Id));
        }

        private IEnumerator OnlineBackgroundRoutine(int id)
        {
            string coverUrl = BeatmapDownloader.CoverUrl(id);
            using var req = UnityWebRequestTexture.GetTexture(coverUrl);
            ApiLog.Begin("cover", coverUrl);
            var sw = Stopwatch.StartNew();
            yield return req.SendWebRequest();
            ApiLog.End("cover", req, sw);
            if (req.result != UnityWebRequest.Result.Success || _source != Source.Online) yield break;
            if (_onlineIndex < 0 || _onlineIndex >= _online.Count || _online[_onlineIndex].Id != id) yield break;
            SetBackground(DownloadHandlerTexture.GetContent(req));
        }

        private static string FormatSec(int sec)
        {
            if (sec <= 0) return "--:--";
            return $"{sec / 60}:{sec % 60:00}";
        }

        // ============================================================= uGUI construction

        private void Build()
        {
            var canvas = UiKit.CreateCanvas("SongSelectCanvas", Util.RenderOrder.CanvasScreen);
            _root = canvas.gameObject;
            var rootRect = (RectTransform)_root.transform;

            // background art + scrim
            _bgImage = UiKit.NewRect("Background", rootRect).gameObject.AddComponent<RawImage>();
            UiKit.Stretch(_bgImage.rectTransform);
            _bgImage.color = new Color(1, 1, 1, 0);
            _bgImage.raycastTarget = false;
            UiKit.Scrim(rootRect).raycastTarget = false;

            var title = UiKit.Label(rootRect, "Song Select", UiTheme.Text.Title, TextAlignmentOptions.Left);
            UiKit.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, -74), new Vector2(-40, -28));

            BuildTopBar(rootRect);
            BuildCarousel(rootRect);
            BuildDetail(rootRect);
            BuildFilterPanel(rootRect);
            BuildDownloadBar(rootRect);

            Hide();
        }

        private void BuildTopBar(RectTransform root)
        {
            // search field (left ~46%)
            _searchField = UiKit.SearchField(root, "Type to search — artist / title / difficulty", OnSearchChanged);
            var sf = (RectTransform)_searchField.transform.parent; // container carries the LayoutElement; anchor the container
            sf.GetComponent<LayoutElement>().ignoreLayout = true;
            UiKit.Anchor(sf, new Vector2(0, 1), new Vector2(0.46f, 1), new Vector2(40, -122), new Vector2(-8, -82));

            // sort (cycles Date Added → Title → Artist → Stars → SetId)
            var sortBtn = UiKit.Button(root, "", CycleSort);
            var sr = (RectTransform)sortBtn.transform;
            sortBtn.GetComponent<LayoutElement>().ignoreLayout = true;
            UiKit.Anchor(sr, new Vector2(0.46f, 1), new Vector2(0.63f, 1), new Vector2(8, -122), new Vector2(-4, -82));
            _sortLabel = sortBtn.GetComponentInChildren<TMP_Text>();
            UpdateSortLabel();

            // filters toggle
            var filterBtn = UiKit.Button(root, "Filters", ToggleFilterPanel);
            var fr = (RectTransform)filterBtn.transform;
            filterBtn.GetComponent<LayoutElement>().ignoreLayout = true;
            UiKit.Anchor(fr, new Vector2(0.63f, 1), new Vector2(0.78f, 1), new Vector2(4, -122), new Vector2(-4, -82));

            // Local / Online source toggle (§2.3 — one interface, two sources)
            var sourceBtn = UiKit.Button(root, "", ToggleSource);
            var sourceRect = (RectTransform)sourceBtn.transform;
            sourceBtn.GetComponent<LayoutElement>().ignoreLayout = true;
            UiKit.Anchor(sourceRect, new Vector2(0.78f, 1), new Vector2(1, 1), new Vector2(4, -122), new Vector2(-40, -82));
            _sourceLabel = sourceBtn.GetComponentInChildren<TMP_Text>();
            UpdateSourceLabel();
        }

        private void BuildCarousel(RectTransform root)
        {
            var panel = UiKit.Panel(root, "CarouselPanel");
            UiKit.Anchor(panel.rectTransform, new Vector2(0, 0), new Vector2(0.46f, 1), new Vector2(40, 96), new Vector2(-8, -134));

            var scrollGO = UiKit.NewRect("Carousel", panel.rectTransform);
            UiKit.Stretch(scrollGO, UiTheme.SpaceSM, UiTheme.SpaceSM);
            _carousel = scrollGO.gameObject.AddComponent<ScrollRect>();
            _carousel.horizontal = false;
            _carousel.vertical = true;
            _carousel.scrollSensitivity = 24f;

            var viewport = UiKit.NewRect("Viewport", scrollGO);
            UiKit.Stretch(viewport);
            viewport.gameObject.AddComponent<Image>().color = Color.white;
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            _listContent = MakeVerticalContent(viewport, out _);
            _carousel.viewport = viewport;
            _carousel.content = _listContent;

            _setList = UiScaffold.Ensure<UiListView>(_listContent.gameObject);
            _setList.rowPrefab = setRowPrefab;
            _setList.fallbackFactory = DefaultSetRow;

            _statusText = UiKit.Label(viewport, "Scanning for beatmaps...", UiTheme.Text.Body, TextAlignmentOptions.TopLeft, UiTheme.TextSecondary);
            UiKit.Stretch(_statusText.rectTransform, 12, 12);
        }

        private void BuildDetail(RectTransform root)
        {
            var panel = UiKit.Panel(root, "DetailPanel");
            UiKit.Anchor(panel.rectTransform, new Vector2(0.46f, 0), new Vector2(1, 1), new Vector2(8, 96), new Vector2(-40, -134));

            _detailTitle = UiKit.Label(panel.rectTransform, "Select a beatmap", UiTheme.Text.Heading, TextAlignmentOptions.TopLeft);
            UiKit.Anchor(_detailTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -78), new Vector2(-16, -12));

            _detailMeta = UiKit.Label(panel.rectTransform, "", UiTheme.Text.Label, TextAlignmentOptions.TopLeft, UiTheme.TextSecondary);
            UiKit.Anchor(_detailMeta.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -130), new Vector2(-16, -82));

            // difficulty list
            var scrollGO = UiKit.NewRect("DiffScroll", panel.rectTransform);
            UiKit.Anchor(scrollGO, new Vector2(0, 0), new Vector2(1, 1), new Vector2(12, 64), new Vector2(-12, -134));
            var diffScroll = scrollGO.gameObject.AddComponent<ScrollRect>();
            diffScroll.horizontal = false;
            diffScroll.vertical = true;
            var diffViewport = UiKit.NewRect("Viewport", scrollGO);
            UiKit.Stretch(diffViewport);
            diffViewport.gameObject.AddComponent<Image>().color = Color.white;
            diffViewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            _diffListContent = MakeVerticalContent(diffViewport, out _);
            diffScroll.viewport = diffViewport;
            diffScroll.content = _diffListContent;

            _diffList = UiScaffold.Ensure<UiListView>(_diffListContent.gameObject);
            _diffList.rowPrefab = diffRowPrefab;
            _diffList.fallbackFactory = DefaultDiffRow;

            // Play button (primary), pinned to the bottom of the detail panel
            _playButton = UiKit.Button(panel.rectTransform, "▶  Play", PrimaryAction, primary: true, role: UiTheme.Text.Heading);
            var pr = (RectTransform)_playButton.transform;
            _playButton.GetComponent<LayoutElement>().ignoreLayout = true;
            UiKit.Anchor(pr, new Vector2(0, 0), new Vector2(1, 0), new Vector2(12, 12), new Vector2(-12, 56));
            _playButton.gameObject.SetActive(false);
        }

        private void BuildFilterPanel(RectTransform root)
        {
            var panel = UiKit.Panel(root, "FilterPanel");
            _filterPanel = panel.gameObject;
            UiKit.Anchor(panel.rectTransform, new Vector2(0, 1), new Vector2(0.46f, 1), new Vector2(40, -360), new Vector2(-8, -130));

            var col = UiKit.NewRect("Col", panel.rectTransform);
            UiKit.Stretch(col, UiTheme.SpaceLG, UiTheme.SpaceMD);
            var vlg = col.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = UiTheme.SpaceXS;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;

            UiKit.SectionHeader(col, "Filters");
            AddFilterPair(col, "Stars", StarLo, StarHi, ref _starMin, ref _starMax, "0.#");
            AddFilterPair(col, "Length (s)", LenLo, LenHi, ref _lenMin, ref _lenMax, "0");
            AddFilterPair(col, "BPM", BpmLo, BpmHi, ref _bpmMin, ref _bpmMax, "0");

            _filterPanel.SetActive(false);
        }

        // A min/max slider pair on one labelled block. The ref locals can't be captured in lambdas, so we
        // bind through small setter closures over field-backed accessors selected by the label.
        private void AddFilterPair(Transform parent, string label, float lo, float hi, ref float min, ref float max, string fmt)
        {
            var head = UiKit.Label(parent, label, UiTheme.Text.Label, TextAlignmentOptions.Left);
            head.rectTransform.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            string key = label;
            UiKit.Slider(parent, lo, hi, min, lo, v => { SetFilter(key, true, v); RefreshList(selectFirst: true); }, fmt);
            UiKit.Slider(parent, lo, hi, max, hi, v => { SetFilter(key, false, v); RefreshList(selectFirst: true); }, fmt);
        }

        private void SetFilter(string key, bool isMin, float v)
        {
            switch (key)
            {
                case "Stars": if (isMin) _starMin = v; else _starMax = v; break;
                case "Length (s)": if (isMin) _lenMin = v; else _lenMax = v; break;
                case "BPM": if (isMin) _bpmMin = v; else _bpmMax = v; break;
            }
        }

        private void ToggleFilterPanel() => _filterPanel.SetActive(!_filterPanel.activeSelf);

        private void BuildDownloadBar(RectTransform root)
        {
            _downloadIdField = UiKit.SearchField(root, "Beatmapset id to download…", null);
            var idc = (RectTransform)_downloadIdField.transform.parent;
            idc.GetComponent<LayoutElement>().ignoreLayout = true;
            UiKit.Anchor(idc, new Vector2(0, 0), new Vector2(0.18f, 0), new Vector2(40, 40), new Vector2(-8, 84));
            _downloadIdField.contentType = TMP_InputField.ContentType.IntegerNumber;

            var dlBtn = UiKit.Button(root, "Download", OnDownloadClicked);
            var dr = (RectTransform)dlBtn.transform;
            dlBtn.GetComponent<LayoutElement>().ignoreLayout = true;
            UiKit.Anchor(dr, new Vector2(0.18f, 0), new Vector2(0.30f, 0), new Vector2(8, 40), new Vector2(-8, 84));

            _downloadStatus = UiKit.Label(root, "", UiTheme.Text.Label, TextAlignmentOptions.Left, UiTheme.TextSecondary);
            UiKit.Anchor(_downloadStatus.rectTransform, new Vector2(0.30f, 0), new Vector2(0.46f, 0), new Vector2(8, 40), new Vector2(-8, 84));

            _hintLabel = UiKit.Label(root, "", UiTheme.Text.Caption, TextAlignmentOptions.Right, UiTheme.TextSecondary);
            UiKit.Anchor(_hintLabel.rectTransform, new Vector2(0.46f, 0), new Vector2(1, 0), new Vector2(8, 40), new Vector2(-40, 84));
            UpdateHint();
        }

        // ------------------------------------------------------------- small helpers

        private void CycleSort()
        {
            _sort = (SortMode)(((int)_sort + 1) % Enum.GetValues(typeof(SortMode)).Length);
            UpdateSortLabel();
            RefreshList();
        }

        private void UpdateSortLabel()
        {
            if (_sortLabel == null) return;
            string name = _sort == SortMode.DateAdded ? "Date Added" : _sort.ToString();
            _sortLabel.text = $"Sort: {name}";
        }

        // A thin accent bar on a row's left edge that marks persistent selection (survives hover, unlike the
        // row fill which UiInteractive re-tints). Disabled by default.
        private static Image SelectionMarker(Transform content)
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

        private static RectTransform MakeVerticalContent(RectTransform viewport, out VerticalLayoutGroup vlg)
        {
            var content = UiKit.NewRect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
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

        private static void ScrollTo(ScrollRect sr, int index, int count)
        {
            if (sr == null || count <= 1) { if (sr != null) sr.verticalNormalizedPosition = 1f; return; }
            sr.verticalNormalizedPosition = Mathf.Clamp01(1f - (float)index / (count - 1));
        }
    }
}
