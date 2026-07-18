# docs/

Reference & design docs for **Fallcall**. Root-level `STRUCTURE.md` (vision),
`PLAN.md` (task board), and `INDEX.md` (per-file map) live one level up.

Files are flat with a category prefix so related docs cluster when sorted.

## Guides — how we build

| File | What |
|---|---|
| [guide-ui-design.md](guide-ui-design.md) | UI & game-design principles. Binding for any menu, HUD, or interaction; osu!lazer is the reference client. Includes the new-element checklist. |
| [guide-optimization.md](guide-optimization.md) | Performance playbook — pooling, managers, batching, profiling. |

## osu! reference — what gameplay must match

| File | What |
|---|---|
| [osu-leniency.md](osu-leniency.md) | Where gameplay must match real osu!/osu!lazer; divergences as current-vs-correct. |
| [osu-format.md](osu-format.md) | `.osu`/`.osz` format audit — every field we can exploit. |
| [osu-api.md](osu-api.md) | osu! online API / mirror / preview-CDN audit. Feeds the online-browse work. |

## Progress

| File | What |
|---|---|
| [progress-u6.md](progress-u6.md) | Resume point for the U6 online-browser block. |

## Design artifacts

| Path | What |
|---|---|
| [wireframes/](wireframes/) | Interactive wireframe viewer. `viewer.html` + one folder per page (`layout.html` + `notes.md`); pages listed in `pages.json`. |
| [mockups/](mockups/) | Higher-fidelity HTML mockups. |
| [prompts/](prompts/) | Design briefs handed to design tooling. |
| [examples/](examples/) | Reference screenshots (osu!lazer, prior builds). |

## Archive

[archive/](archive/) — historical snapshots (e.g. superseded PLAN boards). Kept
as-is for the record; paths inside may point at pre-rename filenames.
