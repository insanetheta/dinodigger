#!/usr/bin/env python3
"""Production Jurassic-earth ENVIRONMENT asset set (DinoDigger-c7m).

Production-ises Tools/generate_terrain_concepts.py (concepts, DinoDigger-cdt) into the
FULL land-asset set: ground, transitions, decals, tappable props, decor.

Raw gens land in Tools/raw/env_*.png. Finished art lands in Assets/Art/Generated/env/.
The Unity importer integration is a FOLLOW-UP ticket — nothing here touches Assets/Scripts.

===================================================================================
 MANIFEST — what is produced, and the old -> new mapping (world size is PRESERVED)
===================================================================================
World size == pixels / PPU. Every prop below keeps the EXACT world footprint of the
sprite it replaces, so no collider, no cell pitch and no spawn rect needs retuning.
The follow-up importer just has to set the PPU in the last column.

 out file (under Assets/Art/Generated/env/)   px        replaces (Placeholder/Sprites)  old px @ PPU   world W x H   NEW PPU
 -------------------------------------------- --------- ------------------------------- -------------- ------------- -------
 ground/tile_grass_00..15.png                  256x128   tile_grass.png                  128x64  @128   1.000 x 0.500   256
 ground/tile_path_00..15.png                   256x128   tile_path.png                   128x64  @128   1.000 x 0.500   256
 ground/tile_water_00..15.png                  256x128   tile_water.png                  128x64  @128   1.000 x 0.500   256
 ground/tile_bed_00..03.png                    256x128   (new — berry garden bed)        --             1.000 x 0.500   256
 ground/edge_grass_path_<1..15>.png            256x128   (new — transition layer)        --             1.000 x 0.500   256
 ground/edge_grass_water_<1..15>.png           256x128   (new — shoreline layer)         --             1.000 x 0.500   256
 ground/edge_grass_bed_<1..15>.png             256x128   (new — garden bed edge)         --             1.000 x 0.500   256
 decor/bridge_a.png, bridge_b.png              256x128   tile_bridge.png                 128x64  @128   1.000 x 0.500   256
 prop/mound.png                                256x128   tile_mound.png                  128x64  @128   1.000 x 0.500   256
 prop/tree_cycad|gingko|conifer.png            256x256   tile_tree.png                   128x128 @128   1.000 x 1.000   256
 prop/tree_gingko_shake.png                    256x256   (new — Brachio shake pose)      --             1.000 x 1.000   256
 prop/rock_boulder.png, rock_mossy.png         256x256   tile_rock.png                   128x128 @128   1.000 x 1.000   256
 decor/fence_x.png, fence_y.png                256x512   Kenney fenceLow_E / fenceLow_N  256x512 @100   2.560 x 5.120   100
 decor/nest.png                                256x256   nest_base.png                   128x128 @100   1.280 x 1.280   200
 decal/decal_*.png                             <=256     (new — scatter layer)           --             see DECAL_WORLD_W  --

 fence_x/fence_y are byte-for-byte canvas-compatible drop-ins for the Kenney pieces:
 the art is composited into the SAME 256x512 canvas at the SAME alpha bbox the Kenney
 sprite uses (x 0..133, y 315..450 for _E / y 377..511 for _N), so SceneBuilder's
 "scale to sprite.bounds.size.x == 1 cell" maths lands the piece in the identical spot.

 ground/plate_*.png are the 1024^2 PIPELINE MASTERS. They are NOT shipped sprites —
 they exist so tiles can be re-sliced without re-generating. Everything shipped is <=256 px
 (the pitch's WebGL sprite budget).

 EDGE-TILE MASK BITS (map-cell space, the same axes OverworldMap uses):
   bit0 (1) = -Y neighbour   -> screen UPPER-RIGHT diamond edge
   bit1 (2) = +X neighbour   -> screen LOWER-RIGHT diamond edge
   bit2 (4) = +Y neighbour   -> screen LOWER-LEFT diamond edge
   bit3 (8) = -X neighbour   -> screen UPPER-LEFT diamond edge
 edge_grass_path_6.png therefore melts path in from the +X and +Y neighbours.

===================================================================================
 STYLE CONTRACT (docs/terrain-art-pitch.md — the 6 rules, enforced in CODE)
===================================================================================
  1. TERRAIN GETS NO OUTLINES, EVER. Outlines are the tap affordance. Enforced by
     splitting the styles: TERRAIN_STYLE / DECAL_STYLE / DECOR_STYLE all forbid dark
     lines; only PROP_STYLE (trees, rocks, mounds — real interaction targets) carries
     the thick black actor outline, matching generate_sprites.STYLE's weight.
  2. CAPPED ENERGY, IN THE PIPELINE. quiet() caps saturation ~80% and compresses value
     contrast to ~85% around the local mean on EVERY ground/decal/decor output. Never
     trusted to the prompt — the model always over-saturates. Props are NOT quieted:
     they are supposed to sit at actor energy.
  3. BIG SOFT SHAPES, NEVER NOISE. Plates ask for 2-3 close tones in 1-3-cell blotches.
  4. LIFE IN CLUSTERS, NOT CARPETS — decal grammar: grass gets ferns/moss/clover only;
     path gets footprints + pebbles + ONE warm stone cluster per region; water gets
     lilies. NOTHING on grass may read as pickable (the concept-v1 "fruit stones" bug).
  5. WARM ANCIENT-EARTH ACCENTS, NEVER DANGER. No lava/fire/smoke/ash/cracks.
  6. PALETTE CONTINUITY. quiet(tint=...) mean-matches each biome to its shipped flat
     tile colour (grass 96,190,84 · path 196,158,104 · water 80,150,230).

 PROMPT SCARS (inherited — do NOT simplify away):
  * generate_dig_props: lone small marks invite an inventor — the footprint decal must
    pin "flat painted marks, no creature anywhere".
  * generate_dig_props: pit/ring/canopy shapes read as FACES; every prompt carries the
    faceless clause. Trees especially: "no face, no eyes" or you get a Truffula ent.
  * generate_sprites BG_STYLE: full-bleed art must forbid frames/borders/vignettes;
    plates additionally forbid horizon/sky/perspective so the swatch stays a flat
    overhead texture and not a landscape painting.
  * generate_terrain_concepts: "volcanic" is poison for a toddler palette. The warm
    accent is "smooth warm amber stones", never named volcanic.
  * NEW (c7m): the concept plates came back VIGNETTED — the model rings its accents
    around the frame and leaves the middle bare, which slices into 16 tiles where 4
    are empty and 12 are busy. Every plate prompt now pins "spread evenly over the
    WHOLE frame INCLUDING the exact centre".
  * NEW (c7m): "isometric" in a prop prompt makes the model draw a diamond BASE PLATE
    under the prop. Props say "slightly from above, three-quarter view" instead, and
    explicitly forbid any ground/base/platform/tile under the subject.

Usage:
    python3 Tools/generate_env.py list
    python3 Tools/generate_env.py gen [<name>...]     # AI gens -> Tools/raw/env_*.png
    python3 Tools/generate_env.py bake [<group>...]   # raw -> Assets/Art/Generated/env/
    python3 Tools/generate_env.py sheet               # contact sheet for review
    python3 Tools/generate_env.py verify              # per-group verification sheets
"""
import os
import sys

REPO = "/Users/greg/projects/DinoDigger"
sys.path.insert(0, os.path.join(REPO, "Tools", "venv", "lib", "python3.13",
                                "site-packages"))
sys.path.insert(0, os.path.join(REPO, "Tools"))

import numpy as np                                            # noqa: E402
from PIL import Image, ImageFilter                            # noqa: E402
import generate_sprites as G                                  # noqa: E402
import slice_sprites as S                                     # noqa: E402
from generate_terrain_concepts import (quiet, neutralize,     # noqa: E402
                                       _unmix, TERRAIN_STYLE, DECAL_STYLE)

RAW = G.RAW_DIR
OUT = os.path.join(REPO, "Assets", "Art", "Generated", "env")
GROUND = os.path.join(OUT, "ground")
DECAL = os.path.join(OUT, "decal")
PROP = os.path.join(OUT, "prop")
DECOR = os.path.join(OUT, "decor")
PLACEHOLDER = os.path.join(REPO, "Assets", "Art", "Placeholder", "Sprites")
KENNEY_FENCE = os.path.join(REPO, "Assets", "Art", "Kenney",
                            "IsometricMiniatureFarm", "Isometric")

TILE_W, TILE_H = 256, 128       # one iso ground cell, PPU 256 -> 1.0 x 0.5 world
PROP_PX = 256                   # tree/rock canvas, PPU 256 -> 1.0 x 1.0 world
CELL = 256                      # map-space px per cell inside a plate
PLATE = 1024                    # plate edge -> PLATE/CELL == 4 cells across (4x4 = 16)

# Suggested world WIDTH for each decal, for the follow-up scatter layer. Decals ship
# trimmed (no fixed canvas) so the importer sizes them by width, not by PPU-per-file.
DECAL_WORLD_W = {
    "decal_fern": 0.42, "decal_moss": 0.34, "decal_footprints": 0.30,
    "decal_stones": 0.30, "decal_lily_blossom": 0.26, "decal_lily": 0.22,
    "decal_clover": 0.20, "decal_pebbles": 0.26,
}

# --- Styles -------------------------------------------------------------------------
# TERRAIN_STYLE and DECAL_STYLE are inherited verbatim from the concept pass (they are
# the pitch's rule 1 in prompt form). Two NEW styles are added here.

# The plate scar: kill the vignette bias so all 16 sliced variants carry material.
EVEN_SPREAD = (
    "CRITICAL: spread the tones and the few accents EVENLY over the WHOLE square frame, "
    "including the exact CENTRE of the image and all four corners. Do NOT leave the "
    "middle of the frame empty or plain, do NOT ring the accents around the border, do "
    "NOT make the centre brighter or emptier than the edges. The texture must look the "
    "same everywhere, with no focal point and no composition. "
)

# Tappable props. Derived from generate_sprites.PART_STYLE (which already strips the
# "big cute eyes" character cue that turns props into creatures) with the mechanical
# noun swapped for scenery, and the outline weight pinned to the ACTOR outline so a
# tree reads as tappable next to a dino. NOT quiet()-ed downstream — props sit at
# actor energy on purpose. That energy gap IS the tap language.
PROP_STYLE = (
    "Chunky toddler-friendly cartoon style scenery prop for a preschool game. THICK "
    "BOLD BLACK OUTLINES all the way around the shape and around every interior shape, "
    "the same heavy black outline weight as a cartoon character sprite. Bright "
    "saturated colors, soft simple cel shading, rounded friendly chunky shapes, flat "
    "2D game sprite look. It is an inanimate scenery object, a plain thing: absolutely "
    "NO face, NO eyes, NO mouth, no nose, no smile, no character features, not a "
    "creature. No lava, no fire, no smoke, no ash, no cracks, no danger. Absolutely no "
    "text, no letters, no numbers, no words, no logos, no watermark. ONE single object "
    "alone in the middle of the frame, seen slightly from above in a gentle "
    "three-quarter view, standing upright. There is NOTHING under it: no ground, no "
    "grass, no soil patch, no base plate, no platform, no tile, no diamond, no circle "
    "under the object. The entire background must be a single solid flat pure magenta "
    "color #FF00FF (RGB 255,0,255) with nothing else on it, no gradient, no vignette. "
    "The subject casts NO shadow at all: no drop shadow, no ground shadow, no contact "
    "shadow - the area directly under and around the subject is pure flat magenta. "
)

# Decor that belongs to the TERRAIN language (bridge, fence, garden bed): objects, but
# NOT interaction targets, so they must NOT wear the tap outline. Same no-outline law
# as the ground, on a keyable magenta field.
DECOR_STYLE = (
    "Soft quiet cartoon scenery for a preschool game, meant to sit BEHIND bold "
    "black-outlined characters as quiet furniture. Gentle slightly muted colors, LOW "
    "contrast, soft flat painterly shapes with absolutely NO outlines: no dark lines, "
    "no black edges, no line art, no ink. Shapes end in soft colour-against-colour "
    "edges. It is an inanimate object: no creatures, no animals, no insects, no faces, "
    "no eyes, no mouths, no characters. "
    # THE SKETCH SCAR (measured, c7m first bake): "soft painterly + no outlines" alone
    # made the model deliver a loose WATERCOLOUR SKETCH — scratchy dark-brown strokes
    # standing in for the wood grain. Those strokes are dark lines, i.e. line art, i.e.
    # rule 1 broken by the back door, and the scratchy texture also fights the flat cel
    # look every other asset in the set has. "No outlines" must therefore be paired with
    # a positive instruction for FLAT SOLID FILLS, or the model reads it as "sketch".
    "Every shape is filled with FLAT SOLID COLOR and ends in a clean smooth crisp edge. "
    "It is clean vector-like cel art, NOT a sketch: no scratchy strokes, no visible "
    "brush strokes, no pencil or charcoal texture, no hatching, no scribble, no rough "
    "grainy shading, no watercolor washes, no paper texture. Wood grain (if any) is one "
    "or two soft SOLID lighter shapes, never thin dark scratchy lines. "
    "Absolutely no text, no letters, no numbers, no "
    "words, no logos, no watermark. The entire background must be a single solid flat "
    "pure magenta color #FF00FF (RGB 255,0,255) with nothing else on it, no gradient, "
    "no vignette. The subject casts NO shadow at all: no drop shadow, no ground "
    "shadow, no contact shadow - the area directly under and around the subject is "
    "pure flat magenta with nothing on it. "
)


# --- Specs --------------------------------------------------------------------------
# kind:
#   plate  full-bleed square swatch -> quiet() -> iso-sliced into cell variants
#   decals 2x2 magenta sheet -> quadrant slice -> chroma key -> quiet()
#   prop   single outlined subject -> chroma key -> fit into a fixed canvas (NO quiet)
#   decor  single un-outlined subject -> chroma key -> quiet() -> fit
#   sheet2 1x2 magenta sheet of two un-outlined subjects (the fence pair)

SPECS = {
    # ---------------------------------------------------------------- ground plates
    "env_grass_plate": dict(
        kind="plate", group="ground", cells=4,
        # Rule 6: pull the mean onto the shipped flat grass (96,190,84). Not all the
        # way — a full mean-match drags the whole swatch to the flat tile's chroma and
        # undoes rule 2. 0.55 keeps the tuned actor/UI contrast without re-saturating.
        quiet_kw=dict(sat=0.80, con=0.85, lift=4.0, tint=(96, 190, 84), tint_amt=0.55),
        prompt=(
            f"Generate an image. {TERRAIN_STYLE}{EVEN_SPREAD}"
            "The texture: a lush calm prehistoric meadow of soft spring-green grass, "
            "seen from straight above. The ground is gently mottled from only two or "
            "three CLOSE shades of leaf green in big soft irregular blotches, each "
            "blotch roughly a quarter to a third of the frame wide, melting into each "
            "other with soft edges. Scattered THINLY and EVENLY across the whole "
            "square, centre included: a few small soft clusters of light yellow-green "
            "fern fronds lying flat on the grass, a few tiny pale-green clover tufts, "
            "and a few slightly darker soft moss blotches. Most of the swatch is plain "
            "calm grass — the accents are small, few, and far apart."),
    ),
    "env_path_plate": dict(
        kind="plate", group="ground", cells=4,
        # The raw gen leans salmon (concept-pass finding); pull hard onto the shipped
        # path tan, slightly lifted so the re-dress stays airy.
        quiet_kw=dict(sat=0.72, con=0.85, lift=4.0, tint=(206, 176, 128), tint_amt=0.90),
        prompt=(
            f"Generate an image. {TERRAIN_STYLE}{EVEN_SPREAD}"
            "The texture: a warm packed-earth dirt path surface in soft sandy tans, "
            "seen from straight above. The ground is gently mottled from only two or "
            "three CLOSE shades of warm tan and light earthy brown in big soft "
            "irregular blotches that melt into each other. Scattered THINLY and EVENLY "
            "across the whole square, centre included: a few faint slightly-paler cream "
            "mineral veins meandering gently like thin soft ribbons, and a few small "
            "smooth rounded pebbles in a slightly warmer soft amber tone. Most of the "
            "swatch is plain calm packed earth — the veins and pebbles are faint, few "
            "and far apart."),
    ),
    "env_water_plate": dict(
        kind="plate", group="ground", cells=4,
        # Water alone keeps extra chroma: it is a hard walkability boundary and must
        # read as WATER at toddler glance-speed (pitch rule 6, water exception).
        quiet_kw=dict(sat=0.95, con=0.95, lift=0.0, tint=(80, 150, 230), tint_amt=0.25),
        prompt=(
            f"Generate an image. {TERRAIN_STYLE}{EVEN_SPREAD}"
            "The texture: calm shallow fresh water in soft friendly blues, seen from "
            "straight above. The water is gently mottled from only two or three CLOSE "
            "shades of soft blue, with a few wide soft lighter ripple bands curving "
            "gently across the whole frame. Floating on it, spread EVENLY and far "
            "apart across the whole square including the centre: four or five round "
            "soft-green lily pads, one of them carrying a single tiny pale-pink "
            "blossom. Most of the swatch is plain calm water — the lily pads are small "
            "and the ripples are faint and wide."),
    ),
    "env_stone_plate": dict(
        kind="plate", group="decor", cells=4,
        quiet_kw=dict(sat=0.78, con=0.82, lift=6.0, tint=(168, 166, 158), tint_amt=0.55),
        prompt=(
            f"Generate an image. {TERRAIN_STYLE}{EVEN_SPREAD}"
            "The texture: an old walkway of big flat weathered stone slabs, seen from "
            "straight above, as if looking down at a garden path deck. Five or six "
            "large rounded soft grey stone slabs fill the frame, packed close with soft "
            "pale sandy grit in the narrow gaps between them, and soft muted green moss "
            "creeping in a few of those gaps and along a few slab edges. The slabs are "
            "in two or three CLOSE shades of warm soft grey. No dark grout lines, no "
            "black edges — the gaps are pale, not dark."),
    ),
    "env_bed_plate": dict(
        kind="plate", group="ground", cells=2,
        quiet_kw=dict(sat=0.78, con=0.82, lift=5.0, tint=(150, 120, 92), tint_amt=0.60),
        prompt=(
            f"Generate an image. {TERRAIN_STYLE}{EVEN_SPREAD}"
            "The texture: the raked soil of a tended garden bed, seen from straight "
            "above. Soft crumbly cocoa-brown earth in two or three CLOSE shades, in "
            "wide soft rows that melt into each other. Scattered THINLY and EVENLY "
            "across the whole square including the centre: a few tiny soft green "
            "seedling sprouts with two little round leaves each, and a few small soft "
            "pale straw wisps. Most of the swatch is plain calm soil."),
    ),
    # ---------------------------------------------------------------------- decals
    "env_decals_a": dict(
        kind="decals", group="decal",
        names=["decal_fern", "decal_moss", "decal_footprints", "decal_stones"],
        prompt=(
            f"Generate an image. {DECAL_STYLE}"
            "A 2x2 grid of four separate small ground decals, evenly spaced and the "
            "same size, one per cell, each floating alone on the flat magenta with a "
            "wide magenta gap between them. Top-left: ONE small cluster of soft light "
            "yellow-green fern fronds fanning out from a centre, lying flat on the "
            "ground. Top-right: ONE soft irregular moss patch in two close soft greens "
            "with three tiny round pale-green clover dots on it. Bottom-left: ONE short "
            "walking trail of exactly three small round soft warm-tan footprint marks "
            "in a gentle zigzag line — they are simple flat painted oval marks on "
            "nothing, there is NO creature, NO foot and NO animal anywhere in the "
            "picture. Bottom-right: ONE small cluster of exactly three smooth rounded "
            "stones in warm soft amber-orange nestled together — plain stones, no "
            "cracks, no lava, no fire, no smoke, no sparkle, no glow."),
    ),
    "env_decals_b": dict(
        kind="decals", group="decal",
        names=["decal_lily_blossom", "decal_lily", "decal_clover", "decal_pebbles"],
        prompt=(
            f"Generate an image. {DECAL_STYLE}"
            "A 2x2 grid of four separate small ground decals, evenly spaced and the "
            "same size, one per cell, each floating alone on the flat magenta with a "
            "wide magenta gap between them. Top-left: ONE round soft-green water lily "
            "pad seen from above, with a small notch cut in one side, carrying a single "
            "small pale-pink water lily blossom. Top-right: ONE round soft-green water "
            "lily pad seen from above with a small notch cut in one side, plain, no "
            "flower. Bottom-left: ONE small soft tuft of three or four pale-green "
            "clover leaves and two slender grass blades, lying flat. Bottom-right: ONE "
            "loose scatter of five or six small smooth rounded pale sandy pebbles in "
            "close soft tan tones, lying flat and spread apart."),
    ),
    # ----------------------------------------------------------------- outlined props
    "env_tree_cycad": dict(
        kind="prop", group="prop", out="tree_cycad", canvas=(PROP_PX, PROP_PX),
        prompt=(
            f"Generate an image. {PROP_STYLE}"
            "The object: a chunky cute cartoon CYCAD tree — a short fat stubby brown "
            "trunk like a stumpy pineapple, topped by a crown of about seven broad "
            "arching feathery fern fronds in two close fresh greens that spray outward "
            "and droop at the tips like a fountain. Squat and wide and friendly, wider "
            "than it is tall. Thick black outline around the trunk and around every "
            "frond."),
    ),
    "env_tree_gingko": dict(
        kind="prop", group="prop", out="tree_gingko", canvas=(PROP_PX, PROP_PX),
        prompt=(
            f"Generate an image. {PROP_STYLE}"
            "The object: a chunky cute cartoon GINGKO tree — a short sturdy brown trunk "
            "with two stubby branches, carrying one big soft rounded cloud-shaped "
            "canopy made of overlapping fan-shaped leaves in warm yellow-green and "
            "fresh green. The canopy is one fat friendly blob shape, not a bushy mess. "
            "Thick black outline around the trunk and around the whole canopy."),
    ),
    "env_tree_conifer": dict(
        kind="prop", group="prop", out="tree_conifer", canvas=(PROP_PX, PROP_PX),
        prompt=(
            f"Generate an image. {PROP_STYLE}"
            "The object: a chunky cute cartoon ancient CONIFER tree — a short thick "
            "reddish-brown trunk carrying three stacked soft rounded tiers of "
            "needle foliage in two close deep blue-greens, the bottom tier widest and "
            "the top tier a soft round dome, like a fat friendly stack of pillows. Not "
            "a sharp spiky triangle. Thick black outline around the trunk and around "
            "every tier."),
    ),
    "env_tree_gingko_shake": dict(
        kind="prop", group="prop", out="tree_gingko_shake", canvas=(PROP_PX, PROP_PX),
        ref="env_tree_gingko",
        prompt=(
            f"Generate an image. Here is a reference picture of a cartoon tree. Redraw "
            f"the EXACT SAME tree in the EXACT SAME art style, colours, outline weight "
            f"and size, but SHAKEN: the trunk is bent a little to the left, the whole "
            f"canopy is squashed slightly and leaning to the left as if it just got a "
            f"push, and exactly three loose fan-shaped leaves are tumbling free in the "
            f"air to the upper right of the canopy, well clear of it. Keep it the same "
            f"tree — same trunk colour, same canopy colour, same thick black outlines. "
            f"{PROP_STYLE}"),
    ),
    "env_rock_boulder": dict(
        kind="prop", group="prop", out="rock_boulder", canvas=(PROP_PX, PROP_PX),
        prompt=(
            f"Generate an image. {PROP_STYLE}"
            "The object: a chunky cute cartoon BOULDER — one fat rounded grey rock with "
            "a few big flat facets and two or three soft chunky chips already knocked "
            "off its top edge, so it looks like it wants to be broken apart. Two close "
            "shades of warm grey with a soft pale highlight on the upper left. It sits "
            "with its widest part at the bottom. Thick black outline around the rock "
            "and around each facet line. No cracks that look like a face, no eyes, no "
            "mouth."),
    ),
    "env_rock_mossy": dict(
        kind="prop", group="prop", out="rock_mossy", canvas=(PROP_PX, PROP_PX),
        prompt=(
            f"Generate an image. {PROP_STYLE}"
            "The object: a chunky cute cartoon MOSSY ROCK — one fat rounded grey-blue "
            "stone, smooth and worn, with a soft blanket of muted green moss draped "
            "over its top and spilling a little down one side, plus one tiny fern frond "
            "sprouting from the moss. Widest at the bottom. Thick black outline around "
            "the stone and around the moss blanket. No eyes, no mouth, no face."),
    ),
    "env_mound": dict(
        kind="prop", group="prop", out="mound", canvas=(TILE_W, TILE_H),
        prompt=(
            f"Generate an image. {PROP_STYLE}"
            "The object: a chunky cute cartoon DIG MOUND — a low wide heap of loose "
            "warm brown soil, shaped like a soft rounded hill that is TWICE AS WIDE AS "
            "IT IS TALL, very flat and squat, with a few chunky soil clods on top in a "
            "slightly darker brown. Around the bottom rim of the heap, half buried in "
            "the loose soil, the pale cream tips of two or three small fossil bones and "
            "one little curved shell peek out — just the tips, mostly buried. Thick "
            "black outline around the heap, around the clods and around each bone tip. "
            "Wide and low and squat, like a flattened pile, not a tall cone."),
    ),
    # --------------------------------------------------------------------- decor
    "env_fence": dict(
        kind="sheet2", group="decor", names=["fence_x", "fence_y"],
        prompt=(
            f"Generate an image. {DECOR_STYLE}"
            "TWO separate short weathered wooden fence segments, side by side with a "
            "wide magenta gap between them, both the same size and the same style. Each "
            "segment is two stubby round-topped wooden posts joined by two horizontal "
            "rails, made of soft weathered silvery-brown driftwood in two close muted "
            "tones, low and friendly and chunky. The LEFT segment runs away from the "
            "viewer toward the UPPER RIGHT of the frame, at a shallow angle, seen "
            "slightly from above. The RIGHT segment is its mirror image and runs away "
            "toward the UPPER LEFT of the frame at the same shallow angle. On the left "
            "segment only, one small soft muted-green moss tuft sits at the foot of a "
            "post. Remember: absolutely NO dark outlines on either segment."),
    ),
    "env_nest": dict(
        kind="prop", group="decor", out="nest", canvas=(PROP_PX, PROP_PX),
        prompt=(
            f"Generate an image. {PROP_STYLE}"
            "The object: a chunky cute cartoon NEST — a wide shallow bowl woven from "
            "warm tan twigs and soft dry straw, seen from slightly above so the round "
            "hollow inside is clearly visible and EMPTY, with a few soft green fern "
            "fronds tucked into the woven rim. Twice as wide as it is tall. The nest is "
            "EMPTY: no eggs, no eggshell, no creature, no bird, no chick, and the "
            "hollow must NOT look like a face — no eyes, no mouth. Thick black outline "
            "around the bowl and around the rim."),
    ),
}

ORDER = list(SPECS)
GROUPS = ["ground", "decal", "prop", "decor"]


# --- Post helpers -------------------------------------------------------------------

def _fit(img: Image.Image, canvas, margin: int = 4) -> Image.Image:
    """Scale `img` (RGBA, already trimmed) to fit `canvas` preserving aspect, and centre
    it on a transparent canvas. Preserving aspect is the whole point: the canvas is what
    pins the world size (px / PPU), so a prop that comes back a different shape gets
    letterboxed rather than stretched, and its collider stays honest."""
    cw, ch = canvas
    k = min((cw - 2 * margin) / img.width, (ch - 2 * margin) / img.height)
    w, h = max(1, int(round(img.width * k))), max(1, int(round(img.height * k)))
    small = img.resize((w, h), Image.LANCZOS)
    out = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
    out.alpha_composite(small, ((cw - w) // 2, (ch - h) // 2))
    return out


def _key(raw: Image.Image, pad: int = 3) -> Image.Image:
    """Full chroma-key chain: key -> despeckle -> un-magenta the feather -> trim ->
    neutralise transparent RGB (else bilinear filtering bleeds a pink halo)."""
    img = S.despeckle(S.chroma_key(raw))
    img = _unmix(img, raw)
    img = S.trim(img, pad)
    return neutralize(img)


def _quiet_rgba(img: Image.Image, **kw) -> Image.Image:
    """quiet() the colour of an RGBA image, leaving alpha alone."""
    rgb = quiet(img.convert("RGB"), **kw)
    out = rgb.convert("RGBA")
    out.putalpha(img.getchannel("A"))
    return out


# --- The iso diamond ----------------------------------------------------------------
# Map space -> screen space, matching the shipped tilemap (Grid IsometricZAsY,
# cellSize 1 x 0.5). A CELL x CELL map square becomes a TILE_W x TILE_H diamond:
#     X = (u - v)/2 + TILE_W/2      Y = (u + v) * TILE_H / (2*CELL)
# PIL wants the INVERSE (output -> input), which is the affine below.

ISO_PAD = 16        # map-space px of edge-replicated bleed around each sliced square


def _iso_affine(pad: int = ISO_PAD):
    """(a,b,c,d,e,f) mapping output (X,Y) -> input (u,v) for the cell diamond.

    `pad` shifts the sample point into a square that has been padded by edge
    replication. WITHOUT the pad the four diamond corners sample EXACTLY on the source
    square's corners, so PIL's bilinear filter reaches one texel past the edge, gets
    `fillcolor`, and paints a dark hairline all the way round every tile — which
    tessellates into precisely the dark grid the pitch exists to delete. (Measured:
    the first two tessellation tests both showed it; dilating the alpha did NOT fix
    it because the black was in the COLOUR channels, not the alpha.)"""
    # u = (X - TILE_W/2) * (CELL/TILE_W) + Y * (CELL/TILE_H) + pad
    # v = -(X - TILE_W/2) * (CELL/TILE_W) + Y * (CELL/TILE_H) + pad
    ax = CELL / float(TILE_W)
    ay = CELL / float(TILE_H)
    return (ax, ay, -ax * TILE_W / 2.0 + pad,
            -ax, ay, ax * TILE_W / 2.0 + pad)


def _diamond_alpha(feather: float = 1.2, dilate: float = 3.0) -> Image.Image:
    """Antialiased diamond alpha for one ground tile.

    DILATED BY 3 px BEFORE feathering, and that number is load-bearing. Neighbouring
    diamonds tessellate edge-to-edge, so if the feather straddles the true diamond
    boundary both neighbours land at ~50% alpha there and the background paints a
    visible dark lattice over the whole map (measured in the first tessellation test —
    it looked exactly like the grid lines the pitch is trying to delete). Dilating by
    more than the blur radius puts the alpha ramp entirely OUTSIDE the true boundary:
    alpha is 100% at the edge, neighbours overlap by ~3 px, and the overlap is opaque
    so the later tile simply wins. This is the pitch's "2-3 px edge feather"."""
    ss = 4
    w, h = TILE_W * ss, TILE_H * ss
    ys, xs = np.mgrid[0:h, 0:w]
    cx, cy = (w - 1) / 2.0, (h - 1) / 2.0
    hw, hh = w / 2.0 + dilate * ss, h / 2.0 + dilate * ss
    inside = (np.abs(xs - cx) / hw + np.abs(ys - cy) / hh) <= 1.0
    m = Image.fromarray((inside * 255).astype(np.uint8), "L")
    m = m.resize((TILE_W, TILE_H), Image.BOX)
    return m.filter(ImageFilter.GaussianBlur(feather))


_DIAMOND = None


def diamond_alpha() -> Image.Image:
    global _DIAMOND
    if _DIAMOND is None:
        _DIAMOND = _diamond_alpha()
    return _DIAMOND


def mean_match(square: Image.Image, target: np.ndarray,
               amt: float = 0.85) -> Image.Image:
    """Pull one sliced square's MEAN colour toward the plate mean.

    Without this the 16 variants are a checkerboard: each cell inherits whichever big
    blotch it happened to land on, so neighbouring tiles jump a whole tone and the map
    reads as a patchwork quilt (measured in the first tessellation test). Matching the
    means kills the inter-tile jump while leaving every tile's INTERNAL mottle intact —
    which is the only thing rule 3 actually wants. 15% of the drift is left in so the
    variants are not clones."""
    a = np.asarray(square.convert("RGB"), np.float32)
    cur = a.mean(axis=(0, 1), keepdims=True)
    a = a + (target.reshape(1, 1, 3) - cur) * amt
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGB")


def to_tile(square: Image.Image) -> Image.Image:
    """One map-space square -> one finished RGBA ground tile."""
    sq = square.convert("RGB")
    if sq.size != (CELL, CELL):
        sq = sq.resize((CELL, CELL), Image.LANCZOS)
    a = np.pad(np.asarray(sq), ((ISO_PAD, ISO_PAD), (ISO_PAD, ISO_PAD), (0, 0)),
               mode="edge")
    sq = Image.fromarray(a, "RGB")
    iso = sq.transform((TILE_W, TILE_H), Image.AFFINE, _iso_affine(),
                       resample=Image.BILINEAR)
    out = iso.convert("RGBA")
    out.putalpha(diamond_alpha())
    return out


# --- Transition masks (procedural — NOT AI strips) -----------------------------------
# The pitch is explicit: AI-generated transition strips cannot guarantee edge alignment.
# Blurred procedural masks over the SAME continuous plates get alignment for free, and
# the melt reads irregular because the mask is warped by a low-frequency noise field.

SIDES = {0: "-Y", 1: "+X", 2: "+Y", 3: "-X"}


def _side_ramp(bit: int, reach: float) -> np.ndarray:
    """1 at the named map-space edge, falling to 0 `reach` of the way across the cell."""
    t = np.linspace(0.0, 1.0, CELL, dtype=np.float32)
    if bit == 0:                        # -Y neighbour: near v == 0
        f = np.clip(1.0 - (t / reach), 0, 1)[:, None] * np.ones((1, CELL), np.float32)
    elif bit == 2:                      # +Y neighbour: near v == CELL
        f = np.clip(1.0 - ((1.0 - t) / reach), 0, 1)[:, None] * np.ones((1, CELL), np.float32)
    elif bit == 3:                      # -X neighbour: near u == 0
        f = np.ones((CELL, 1), np.float32) * np.clip(1.0 - (t / reach), 0, 1)[None, :]
    else:                               # +X neighbour: near u == CELL
        f = np.ones((CELL, 1), np.float32) * np.clip(1.0 - ((1.0 - t) / reach), 0, 1)[None, :]
    return f


def _noise(seed: int, sigma: float = 22.0) -> np.ndarray:
    rng = np.random.default_rng(seed)
    n = rng.random((CELL, CELL)).astype(np.float32)
    n = np.asarray(Image.fromarray((n * 255).astype(np.uint8), "L")
                   .filter(ImageFilter.GaussianBlur(sigma)), dtype=np.float32) / 255.0
    lo, hi = n.min(), n.max()
    return (n - lo) / max(1e-5, hi - lo)


def edge_mask(mask: int, seed: int, reach: float = 0.46,
              hardness: float = 2.1) -> np.ndarray:
    """Soft irregular coverage (0..1) of the OTHER biome inside this cell.

    Tuning history (both ends were wrong first):
      hardness 6.0 -> the melt came back a crisp cut-out. A hard colour boundary IS an
        outline as far as a 2-year-old is concerned, so it broke rule 1 on the terrain
        layer. Down to ~2.1: a real melt, several px wide after projection.
      the opposite failure is the concept pass's "muddy rust halo" — a WIDE half-blend
        of tan sitting on grass. That is why hardness is not simply 1.0, and why
        `reach` only lets the other biome creep <half a cell in."""
    f = np.zeros((CELL, CELL), np.float32)
    for bit in range(4):
        if mask & (1 << bit):
            f = np.maximum(f, _side_ramp(bit, reach))
    if not f.any():
        return f
    # Warp the ramp with a low-frequency field so the melt wanders, never a straight
    # ruled line (a clean straight melt reads as a seam, i.e. as an outline again).
    f = np.clip(f + (_noise(seed, sigma=34.0) - 0.5) * 0.55, 0, 1)
    f = np.asarray(Image.fromarray((f * 255).astype(np.uint8), "L")
                   .filter(ImageFilter.GaussianBlur(9)), np.float32) / 255.0
    return np.clip((f - 0.5) * hardness + 0.5, 0, 1)


def bake_edges(base_sq: Image.Image, over_sq: Image.Image, prefix: str,
               shore: tuple | None = None, seed0: int = 1000) -> int:
    """15 transition tiles (mask 1..15) melting `over` into `base`."""
    a = np.asarray(base_sq.convert("RGB").resize((CELL, CELL), Image.LANCZOS), np.float32)
    b = np.asarray(over_sq.convert("RGB").resize((CELL, CELL), Image.LANCZOS), np.float32)
    n = 0
    for mask in range(1, 16):
        f = edge_mask(mask, seed0 + mask)[..., None]
        comp = a * (1 - f) + b * f
        if shore is not None:
            # A pale sand beach on the LAND side of the waterline: the pitch's
            # mitigation for "water misread as walkable" once the hard blue diamond
            # edge is gone. Centred below 0.5 on purpose so the sand sits on grass, not
            # in the water, and wide enough to read at toddler glance-speed.
            # NOT a fixed-width ribbon following the contour — that is a LINE, and a
            # line on terrain is an outline (first water take did exactly this: a thin
            # bright rim traced the grass and read as a drawn edge). Instead the sand
            # is a broad two-sided ramp: it fades UP out of the grass and DOWN into the
            # water, so it is a beach, not a stroke.
            g = f[..., 0]
            band = np.clip((g - 0.02) / 0.30, 0, 1) * np.clip((0.66 - g) / 0.34, 0, 1)
            band = np.asarray(Image.fromarray((band * 255).astype(np.uint8), "L")
                              .filter(ImageFilter.GaussianBlur(6)), np.float32) / 255.0
            sand = np.asarray(shore, np.float32).reshape(1, 1, 3)
            comp = comp * (1 - band[..., None] * 0.72) + sand * (band[..., None] * 0.72)
        tile = to_tile(Image.fromarray(np.clip(comp, 0, 255).astype(np.uint8), "RGB"))
        tile.save(os.path.join(GROUND, f"{prefix}_{mask}.png"))
        n += 1
    return n


# --- gen ----------------------------------------------------------------------------

def gen(name: str, force: bool = False) -> bool:
    spec = SPECS[name]
    path = os.path.join(RAW, f"{name}.png")
    if os.path.exists(path) and not force:
        print(f"[skip] {name} (raw exists)")
        return True
    ref_b64 = None
    if spec.get("ref"):
        rp = os.path.join(RAW, f"{spec['ref']}.png")
        if not os.path.exists(rp):
            print(f"[fail] {name}: reference {spec['ref']} not generated yet")
            return False
        import base64
        with open(rp, "rb") as f:
            ref_b64 = base64.b64encode(f.read()).decode()
    b64 = G._attempt(spec["prompt"], ref_b64, name, 2)
    if not b64:
        print(f"FAILED {name}")
        return False
    G._save_raw(b64, path)
    return True


# --- bake ---------------------------------------------------------------------------

def _dirs():
    for d in (GROUND, DECAL, PROP, DECOR):
        os.makedirs(d, exist_ok=True)


def _plate(name: str) -> Image.Image:
    """Quiet-passed plate at PLATE x PLATE (rule 2 applied before ANY slicing, so every
    derived tile inherits the cap — it can never be forgotten per-tile)."""
    spec = SPECS[name]
    raw = Image.open(os.path.join(RAW, f"{name}.png"))
    img = quiet(raw, **spec.get("quiet_kw", {}))
    return img.resize((PLATE, PLATE), Image.LANCZOS)


# Which (i, j) block of each plate feeds the TRANSITION tiles.
#
# THE ACCENT-REPEAT SCAR (measured on the first full bake). All 45 edge tiles are built
# from ONE base square and ONE over-square, so any accent inside those two squares is
# reproduced pixel-identically in every single transition tile on the map. The first
# bake used block (1,1) for everything and it put a brown pebble at the bottom tip of
# all 15 grass->path tiles and a whole green LILY PAD at the bottom tip of all 15
# grass->water tiles — the exact "obvious repeat" pitch rule 3 bans, and the lily also
# broke rule 4 (lilies belong on water, not straddling a shoreline).
#
# Picked by scoring every block's high-frequency energy — percentile-99.5 of
# |luma - GaussianBlur(luma, 9)|, which finds small sharp accents that plain variance
# misses under the big soft tonal gradients — then eyeballing the top candidates as
# projected diamonds. Scores at the time of picking (p99.5, lower == calmer):
#   grass (1,1)  7.0  <- already the calmest of 16
#   path  (0,2)  7.0  vs the old (1,1) 8.0, which held the pebble
#   water (3,1) 12.0  vs the old (1,1) 27.0, which held the lily pad
# Transition squares want the CALMEST block available: accents belong on the plain
# variant tiles (which are all different, so they never repeat), never on the edges.
EDGE_BLOCK = {"grass": (1, 1), "path": (0, 2), "water": (3, 1), "bed": (0, 0)}

# The bridge wants the OPPOSITE: the two BUSIEST blocks of the stone plate, so the deck
# actually reads as slabs. The stone plate's slabs are ~350 px across on a 1024 plate,
# so a 256 block can land entirely INSIDE one slab and bake out as a blank grey diamond
# — which is what the old (2,2) pick did (p99.5 32 but hf-mean only 2.28: one slab and a
# sliver of grout). (0,2) gives two parallel grout runs with moss, (2,3) a Y-junction;
# they are far apart on the plate so the two variants stay visibly different.
BRIDGE_BLOCKS = (("bridge_a", (0, 2)), ("bridge_b", (2, 3)))


def bake_ground() -> int:
    _dirs()
    n = 0
    plates = {}
    for key, biome, cells in (("env_grass_plate", "grass", 4),
                              ("env_path_plate", "path", 4),
                              ("env_water_plate", "water", 4),
                              ("env_bed_plate", "bed", 2)):
        if not os.path.exists(os.path.join(RAW, f"{key}.png")):
            print(f"[skip] {biome}: no raw")
            continue
        p = _plate(key)
        plates[biome] = p
        p.save(os.path.join(GROUND, f"plate_{biome}.png"))
        n += 1
        pmean = np.asarray(p.convert("RGB"), np.float32).mean(axis=(0, 1))
        step = PLATE // cells
        for j in range(cells):
            for i in range(cells):
                sq = p.crop((i * step, j * step, (i + 1) * step, (j + 1) * step))
                idx = j * cells + i
                to_tile(mean_match(sq, pmean)).save(
                    os.path.join(GROUND, f"tile_{biome}_{idx:02d}.png"))
                n += 1
        print(f"       tile_{biome}_00..{cells*cells-1:02d}  ({TILE_W}x{TILE_H})")

    # Transitions. Base square = a mid-plate grass block so the melt starts from real
    # grass material; the over-square is the matching block of the other biome.
    if "grass" in plates:
        def _blk(img, cells, i, j):
            """Mean-matched block (i,j) of a plate — same normalisation the variant
            tiles get, so an edge tile abuts its plain neighbours without a step."""
            step = PLATE // cells
            m = np.asarray(img.convert("RGB"), np.float32).mean(axis=(0, 1))
            return mean_match(img.crop((i * step, j * step,
                                        (i + 1) * step, (j + 1) * step)), m)

        gsq = _blk(plates["grass"], 4, *EDGE_BLOCK["grass"])
        # Fixed seeds (never hash()) so a re-bake reproduces byte-identical edge tiles.
        for biome, shore, seed0 in (("path", None, 1100),
                                    ("water", (218, 206, 176), 1200),
                                    ("bed", None, 1300)):
            if biome not in plates:
                continue
            cells = 2 if biome == "bed" else 4
            osq = _blk(plates[biome], cells, *EDGE_BLOCK[biome])
            n += bake_edges(gsq, osq, f"edge_grass_{biome}", shore=shore, seed0=seed0)
            print(f"       edge_grass_{biome}_1..15  ({TILE_W}x{TILE_H})")
    return n


def bake_decals() -> int:
    _dirs()
    n = 0
    for key in ("env_decals_a", "env_decals_b"):
        raw_path = os.path.join(RAW, f"{key}.png")
        if not os.path.exists(raw_path):
            print(f"[skip] {key}: no raw")
            continue
        raw = Image.open(raw_path).convert("RGB")
        w, h = raw.size
        cells = [raw.crop((0, 0, w // 2, h // 2)), raw.crop((w // 2, 0, w, h // 2)),
                 raw.crop((0, h // 2, w // 2, h)), raw.crop((w // 2, h // 2, w, h))]
        for cell, out_name in zip(cells, SPECS[key]["names"]):
            img = _quiet_rgba(_key(cell, pad=4), sat=0.82, con=0.88, lift=3.0)
            if max(img.size) > 256:
                k = 256 / max(img.size)
                img = img.resize((max(1, int(img.width * k)),
                                  max(1, int(img.height * k))), Image.LANCZOS)
            img.save(os.path.join(DECAL, f"{out_name}.png"))
            print(f"       {out_name}.png  ({img.width}x{img.height})")
            n += 1
    return n


def bake_props() -> int:
    _dirs()
    n = 0
    for key, spec in SPECS.items():
        if spec["kind"] != "prop":
            continue
        raw_path = os.path.join(RAW, f"{key}.png")
        if not os.path.exists(raw_path):
            print(f"[skip] {key}: no raw")
            continue
        img = _key(Image.open(raw_path).convert("RGB"), pad=2)
        tight = img.size
        img = _fit(img, spec["canvas"], margin=3)
        # Props keep FULL actor energy — no quiet() here. That gap is the tap language.
        d = PROP if spec["group"] == "prop" else DECOR
        if spec["group"] == "decor":
            img = _quiet_rgba(img, sat=0.86, con=0.90, lift=2.0)
        img.save(os.path.join(d, f"{spec['out']}.png"))
        print(f"       {spec['out']}.png  ({img.width}x{img.height})  "
              f"art {tight[0]}x{tight[1]} -> aspect {tight[0]/max(1,tight[1]):.2f}")
        n += 1
    return n


# Kenney fenceLow canvas + alpha bbox, MEASURED — reproduce exactly so the new pieces
# are drop-in replacements and SceneBuilder's "scale to one cell wide" maths is untouched.
# Re-measured from the shipped Kenney PNGs' alpha bbox:
#   fenceLow_E  (0, 315, 134, 451) -> x 0, y 315, w 134, h 136
#   fenceLow_N  (0, 377, 134, 512) -> x 0, y 377, w 134, h 135
FENCE_CANVAS = (256, 512)
FENCE_BOX = {"fence_x": (0, 315, 134, 136), "fence_y": (0, 377, 134, 135)}


def bake_decor() -> int:
    _dirs()
    n = 0
    # --- bridge: the stone plate, iso-projected like ground (guarantees a clean
    # 256x128 diamond, which a free-drawn "isometric bridge" prompt never gives).
    if os.path.exists(os.path.join(RAW, "env_stone_plate.png")):
        p = _plate("env_stone_plate")
        p.save(os.path.join(DECOR, "plate_stone.png"))
        n += 1
        for out_name, (i, j) in BRIDGE_BLOCKS:
            step = PLATE // 4
            sq = p.crop((i * step, j * step, (i + 1) * step, (j + 1) * step))
            to_tile(sq).save(os.path.join(DECOR, f"{out_name}.png"))
            print(f"       {out_name}.png  ({TILE_W}x{TILE_H})")
            n += 1
    # --- fence pair
    raw_path = os.path.join(RAW, "env_fence.png")
    if os.path.exists(raw_path):
        raw = Image.open(raw_path).convert("RGB")
        w, h = raw.size
        halves = [raw.crop((0, 0, w // 2, h)), raw.crop((w // 2, 0, w, h))]
        for half, out_name in zip(halves, SPECS["env_fence"]["names"]):
            art = _quiet_rgba(_key(half, pad=2), sat=0.84, con=0.88, lift=3.0)
            bx, by, bw, bh = FENCE_BOX[out_name]
            k = min(bw / art.width, bh / art.height)
            art = art.resize((max(1, int(art.width * k)), max(1, int(art.height * k))),
                             Image.LANCZOS)
            canvas = Image.new("RGBA", FENCE_CANVAS, (0, 0, 0, 0))
            canvas.alpha_composite(art, (bx + (bw - art.width) // 2,
                                         by + (bh - art.height)))
            canvas.save(os.path.join(DECOR, f"{out_name}.png"))
            print(f"       {out_name}.png  ({FENCE_CANVAS[0]}x{FENCE_CANVAS[1]}, "
                  f"art {art.width}x{art.height} @ Kenney bbox)")
            n += 1
    n += 0
    return n


def bake(groups) -> None:
    total = 0
    for g in groups:
        print(f"[bake] {g}")
        total += {"ground": bake_ground, "decal": bake_decals,
                  "prop": bake_props, "decor": bake_decor}[g]()
    print(f"[bake] {total} files")


# --- Review sheets ------------------------------------------------------------------

CHECKER = (236, 232, 224), (250, 248, 244)


def _bg(size, cell=16):
    w, h = size
    im = Image.new("RGB", size, CHECKER[0])
    px = im.load()
    for y in range(h):
        for x in range(w):
            if ((x // cell) + (y // cell)) % 2:
                px[x, y] = CHECKER[1]
    return im


def _grid(paths, cols, thumb, title_pad=0, scale=1):
    tw, th = thumb
    rows = (len(paths) + cols - 1) // cols
    pad = 8
    sheet = _bg((cols * (tw + pad) + pad, rows * (th + pad) + pad + title_pad))
    for i, p in enumerate(paths):
        im = Image.open(p).convert("RGBA")
        k = min(tw / im.width, th / im.height)
        im = im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))),
                       Image.NEAREST if scale > 1 else Image.LANCZOS)
        x = pad + (i % cols) * (tw + pad) + (tw - im.width) // 2
        y = pad + title_pad + (i // cols) * (th + pad) + (th - im.height) // 2
        sheet.paste(im, (x, y), im)
    return sheet


def _ls(d, pat):
    import glob
    return sorted(glob.glob(os.path.join(d, pat)))


def verify() -> None:
    """Per-group verification sheets — every sliced asset appears at review size so the
    style rules (no outlines on ground/decor, outlines on props, no faces) can be
    checked on EVERY output, not a sample."""
    jobs = [
        ("verify_tiles_grass", _ls(GROUND, "tile_grass_*.png"), 4, (256, 128)),
        ("verify_tiles_path", _ls(GROUND, "tile_path_*.png"), 4, (256, 128)),
        ("verify_tiles_water", _ls(GROUND, "tile_water_*.png"), 4, (256, 128)),
        ("verify_tiles_bed", _ls(GROUND, "tile_bed_*.png"), 4, (256, 128)),
        ("verify_edges_path", _ls(GROUND, "edge_grass_path_*.png"), 5, (256, 128)),
        ("verify_edges_water", _ls(GROUND, "edge_grass_water_*.png"), 5, (256, 128)),
        ("verify_edges_bed", _ls(GROUND, "edge_grass_bed_*.png"), 5, (256, 128)),
        ("verify_decals", _ls(DECAL, "decal_*.png"), 4, (256, 256)),
        ("verify_props", _ls(PROP, "*.png"), 4, (256, 256)),
        ("verify_decor", _ls(DECOR, "*.png"), 3, (300, 300)),
    ]
    for name, paths, cols, thumb in jobs:
        if not paths:
            continue
        sheet = _grid(paths, cols, thumb)
        out = os.path.join(OUT, f"{name}.png")
        sheet.save(out)
        print(f"       {out}  ({sheet.width}x{sheet.height}, {len(paths)} assets)")


# --- Contact sheet ------------------------------------------------------------------
# One image showing the WHOLE set together: a dressed scene built from the real baked
# tiles (so the tap-language question the pitch opens with can be answered at a glance)
# over a catalogue of every asset group.

BACKHOE = os.path.join(REPO, "Assets", "Art", "Generated", "backhoe", "backhoe_SE.png")
DINO = os.path.join(REPO, "Assets", "Art", "Generated", "parasaurolophus", "kid_SE.png")

#  G grass  P path  W water  B bridge  D garden bed
#  props (all stand on grass):  1/2/3 trees  4/5 rocks  M mound  N nest  f fence
SCENE = [
    "GGGGGGGWWGGGGG",
    "GG1GGGGWWGG4GG",
    "GGGGGGGWWGGGGG",
    "GGPPPPPBBPPPGG",
    "GGPGGGGWWGGPGG",
    "G2PGGMGWWGG5PG",
    "GGPGfffffGGPGG",
    "GGPGfDDDfGG3GG",
    "GGGGfDNDfGGGGG",
    "GGGGfffffGGGGG",
    "GGGGGGGGGGGGGG",
]
BIOME_OF = {"G": "grass", "P": "path", "W": "water", "D": "bed"}
PROP_CHARS = {"1": "tree_cycad", "2": "tree_gingko", "3": "tree_conifer",
              "4": "rock_boulder", "5": "rock_mossy", "M": "mound", "N": "nest"}


def _scene_biome(ch: str) -> str:
    if ch in BIOME_OF:
        return BIOME_OF[ch]
    if ch == "B":
        return "bridge"
    return "grass"                      # props and fences stand on grass


def _center(x, y, rows):
    return (x - y) * (TILE_W // 2) + rows * (TILE_W // 2), \
           (x + y) * (TILE_H // 2) + TILE_H // 2


def _load(p):
    return Image.open(p).convert("RGBA") if os.path.exists(p) else None


def scene(rng) -> Image.Image:
    rows, cols = len(SCENE), len(SCENE[0])
    W = (cols + rows) * (TILE_W // 2)
    H = (cols + rows) * (TILE_H // 2) + TILE_H
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    biome = [[_scene_biome(SCENE[y][x]) for x in range(cols)] for y in range(rows)]

    def var(b, i):
        n = 4 if b == "bed" else 16
        p = os.path.join(GROUND, f"tile_{b}_{i % n:02d}.png")
        return _load(p)

    # ---- ground, with procedural transition tiles wherever grass meets something else
    for y in range(rows):
        for x in range(cols):
            b = biome[y][x]
            cx, cy = _center(x, y, rows)
            if b == "bridge":
                t = _load(os.path.join(DECOR,
                                       f"bridge_{'ab'[(x + y) % 2]}.png"))
            elif b == "grass":
                # mask bits: 1 = -Y, 2 = +X, 4 = +Y, 8 = -X  (map-cell space)
                best, mask = None, 0
                for bit, (dx, dy) in enumerate(((0, -1), (1, 0), (0, 1), (-1, 0))):
                    i, j = x + dx, y + dy
                    if 0 <= i < cols and 0 <= j < rows:
                        nb = biome[j][i]
                        nb = "water" if nb == "bridge" else nb
                        if nb != "grass":
                            if best is None or nb == best:
                                best, mask = nb, mask | (1 << bit)
                t = (_load(os.path.join(GROUND, f"edge_grass_{best}_{mask}.png"))
                     if mask else var("grass", rng.randrange(16)))
                if t is None:
                    t = var("grass", rng.randrange(16))
            else:
                t = var(b, rng.randrange(16))
            if t is not None:
                img.alpha_composite(t, (cx - TILE_W // 2, cy - TILE_H // 2))

    # ---- decals, honouring the rule-4 grammar
    decals = {n: _load(os.path.join(DECAL, f"{n}.png")) for n in DECAL_WORLD_W}
    stones_done = False

    def put(name, x, y, wpx, jitter=26):
        d = decals.get(name)
        if d is None:
            return
        k = wpx / d.width
        dd = d.resize((wpx, max(1, int(d.height * k * 0.62))), Image.LANCZOS)
        cx, cy = _center(x, y, rows)
        cx += rng.randint(-jitter, jitter)
        cy += rng.randint(-jitter // 2, jitter // 2)
        img.alpha_composite(dd, (cx - dd.width // 2, cy - dd.height // 2))

    for y in range(rows):
        for x in range(cols):
            b, ch = biome[y][x], SCENE[y][x]
            if ch in PROP_CHARS or ch == "f":
                continue
            if b == "grass":
                nbrs = [biome[j][i] for i, j in
                        ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1))
                        if 0 <= i < cols and 0 <= j < rows]
                if not stones_done and "path" in nbrs and 3 < x < 11 and y > 4:
                    put("decal_stones", x, y, 64)     # ONE warm accent, beside the path
                    stones_done = True
                elif rng.random() < 0.20:
                    put(rng.choice(["decal_fern", "decal_moss", "decal_clover"]), x, y,
                        rng.choice([90, 72, 60]))
            elif b == "path" and rng.random() < 0.34:
                put(rng.choice(["decal_footprints", "decal_pebbles"]), x, y, 56)
            elif b == "water" and rng.random() < 0.22:
                put(rng.choice(["decal_lily", "decal_lily_blossom"]), x, y, 54)

    # ---- fences, props and actors in painter order (back rows first)
    fx, fy = _load(os.path.join(DECOR, "fence_x.png")), _load(os.path.join(DECOR, "fence_y.png"))
    for y in range(rows):
        for x in range(cols):
            ch = SCENE[y][x]
            cx, cy = _center(x, y, rows)
            if ch == "f" and fx is not None and fy is not None:
                # Along-X edges take the _E piece, along-Y edges the _N piece — the
                # same choice SceneBuilder makes for the Kenney ring.
                horiz = (y in (6, 9)) or SCENE[y][x] == "f" and SCENE[max(0, y - 1)][x] != "f"
                s = fx if horiz else fy
                k = TILE_W / s.width          # SceneBuilder: scale to one cell wide
                s2 = s.resize((TILE_W, max(1, int(s.height * k))), Image.LANCZOS)
                img.alpha_composite(s2, (cx - TILE_W // 2, cy - s2.height + TILE_H // 2 + 32))
            elif ch in PROP_CHARS:
                d = PROP if ch not in ("N",) else DECOR
                s = _load(os.path.join(d, f"{PROP_CHARS[ch]}.png"))
                if s is None:
                    continue
                img.alpha_composite(s, (cx - s.width // 2, cy + TILE_H // 3 - s.height))

    for path, cell, wpx in ((BACKHOE, (4, 4), 300), (DINO, (8, 6), 150)):
        s = _load(path)
        if s is None:
            continue
        k = wpx / s.width
        s = s.resize((wpx, max(1, int(s.height * k))), Image.LANCZOS)
        cx, cy = _center(cell[0], cell[1], rows)
        img.alpha_composite(s, (cx - s.width // 2, cy - s.height + 24))
    return img


def _row(paths, h, gap=10, label_h=0):
    ims = []
    for p in paths:
        im = Image.open(p).convert("RGBA")
        k = h / im.height
        ims.append(im.resize((max(1, int(im.width * k)), h), Image.LANCZOS))
    w = sum(i.width for i in ims) + gap * (len(ims) + 1)
    strip = Image.new("RGBA", (w, h + gap * 2 + label_h), (0, 0, 0, 0))
    x = gap
    for i in ims:
        strip.alpha_composite(i, (x, gap + label_h))
        x += i.width + gap
    return strip


INK, DIM = (238, 238, 242), (150, 152, 162)


def _font(sz, bold=False):
    from PIL import ImageFont
    for f in (("/System/Library/Fonts/Supplemental/Arial Bold.ttf" if bold else
               "/System/Library/Fonts/Supplemental/Arial.ttf"),
              "/Library/Fonts/Arial.ttf"):
        try:
            return ImageFont.truetype(f, sz)
        except OSError:
            continue
    from PIL import ImageFont as _IF
    return _IF.load_default()


def _section(paths, cols, thumb, title, note=""):
    """One labelled catalogue block: title + note + EVERY asset in the group.

    The contact sheet has to be auditable on its own — a sampled sheet cannot answer
    "does any tile in the shipped set break a rule?", which is the whole point of
    reviewing it. So every shipped file appears here, not a selection."""
    from PIL import ImageDraw
    tw, th = thumb
    gap, head = 6, 46
    rows = (len(paths) + cols - 1) // cols
    W = cols * (tw + gap) + gap
    H = head + rows * (th + gap) + gap
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.text((gap, 6), title, font=_font(21, True), fill=INK)
    if note:
        d.text((gap, 28), note, font=_font(14), fill=DIM)
    for i, p in enumerate(paths):
        s = Image.open(p).convert("RGBA")
        k = min(tw / s.width, th / s.height)
        s = s.resize((max(1, int(s.width * k)), max(1, int(s.height * k))),
                     Image.LANCZOS)
        x = gap + (i % cols) * (tw + gap) + (tw - s.width) // 2
        y = head + (i // cols) * (th + gap) + (th - s.height) // 2
        im.alpha_composite(s, (x, y))
    return im


def sheet() -> str:
    """ONE sheet: the dressed scene (the tap-language verdict) over a complete labelled
    catalogue of every shipped file in the set."""
    from PIL import ImageDraw
    import random
    rng = random.Random(11)
    sc = scene(rng)

    fence = [p for p in _ls(DECOR, "*.png") if "fence" in p]
    secs = [
        _section(_ls(PROP, "*.png"), 8, (188, 188), "TAPPABLE PROPS — outlined",
                 "thick black actor-weight outline = tap affordance. no faces, full "
                 "saturation (never quiet-passed)"),
        _section([p for p in _ls(DECOR, "*.png")
                  if "plate" not in p and "fence" not in p] + fence, 8, (188, 188),
                 "DECOR — outline-FREE", "quiet furniture: bridge deck, fence pair "
                 "(fence_* sit in the Kenney 256x512 canvas, art at the Kenney bbox), nest"),
        _section(_ls(DECAL, "decal_*.png"), 8, (140, 140), "DECALS — outline-FREE",
                 "rule-4 grammar: grass=fern/moss/clover · path=footprints/pebbles · "
                 "water=lily · amber stones ONE cluster per region, beside the path"),
        _section(_ls(GROUND, "tile_grass_*.png"), 16, (150, 76),
                 "GROUND tile_grass_00..15", "one plate -> 16 variants, mean-matched"),
        _section(_ls(GROUND, "tile_path_*.png"), 16, (150, 76),
                 "GROUND tile_path_00..15"),
        _section(_ls(GROUND, "tile_water_*.png"), 16, (150, 76),
                 "GROUND tile_water_00..15"),
        _section(_ls(GROUND, "tile_bed_*.png"), 16, (150, 76),
                 "GROUND tile_bed_00..03"),
        _section(_ls(GROUND, "edge_grass_path_*.png"), 16, (150, 76),
                 "TRANSITION edge_grass_path_1..15",
                 "procedural masks over the same plates — mask bit0=-Y bit1=+X bit2=+Y "
                 "bit3=-X"),
        _section(_ls(GROUND, "edge_grass_water_*.png"), 16, (150, 76),
                 "TRANSITION edge_grass_water_1..15", "+ pale sand shoreline ramp"),
        _section(_ls(GROUND, "edge_grass_bed_*.png"), 16, (150, 76),
                 "TRANSITION edge_grass_bed_1..15"),
    ]
    pad = 30
    W = max([sc.width] + [s.width for s in secs]) + pad * 2
    H = 88 + sc.height + sum(s.height + pad for s in secs) + pad * 2
    out = Image.new("RGB", (W, H), (26, 27, 31))
    d = ImageDraw.Draw(out)
    d.text((pad, 22), "DinoDigger ENV set — Jurassic-earth toy-box  (DinoDigger-c7m)",
           font=_font(30, True), fill=INK)
    n = sum(len(_ls(x, y)) for x, y in ((PROP, "*.png"), (DECAL, "decal_*.png")))
    n += len([p for p in _ls(DECOR, "*.png") if "plate" not in p])
    n += len(_ls(GROUND, "tile_*.png")) + len(_ls(GROUND, "edge_*.png"))
    d.text((pad, 58), f"{n} shipped sprites, all <=256 px. Terrain, decals and decor "
           f"carry NO outlines; only tappable props do — that gap is the whole "
           f"interaction language.", font=_font(16), fill=DIM)
    out.paste(sc, ((W - sc.width) // 2, 88), sc)
    y = 88 + sc.height + pad
    for s in secs:
        out.paste(s, (pad, y), s)
        y += s.height + pad
    p = os.path.join(OUT, "contact_sheet.png")
    out.save(p)
    print(f"       {p}  ({out.width}x{out.height}, {n} shipped sprites catalogued)")
    return p


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "list"
    args = sys.argv[2:]
    if cmd == "list":
        for k, v in SPECS.items():
            print(f"{k:24s} {v['kind']:7s} {v['group']}")
    elif cmd == "gen":
        names = args or ORDER
        ok = all(gen(n) for n in names)
        sys.exit(0 if ok else 1)
    elif cmd == "regen":
        ok = all(gen(n, force=True) for n in args)
        sys.exit(0 if ok else 1)
    elif cmd == "bake":
        bake(args or GROUPS)
    elif cmd == "verify":
        verify()
    elif cmd == "sheet":
        sheet()
    else:
        print(__doc__)
        sys.exit(2)
