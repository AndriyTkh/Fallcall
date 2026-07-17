# Design Brief — Beatmap Selection Screen (Fallcall)

> Hand this file, whole and unedited, to a design agent. It is self-contained: no repo access,
> no codebase knowledge, no engine knowledge required. Same brief goes to every agent so their
> outputs are comparable.

---

## 0. Your task in one line

Design the **beatmap selection screen** — the screen where a player picks which song to play —
for a rhythm game called **Fallcall**. Deliver a visual design, not code.

**Technology is irrelevant.** Do not think about engines, frameworks, or implementation. Design
as if for a static mockup. Someone else will build it.

---

## 1. The game (all context you need)

Fallcall is a **3D rhythm game**. The player clicks circles in time with music, mouse-driven,
often fast — 5–8 clicks per second at high difficulty.

**The signature experience:** you are **falling through a vast, fast, geometric 3D space**. The
gameplay surface wraps around you on a sphere; the world is low-poly, mostly grey, with clouds
and sky and simple cubist geometry sweeping past. The reference for atmosphere is *Alto's
Odyssey* — minimalist, pastel, calm-but-moving. The music supplies the energy; the visuals
supply the space.

**Beatmaps** are community-made song charts. Each is a song plus a set of difficulty levels.
Players own hundreds to thousands of them. Picking one is a thing they do many times per
session, often while the previous song's audio is still fading — this screen is high-traffic
furniture, not a destination.

**Who plays:** rhythm-game players. Fast, opinionated, keyboard-and-tablet users, many with no
mouse at all. They already know this genre's conventions cold. They do not need hand-holding;
they need speed and legibility.

---

## 2. What the screen must do

The player arrives with one of three intents. Design for all three; they are not equally common.

1. **"Play the thing I always play."** (most common) — muscle memory, near-instant, minimal
   reading.
2. **"Find that one map."** — they know the title or the artist; they type.
3. **"Show me something."** — browsing, no target, driven by artwork and mood.

The screen must let them do all three and start a map. Nothing else.

---

## 3. The content you are designing with

This is the real data available. You may show all, some, or restructure it — but you may not
invent data that does not exist here.

**Per song (a "set"):**

| Field | Example | Notes |
|---|---|---|
| Title | `Blue Zenith` | The thing players search by most |
| Artist | `Hommarju` | |
| Creator (mapper) | `Asphyxia` | The community member who charted it |
| Background artwork | image | Every set has one. Wildly inconsistent: anime art, photos, black voids, blinding white, text-covered. **Assume the worst artwork you can imagine.** |
| Video | yes/no | Some sets have a background video |
| Storyboard | yes/no | Some sets have scripted visuals |
| Rank status | `ranked` / `loved` / `graveyard` | Community approval state |
| Favourites | `8,412` | |
| Difficulty count | 1–20+ | Sets expand into their difficulties |

**Per difficulty (inside a set):**

| Field | Example | Notes |
|---|---|---|
| Name | `FOUR DIMENSIONS` | Mapper-authored, often chaotic, often long, often non-Latin |
| Star rating | `7.79` | **The single most-read number on this screen.** Ranges 0–10+. Players filter their whole library by it. |
| Length | `4:16` | |
| BPM | `200` | |
| CS / AR / OD / HP | `4 / 9.8 / 9 / 5` | Four 0–10 gameplay parameters. Secondary — read on decision, not on scan. |

**Controls that must exist somewhere:**

- **Search** — filters as you type, across title/artist/creator. Typing anywhere on the screen
  starts a search; the player never clicks a field first.
- **Filters** — star range, length range, BPM range, has-video, has-storyboard.
- **Sort** — title / artist / difficulty / recently played.
- **Local ⇄ Online toggle** — the same screen browses the local library or an online database of
  downloadable maps. Online rows need a download affordance and download progress.

---

## 4. Hard constraints (violating any = failed brief)

**4.1 — Legibility over artwork, always.** Text sits on top of arbitrary user images. Every piece
of text must be readable over a blinding-white photo *and* a black void without the layout
changing. Solve this in the design; do not hope for good artwork.

**4.2 — Not a clone.** The genre's market leader is *osu!lazer*. You may adopt its **behaviors**
freely — they are industry convention and it executes them well: type-to-search without focusing
a field, card carousels, hover preview, keyboard-first navigation, live filters. You may **not**
reproduce its **look**: no pink/magenta `#FF66AA`-family palette, no triangle motifs, no
reproduction of its card art, layout proportions, or iconography. If a reviewer can point at your
mockup and say "that's lazer" — failed.

**4.3 — Keyboard-only operation is a requirement, not an accommodation.** The entire screen works
with arrows / Tab / Enter / Esc and nothing else. Focus state must be as loud as hover state.
Design them both; show them both.

**4.4 — No information by color alone.** Colorblind-safe. If star rating is colored, it is also
numbered. Contrast is brightness/saturation-driven, not hue-driven.

**4.5 — One element, one function.** No click-vs-long-press, no icon meaning different things in
different states, no overloaded rows.

**4.6 — Speed of return.** The player has done this 400 times. The design must not cost them a
re-read every time. Whatever is biggest and first should be what intent #1 needs.

---

## 5. The design feel — what we are reaching for

This is the part the brief cares most about. Read it twice.

**The screen should feel like the game's world, at rest.** Fallcall's world is grey, geometric,
airy, and vast; things move through it fast but the space itself is calm. Song select is that
same space, standing still, waiting. It is **not** a dashboard, **not** a store page, **not** a
media library with rounded corners.

Anchors:

- **Grey, structured, wide.** Mostly-neutral base. Color arrives from the *content* — each
  beatmap carries its own colors, and the screen borrows them rather than asserting its own.
  A screen that looks the same with every map selected has missed this.
- **Geometry over ornament.** Straight lines, flat planes, deliberate angles, hard shadow or no
  shadow. Not glassmorphism, not neon, not gradient-glow. If a shape exists, it is doing
  structural work.
- **Air.** The world is vast and mostly empty. The screen should have room in it. Density is
  earned per-element, not applied globally.
- **Motion is slow and large, or absent.** The game moves fast; the menu does not compete.
  Transitions communicate structure — a thing that slides in from an edge is anchored to that
  edge, and it always will be. Nothing flickers, nothing pops, nothing animates fast enough to
  pull the eye during a scan.
- **Hierarchy is brutal.** Title, then star rating, then everything else, and the gap between
  tiers is obvious from across the room. CS/AR/OD/HP are near-invisible until asked for.

Anti-references — say why in your writeup if you go near any: neon-cyberpunk, glass/frosted
panels, corporate-SaaS card grids, dark-mode-with-purple-accents, anything that looks like a
music streaming app.

**Palette note:** the current build ships a **grey/blue placeholder palette** deliberately —
final accent colors are a per-player setting, not a brand decision. Design accordingly: your
design must survive the player recoloring its accent. Do not build an identity that dies when the
accent changes.

---

## 6. What to deliver

1. **The screen, designed.** Default state — a library of ~200 sets, one selected, nothing
   searched. Whatever medium shows it best: mockup, high-fidelity wireframe, rendered image,
   annotated layout. Judged on the design, never on the medium.
2. **Four more states**, at whatever fidelity communicates them:
   - a set **expanded** into its difficulties,
   - **mid-search** — three characters typed, list filtered,
   - **filters open**, at least one active,
   - **keyboard focus** on a row, no mouse involved.
3. **Worst-case artwork proof.** The selected-card treatment shown over (a) a blinding-white
   image and (b) a near-black one. Same layout, both readable.
4. **A short writeup — under 400 words.** What the feel is, what you cut and why, where you
   deviated from this brief and what you were buying with the deviation. Deviation is allowed;
   silent deviation is not.

**Do not deliver:** code, engine-specific anything, implementation notes, asset lists, or a
component library. Design only.

---

## 7. How this will be judged

Ranked. Earlier criteria dominate.

1. **Intent #1 speed.** Can a returning player start their usual map without reading? This is
   most of the score.
2. **Legibility under hostile artwork.** Does it hold over the white image and the black one?
3. **Feel match.** Does it read as grey/geometric/airy/vast-at-rest — the game's world at rest —
   or as a generic dark UI with this brief's words pasted on?
4. **Own identity.** Distinct from osu!lazer in form while keeping the proven behaviors.
5. **Keyboard-first evidence.** Focus states designed, not mentioned.
6. **Hierarchy discipline.** Is the star rating instantly findable? Is CS/AR/OD/HP correctly
   subordinate?
7. **What you cut.** A smaller certain surface beats a larger vague one. Cuts, defended in the
   writeup, score positively.

---

## 8. Notes for the agent

- Ask no clarifying questions. Ambiguity is deliberate — how you resolve it is part of what is
  being measured. State your assumption in the writeup and move.
- Do not research osu! or its clients before designing. Section 3 is the whole data model and
  section 5 is the whole feel; going to look at the market leader first is exactly how the form
  leaks in.
- Numbers in this brief (star ranges, field names, counts) are real. Use real-shaped example
  data — long non-Latin difficulty names, a 12-difficulty set, a `0.8★` and a `9.4★` in the same
  list. Mockups that only contain tidy data have not been tested.
