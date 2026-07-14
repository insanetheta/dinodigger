# Dino Roster (9 species)

Design reference for the full 9-species roster behind the "unique dinos + egg-shard
nest" epic (DinoDigger-bl6). The first four are the **original egg-hatchable** species
(dug eggs only ever roll these). The five new ones are **shard-exclusive**: they never
come from an egg and are unlocked from egg shards assembled at the nest (bl6.4).

Colors are the placeholder blob/egg tints authored by `PlaceholderArtGenerator`
(`Assets/Scripts/Editor/PlaceholderArtGenerator.cs`, `DinoColors`); real turnarounds +
eggs land in bl6.2. Each was chosen to be **silhouette-distinct and color-distinct**
from every other so a toddler tells them apart instantly.

## Egg-hatchable (dug from eggs)

| # | Species | Color | Hex | Egg pattern | Dance | One-liner |
|---|---------|-------|-----|-------------|-------|-----------|
| 0 | **T-Rex** | green | `#5CB85C` | pale-green shell, dark-green blotches | Stomp & Roar | The big-toothed boss who stomps and roars with joy. |
| 1 | **Triceratops** | orange | `#F28C33` | cream shell, three orange dots (like its horns) | Head Shake | Three-horned charger who waggles its frill hello. |
| 2 | **Brachiosaurus** | blue | `#4D8CF2` | sky-blue shell, soft blue speckles | Neck Sway | Gentle long-neck who sways up high to shake fruit trees. |
| 3 | **Stegosaurus** | purple | `#9E66D9` | lilac shell, purple plate-shaped spots | Tail Wag | Plated pal who wags its spiky tail like a happy puppy. |

## Shard-exclusive (assembled at the nest)

| # | Species | Color | Hex | Egg pattern | Dance | One-liner |
|---|---------|-------|-----|-------------|-------|-----------|
| 4 | **Pteranodon** | teal | `#33BFB8` | teal shell, thin swept "wing" streaks | Wing Flap | Sky-gliding crest-head who flaps its big wings to dance. |
| 5 | **Ankylosaurus** | red | `#D94038` | brick-red shell, armored pebble texture | Tail Club | Tank on legs who thumps its heavy tail-club: BONK! |
| 6 | **Spinosaurus** | yellow-green | `#B3CC40` | olive shell, tall sail-stripe down the middle | Sail Wiggle | River-runner who wiggles the giant sail on its back. |
| 7 | **Parasaurolophus** | pink | `#F28CBF` | pink shell, one long curved crest-stripe | Crest Toot | Musical duck-bill who toots a tune through its crest. |
| 8 | **Velociraptor** | sky-grey | `#9EB8CC` | grey shell, quick zig-zag feather flecks | Spin Hop | Speedy little scamp who spin-hops in excited circles. |

## `DinoType` enum extension (done)

`Assets/Scripts/Config/GameEnums.cs` — appended indices 4-8 (order is load-bearing;
code keys "egg species" off `index < 4`, so never reorder or insert before index 4):

```
TRex=0, Triceratops=1, Brachiosaurus=2, Stegosaurus=3,          // egg-hatchable
Pteranodon=4, Ankylosaurus=5, Spinosaurus=6,                    // shard-exclusive
Parasaurolophus=7, Velociraptor=8
```

Helper `DinoSpecies` (same file) centralizes `EggHatchableCount = 4`, `TotalCount = 9`
and `IsEggHatchable(type)`. `DanceType` gained matching entries `WingFlap, TailClub,
SailWiggle, CrestToot, SpinHop` (4-8).

## `GameConfig.Dinos` roster notes

`GameConfig.Dinos` is a plain list of `DinoDefinition` assets, matched by `Type`
(`GetDino`). The list order is not significant — only `Type` matters. Two generators
populate it:

- **`PlaceholderArtGenerator`** (this lane) now builds all **9** placeholder
  `DinoDefinition` assets (`Dino_TRex` … `Dino_Velociraptor`) so every species has a
  colored blob + egg and the game/tests run before real art. Egg-hatchable vs
  shard-exclusive is **not** a `DinoDefinition` flag — it is derived from the enum
  index (`DinoSpecies.IsEggHatchable`), so no schema change was needed.
- **`GeneratedArtImporter`** (other lane, bl6.2) will wire the real per-species art
  into its own `GameConfig`. It currently knows only the original four; adding the new
  five there is bl6.2's job.

## Placeholder `DinoDefinition` assets plan

Re-running **DinoDigger/Generate Placeholder Art** now emits, under
`Assets/Art/Placeholder/`:

- `Sprites/dino_0..dino_8.png` + `Sprites/egg_0..egg_8.png` (one blob + egg per
  species, tinted per `DinoColors`).
- `Sprites/item_shard.png` — the sparkly broken-shell **egg shard** (new `ItemType.Shard`).
- `Config/Dino_<Name>.asset` for all 9 species, each with `Type`, `DisplayName`,
  `Dance`, `BodyColor`/`EggColor`, blob walk/idle sprites, and egg sprite.
- `PlaceholderLibrary.ShardSprite` wired to `item_shard`.

The blobs are intentionally identical in shape (color-only distinction) — that is
enough for logic + integration tests; true silhouettes are bl6.2.
