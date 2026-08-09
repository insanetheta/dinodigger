# Dino Digger — Asset Sources

All third-party art and audio used in Dino Digger are **CC0 (public domain)**.
No attribution is legally required, but sources are recorded here for provenance.
Re-download everything with `bash Tools/download_assets.sh`.

## Art (Kenney — kenney.nl)

| Pack | Folder | License | Source page / zip | Used for |
|------|--------|---------|-------------------|----------|
| Isometric Blocks | `Assets/Art/Kenney/IsometricBlocks/` | CC0 | https://kenney.nl/assets/isometric-blocks<br>zip: https://kenney.nl/media/pages/assets/isometric-blocks/86a0152f5b-1677662261/kenney_isometric-blocks.zip | Grass / dirt / water / voxel terrain blocks for the dig grid |
| UI Pack | `Assets/Art/Kenney/UIPack/` | CC0 | https://kenney.nl/assets/ui-pack<br>zip: https://kenney.nl/media/pages/assets/ui-pack/f651646eab-1718203990/kenney_ui-pack.zip | Big rounded buttons, panels, and UI frames (toddler-friendly menus) |
| Isometric Miniature Farm | `Assets/Art/Kenney/IsometricMiniatureFarm/` | CC0 | https://kenney.nl/assets/isometric-miniature-farm<br>zip: https://kenney.nl/media/pages/assets/isometric-miniature-farm/abd0274182-1670690319/kenney_isometric-miniature-farm.zip | Nature / farm props: trees, crops, rocks, fences, buildings (isometric decor) |

Payload kept: individual `PNG/` sprites (+ `Tilesheet/` for blocks, + `Angle/` renders for farm) and `License.txt`.
Stripped to keep the project light: `Vector/` SVG sources, `Preview*`/`Sample*` images, bundled `Font/`, duplicate `Sounds/`, and `Thumbs.db`/`desktop.ini` junk.

## Sound Effects (Kenney — kenney.nl)

| Pack | Folder | License | Source page / zip | Used for |
|------|--------|---------|-------------------|----------|
| Digital Audio | `Assets/Audio/Kenney/DigitalAudio/` | CC0 | https://kenney.nl/assets/digital-audio<br>zip: https://kenney.nl/media/pages/assets/digital-audio/216eac4753-1677590265/kenney_digital-audio.zip | Playful digital blips / rewards / pickup sounds |
| Interface Sounds | `Assets/Audio/Kenney/InterfaceSounds/` | CC0 | https://kenney.nl/assets/interface-sounds<br>zip: https://kenney.nl/media/pages/assets/interface-sounds/fa43c1dd4d-1677589452/kenney_interface-sounds.zip | UI clicks, confirmations, taps, toggles for buttons |
| Impact Sounds | `Assets/Audio/Kenney/ImpactSounds/` | CC0 | https://kenney.nl/assets/impact-sounds<br>zip: https://kenney.nl/media/pages/assets/impact-sounds/87b4ddecda-1677589768/kenney_impact-sounds.zip | Dig one-shots: tile cracks, cascade thumps, crystal/glass pops, bone knocks, pot crack, machine wake bell |
| Music Jingles | `Assets/Audio/Kenney/MusicJingles/` | CC0 | https://kenney.nl/assets/music-jingles<br>zip: https://kenney.nl/media/pages/assets/music-jingles/f37e530b9e-1677590399/kenney_music-jingles.zip | Two pizzicato phrases: coin-spray flourish + the dance party's music-box vamp |

Payload kept: `Audio/*.ogg` and `License.txt` only.

**Impact Sounds and Music Jingles are CURATED, not copied wholesale.** Those packs ship 130
and 85 oggs respectively and the dig audio pass uses 13 of them, so `download_assets.sh` copies
an explicit filename whitelist (`copy_picks`) instead of `*.ogg`. If you wire a new clip from
either pack in `GeneratedArtImporter`, add its filename to the matching list in the script or a
fresh `download_assets.sh` run will leave the importer reporting it missing.

### Dig audio pass — clip manifest (DinoDigger-7c4)

Every game moment below is a named slot on `AudioConfig`, wired to its file by
`GeneratedArtImporter`. "Gain" is the per-clip trim baked into the importer — see the
loudness note under the table.

| Game moment | AudioConfig slot | Pack | File | Gain |
|---|---|---|---|---|
| Tile crack (variant A) | `TileCrackA` | Impact Sounds | `impactMining_000.ogg` | 1.00 |
| Tile crack (variant B) | `TileCrackB` | Impact Sounds | `impactMining_001.ogg` | 1.00 |
| Tile crack (variant C) | `TileCrackC` | Impact Sounds | `impactMining_002.ogg` | 1.00 |
| Tile crumble (tile destroyed) | `Crumble` | Interface Sounds | `scratch_004.ogg` | 1.00 |
| Cascade landing thump | `LandingThump` | Impact Sounds | `impactSoft_heavy_000.ogg` | 0.69 |
| Geode soft whumph | `Whumph` | Impact Sounds | `impactSoft_heavy_001.ogg` | 0.69 |
| Geode fuse sizzle | `FuseSizzle` | Digital Audio | `lowRandom.ogg` | 0.35 |
| Crystal pop (small) | `CrystalPop` | Impact Sounds | `impactGlass_light_000.ogg` | 1.00 |
| Crystal pop (big blob) | `CrystalPopBig` | Impact Sounds | `impactGlass_medium_000.ogg` | 1.00 |
| Pinata pot crack | `PotCrack` | Impact Sounds | `impactTin_medium_000.ogg` | 1.00 |
| Coin spray jingle | `CoinSpray` | Music Jingles | `jingles_PIZZI00.ogg` | 0.71 |
| Bone rattle | `BoneRattle` | Impact Sounds | `impactWood_light_000.ogg` | 1.00 |
| Whole-bone pop | `BonePop` | Digital Audio | `powerUp7.ogg` | 0.51 |
| Coin / treasure collect | `TreasureCollect` | Digital Audio | `highUp.ogg` | 0.35 |
| Egg + machine ceremony poof | `CeremonyPoof` | Impact Sounds | `impactSoft_medium_000.ogg` | 0.48 |
| Machine wake chime | `MachineWake` | Impact Sounds | `impactBell_heavy_002.ogg` | 1.00 |
| Machine not-ready gurgle/wobble | `Gurgle` | Digital Audio | `lowDown.ogg` | 0.35 |
| Dance party music-box loop | `DanceLoop` | Music Jingles | `jingles_PIZZI03.ogg` | 0.60 |
| Duck quack | `Honk` | Digital Audio | `twoTone1.ogg` | 0.56 |
| Tuggy toot | `Toot` | Digital Audio | `twoTone2.ogg` | 0.56 |
| Sprinkles water gush | `WaterGush` | Interface Sounds | `scroll_003.ogg` | 1.00 |
| Giggle-pocket giggle | `Giggle` | Digital Audio | `pepSound3.ogg` | 0.35 |
| Depth ladder reveal ding | `LadderDing` | Digital Audio | `threeTone2.ogg` | 0.50 |
| Gem vein spark zap | `SparkZap` | Digital Audio | `zap1.ogg` | 0.39 |
| Mushroom boing | `Boing` | Digital Audio | `phaseJump1.ogg` | 0.58 |

The Dig Loop 2.0 tiles (`Water`, `Vein`, `Mushroom`) and the depth ladder landed in a parallel
wave while this audio pass was in flight, so those four sounds found real homes after all:
`DigLadder.Build` (ding), `DigToysWave2.GushWaterPocket` (gush), the vein's spark walk, and
`OnMushroomBounced`. `SparkZap` takes the segment index and run length so a vein sparks as one
rising zip rather than N identical zaps.

`WaterGush` is used twice on purpose — the dig's water pocket and Sprinkles' watering spray are
the game's two "water happens now" moments and should share a voice.

**Loudness normalisation.** The packs are mastered at wildly different levels — every clip peaks
near −1 dBFS, but *mean* level runs from about −25 dB (Impact Sounds) to −10 dB (Digital Audio),
so the digital blips scream next to the impacts. Rather than re-encode the CC0 files (which
would fork them from their upstream source), each clip carries a per-clip gain in `AudioConfig`,
computed offline as `clamp(10^((−20 − mean_dB)/20), 0.35, 1.0)` from
`ffmpeg -i <clip> -af volumedetect -f null /dev/null`. Target mean is −20 dB. Gain is never
boosted above 1.0 (peaks are already near full scale, so a boost would clip); it only pulls the
hot clips down. That is what makes the set toddler-soft, and it is why the shipped files are
byte-identical to the pack contents. To re-derive the numbers after changing a clip, re-run
`volumedetect` and apply the formula above.

## Background Music (OpenGameArt)

| Track | File | License | Source | Used for |
|-------|------|---------|--------|----------|
| Bluebonnet (in B major, looped) | `Assets/Audio/Music/Bluebonnet_looped.ogg` | CC0 (verified on page) | Author: **Kistol** — https://opengameart.org/content/bluebonnet<br>file: https://opengameart.org/sites/default/files/bluebonnet_in_b_major_looped_0.ogg | Gentle, happy, calm loopable background music (~2.7 MB OGG, stereo 44.1 kHz) |

## License note

CC0 1.0 Universal — the creators have waived all copyright and related rights.
Assets may be used freely for any purpose, commercial or non-commercial, with no
attribution required. Full text: https://creativecommons.org/publicdomain/zero/1.0/
