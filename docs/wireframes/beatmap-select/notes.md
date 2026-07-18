# Beatmap Select — design notes

One `##` section per block. The heading text must match the layout's
`data-block="…"` id. Body is free Markdown; the viewer renders it.

## toolbar
Persistent nav strip (guide-ui-design §1.4). Same on every screen, toggleable
(`Ctrl+T`). Routes: Home · Play · Browse (online) · Settings (`Ctrl+O`). One
fixed, memorizable route to everything — never moves, never hides content.

## back
Returns to main menu. `Esc` = back everywhere (consistent). Crossfades out;
never traps the player.

## source
Local ⇄ Online segmented toggle. Same cards/filters, two libraries: local `.osz`
import vs online mirror search. Online mode reveals a per-row download affordance
+ progress bar; switching keeps search/filter/sort state.

## search
Live filter across **title / artist / mapper** as you type. Matched substring is
highlighted (`<mark>`). Type-to-search: any printable keypress on the screen
grabs the field — no click first. Empty query = full library. `Esc` clears.

## sort
Dropdown menu, single-choice: Stars · Title · Artist · Length · Recently played.
Default Stars (descending). Checked item marked; menu scales in from top-right.

## filters
Slide-down panel anchored to the command row. Star-rating min, length-max,
BPM-min ranges + Has-video / Has-storyboard pills. Active-filter count badge on
the toggle; per-panel Reset. A set passes if **any** of its diffs qualifies.

## banner
Selected map's background art. Falls back to a procedural gradient when the
skin/beatmap has no image (see `Util/TextureFactory`).

## title
Selected map identity: song title, artist, mapper. Star badge + difficulty
name sit directly under.

## stars
Star rating drives carousel ordering and the badge color. Accent shifts toward
red past 6★. Must match osu!/lazer difficulty numbers — see `docs/osu-leniency.md`.

## stats
osu!-parity metadata row: Length, BPM, AR, OD, HP, CS. Read-only; reflects the
selected difficulty.

## leaderboard
Local scores for the selected map, best-first. Pulled from `LocalScoreStore`.
Selected/own score highlighted. Online tab is future work.

## carousel
Scrollable set list — the primary navigation surface, biggest element on the
right (lazer-style, form is ours). Status pills (All / Ranked / Loved) + live
count on top. Hover nudges a card left + accent border; the selected set grows an
accent spine and expands (animated) to reveal its difficulty rows. Each diff row
shows star (colored past 6★ *and* numbered — colorblind-safe), name, length, BPM.
Keyboard: ↑/↓ move between sets, Enter plays.

## audio
Preview / menu-music transport, always visible. Restart-to-preview-point ·
play/pause (`Space`) · seek scrubber · preview on/off toggle. Preview starts at
`.osu` PreviewTime (ms); missing/-1 → ~40% into song. Off → ambient menu music.
Auto-switches source when carousel selection changes.

## mods
Popover rising from the mods button (`F1`). Toggle chips grouped
increase/reduce (HR DT HD FL · EZ HT NF SD). Active mods echo as badges on the
button + footer label, and re-derive the stats row and shown star rating live.

## random
Jumps to a random map within the current filtered set (`F2`). Re-rollable;
flashes the newly-selected card. Respects active search/status/filters.

## play
Starts the session with the selected difficulty + active mods. Primary action,
bottom-right, the only element allowed a lift on hover. Bound to `Enter`.
