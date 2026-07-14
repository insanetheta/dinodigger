# Dino Digger — Code Architecture

A Unity 6 (URP 2D) tap-to-play game for toddlers. One scene, no fail states, no
menus. Single-touch (mouse-identical). Fully offline.

## First-run setup (do this once)

1. **DinoDigger ▸ Generate Placeholder Art** — procedurally creates all sprites,
   isometric Tile assets, 4 `DinoDefinition`s, `GameConfig`, `AudioConfig`, and the
   `PlaceholderLibrary` under `Assets/Art/Placeholder/`.
2. **DinoDigger ▸ Build Main Scene** — (re)creates `Assets/Scenes/Main.unity` from
   scratch, wires every manager/component, paints the island, and adds the scene to
   Build Settings. Idempotent — safe to re-run. If art is missing it runs step 1
   automatically.

Press Play. Tap grass to drive; tap a sparkling mound to dig; tap dirt to scoop;
uncover eggs (hatch dinos), fruit (feed/grow dinos), treasure (flies to counter).

## Folders (`Assets/Scripts/`)

- **Core/** — `GameManager` (the one MonoBehaviour that owns everything),
  `GameEvents` (static event bus), `GameState`/`GameStateManager` (Roam/Transition/
  Dig), `Direction8` (movement-vector → 8-way facing), `Tween`+`TweenRunner`
  (dependency-free coroutine tweens: PunchScale/ScaleTo/MoveArc/MoveTo/ShakeRotation),
  `ITappable`.
- **Config/** — ScriptableObjects: `GameConfig` (all tunables), `DinoDefinition`
  (per-species data), `AudioConfig` (clip slots), `PlaceholderLibrary` (art registry),
  plus `GameEnums`.
- **Managers/** — plain C# (no MonoBehaviour): `SaveManager` (JSON on native,
  PlayerPrefs on WebGL), `AudioManager` (SFX pool + music + mute), `SpawnManager`
  (mound respawn timers), `SaveData`.
- **Input/** — `InputService`: new Input System, fires `Tapped(screenPos)` for
  touch or mouse, ignores taps over UI.
- **Overworld/** — `OverworldMap` (tilemap walkability), `BackhoeController`
  (tap-to-move + wall-slide + 8-dir sprite + dig arrival), `DinoController` (follow/
  wander/eat/dance/grow), `ItemPickup` (egg wobble→hatch, fruit feed, treasure fly),
  `DigMound`, `CameraFollow` (deadzone follow + dig zoom).
- **Dig/** — `DigModeController` (builds the dirt grid at the dig root, scoop
  animation, buried-item reveal, big-dino helper), `DirtTile` (3 crack states).
- **UI/** — `TreasureCounter` (corner count), `MuteButton` (parent-gate hold 3s).
- **Editor/** — `PlaceholderArtGenerator`, `SceneBuilder`.

## Flow / decoupling

Systems talk through `GameEvents` (OnItemDug, OnEggHatched, OnDinoGrew,
OnTreasureCollected, DigModeEntered/Exited, …). Taps are resolved centrally:
`GameManager` converts the screen tap to world space, `Physics2D.OverlapPoint`s for an
`ITappable` (mound/dino/fruit/dirt tile); an empty tap while roaming drives the
backhoe.

## Dig mode / camera

There is **one** orthographic Main Camera. The dig mini-game lives at a fixed world
offset (`DigRoot` at x=1000). Entering dig builds the grid there and `CameraFollow`
eases + zooms the single camera over; revealing an item eases it back. No second
camera, no scene load — dig is an in-scene sub-view.

## Save

`GameManager` persists hatched dinos (type + growth stage + fruit eaten) and treasure
count via `SaveManager` on hatch/grow/treasure and on pause/quit.

## Adding a new dino

1. Add a value to `DinoType` (Config/GameEnums.cs) and a `DanceType` if new.
2. Create a `DinoDefinition` asset (right-click ▸ Create ▸ DinoDigger ▸ Dino
   Definition): set type, colors, egg sprite, the 8 `WalkSprites` (one turnaround
   sheet sliced into N/NE/E/SE/S/SW/W/NW), and dance.
3. Add it to `GameConfig.Dinos`.

That's it — spawning, eggs, growth, follow, and dig-helper logic are all data-driven.

## Platform notes

- WebGL-safe: no threads, no `System.IO` on WebGL (guarded in `SaveManager`).
- Unity 6 APIs only (`FindFirstObjectByType` not used; no reflection; no async net).
- Null-tolerant everywhere: missing art/audio degrade silently so real assets can
  drop in later without code changes.
