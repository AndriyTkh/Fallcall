# U6 (map browser) — pass 1 progress & handoff

_Written 2026-07-15, updated the same day: the screen landed and **the code compiles in-editor** (human-run).
Status on the board: **IN-PROGRESS (opus, 2026-07-15)** — pass 1 is code-complete but **unverified at runtime**,
which is where its "done when" lives. This file is the resume point; delete it when U6 pass 1 lands in the Done log._

---

## Scope decided for this pass

Human asked for **the regular browser first, without the preview**, and for the screen to be **split into
several small scripts** rather than one blob.

- **Layout: B frame** (single map column left; right = search+filters top, map detail bottom).
  Chosen without the human answering the A/B open question, on this reasoning: B is a **superset** of A —
  the autoplay panel is the only thing A lacks, and it is deferred anyway. The detail panel's top band is a
  named node `PreviewSlot` (cover art today) so the next pass mounts the autoplay panel into one node instead
  of re-cutting the screen. **If the human wants A, only anchors change — no logic is layout-bound.**
- **No autoplay preview** this pass (that's the whole point of "without preview"). Audio demo + cover art are
  in, because they're the existing song-select behaviour and they carry the `AudioType` bug fix.
- **`SongSelectUI`'s Online tab is left alone** (only the `AudioType` fix landed there). Retiring it in favour
  of this screen is a **later, separate step** — same precedent as the IMGUI pause-menu note in `PLAN.md`.
  Until then the two online paths coexist; do not silently rip one out.

## Landed (compiles; **no runtime verification** — no headless Unity path)

New, all `namespace OsuUnity.UI`, under `Assets/Scripts/UI/Browser/`:

| File | What it holds |
| --- | --- |
| `MapBrowser.cs` | MonoBehaviour, **the screen**. The only holder of browse state (`_results` raw → `_shown` filtered+sorted, `_index`, `_diffIndex`, `_library` for ✓). Owns selection, row building, keyboard flow, and the primary action (download→auto-import, or `PlayRequested` when already owned). Events out: `PlayRequested(setId)`, `SetImported(BeatmapSetInfo)`; in: `Populate(sets)`, `Show`/`Hide`. |
| `MapBrowserModel.cs` | `BrowseSet` / `BrowseDiff` (mirror result shape), `BrowseSort` enum, `BrowseFilters` (star/len/BPM ranges, client-side `Passes`), `BrowseText` (every player-facing string). Pure C#, no uGUI. |
| `MapBrowserSearch.cs` | MonoBehaviour. Debounced query → `BeatmapDownloader.Search` → `BrowseSet` list; sequence guard drops superseded responses; osu!std-only mapping; `IsInLibrary` hook for the ✓ marker. Events: `Started`, `Completed(list)` (`null` = all mirrors failed). |
| `MapBrowserMedia.cs` | MonoBehaviour. Audio demo (`AudioType.OGGVORBIS` — the fix) + cover fetch, both debounced with a token guard; 48-entry cover cache w/ FIFO eviction. `Play(setId)` / `LoadCover(setId, cb)` / `Stop()`. |
| `MapBrowserRows.cs` | Static. Code-default set/diff rows (fallbacks for `UiListView`; a developer row prefab still wins), `SelectionMarker`, `ScrollList`, `VerticalContent`, `ScrollTo`. |
| `MapBrowserView.cs` | Static-ish builder. Builds the whole Layout-B chrome, returns the widget bag. `Callbacks` class = the only way chrome talks back. `PreviewSlot` reserved. |

Edited (small, deliberate):

- `Gameplay/BeatmapDownloader.cs` — added `PreviewUrl(setId)` / `CoverUrl(setId)` (ppy CDN, with the
  never-proxy note, `docs/osu-api.md` §6); **fixed the catboy search params**: it takes `query/mode/limit`,
  not nerinyan's `q/m/ps` (§7) — it was silently ignoring `q` and answering with its default listing, so the
  fallback mirror has never actually searched.
- `Gameplay/SongSelectUI.cs` — the assigned bug: `AudioType.MPEG` → `OGGVORBIS` on the Vorbis-under-`.mp3`
  preview stream (was a silent no-op); both ppy URLs now go through the new helpers. Plus one public
  wrapper, `FocusLocalSet(onlineSetId)` → the existing private `JumpToLocal`, so Bootstrap can honour the
  browser's `PlayRequested`. **Nothing else touched.**
- `Gameplay/Bootstrap.cs` — spawns the browser on the same pivot as `SongSelectUI` (a scene-authored
  instance wins, else auto-spawn), adds `State.Browsing`, routes **`MenuRoute.Browse` → the browser**
  (it went to song select before), Esc → main, `PlayRequested` → song select focused on that set,
  `SetImported` → re-scan + `_hasBeatmaps`. The three route guards now share one `InMenus` predicate
  instead of each listing states. A direct edit, not U2's hook seam: the board's `Owns` for U6 names
  `Bootstrap.cs` (Browse route) outright and U3 is `DONE`.
- `PLAN.md` — U6 row set `IN-PROGRESS`, `Owns` widened to `UI/Browser/*.cs` + `Bootstrap.cs`, one activity-log
  line. Nothing else.
- `INDEX.md` — regenerated (55 scripts).

## Not done — the resume list, in order

1. **In-editor verify** (the code compiles — human-confirmed — but **none of this has ever run**; no
   headless path exists, so it cannot be checked from an agent session):
   - the browser opens from Browse / the toolbar, and Esc backs out to main;
   - a mirror search returns rows; filters + sort narrow them; keyboard-only flow works;
   - **the audio demo is audibly playing** — that's the `AudioType` fix, and it is U6's "done when";
   - download → auto-import → ✓ → Enter routes into song select on that map.
2. **Then close the block** in `PLAN.md`: Done log, Current state, status, unblocked blocks.

## Open questions still on the board (unchanged by this pass)

- **Layout A vs B** — built B's frame; see the reasoning above. Human never answered.
- **Default-difficulty rule** — not implemented. Proposal stands: highest-star osu!std diff, as an
  `Osu3DSettings` tunable. The browser currently selects the **easiest** diff (index 0, sorted easiest →
  hardest) with no rule applied.
- **Do mirror filters match the official set?** Still unverified — this pass sidesteps it by filtering
  **client-side** (every result already carries stars/length/BPM, so it costs one pass over ~50 rows and no
  extra request). Genre/status/language filters are **not** implemented and would be the thing that forces
  the credentials question (`docs/osu-api.md` §7).

## Known debt created here

- `MapBrowserRows` is a near-duplicate of the row/scroll helpers inside `SongSelectUI`. Pulling them up into
  `UiKit` touches a file this block doesn't own → left as a follow-up, noted rather than done silently.
- Two online browse paths coexist until the `SongSelectUI` Online tab is retired (see Scope above).
