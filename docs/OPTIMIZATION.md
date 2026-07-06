# OPTIMIZATION

> **Deliverable of block R1** (see `PLAN.md`). Playbook for optimizing Fallcall.
> Until R1 is `DONE` this is an outline — fill each section with what it is, when it
> applies **in this project**, and the concrete change (name the real file).

Status: **STUB — R1 not started.**

## To research & document

- **Object pooling** — hit objects spawn/despawn constantly; pool `HitCircleObject` /
  `SliderObject` / `SpinnerObject` instead of Instantiate/Destroy.
- **Manager over many scripts** — replace N per-object `Update()` with one manager tick
  (e.g. a central drawable updater). Cost of `MonoBehaviour.Update` at scale.
- **Draw-call batching** — static/dynamic batching, **SRP batcher**, **GPU instancing**,
  skin-sprite **texture atlasing**. Which apply to the built-in RP here.
- **Mesh reuse** — sliders build meshes; reuse/pool vs. rebuild.
- **Materials** — `Util/MaterialFactory` sharing vs. per-object material (breaks batching).
- **Jobs / Burst / structs** — where geometry or projection math could move off the main
  thread.
- **Profiling workflow** — Unity Profiler + Frame Debugger; what to measure first.

Each item → tie to a real file, give the concrete change. Then link this doc from
`STRUCTURE.md` §6 and `CLAUDE.md`.
