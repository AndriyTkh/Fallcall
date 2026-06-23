# Fallcall — osu! gameplay in Unity 3D

A from-scratch implementation of osu!standard gameplay running in Unity (2022.3 LTS), built as a
foundation for later experiments in projecting the playfield into 3D space.

Everything is driven by code and generated at runtime (procedural sprites, hit sounds and HUD) so
there are **no art/audio assets to wire up** — just open the project and press **Play**.

## Quick start

1. Open the project in Unity **2022.3 LTS**.
2. Open `Assets/Scenes/SampleScene.unity` (or any scene — the game bootstraps itself).
3. Press **Play**.
4. Pick a difficulty from the song-select list and play.

A bundled beatmap (`Assets/1629837 kurokumo - God-ish.osz`) is detected and extracted
automatically. To add your own, drop any `.osz` into one of:

- the project `Assets/` folder (editor),
- `Assets/StreamingAssets/` (works in builds too), or
- the persistent data folder (`Application.persistentDataPath`).

### Controls

| Action | Key |
| --- | --- |
| Hit / hold | **Z**, **X**, or left/right mouse button |
| Aim | mouse |
| Restart | **R** |
| Back to song select | **Esc** |

## Implemented gameplay features

- **Beatmap parser** for the `.osu` format (v3–v14): General, Metadata, Difficulty, Events
  (background, breaks), Colours, TimingPoints (inherited + uninherited / SV) and HitObjects.
- **`.osz` import**: extracts the archive and loads audio (`.mp3`/`.ogg`/`.wav`) and background.
- **Hit circles** with approach circles, combo colours, combo numbers and fade-in.
- **Sliders**: Linear / Bézier (multi-segment) / Perfect-circle / Catmull curves, resampled to the
  authored pixel length; moving ball, follow circle, slider ticks, repeats and tail, with
  fraction-based 300/100/50/miss scoring.
- **Spinners**: rotation tracking against an OD/length-derived spin requirement.
- **Difficulty maths**: CS→radius, AR→preempt/fade-in, OD→hit windows (300/100/50).
- **Input model**: osu!-style note-lock (only the front-most object can be hit), key/mouse taps,
  per-object hit windows and timing-based judgements.
- **Scoring**: combo, max combo, combo-scaled score, accuracy (standard weighting), HP drain/recover,
  rank, and an end-of-map results screen.
- **Procedural hit sounds** (normal/whistle/finish/clap/tick) and a HUD (score / combo / accuracy /
  HP bar) drawn with IMGUI.

## Project layout

```
Assets/Scripts/
  Beatmaps/                 # pure data + parsing (no Unity gameplay deps)
    HitObjects.cs           #   HitCircle / Slider / Spinner models + enums
    TimingPoint.cs
    Beatmap.cs              #   sections + timing lookup helpers
    SliderPath.cs           #   curve geometry (bezier/linear/circle/catmull) + arc-length resample
    DifficultyCalculator.cs #   CS/AR/OD maths + post-parse processing (combos, slider timing/ticks)
    BeatmapParser.cs        #   .osu text -> Beatmap
  Gameplay/
    Bootstrap.cs            #   auto-spawned entry point + song-select menu
    OszImporter.cs          #   .osz (zip) extraction
    AssetLoader.cs          #   runtime audio/texture loading
    GameManager.cs          #   session orchestration, spawning, note-lock, HUD, results
    GameContext.cs          #   shared tuned values handed to drawables
    GameClock.cs            #   dspTime-based audio-synced clock
    Playfield.cs            #   osu! coords <-> 3D world mapping (rotatable for future 3D work)
    CursorController.cs     #   mouse->plane raycast + tap input
    ScoreProcessor.cs       #   score / combo / accuracy / HP
    HitSoundPlayer.cs       #   procedurally synthesized hit sounds
  Visual/
    DrawableHitObject.cs    #   base class for on-screen objects
    HitCircleObject.cs
    SliderObject.cs
    SpinnerObject.cs
    FloatingText.cs         #   judgement popups
    VisualResources.cs      #   shared built-in font
  Util/
    TextureFactory.cs       #   procedural disc/ring sprites
    MaterialFactory.cs      #   shared transparent material for slider meshes
```

## How the playfield maps to 3D (for the next phase)

All hit objects are children of a single `Playfield` transform. osu! coordinates (512×384, origin
top-left, y-down) are mapped onto that transform's local XY plane via `Playfield.ToWorld`. The
cursor is projected with a ray against `Playfield.WorldPlane`, and hit-testing happens in world
units. Because every position flows through this one transform, the entire playfield can later be
rotated, tilted or otherwise projected for "3D osu" without touching the gameplay/judgement code —
just move the `Playfield` object (and switch the camera to perspective).

## Known limitations / next steps

- mp3 is decoded at runtime via `UnityWebRequestMultimedia`. If your platform can't decode mp3,
  convert the audio to `.ogg` (the loader picks the type by extension automatically).
- Beatmap hit sounds in the `.osz` are not used yet — sounds are synthesized. Hooking the real
  samples (and per-timing-point sample sets/volume) is a straightforward extension.
- Storyboards, breaks visuals, sliderslide audio, and the stacking algorithm (stack leniency) are
  parsed where relevant but not yet rendered/applied.
- Scoring is an osu!-flavoured approximation rather than a byte-exact reproduction of stable's
  score v1/v2 formula.
