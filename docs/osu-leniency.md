# osu! LENIENCY & FAITHFULNESS

> **Deliverable of block R2** (see `PLAN.md`). Spec for block **C**. For each divergence:
> **current vs. correct**, the **fix location**, and osu!lazer behaviour cited where known.
>
> Scope: where our gameplay logic diverges from real osu!standard / osu!lazer. Projection,
> camera and rendering are **out of scope** (that's block A). This doc is gameplay judgement,
> combo, scoring, timing and input only.

Status: **R2 done — ready for C.**

Legend: 🔴 breaks faithfulness (fix) · 🟡 minor / cosmetic · 🟢 already correct (keep, don't
"fix"). Priorities are C's suggested order.

---

## 0. TL;DR — what C must change

| # | Divergence | Sev | Fix location |
| --- | --- | --- | --- |
| 1 | Slider **tail** miss breaks combo | 🔴 | `SliderObject.Finalize` |
| 2 | Slider **head** carries no accuracy (300/100/50) | 🟡 | `SliderObject.HandleHead`/`HeadResult` |
| 3 | Spinner **bonus spins** unscored (dead code) | 🔴 | `SpinnerObject.AccumulateSpin` |
| 4 | Spinner **combo/partial** thresholds vs lazer | 🟡 | `SpinnerObject.Resolve` |
| 5 | **Cursor hitbox** not adjustable + cursor small | 🔴 (feature) | `GameSettings`, `GameContext.CursorWithin` callers, `CursorController.Init` |
| 6 | Hit windows / OD | 🟢 | fixed — `Floor()-0.5` added to match lazer |
| 7 | Note-lock ordering | 🟢 | — (correct, keep) |
| 8 | Follow-circle radius (2.4×) | 🟢 | — (correct, keep) |
| 9 | Slider **tick** miss breaks combo | 🟢 | — (correct, keep) |
| 10 | HP drain model | 🟡 | out of R2 scope, noted |
| 11 | Rank thresholds | 🟡 | out of R2 scope, noted |

---

## 1. 🔴 Slider tail miss breaks combo

**PLAN's headline rule:** *a slider-end (tail) miss does **NOT** break combo — it just
doesn't add to it.*

**Current** — `SliderObject.Finalize` ([SliderObject.cs:300-315](../Assets/Scripts/Visual/SliderObject.cs#L300-L315)):
```csharp
if (_tracking) { _nestedHit++; Ctx.Score.Apply(Judgement.SliderTick, affectsCombo: true, ...); PlayEdge(...); }
else           { Ctx.Score.Apply(Judgement.Miss, affectsCombo: true, affectsAccuracy: false); }  // <-- breaks combo
```
The `else` branch feeds a `Miss` with **`affectsCombo: true`**, so releasing the follow
circle a hair before the end **resets the whole combo**.

**Correct (osu!lazer):** the `SliderTailCircle` is judged with classic **end leniency** and
its miss is a *non-combo-breaking* result (lazer `HitResult.IgnoreMiss` / small-tick style).
Slider-end leniency in stable also judges the tail up to **~36 ms before** the true end.
Missing the tail only forfeits the tail's points — combo survives. Only the **head, ticks and
repeats** break combo.

**Fix (C):** in `Finalize`, the untracked-tail branch must **not** break combo. Either skip
the `Apply` entirely, or call with `affectsCombo: false` so the tail simply doesn't add its
point. (Optionally add the ~36 ms early-window so a clean release near the end still counts.)

---

## 2. 🟡 Slider head carries no accuracy weight

**Current** — a hit head applies `Judgement.SliderTick` with **`affectsAccuracy: false`**
([SliderObject.cs:235](../Assets/Scripts/Visual/SliderObject.cs#L235)); the whole slider's
300/100/50 is derived at the end from `frac = _nestedHit / _nestedTotal`
([SliderObject.cs:317-324](../Assets/Scripts/Visual/SliderObject.cs#L317-L324)). The head is
also accepted anywhere inside the **Hit50** window with no 300/100/50 distinction on its
timing.

**Correct (osu!lazer):** the `SliderHeadCircle` gets a **full circle judgement**
(Great/Ok/Meh by timing) that **does** count toward accuracy, in addition to combo. The
final slider result in lazer's *classic* scoring then combines head + ticks + tail.

**Fix (C):** judge the head with `Ctx.JudgeTiming(|delta|)` (like `HitCircleObject`) and
feed it with `affectsAccuracy: true`; fold the head result into the end-fraction so accuracy
isn't double counted. Lower priority than #1 — it shifts accuracy %, not combo. Decide with
the human whether to match lazer exactly or keep the simpler end-fraction.

---

## 3. 🔴 Spinner bonus spins are unscored (dead code)

**Current** — `SpinnerObject.AccumulateSpin`
([SpinnerObject.cs:162-166](../Assets/Scripts/Visual/SpinnerObject.cs#L162-L166)):
```csharp
if (_accumulated > _required && Mathf.Abs(Mathf.DeltaAngle(_lastAngle, angle)) > 0)
{
    // award bonus roughly once per extra rotation
}
```
Empty body — **every spin past the requirement scores nothing.** The `Judgement.SpinnerBonus`
enum value ([ScoreProcessor.cs:14](../Assets/Scripts/Gameplay/ScoreProcessor.cs#L14)) is
never applied anywhere.

**Correct (osu!):** each completed extra rotation after the clear requirement awards a bonus
(stable **1000** per spin; lazer nests `SpinnerBonusTick`s). Bonus is **score/HP only** —
**not** combo, **not** accuracy.

**Fix (C):** track an integer count of completed bonus rotations; each time `_accumulated`
crosses the next whole rotation past `_required`, apply `Judgement.SpinnerBonus` with
`affectsCombo: false, affectsAccuracy: false`. (Consider bumping the enum value to 1000 to
match stable.)

---

## 4. 🟡 Spinner combo / partial-credit thresholds

**Current** — `SpinnerObject.Resolve`
([SpinnerObject.cs:169-185](../Assets/Scripts/Visual/SpinnerObject.cs#L169-L185)):
```csharp
double ratio = _accumulated / _required;
Judgement result = ratio >= 1.0 ? Great : ratio >= 0.75 ? Ok : ratio >= 0.5 ? Meh : Miss;
Ctx.Score.Apply(result, affectsCombo: true, affectsAccuracy: true);
```
One combo-affecting judgement at the end. `ratio < 0.5` → `Miss` → combo break; anything ≥
0.5 keeps combo. Spinner correctly never note-locks (`HeadJudged = true` in `Init`).

**Correct (osu!lazer):** the spinner is one combo object. It contributes **+1 combo on
completion** and only breaks combo when it **fails to clear**. Partial credit (100/50) exists
in stable via spin-count thresholds, but the pass/fail combo boundary is the **clear
requirement (ratio ≥ 1.0)**, not 0.5.

**Fix (C):** confirm intent with the human, then align the combo-break boundary to the clear
requirement rather than 0.5, and keep the single end judgement. This is the "combo is bugged"
item PLAN flags — pair it with #3 (bonus). Cross-check final thresholds against lazer's
`osu.Game.Rulesets.Osu.Objects.Spinner` before locking numbers.

> **Note for block A:** spin accumulation reads `Ctx.Cursor.WorldPosition` around a fixed
> `_centre`. In curved/first-person mode the cursor rides screen-centre, so "spinning" is a
> projection artefact. That's A's concern, not C's — flagged here only so C doesn't chase it.

---

## 5. 🔴 Cursor size + adjustable hitbox (requested feature, deliberate deviation)

This is an **intentional divergence from osu!** (og osu! has no hitbox knob), requested in
PLAN: *bigger cursor with an adjustable hitbox as a setting.*

**Current:**
- Hit test is **point-in-circle**: `Ctx.CursorWithin(centre, Ctx.RadiusWorld)` compares the
  cursor's single hotspot against the circle radius
  ([GameContext.cs:65-70](../Assets/Scripts/Gameplay/GameContext.cs#L65-L70), callers in
  `HitCircleObject.Tick` and `SliderObject.HandleHead`). This is **faithful osu!** — osu!
  judges by cursor hotspot, and the cursor sprite is purely cosmetic.
- Cursor sprite diameter is hardcoded at session build:
  `OsuToWorldDistance(CircleRadius(CS)) * 0.6f`
  ([GameManager.cs:107-108](../Assets/Scripts/Gameplay/GameManager.cs#L107-L108)) — no
  setting.
- **No cursor settings exist** in `GameSettings`
  ([GameSettings.cs:14-22](../Assets/Scripts/Gameplay/GameSettings.cs#L14-L22)).

**Target:**
- Add `GameSettings.CursorSize` (visual scale, cosmetic) and `GameSettings.CursorHitbox`
  (gameplay). Default `CursorHitbox = 0` = **faithful point-in-circle** so faithfulness is
  the default and the knob is opt-in.
- Effective hit test becomes a **disc-vs-disc**: register when
  `distance ≤ RadiusWorld + hitboxWorld`, where `hitboxWorld` comes from the setting.

**Fix (C):**
1. Add the two fields (+ persistence + pause-menu sliders) in `GameSettings`.
2. Thread the hitbox radius into `GameContext` (e.g. `CursorHitboxWorld`) and add it inside
   `CursorWithin`, or pass an extra radius at the two call sites (`HitCircleObject`,
   `SliderObject.HandleHead`). Keep slider **follow** tracking on `FollowRadiusWorld` unless
   the human wants the hitbox to widen that too.
3. Drive the cursor sprite scale in `CursorController.Init` from `CursorSize`.

Confirm default cursor size + hitbox range with the human (Open question in PLAN).

---

## 6. 🟢 Hit windows / OD mapping — fixed (was ~0.5–1.5 ms too lenient)

`DifficultyCalculator` ([DifficultyCalculator.cs](../Assets/Scripts/Beatmaps/DifficultyCalculator.cs)):
```
300: Floor(80 - 6*OD) - 0.5    100: Floor(140 - 8*OD) - 0.5    50: Floor(200 - 10*OD) - 0.5   (ms)
```
The linear slopes (80/140/200 − k·OD) always matched, but the raw doubles were **too
lenient**: osu!lazer `OsuHitWindows.SetDifficulty` stores each window as
`Math.Floor(DifficultyRange(...)) - 0.5` (reproducing stable's 79.5/139.5/199.5 − k·OD
integer edges), and `HitWindows.ResultFor` compares `|offset| <= window`. Without the
`Floor()-0.5` the old code was up to ~1.5 ms loose (worst at fractional OD near .9). Now
matches lazer; `JudgeTiming` already uses `<=`. `Preempt` (1800/1200/450) and `FadeIn`
(1200/800/300) also match stable's AR mapping, and `CircleRadius = 54.4 - 4.48*CS` is the osu!
formula. **Keep as-is.** Early presses outside the window are ignored (not a miss) — also
correct osu! behaviour ([HitCircleObject.cs:72-84](../Assets/Scripts/Visual/HitCircleObject.cs#L72-L84)).

---

## 7. 🟢 Note-lock ordering — correct

`GameManager.Update` picks the front object as the earliest-`StartTime` object whose head is
un-judged, and only that object consumes a press
([GameManager.cs:143-151](../Assets/Scripts/Gameplay/GameManager.cs#L143-L151)). A press that's
too early or off the front circle is **not** redirected to a later object — matching osu!
note-lock (you can't hit a later circle before an earlier un-hit one). **Keep.** If C ever
wants stable's exact "unlock window" nuance it can revisit, but current behaviour is faithful.

---

## 8. 🟢 Follow-circle leniency — correct

Follow radius is `RadiusWorld * 2.4`
([GameContext.cs:25](../Assets/Scripts/Gameplay/GameContext.cs#L25)); tracking holds while the
cursor is within it and any tap key is `Held`
([SliderObject.cs:258](../Assets/Scripts/Visual/SliderObject.cs#L258)). osu! uses ~2.4× the
circle radius for the follow circle. **Keep.**

---

## 9. 🟢 Slider tick / repeat miss breaks combo — correct

Missing a slider tick or repeat applies `Judgement.Miss` with `affectsCombo: true,
affectsAccuracy: false` ([SliderObject.cs:264-297](../Assets/Scripts/Visual/SliderObject.cs#L264-L297)).
That's right: in osu! **missing a tick/repeat breaks combo**, and ticks are **combo/score
only, never accuracy**. **Keep** — do not lump these into #1's tail fix. (The tail is the only
nested element whose miss must *not* break combo.)

---

## 10. 🟡 HP drain (out of R2 scope — noted)

`ScoreProcessor.Configure` uses a custom forgiving drain/recover model
([ScoreProcessor.cs:35-41](../Assets/Scripts/Gameplay/ScoreProcessor.cs#L35-L41)), not osu!'s
real HP-drain curve. Not in R2's remit; leave unless the human asks. Flagged so C doesn't
assume HP is osu!-accurate.

---

## 11. 🟡 Rank thresholds (out of R2 scope — noted)

`ScoreProcessor.RankString` ([ScoreProcessor.cs:103-114](../Assets/Scripts/Gameplay/ScoreProcessor.cs#L103-L114))
approximates ranks by accuracy only; osu!'s S/A boundaries use the **300-hit ratio** and
no-miss rules. Cosmetic (results screen). Defer unless asked.

---

## Sources

- Hit windows / AR / CS / OD formulas: osu! wiki *Beatmap difficulty*; cross-checked against
  `DifficultyCalculator` — they match.
- Slider tail leniency + "tail miss doesn't break combo": osu!lazer `SliderTailCircle` /
  `SliderEventGenerator` and the classic-slider scoring path (`HitResult.IgnoreMiss` for the
  tail); osu! wiki *Score* (slider judgement).
- Spinner bonus + nested ticks: osu!lazer `osu.Game.Rulesets.Osu.Objects.Spinner`
  (`SpinnerTick`, `SpinnerBonusTick`); osu! wiki *Spinner*.
- Follow circle 2.4× radius, note-lock: osu! wiki *Slider* / *Hit object* and lazer
  `OsuHitObject`.

Where a number is marked "confirm against lazer source", C should verify before hardcoding.
