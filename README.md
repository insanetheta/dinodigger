# Dino Digger

A tap-to-play dinosaur digging game for toddlers, built in **Unity 6**. Drive a
friendly backhoe around an isometric island, dig up sparkling mounds, and uncover
eggs (that hatch dinos), fruit (that feeds and grows them), and treasure (that flies
to your counter). No menus, no fail states, no reading required — single-touch
(mouse-identical in the editor) and fully offline.

- **Engine:** Unity `6000.0.61f1` (URP 2D, new Input System)
- **Targets:** iOS, Android, WebGL — **landscape only** (portrait autorotation is
  disabled in Project Settings)
- **Company / product:** PlayStudios / Dino Digger

---

## Open, set up, and play

1. Open the project folder in Unity `6000.0.61f1` (Unity 6). Let it import.
2. Run the two art/config steps from the **DinoDigger** menu **in this order**:
   1. **DinoDigger ▸ Generate Placeholder Art** — procedurally creates every
      sprite, the isometric Tile assets, the four `DinoDefinition`s, `GameConfig`,
      `AudioConfig`, and the `PlaceholderLibrary` under
      `Assets/Art/Placeholder/`. **Run this first** — it creates the config
      ScriptableObjects that later steps depend on.
   2. **DinoDigger ▸ Import Generated Art** *(optional)* — overlays the final
      AI-generated character/item sprites and Kenney audio onto the config assets
      the previous step created. It does **not** create those assets, so running it
      before *Generate Placeholder Art* just reports everything as missing. The game
      is fully playable on placeholders without ever running this step.
3. **DinoDigger ▸ Build Main Scene** — (re)creates `Assets/Scenes/Main.unity` from
   scratch, wires every manager/component, paints the island, and adds the scene to
   Build Settings. Idempotent and safe to re-run; if art/config is missing it runs
   *Generate Placeholder Art* automatically.
4. Open `Assets/Scenes/Main.unity` and press **Play**.

> **Menu-order rule:** *Generate Placeholder Art* must run before *Import Generated
> Art*, because the importer writes over the config assets (`GameConfig`,
> `AudioConfig`, `PlaceholderLibrary`, and the `Dino_*` definitions) that *Generate*
> creates. Re-running *Generate Placeholder Art* rewrites `GameConfig.Dinos` back to
> the built-in four species (see "Add a new dino type" below).

### Build

Standard Unity build via **File ▸ Build Profiles / Build Settings** with `Main.unity`
enabled (the scene builder adds it automatically). Select iOS, Android, or WebGL.
Everything is WebGL-safe: no threads, no `System.IO` at runtime (save falls back to
`PlayerPrefs` on WebGL), no reflection, no async networking. Keep the player
orientation landscape.

---

## How to play

Tap grass to drive the backhoe. Tap a sparkling mound to drive there and dig. In the
dig view, tap dirt tiles to scoop them away until every buried item is uncovered —
they then spill out onto the grass by the backhoe. Eggs hatch into dinos, fruit can
be tapped to feed a hungry dino (three fruit grows it a stage; a big dino helps you
dig), and treasure flies to the corner counter. Tap a dino to make it dance. Hold the
mute button for ~3 seconds (a parent gate) to toggle sound.

### Dino companions

- **Walk buddies + meadow** — at most **two** dinos follow the backhoe (the
  "buddies"); everyone else lives in a fenced **meadow** in the island's north-east,
  where they stroll, nap (with sleepy star puffs), and dance when tapped. Tapping a
  meadow dino swaps it in: it joins the walk and the longest-serving buddy happily
  trots home. Newly hatched dinos take a free buddy slot, or head to the meadow after
  the hatch celebration. Mounds never spawn inside the meadow.
- **Species superpowers** (active while that species is a buddy, all wordless):
  - **T-Rex** (big): helps in the dig view, crumbling an extra dirt tile per scoop.
  - **Brachiosaurus:** tap a tree while it's nearby — it wanders over, sways its
    neck, and 1–2 fruit tumble out of the canopy (each tree rests ~10s; a little
    leaf rustle still plays so every tap does something).
  - **Stegosaurus:** ambient sniffer — every few seconds it aims a small star trail
    toward the nearest sparkling mound with a soft chime.
  - **Triceratops:** fruit courier — carries any far-away fruit back on its head and
    sets it down beside the backhoe, still tappable as normal.
- **Milestone parade** — the first time all four species exist and are all grown
  Big: confetti, celebration stings, and the whole family parades a loop around the
  backhoe (~8s), then returns. Plays exactly once per save (`ParadeDone`).
- Buddy assignment persists in the save (v2). Old v1 saves load fine: their first
  two dinos become the buddies.

---

## Project structure

```
Assets/
  Scripts/            # all game code (namespaces DinoDigger.*)
    Core/             # GameManager, event bus, state, tweens, ITappable
    Config/           # ScriptableObjects: GameConfig, DinoDefinition, AudioConfig,
                      #   PlaceholderLibrary, plus GameEnums
    Managers/         # plain C#: SaveManager, AudioManager, SpawnManager, SaveData
    Input/            # InputService (new Input System tap routing)
    Overworld/        # BackhoeController, DinoController, ItemPickup, DigMound,
                      #   OverworldMap, CameraFollow
    Dig/              # DigModeController, DirtTile (the side-view mini-game)
    UI/               # TreasureCounter, MuteButton
    Editor/           # PlaceholderArtGenerator, GeneratedArtImporter, SceneBuilder
    README.md         # code architecture / flow deep-dive
  Art/
    Placeholder/      # generated placeholder sprites + Config/ ScriptableObjects
    Generated/        # AI character turnarounds + item PNGs (see Tools/)
    Kenney/           # CC0 tile/UI source art
  Audio/              # Kenney SFX + CC0 music
  Scenes/Main.unity   # the single scene (built by SceneBuilder)
Tools/                # asset pipeline: art generation, slicing, downloads
docs/                 # design spec
```

For the runtime architecture (the single `GameManager`, the static `GameEvents` bus,
the roam↔dig flow, and the single-camera dig sub-view), see
`Assets/Scripts/README.md`.

---

## Asset pipeline

All third-party art and audio is **CC0 (public domain)**. Provenance and re-download
URLs are recorded in `Tools/ASSET_SOURCES.md`; `bash Tools/download_assets.sh`
re-fetches and re-organizes every Kenney pack and the CC0 music track into `Assets/`.

The AI-generated character and item sprites are produced by the Python scripts in
`Tools/` (OpenRouter / Gemini Flash Image for generation, then a dependency-light
Pillow/numpy slicer that chroma-keys and trims each PNG):

- `Tools/generate_sprites.py` — text/image prompts → `Tools/raw/*.png`
- `Tools/slice_sprites.py` — `Tools/raw/*.png` → `Assets/Art/Generated/<group>/*.png`

See `Tools/README.md` for the full art-direction notes, run order, and how the
5-facing turnarounds are mirrored into the 8-way sets. **DinoDigger ▸ Import
Generated Art** then configures the import settings (per-category pixels-per-unit)
and wires the results into the config assets.

The tilemap tiles, mound, and UI icons intentionally stay on the procedural
placeholders — the importer only swaps the actors, items, dirt, particles, and audio.

---

## How to add a new dino type

Spawning, eggs, growth, following, dancing, and the dig-helper logic are all
data-driven, so a new species is mostly data + one roster line. The steps below are
verified against `GameEnums.cs`, `DinoDefinition.cs`, `GeneratedArtImporter.cs`, and
the `Tools/` scripts.

1. **Enum** — add a value to `DinoType` in `Assets/Scripts/Config/GameEnums.cs` (and
   a `DanceType` if you want a new dance style).

2. **Generate + slice the turnaround art** — add one `turnaround` entry to the
   `SPRITES` list in `Tools/generate_sprites.py` (`name`, `outdir`, `subject`), then:

   ```bash
   cd Tools
   python3 generate_sprites.py --only <id>   # 5 facings via image-to-image
   python3 slice_sprites.py   --only <id>     # -> Assets/Art/Generated/<id>/<id>_<DIR>.png (8)
   ```

   The dino also needs an egg sprite. Either reuse one of the existing four egg
   colors, or add a cell to the `eggs` item entry in `generate_sprites.py` and
   re-slice it.

3. **DinoDefinition asset** — create
   `Assets/Art/Placeholder/Config/Dino_<Name>.asset`
   (right-click ▸ Create ▸ DinoDigger ▸ Dino Definition). Set `Type`, `DisplayName`,
   `Dance`, `BodyColor`, and `EggColor`. (Sprites get filled in by the importer in
   step 5 — you can leave them empty.)

4. **Roster line in `GeneratedArtImporter`** — add a `DinoWire` to the `Dinos` array
   in `Assets/Scripts/Editor/GeneratedArtImporter.cs`:

   ```csharp
   new DinoWire("<Name>", "<folder>", "egg_<color>"),
   ```

   and add `"<folder>"` to the `CharacterFolders` array in the same file so the new
   turnaround PNGs get the correct per-actor pixels-per-unit on import. (`<Name>`
   must match the `Dino_<Name>.asset` file; `<folder>` is the `outdir` from step 2;
   `egg_<color>` is the egg PNG under `Assets/Art/Generated/eggs/`.)

5. **Register it in `GameConfig.Dinos`** — add the new `DinoDefinition` to the
   `Dinos` list on `Assets/Art/Placeholder/Config/GameConfig.asset` (drag it into the
   list in the inspector). Note that re-running *Generate Placeholder Art* rebuilds
   `GameConfig.Dinos` from the built-in four names — to make a fifth species survive
   regeneration, also add its name/dance to the `names`/`dances` arrays in
   `PlaceholderArtGenerator.BuildConfigs`.

6. Run **DinoDigger ▸ Import Generated Art** to wire the sliced walk sprites and egg
   onto the new definition, then press Play.
