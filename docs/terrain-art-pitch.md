# Terrain Art Pass — Concept Pitch: "Jurassic Earth Toy-Box"

Ticket: DinoDigger-cdt. Companion to `docs/backstory.md` (post-human thriving-nature
fiction) and the shipped art direction (outlined AI actors over simple flat
environments — see auto-memory `dinodigger-art-direction.md`).

## The one question that matters

At ages 2-3, **outlined = tappable** is the game's entire interaction language. Any
terrain upgrade lives or dies on one test: *do the outlined actors still pop?*

Judge it here (left = current shipped tiles, right = concept re-dress, **identical
actors, props and layout on both sides**):

- `Assets/Art/Concepts/terrain/mockup_meadow_before_after.png` — meadow + path region
- `Assets/Art/Concepts/terrain/mockup_stream_before_after.png` — stream + bridge region

The concept side arguably pops *harder* than the flats: the white tile-grid rims are
gone (the current tiles carry dark rims and grid lines — terrain is wearing outlines
today, which dilutes the tap language), and the dressed ground is deliberately capped
below the actors in saturation and contrast, exactly like the shipped
`Assets/Art/Generated/digbg/dig_background.png` — the one rich painted environment
already in the game, which works because it sits *behind* outlined art at lower
saturation with big soft shapes.

## Style definition — six rules

**"Jurassic earth toy-box": a living ancient meadow painted like a picture-book
endpaper — rich enough to feel like a world, quiet enough to stay furniture.**

1. **Terrain gets NO dark outlines, ever.** The black outline is the tap affordance
   and belongs exclusively to actors, machines, mounds, eggs and other tappables.
   Ground shapes end in soft color-against-color edges. (This also means the current
   tiles' dark rims and grid lines go away — a readability *gain*.)
2. **Capped energy, enforced in the pipeline.** Terrain saturation is capped at
   ~80% and value contrast compressed to ~85% around the local mean ("quiet pass" in
   `Tools/generate_terrain_concepts.py`). Never trusted to prompts — generators
   always over-saturate. Actors keep full saturation + black outlines + white
   highlights, so a permanent energy gap separates the two layers.
3. **Big soft shapes, never noise.** Each biome is 2-3 *close* tones in blotches
   roughly 1-3 cells across, melting into each other. No high-frequency texture, no
   obvious repeats. (Big readable shapes are also what hides tile seams — see risks.)
4. **Life in clusters, not carpets — with a decal grammar.** Jurassic dressing is
   sparse decals with strict placement rules: grass gets ferns + moss/clover only;
   paths get footprint trails + faint cream mineral veins; water gets lily pads with
   one blossom. **Nothing on grass may look pickable** — concept v1 scattered warm
   round stones on grass and they instantly read as tappable fruit; rationed to one
   accent cluster per region, beside the path. Sparkle stays reserved for
   interactables.
5. **Warm ancient-earth accents, never danger.** The "volcanic" note from the
   fiction is carried by sun-warmed amber stones and pale mineral veins — no lava,
   fire, smoke, cracks or ash (backstory's "no extinction imagery" rule). Footprint
   trails and fern clusters quietly say *dinosaurs live here*; mineral veins hint at
   the buried machine-age below.
6. **Palette continuity.** Each biome's mean color stays pinned to its shipped flat
   tile (grass ≈ RGB 96,190,84 · path ≈ 196,158,104 · water ≈ 80,150,230) via a
   mean-match tint in the pipeline, so every already-tuned actor/UI contrast
   relationship survives the re-dress. Water alone keeps extra chroma — it is a
   walkability boundary and must read as WATER at toddler glance-speed.

Concept swatches (all generated, quiet-passed):

- `Assets/Art/Concepts/terrain/grass_plate.png` — mottled meadow, ferns, clover, moss
- `Assets/Art/Concepts/terrain/path_plate.png` — packed earth, mineral veins, pebbles
- `Assets/Art/Concepts/terrain/water_plate.png` — calm water, ripple bands, lily pads
- `Assets/Art/Concepts/terrain/decal_{fern,moss,footprints,stones}.png` — decal set

## Production approach — recommendation: (c) overlay layer, borrowing (b)'s slicing

Options weighed for a 48x48 isometric tilemap (cell 1x0.5, 128x64 px tiles) on a
WebGL build where shipped sprites should stay ≤256 px:

| | Approach | Cost | Risk | Consistency |
|---|---|---|---|---|
| a | Per-tile generated variants w/ seam-blend borders | High (gen churn: every tile must edge-match every neighbor; heavy retries) | **High** — AI cannot hit pixel-exact edges; seams everywhere | Low |
| b | Large generated plates (4-8 cells) sliced to tiles | Medium | Medium — perfect inside a plate, visible seams at plate borders; repeats at plate period | High inside plate |
| **c** | **Keep base tiles + generated OVERLAY layer (recommended)** | **Low** | **Low — additive & toggleable; base gameplay art untouched** | **High** |
| d | Full underlay painting per island region | Highest (repaint on any layout change) | High — 48-cell region ≈ 3k-px textures; blows the WebGL 256 px sprite budget and memory | Highest |

**(c) in concrete terms — three additive layers, each shippable alone:**

1. **Base v2 tiles** — replace the flat rimmed diamonds with 4-6 interchangeable
   "quiet mottle" variants per biome, *sliced from one generated plate* (that's the
   (b) borrow: one plate guarantees the variants share palette and blotch scale).
   Diamond edges get a 2-3 px feather; at rule-3 contrast levels the seams sit below
   visual threshold — the mockups' ground is exactly this material. 128x64 each,
   one 1024x512 atlas per biome worst case.
2. **Transition dressing** — grass→path melts and the water shoreline (pale sand rim
   + soft waterline) are *baked offline* by the Python pipeline as edge overlay
   tiles, using blurred masks over the same plates (precisely what the mockups do).
   Not generated as strips — AI strips can't guarantee edge alignment; procedural
   masks over continuous plates get it for free. Unity side: one extra Tilemap
   layer with rule-tile (or hand-keyed) edge pieces.
3. **Decal scatter** — fern/moss/footprint/stone decals as a third layer
   (deterministic seeded scatter honoring the rule-4 grammar), one 512x512 atlas.

Structures, mounds, actors, eggs, bridge planks: untouched. (The placeholder bridge
tile needs its baked-in blue water diamond cropped to planks — 10 minutes.)

## Cost estimate

- **Generation:** the whole concept set was 4 gens, first-take, ≈ $0.16
  (gemini-2.5-flash-image via OpenRouter). Production set — 2-3 plate takes per
  biome to pick from, 2 decal sheets, retries — ≈ 25-35 gens, **under $1.50**.
- **Pipeline work:** extend `Tools/generate_terrain_concepts.py` into a
  plate→variant-tile/edge-tile/decal-atlas baker: **~1 session**.
- **Unity work:** import + Tilemap variant painting, transition layer, decal
  scatter layer, integration-test rerun: **1-2 sessions**.
- **Shipped size:** ~3 biome atlases + 1 decal atlas + edge pieces ≈ **+0.5 MB**
  (compressed) to the WebGL build. Nothing over 1024 px ships; individual sprites
  stay ≤256 px.

## Phasing

1. **Pilot (recommended first slice):** meadow region around spawn, base-v2 tiles +
   decals only — no transitions yet. Cheapest reversible slice that answers "does it
   feel better in motion?" Run integration tests + a tap-language screenshot audit
   (kid-eye check: nothing non-tappable draws the eye).
2. **Transitions:** grass→path melt + water shoreline island-wide.
3. **Full island + regional flavor:** warmer stone accents near dig sites, denser
   ferns near streams, footprint trails on walkways; dig-mound surrounds dressed.

## Risks & mitigations

- **Seams between variant tiles** — the classic killer of approach (a)/(b). Mitigated
  three ways: all variants sliced from one plate; rule-3 contrast caps keep local
  tone deltas tiny (the mockup ground shows the material at target contrast — no
  visible grid); 2-3 px edge feather. Fallback if seams still show on device: ship
  flat-color base v2 (rims removed, mean-matched) + decals only — still a big win.
- **Readability regression (the load-bearing constraint).** Mitigations: quiet pass
  is code, not taste (saturation/contrast caps in the baker); decal grammar bans
  pickable-looking shapes on grass (already caught once in concepting — the
  fruit-like stones); before/after actor-pop mockups repeated per phase; phased
  rollout is fully toggleable per layer.
- **WebGL texture budget.** All shipped art is small atlases (≈+0.5 MB); the big
  1024 plates never ship — they are offline pipeline inputs only. Reject of option
  (d) is exactly this risk.
- **Palette drift across gens** (every future re-gen looks slightly different).
  Mitigated by rule 6: the baker mean-matches every plate to the pinned biome color,
  so re-gens stay interchangeable.
- **Water misread as walkable** once soft-edged. Mitigated: water keeps higher
  chroma + a distinct pale shoreline band (both already in the stream mockup);
  walkability logic (`OverworldMap`) is untouched either way.

## Reproduce / iterate

```bash
python3 Tools/generate_terrain_concepts.py gen     # 4 gens, ~$0.16
python3 Tools/generate_terrain_concepts.py slice   # raw -> Concepts/terrain (quiet pass)
python3 Tools/generate_terrain_concepts.py mock    # before/after sheets
```
