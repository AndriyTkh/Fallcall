# docs/wireframes — viewer infrastructure

Interactive wireframe viewer. Shows a live HTML layout next to per-block design
notes, joined by matching ids. Use for low/mid-fidelity screen wireframes; keep
polished art in `../mockups/`.

## Layout

```
wireframes/
  viewer.html          # the viewer app (vanilla JS + Tailwind CDN, zero build)
  pages.json           # manifest — every page must be listed here for the dropdown
  <page-id>/
    layout.html        # the screen; tag regions with data-block="<id>"
    notes.md           # design notes; one `## <id>` section per block
```

One folder per page. `<page-id>` is the folder name and the `?doc=` value.

## How the viewer works

- Opens `?doc=<page-id>` (defaults to `beatmap-select`). Loads
  `<page-id>/layout.html` into an iframe and `<page-id>/notes.md` beside it.
- **Binding:** every `[data-block="id"]` in the layout pairs with the `## id`
  section in `notes.md`. Cards render in layout order; md-only sections show as
  orphans (`no block`), layout blocks with no note show `no note`.
- **Interactions:** hover a card → highlights its block in the iframe; click a
  block → opens+scrolls its card and flashes it; expand/collapse all.
- **Dropdown** (header) reads `pages.json` and switches pages via `?doc=`. A doc
  not in the manifest still loads (shown as `(unlisted)`); if `pages.json` can't
  be fetched the dropdown disables and shows only the current page.
- **Export .md** merges the (editable) note textareas back into `notes.md`
  ordering, downloads as `notes.md`. Notes are editable in-panel for quick
  iteration — copy the export over the file to persist.

## Adding a page

1. `mkdir <page-id>/`, add `layout.html` (tag regions `data-block="…"`) and
   `notes.md` (a `## <id>` per block).
2. Add one line to `pages.json`: `{ "id": "<page-id>", "title": "<Title>" }`.

## Serving

The iframe + `fetch` need HTTP — `file://` blocks cross-frame access and md
fetch. Serve the folder: `python -m http.server` from `docs/wireframes/`, then
open `http://localhost:8000/viewer.html?doc=<page-id>`.

## Conventions

- `data-block` ids: short kebab-case, unique per page, match the `##` heading
  text exactly (the join is case-sensitive).
- Keep `layout.html` self-contained (Tailwind CDN, no repo assets) so it renders
  standalone via "open layout ↗".
- No build step, no framework — kept vanilla on purpose (see the React-vs-vanilla
  call: token savings don't materialize at wireframe fidelity).
