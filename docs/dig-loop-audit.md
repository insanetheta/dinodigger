# Dig Loop 2.0 — Final Feature-Completeness + Fun-Progression Audit

**Ticket:** DinoDigger-p2t (ship gate for epic DinoDigger-6fi) · **Date:** 2026-08-09
**Verdict: PASS — ship it.** Every feature promised by the epic, its phase tickets and the
backstory/roster docs is implemented, wired to a certifying integration case, and was exercised
live in a scripted dig-by-dig playthrough with **zero console errors**. One new P1 design bug was
found and filed (DinoDigger-tyf, mega-fossil pity floods the island with skull markers — nothing
breaks, but the "rare" event reads common). Known art gaps were already tracked before this audit
(DinoDigger-hoy, -5bn, -dgn).

**Suite:** 86/86 double-green at HEAD (`Logs/integration_report.json`: passed=86 failed=0;
`NoConsoleErrors` registered last). Suite was not re-run by this audit — it audits the code and
the live game against the tickets.

---

## Part 1 — Feature completeness vs tickets + docs

Every row was verified in source by file+symbol and its case confirmed registered in
`Assets/Scripts/Testing/IntegrationTestCases.cs` with a body that actually tests the promise.

### Epic DinoDigger-6fi — Dig Loop 2.0

| Feature (ticket) | Code (file → symbol) | Certifying case | Status |
|---|---|---|---|
| D1a gravity cascade (7fw) | `Dig/DigModeController.cs` → `ClearTile` L3552, `SettleGrid` L3624, `SettlePass` L3720, `MaxSettlePasses=64` L175; taps dropped on falling tiles (`OnTileTapped` L3915); items ride their tile | `TilesFallAndSettle`, `CascadeNeverWedges` | VERIFIED |
| D1b crystals/geodes/pots (z4d) | `Config/GameEnums.cs` → `DigTileKind` L65; `PopCrystalBlobLogical` L3069; auto-pop pass in `SettleGrid` L3265; geode arms in `DirtTile.Damage` L542 → `FireBoomGeode` L3345 → `CameraFollow.ShakeDig` L225; `SprayPotCoins` L3403 | `CrystalPopFloodFill`, `BoomChainsResolve`, `PinataPotPays` | VERIFIED |
| D1c featured-toy roller (qhy) | `PlacePrimaryToy` L1938 + `EnsurePrimaryToy` L2079; `GameConfig.DigPrimaryToyWeights` {3,2,2,3,2,2,2,2}; `SaveData.LastPrimaryToy` persisted (GameManager L482) and restored (L711) | `EveryDigHasAToy` (10 sites, 8-kind roster, no-repeat) | VERIFIED |
| D1 feel harness (73a) | `GameConfig` DigFall*/DigSquash*/`DigFallEase` read live per mover (`SettlePass` L3758); `Editor/DemoDigMenu.cs` (8 menu items); public `Demo*` surface L770-812 | (visual harness; motion covered by D1a cases) | VERIFIED |
| D2a multi-cell bones (0z5) | `BoneType` GameEnums L88; `DirtTile.SetBonePeek/ClearBonePeek/CoversBone`; `GameManager.BankBone` L174; monotonic `Uncovered` L345 | `BoneSpansCells` | VERIFIED |
| D2b skeleton board + save v5 (5ve) | `UI/SkeletonBoard(.Slot/.Tap).cs`; `Config/SkeletonPlan.cs` (5 species, 3/3/6/6/6 = 24 bones); `SaveData.CurrentVersion=5`, `Bones/RevivedSpecies/DinoMatic*`; `SaveManager.MigrateToV5` L119 | `SkeletonBoardFills`, `BoneDropRate`, `SaveRoundtrip` (v2→v5, v4→v5) | VERIFIED |
| D2c Dino-Matic + revival + dup payout (3rz) | `Overworld/DinoMaticController.cs`; `DinoMatic : BuildingController` (DinoMatic.cs L37); `TownController.SetFreeSite` L446; ceremony reuse `GameManager.RequestRevival` L3008; `DuplicateBoneCoins=5` L149 | `MachineExcavates`, `ReviveCeremonyJoins`, `DuplicateBonePaysOut` | VERIFIED |
| D3a depth layers + ladder (dv1) | `Dig/DigDepth.cs`, `Dig/DigLadder.cs`; `CameraFollow.DipDig` L276; `DigLadderRevealFraction=0.6`; 6 deep-layer multipliers; 2-layer max; no ladder on mega (DigDepth L195) | `LadderDescends`, `DeeperLayerRicher` | VERIFIED |
| D3b mega-fossil sites (84f) | `Dig/DigMegaFossil.cs`; `DigMound.SetMegaFossil/IsMegaFossil`; `GameManager.RollMegaFossilMound` L1100 (pity L1117); 7x9 `DigMegaRows/Columns`; round ends on bones not loot (`MaybeFinishMegaRound` L266); one-marked-mound cap + pity paid at the mark (`MarkMegaFossilMound`, DinoDigger-tyf) | `MegaFossilCompletes`, `MegaFossilOneAtATime` | VERIFIED (finding 1 fixed) |
| D3c wave-2 toys (u47) | `Dig/DigToysWave2.cs`, `Dig/DigCritter.cs`; `DigTileKind.Water/Vein/Mushroom`; `MoveBuriedItem` transaction chokepoint (DigToysWave2 L323) | `WaterPocketWashes`, `CritterCatchable`, `GemVeinChains`, `MushroomBoings` | VERIFIED |
| Dig audio pass (7c4) | `Managers/AudioManager.cs` → `SfxCategory` buses L9, per-clip gain L95-127, mute suppresses at the single chokepoint L107, second music voice `_danceLoop` L52; `Tools/ASSET_SOURCES.md` documents both new Kenney packs + gain manifest | `AudioHooksFire` (registered just before `NoConsoleErrors`) | VERIFIED |
| Egg shards retired, eggs kept | zero `CollectShard/ShardCollected/ShardsPerHatch` in runtime; `LegacyShardsPerHatch` frozen in SaveData for migration only; stray shards downgrade to treasure (GameManager L1524); egg/hatch path intact for the 4 egg species | `BoneDropRate`, `UniqueDinoNoDupes`, `EggHatch` | VERIFIED |

### Epic DinoDigger-b48 — Machine Friends (vs docs/backstory.md + docs/machine-roster-eval.md)

Docs promise five machines: Sprinkles, Tuggy, Glow, Zippy, Doodle. Eval verdicts: Glow BUILD,
Doodle BUILD, Sprinkles REDESIGN-then-build, Tuggy CUT, Zippy CUT. Shipped: **Doodle, Sprinkles,
Glow as evaluated; Tuggy built per Greg's override (as the duck-amplifying redesign, not the
doc's ferry); Zippy genuinely absent from all runtime code** (concept PNG only). Matches the
epic's ruling exactly — nothing silently dropped, nothing cut that was ordered built.

| Machine | Code | Case | Status |
|---|---|---|---|
| Shared base | `Overworld/MachineFriend.cs` (state-derived visuals, gauge, beacon, resting-scale-safe wobble, 4-outcome tap contract) + `MachineFriendController.cs` (gates, one-at-a-time pacing, placement, persistence) | `MachineDiscoveryQueue` | VERIFIED |
| Doodle | `DoodleMachine.cs`; dancers only via `GameManager.MachineAcquireDancers` L2929 (never buddies/builders) | `DoodleDanceParty` | VERIFIED |
| Sprinkles | `SprinklesMachine.cs`; `BerrySprout.WaterNow()` → private `Ripen()`; `IsReady` needs water AND a thirsty sprout | `SprinklesRipensOnTap` | VERIFIED |
| Tuggy | `TuggyMachine.cs`; `SpawnEscortDuck` into separate `_escorts` (never counts against `MaxAlive`); duck out-ranks machine in `TappableRank` | `TuggyTowsDucklings` | VERIFIED |
| Glow | `Dig/GlowBot.cs` + `Dig/DigGlow.cs`; gate `GameManager.NotifyDeepDigLayer` (first dark stratum); alpha-floor-only beam; excluded from overworld placement | `GlowRevealsAdjacent` | VERIFIED |
| Persistence | `SaveData.MachinesWoken` + `MachineGatesTripped` (string ids, additive-on-v4) | `SaveRoundtrip` + machine cases | VERIFIED |

### Epic DinoDigger-5yw — Jurassic-earth environment

`Config/EnvDressing.cs` (47-piece connected-blob key, exact-seam proof), `PlaceholderLibrary` env
block, `GeneratedArtImporter` env section (141 blob pieces = 3 biomes x 47), `Editor/SceneBuilder.cs`
deterministic hash painting + "Decals" tilemap + `SameBiomeAt`. On disk: 277 PNGs in
`Assets/Art/Generated/env/` incl. all 141 `*_b###` pieces and `contact_sheet.png`. Case
`EnvDressingApplied` asserts variants, per-cell blob topology (>100 cells, 0 wrong), transition
masks, determinism (>500 cells), decal grammar, and unchanged prop footprints/colliders.
**VERIFIED** — and confirmed live: the island renders with connected banks, no spill/cut-feature
defects (see `Logs/audit-captures/overworld_wide.png`).

### Beads hygiene

All epic children are closed. Still open (correctly): DinoDigger-hoy (D3 placeholder art incl.
Glow's sprite), -5bn (machine dormant/awake art), -dgn (tree/rock variant painting), -aop
(stego pose bug), -p9g (latent pink-halo risk), plus the new -tyf (below). Epic DinoDigger-6fi
has no remaining open children and can close on Greg's sign-off of this audit.

---

## Part 2 — Scripted dig-by-dig playthrough (live, editor play mode)

Seeded demo save (v4 JSON: 2 buddies + 2 residents = all four egg species, ParadeDone, 12 coins,
1 finished building, doodle gate tripped, 4 legacy shards) then played through the real input
pipeline (`InputService.SimulateTap`) via the Demo menu + public API. **Bonus verification:** the
v4→v5 migration ran live and converted exactly per formula — 4 shards → floor(4·3/5) = 2
Pteranodon bones, shards zeroed.

**Featured-toy variety across 7 consecutive site builds** (read from the roller's own persisted
`LastPrimaryToy`, cross-checked visually):

| Site | Board | Featured toy | Board evidence |
|---|---|---|---|
| Dig 1 | standard 5x7 | **Surprise Pocket** | `dig1_pocket_fresh.png` |
| Dig 2, layer 1 | standard 5x7 | **Boom Geode** | `dig2_geode_fresh.png` |
| Dig 2, layer 2 | ladder descent | **Critter** | `dig2_layer2_dark.png` |
| Dig 3 | mega 7x9 | **Crystal Cluster** | `dig3_mega_crystal_fresh.png` |
| Dig 4 | mega 7x9 | **Water Pocket** | `dig4_mega_water_fresh.png` |
| Dig 5 | mega 7x9 | **Critter** | `dig5_mega_critter_fresh.png` |
| Dig 6 | mega 7x9 | **Surprise Pocket** | `dig6_mega_pocket_fresh.png` |

**Claim verified: every dig led with a toy, and no two consecutive sites repeated one** —
5 distinct kinds across the run (secondary rolls added veins, pots, mushrooms and more crystals
on top). No P0 filed.

**Fossil/fun progression, dig by dig:** dig 1 banked the migrated skeleton's missing femur →
Pteranodon complete; the Dino-Matic arrived only after Doodle had been discovered (pacing gate
held) and was **excavated by the town crew in the background as a free site** — no plot, no
coins, player never drafted. Revival ceremony ran skip-tolerant on tap → baby Pteranodon roaming
(`dinomatic_excavated.png`, `dinomatic_revival_baby.png`, `revived_pteranodon_baby.png`). Dig 2's
ladder appeared at the reveal threshold; layer 2 was visibly darker, harder and richer (wallet
6 → 47 in one deep layer), and **Glow** was met exactly as designed: glint from behind a wall
tile, reveal, one tap wakes it forever (persisted `MachinesWoken=[doodle,glow]`), perch + beam
(`dig2_glow_awake_lit.png`). Mega sites buried entire remaining skeletons and refused to end
until the bones were out; digs 3-6 completed Velociraptor, Ankylosaurus, Parasaurolophus and
Spinosaurus; all five ceremonies played; **skeleton board fully complete + revived**
(`skeleton_board_full.png`); 9 dinos in the world at close. **Zero console errors for the whole
session.**

**Contact sheet (all 16 captures, captioned):**
`Logs/audit-captures/contact_sheet_audit.png`

---

## Findings

1. **P1 bug, filed as DinoDigger-tyf — FIXED** — mega-fossil pity flooded the island:
   `_megaFossilSeenThisSession` was only set on *entering* a mega dig, so at island build every
   mound rolled past the pity threshold was guaranteed mega — observed 7/12 and 9/12 skull-marked
   mounds. Fixed by paying pity at the MARK rather than at the dig (`MarkMegaFossilMound`, the one
   door every mark now goes through) plus a hard one-marked-mound cap checked against the live
   mounds, so island build, respawn and the force hook all obey it. Case
   `MegaFossilOneAtATime` asserts exactly one skull after a whole-island roll, never two at any
   point, that the mark still lands inside the pity window, and that repeated forced respawns
   cannot add a second.
2. **Pacing observation (no bead)** — with two builder residents the Dino-Matic free-site
   excavation completes within a couple of minutes of arrival, so "found buried → dug out"
   can resolve before the child returns from one dig. All knobs are in GameConfig / BuildingController
   work rates if Greg wants the landmark to linger.
3. **Known art gaps (already tracked, not new):** Glow + wave-2 toys + ladder ride placeholder
   tints (DinoDigger-hoy); machines use tinted concept paintings for dormant state (DinoDigger-5bn);
   tree/rock variants painted [0]-only (DinoDigger-dgn).
