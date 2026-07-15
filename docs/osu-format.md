# The osu! beatmap format (`.osu` v14 / `.osz` / `.osb`) — Fallcall exploitation audit

> **RES1 deliverable.** Catalogue of every field in the osu! beatmap format, whether Fallcall
> parses it today, and what it is worth for the environment/choreography wave (E1–E5).
> Sources: official osu! wiki (fetched from the `ppy/osu-wiki` GitHub mirror, 2026-07-11),
> cross-checked against our parser. Parser citations refer to
> `Assets/Scripts/Beatmaps/BeatmapParser.cs` (+ `Beatmap.cs`, `TimingPoint.cs`, `HitObjects.cs`).
>
> **Tags:** `[choreography]` mode/event-track input · `[pacing]` skip/rest/preview timing ·
> `[VE]` visual effects · `[seed]` deterministic seeding/identity · `[audio]` sound playback ·
> `[none]` no realistic Fallcall use.

---

## 0. Containers: `.osz` and file layout

- **`.osz` = a renamed ZIP archive** of one beatmap *set*. Contains: one `.osu` per
  difficulty, the audio file (mp3/ogg), background image(s), optional video, optional
  **`.osb`** storyboard (shared across difficulties), custom hitsound samples
  (`normal-hitnormal.wav`, `soft-hitclap2.wav`, …), and optionally skin-element overrides.
  (The wiki page for `.osz` is a stub; ZIP-ness is confirmed by osu!lazer's importer and
  universal practice.)
- Fallcall already extracts these: `Assets/Scripts/Util/ArchiveExtractor.cs`, cached under
  `persistentDataPath/Songs` (see `BeatmapLibrary`).
- A `.osu` file is plain text (UTF-8), `key: value` lines under `[Section]` headers, first
  line `osu file format v14` (older versions exist; v14 is current & what mirrors serve).
  Parser handles the version line and unknown sections tolerantly (`BeatmapParser.ParseText`).

---

## 1. `[General]`

| Field | What it is | Parsed today? | Fallcall use |
| --- | --- | --- | --- |
| `AudioFilename` | Audio file path relative to map folder | **Yes** (`ParseGeneral`) | [audio] |
| `AudioLeadIn` | ms of silence before audio starts | **Yes**; used by `GameManager` → `GameClock.Prepare` | [pacing] skip-intro math: skippable span = first-object time − preempt − lead-in (STRUCTURE §5.7 / E5) |
| `AudioHash` | Deprecated | No | [none] |
| `PreviewTime` | ms where song-select audio preview starts; `-1` = unset (default) | **Yes** (`Beatmap.General.PreviewTime`) — **not consumed anywhere yet** | [pacing][choreography] U4 audio preview; also the *mapper's own pointer at the hook/chorus* — a free peak-section hint for segment weighting (E2) |
| `Countdown` | Countdown before first object: 0 none, 1 normal, 2 half, 3 double | No | [pacing] opener choreography beat (low value) |
| `CountdownOffset` | Beats the countdown is shifted before the first object | No | [pacing] pairs with `Countdown` (low value) |
| `SampleSet` | Beatmap-default sample set (`Normal`/`Soft`/`Drum`) when timing points don't override | **Yes**; consumed in `HitSoundPlayer` | [audio] |
| `StackLeniency` | 0–1 multiplier for stacking co-located objects | **Yes** (stored; stacking itself not implemented) | [none] for choreography (gameplay fidelity only) |
| `Mode` | 0 osu! · 1 taiko · 2 catch · 3 mania | **Yes**; used to filter to standard | [none] |
| `LetterboxInBreaks` | Letterbox effect during breaks (0/1) | No | [VE] break presentation hint (we'd restyle, not letterbox) |
| `StoryFireInFront` | Deprecated | No | [none] |
| `UseSkinSprites` | Storyboard may use skin images (0/1) | No | [none] until storyboards render |
| `AlwaysShowPlayfield` | Deprecated | No | [none] |
| `OverlayPosition` | Hitcircle overlay vs number draw order (`NoChange`/`Below`/`Above`) | No | [none] (skin fidelity nicety) |
| `SkinPreference` | Mapper's preferred skin name | No | [none] |
| `EpilepsyWarning` | Map contains flashing content (0/1) | No | **[VE] safety gate** — when set, cap Fallcall's own flash/strobe VE intensity and show the warning |
| `SpecialStyle` | mania-only key layout | No | [none] |
| `WidescreenStoryboard` | Storyboard authored for 16:9 (0/1) | No | [VE] only if we ever render storyboards |
| `SamplesMatchPlaybackRate` | Pitch samples with rate mods (0/1) | No | [audio] only if rate mods land |

## 2. `[Editor]`  *(entire section unparsed)*

Editor-state persisted into distributed `.osu` files. `BeatmapParser` has **no case for this
section** — all fields below are skipped.

| Field | What it is | Parsed today? | Fallcall use |
| --- | --- | --- | --- |
| `Bookmarks` | Comma-separated ms times of editor bookmarks | No | **[choreography]** mappers habitually bookmark musical section boundaries (verse/chorus/drop). When present, a free, human-authored segmentation input for E2 — same tier as kiai in the §5.3 merge. Caveat: optional, sometimes noisy/absent |
| `DistanceSpacing` | Distance-snap multiplier | No | [none] |
| `BeatDivisor` | Beat-snap divisor (1/4, 1/8, …) | No | [none] (weak rhythm-granularity hint) |
| `GridSize` | Editor grid size | No | [none] |
| `TimelineZoom` | Editor timeline zoom | No | [none] |

## 3. `[Metadata]`

All parsed in `ParseMetadata` → `Beatmap.Metadata`.

| Field | What it is | Parsed today? | Fallcall use |
| --- | --- | --- | --- |
| `Title` / `TitleUnicode` | Romanised / native song title | **Yes** | [none] (display; title-card VE text at the "drop" moment — STRUCTURE §5.2) |
| `Artist` / `ArtistUnicode` | Romanised / native artist | **Yes** | [none] (display) |
| `Creator` | Mapper username | **Yes** | [none] (display) |
| `Version` | Difficulty name | **Yes** | [none] (display) |
| `Source` | Origin media | **Yes** | [none] (search) |
| `Tags` | Space-separated search terms | **Yes** | [choreography] *weak* genre/mood hints ("electronic", "calm") could bias mode weights in E2 — speculative, low priority |
| `BeatmapID` | Per-difficulty online ID | **Yes** | [seed] online lookup (mirror APIs, U5); **not** a seed source — 0/absent for unsubmitted maps, and edits don't change it |
| `BeatmapSetID` | Beatmap-set online ID | **Yes** | [seed] set-level lookup/download (already used by `BeatmapDownloader`) |

## 4. `[Difficulty]`

All six parsed in `ParseDifficulty`; converted to gameplay values in `DifficultyCalculator`.

| Field | What it drives | Parsed today? | Fallcall use |
| --- | --- | --- | --- |
| `HPDrainRate` (HP) | Health drain/refill rates | **Yes** (scoring/HP) | [none] extra |
| `CircleSize` (CS) | Circle radius = 54.4 − 4.48·CS osu!px | **Yes** | [choreography] smaller CS ⇒ denser look; minor camera-zoom input in Ortho2D |
| `OverallDifficulty` (OD) | Hit windows (300/100/50), spinner rate | **Yes** (also AR fallback for old maps) | [none] extra |
| `ApproachRate` (AR) | Preempt/fade-in times (1800→450 ms) | **Yes** | [pacing] preempt defines how far ahead choreography may safely disturb the view — mode switches must respect it (§4 guardrails) |
| `SliderMultiplier` | Base SV: hundreds of osu!px per beat | **Yes** (slider velocity calc) | [choreography] baseline for the SV intensity curve (see §6) |
| `SliderTickRate` | Slider ticks per beat | **Yes** (tick times) | [audio] |

## 5. `[Events]`

Line format: `eventType,startTime,eventParams`. Parsed in `ParseEvent` — **only three event
types**; everything else (storyboard lines) is silently ignored.

| Event | Syntax | Parsed today? | Fallcall use |
| --- | --- | --- | --- |
| Background | `0,0,"bg.jpg",xOffset,yOffset` | **Yes** (filename only → `Beatmap.BackgroundFile`; offsets dropped) | [VE] backdrop (already used w/ background-dim); dominant-colour extraction could feed E3 scene tint |
| Video | `1,startTime,"file.mp4",xOffset,yOffset` (or `Video,…`) | **Yes** (offset + filename → `VideoFile`/`VideoOffset`; used by `VideoPlayback`) | [VE] already wired |
| **Break** | `2,startTime,endTime` (or `Break,…`) | **Yes** → `Beatmap.Breaks` (`BreakPeriod{Start,End}`) — **parsed, not yet wired to gameplay/VE** | **[pacing][choreography]** rest timer + countdown (E5); segmentation boundary (E2); note real break *ends* extend slightly past the event's endTime toward the next object — treat times as approximate |
| Storyboard sprite/anim/sample lines | `Sprite,…` / `Animation,…` / `Sample,…` + indented command lines (see §9) | No (ignored) | [VE][choreography] see §9 — density is the exploitable signal |

## 6. `[TimingPoints]`

Line: `time,beatLength,meter,sampleSet,sampleIndex,volume,uninherited,effects`.
Fully parsed in `ParseTimingPoint` → `TimingPoint.cs`. Lookup helpers exist
(`Beatmap.GetTimingPointAt`, `GetUninheritedTimingPointAt`, `IsKiaiAt`).

| Field | What it is | Parsed today? | Fallcall use |
| --- | --- | --- | --- |
| `time` | Section start, ms (decimal allowed) | **Yes** (truncated to int) | — |
| `beatLength` | **Uninherited:** ms per beat (BPM = 60000/beatLength). **Inherited:** negative inverse SV percentage — SV multiplier = −100/beatLength (exposed as `TimingPoint.SpeedMultiplier`) | **Yes** | **[choreography][VE]** BPM drives every rhythm-synced effect (pulse, day/night beat, wind gusts). BPM *changes* are strong section boundaries. Inherited-point **SV curve** is the mapper's hand-authored intensity dial — dense, near-universal (present even when kiai isn't). Sampled over time ⇒ intensity envelope for E2 segmentation + E4 effect ramp |
| `meter` | Beats per measure (uninherited only) | **Yes** (`Meter`, default 4) | [choreography] downbeat/measure grid — align mode switches & VE hits to measure starts, not just beats |
| `sampleSet` | 0 beatmap default · 1 normal · 2 soft · 3 drum | **Yes**; consumed by `HitSoundPlayer` | [audio]; *weak* mood signal (soft sections ⇒ calm segments) |
| `sampleIndex` | Custom sample index (0 = osu! default) | **Yes**; consumed by `HitSoundPlayer` | [audio] |
| `volume` | Hitsound volume % | **Yes**; consumed by `HitSoundPlayer` | [audio] + **[choreography]** mappers ride volume down for quiet sections / up for choruses — second intensity dial alongside SV |
| `uninherited` | 1 = timing (red) point, 0 = inherited (green) point | **Yes** | — |
| `effects` | Bitfield, below | **Yes** (raw int + `Kiai` property) | see below |

### `effects` bitfield

| Bit | Meaning | Parsed today? | Fallcall use |
| --- | --- | --- | --- |
| 0 (1) | **Kiai time** on — stays on until a later point clears it | **Yes** (`TimingPoint.Kiai`, `Beatmap.IsKiaiAt`); consumed **only** by `ViewModeController` Ortho2D kiai-zoom | **[choreography][VE] gold.** Mapper-declared hype section: tier-2 source in the §5.3 merge (segment boundary + intensity=max + mode-switch candidate at each kiai edge). Often absent (STRUCTURE: 1–2 per map, frequently none) — never assume present |
| 3 (8) | Omit first barline (taiko/mania) | Raw bit stored, no accessor | [none] |
| 1,2,4–7 | Unused | — | [none] |

## 7. `[Colours]`

| Field | What it is | Parsed today? | Fallcall use |
| --- | --- | --- | --- |
| `Combo1`…`ComboN` | RGB combo colours (0–255 triplets), cycled per combo | **Yes** (`ParseColour` → `Beatmap.ComboColours`); used for object tint via `GameContext` (skin fallback when map defines none) | **[VE]** STRUCTURE §5.1: "mostly grey with extra colour pulled live from the beatmap (combo colors → scene tint)" — E3 wants the *active* combo colour bleeding into sky/fog/particles. Parsed; scene-tint wiring is the missing piece |
| `SliderTrackOverride` | RGB slider-body colour override | **No** — `ParseColour` ignores non-`Combo*` keys (we parse it from **skin.ini** only, `SkinConfig.cs`) | [VE] fidelity + extra palette input; trivial parser add |
| `SliderBorder` | RGB slider border colour | **No** (same — skin.ini only) | [VE] same |

## 8. `[HitObjects]`

Line: `x,y,time,type,hitSound,objectParams…,hitSample`. Parsed in `ParseHitObject`.
Playfield is 512×384 osu!px, origin top-left, y down.

### Common fields

| Field | What it is | Parsed today? | Fallcall use |
| --- | --- | --- | --- |
| `x,y` | Position, osu!px | **Yes** | [choreography] spatial spread per window → Ortho2D zoom rect (already used); stream detection (§3b) |
| `time` | Hit time, ms | **Yes** | **[choreography][pacing]** note density over time = the universal intensity signal (E2 segmentation: "large note-density changes") |
| `type` | Bitfield, below | **Yes** | see below |
| `hitSound` | Bitfield, below | **Yes** | see below |
| `hitSample` | `normalSet:additionSet:index:volume:filename` (default `0:0:0:0:`) | **Yes** except `filename` (`ParseHitSample`; s[4] explicitly unsupported) | [audio]; filename = custom per-object sample (rare; keysounded maps) |

### `type` bitfield

| Bit | Meaning | Parsed today? | Fallcall use |
| --- | --- | --- | --- |
| 0 (1) | Hit circle | **Yes** | — |
| 1 (2) | Slider | **Yes** | — |
| 2 (4) | **New combo** | **Yes** (`IsNewCombo`; drives colour cycling in `DifficultyCalculator.ProcessCombos`) | [choreography] combo boundaries ≈ musical phrases; mean combo length is a phrasing signal; each new combo advances the palette (feeds E3 tint) |
| 3 (8) | Spinner | **Yes** | [choreography] spinners = held moments → zoom-in choreography (§3b already specs this) |
| 4–6 (112) | 3-bit **combo-colour skip** ("colour hax") on new combo | **Yes** (`ComboColourSkip`) | [VE] mapper deliberately forcing a colour = intentional palette moment; honour it in scene tint |
| 7 (128) | mania hold | Enum value exists (`ManiaHold`); no parse path (standard-only) | [none] |

### `hitSound` bitfield

| Bit | Meaning | Parsed today? | Fallcall use |
| --- | --- | --- | --- |
| 0 (1) | Normal (default when 0) | **Yes** (`HitSoundType`) | [audio] |
| 1 (2) | Whistle | **Yes** | [audio] + [VE] accent marker |
| 2 (4) | **Finish** (cymbal) | **Yes** | [audio] + **[VE]** mappers place Finish on downbeats/impacts — free, dense accent track for particle bursts / flash pulses (E4). Parsed, never used for VE |
| 3 (8) | Clap | **Yes** | [audio] + [VE] usually a 2/4-beat backbeat pattern |

### Slider extras (`curveType|curvePoints,slides,length,edgeSounds,edgeSets`)

| Field | What it is | Parsed today? | Fallcall use |
| --- | --- | --- | --- |
| `curveType` | `B` bézier · `C` catmull · `L` linear · `P` perfect-circle | **Yes** (`ParseCurveType`; unknown → bézier) | [none] extra (geometry already consumed) |
| `curvePoints` | `x:y` pipe-separated anchors | **Yes** (`ControlPoints`) | — |
| `slides` | Traversal count (repeats+1) | **Yes** (`Slides`) | [choreography] long/repeat sliders = sustained notes → sustained VE (trails, wind swell) |
| `length` | Visual length per slide, osu!px | **Yes** (`PixelLength`) | — (feeds duration = length/SV; slider duration density is part of the intensity signal) |
| `edgeSounds` | Per-edge hitsound bitfields (head, repeats…, tail) | **Yes** (`EdgeSounds`) | [audio] + [VE] accent track incl. slider ends |
| `edgeSets` | Per-edge `normalSet:additionSet` banks | **Yes** (`EdgeSampleSets`) | [audio] |

### Spinner extras

| Field | What it is | Parsed today? | Fallcall use |
| --- | --- | --- | --- |
| `endTime` | Spinner end, ms | **Yes** (`SpinnerEndTime`) | [choreography] zoom-in window (§3b) |

---

## 9. Storyboards: `.osb` + storyboard lines in `.osu` `[Events]`  *(entirely unparsed)*

- Storyboard code lives in the per-difficulty `.osu` `[Events]` **and/or** the set-wide
  `.osb`; when both exist the `.osb` layers as if appended (its objects draw per-layer after
  the `.osu`'s).
- **Objects:** `Sprite,<layer>,<origin>,"path",x,y` · `Animation,…,frameCount,frameDelay,loopType`
  · `Sample,<time>,<layer>,"path",volume`. Layers: Background, Fail, Pass, Foreground, Overlay
  (Fail/Pass are mutually exclusive by player state). Coordinates 640×480, origin top-left.
  Times in ms (negatives allowed). `.osb` supports `[Variables]` (`$name=value` substitution).
- **Commands** (indented under objects; `_<cmd>,easing,start,end,params`):
  `F` fade · `M`/`MX`/`MY` move · `S` scale · `V` vector-scale · `R` rotate (radians) ·
  `C` colour (RGB) · `P` parameter (`H`/`V` flip, `A` additive) · `L` loop ·
  `T` trigger (on hitsound / passing / failing).
- **Fallcall use — [choreography][VE]:** rendering storyboards is out of scope (they're 2D,
  screen-space, and would fight our 3D identity). The exploitable signal is **density**:
  commands-per-second (or active-sprites-per-second) over time is a hand-authored intensity
  envelope from maps that have one. Cheap to extract with a line-counting mini-parser (no
  rendering); tier-2 merge input. Caveat: many ranked maps ship **no** storyboard — fallback
  signal only. `Sample` events are also mapper-placed audio accents.

---

## 10. Beatmap identity: the MD5 hash  *(confirms STRUCTURE §5.6)*

- **Confirmed:** osu!'s beatmap identity is the **MD5 of the raw `.osu` file bytes** —
  exactly as distributed, no normalization. The official API exposes it as `file_md5`
  (v1) / `checksum` (v2); osu!stable **refuses score submission** when the local file's MD5
  doesn't match the online one, and osu!lazer computes `MD5Hash` from the file to match
  local against online copies. Any byte change — including appending data — yields a
  different map identity.
  (Sources: [osu!api `file_md5`](https://osuapi.readthedocs.io/en/latest/osuapi.html),
  [osu-web #11551](https://github.com/ppy/osu-web/issues/11551),
  [ppy/osu discussion #19551](https://github.com/ppy/osu/discussions/19551),
  [forum: what the hash represents](https://osu.ppy.sh/community/forums/topics/201848).)
- **Not computed anywhere in Fallcall yet** (no MD5/hash code under `Assets/Scripts`).
  E1 must add it: hash the `.osu` **bytes** (`File.ReadAllBytes`, not a re-serialized
  string — line endings/encoding must pass through untouched).
- **Sidecar verdict: sound.** `<md5>.fallcall` keyed on this hash gives Fallcall
  (a) a stable, offline-derivable, per-difficulty **rated seed** [seed],
  (b) an authored-data key that survives re-import/dedup, and
  (c) an untouched original `.osu` — appending to the `.osu` would break all three plus the
  user's ability to submit scores in real osu!. One accepted consequence: if the mapper
  *updates* the difficulty, the hash changes → new seed + orphaned sidecar. That is correct
  behaviour (same rule real osu! uses for leaderboards); at most, keep `BeatmapID` in the
  sidecar payload for "a newer version of this map exists" migration hints.

---

## 11. Top opportunities (ranked) for E1–E5

Ranked by (signal value for the merge/segmentation design) × (cost to exploit). "Unparsed
or parsed-but-unwired" only.

1. **Inherited-point SV × volume intensity curve** — `[TimingPoints]` `beatLength` (green
   points) + `volume`. *Parsed, never sampled as a signal.* The mapper's continuous,
   hand-authored intensity dial, present in virtually every ranked map (unlike kiai).
   Sampled per-second and combined with note density ⇒ the segmentation + effect-ramp
   backbone for E2/E4. Effectively free: data is already in `Beatmap.TimingPoints`.
2. **Kiai bit** — `effects` bit 0. *Parsed (`IsKiaiAt`), wired only to Ortho2D zoom.*
   Highest-authority beatmap marker there is: kiai edges are explicit section boundaries and
   intensity=max declarations (STRUCTURE §5.3 tier 2, §5.4 segment input). Must become a
   first-class E1 event source — but design for its frequent absence.
3. **`PreviewTime`** — *parsed, unused.* Two uses for one integer: U4's song-select audio
   preview, and a mapper-authored "this is the hook" pointer that E2 can use to bias which
   segment gets the flagship (Falling/drop) treatment. Zero parsing work left.
4. **`[Editor] Bookmarks`** — *unparsed.* Mapper-placed section markers (verse/chorus/drop
   boundaries) hiding in a section we currently skip entirely. When present, near-direct
   segmentation input for E2 at the same trust tier as kiai. One switch-case + a split to
   parse.
5. **Hitsound accent track (Finish/Clap density) + storyboard command density** — hitsound
   bitfields are *parsed, never used for VE*; storyboard lines are *unparsed*. Finish
   placements give a per-impact accent stream for E4 pulses (dense, universal); storyboard
   command-count-per-second gives a hand-authored intensity envelope where a storyboard
   exists (sparse, fallback tier). Both are cheap counting passes, no rendering.

**Also queue (below top-5):** combo-colour → scene tint wiring for E3 (colours parsed, tint
not wired — plus trivial `SliderTrackOverride`/`SliderBorder` beatmap parse);
`EpilepsyWarning` as a VE-intensity safety gate; `Breaks` → E5 rest timer (parsed, unwired);
**MD5 computation itself** — not a field, but the E1 prerequisite everything rated hangs on.
