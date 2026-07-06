# osu! LENIENCY & FAITHFULNESS

> **Deliverable of block R2** (see `PLAN.md`). Spec for block C. For each divergence give
> **current vs. correct** and the fix location. Cite osu!lazer behavior where possible.

Status: **STUB — R2 not started.**

## Divergences to research (current vs. correct)

- **Slider end** — a missed slider-end **must NOT break combo**; it just doesn't add to it.
  Confirm our `SliderObject` / `ScoreProcessor` behavior.
- **Slider ticks / follow-circle leniency** — tick judgement, follow-circle radius forgiveness.
- **Note-lock** — osu! ordering: you can't hit a later object before an earlier one.
  We claim to have this (`GameManager`) — verify against lazer.
- **Spinner** — scoring + **combo** (currently bugged). Correct spinner combo/score rules.
- **Hit windows / OD mapping** — 300/100/50/miss windows vs. `DifficultyCalculator`.
- **Cursor size / hitbox** — og osu! forgiveness. We want a **bigger cursor with an
  adjustable hitbox** exposed as a `GameSettings` value; document the target range.

Output: table/list of each item → *current → correct → fix location*, ready for C.
Then link this doc from `STRUCTURE.md` and `CLAUDE.md`.
