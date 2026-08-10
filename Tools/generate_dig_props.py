#!/usr/bin/env python3
"""D1 dig-toy props for Dino Digger (ticket DinoDigger-ps5).

Standalone companion to generate_sprites.py (same pattern as generate_ducks.py):
it never edits that module, it only borrows the pure helpers — the OpenRouter
request/save path from generate_sprites and chroma_key/despeckle/trim from
slice_sprites — so keying matches every other sprite in the game.

Produces the dig-site toys:
    crystal_teal / crystal_coral / crystal_gold  — ONE silhouette, three colors.
        A neutral quartz master (dig_crystal_master, never shipped) is generated
        once, then each color is a SINGLE img2img recolor hop off that same
        master. One hop each (not a chain) keeps all three silhouettes pixel
        identical — they slice to the same 545x626 — and costs 3 gens, not 3
        fresh generations that would drift apart.
    boom_geode                — the special "boom" tile: round, gem-heaped, sparkly.
    pinata_pot / pinata_pot_cracked — the pot and its broken state (img2img off
        the whole pot, so the clay and zigzag band carry over exactly).
    dust_thump                — soft landing-feedback dust cloud.

PROMPT SCARS (do not "simplify" these clauses away — each one bought a retry):
  * The geode's first pass drew a ring of pointed crystals around a pale center
    on a round rock and read unmistakably as a TOOTHY MOUTH; the second pass
    lost the teeth but put two curved pit marks and a curve on the stone, which
    read as a sleepy FACE. Hence "SMOOTH ROUNDED bubbly gems, NEVER pointed",
    the explicit no-face list, and the demand for plain unmarked stone.
  * "a small soft puff of dust" made the model draw a blue ELEPHANT puffing a
    little cloud. Hence the drawn-LARGE-and-centered framing plus the blunt
    "NO animal, NO elephant, NO creature, NO foot" list.

Raw generations land in Tools/raw/dig_*.png (gitignored); sliced, transparent,
game-ready PNGs land in Assets/Art/Generated/dig/.

Usage:
    python3 generate_dig_props.py list
    python3 generate_dig_props.py gen [<name>...]      # (re)generate raw
    python3 generate_dig_props.py slice [<name>...]    # raw -> Generated/dig/
"""
import base64
import os
import sys

REPO = "/Users/greg/projects/DinoDigger"
sys.path.insert(0, os.path.join(REPO, "Tools", "venv", "lib", "python3.13",
                                "site-packages"))
sys.path.insert(0, os.path.join(REPO, "Tools"))

import numpy as np                                            # noqa: E402
from PIL import Image                                         # noqa: E402
import generate_sprites as G                                  # noqa: E402
import slice_sprites as S                                     # noqa: E402

RAW = G.RAW_DIR
OUT = os.path.join(REPO, "Assets", "Art", "Generated", "dig")

# The lone-prop style: PART_STYLE's "no eyes/no face" mechanical wording, plus the
# hard single-object pin used for the town props (prop_hardhat / prop_tool_hammer).
PROP_STYLE = (
    "Chunky toddler-friendly cartoon style prop for a preschool game. Thick bold "
    "dark outlines (heavy uniform black outline all the way around the silhouette), "
    "bright saturated colors, soft simple cel shading, rounded friendly chunky "
    "shapes, flat 2D game sprite look, like a toy-box playset piece. It is an "
    "inanimate OBJECT: absolutely NO eyes, no face, no mouth, no nose, no character "
    "features of any kind. Absolutely no text, no letters, no numbers, no words, no "
    "logos, no watermark. EXACTLY ONE single object centered in the frame - one "
    "object only, not a set, not a row, not a grid, no duplicates, no smaller "
    "copies, no extra props of any kind anywhere in the image. The entire "
    "background must be a single solid flat pure magenta color #FF00FF "
    "(RGB 255,0,255) with nothing else on it, no gradient, no vignette. The subject "
    "casts NO shadow at all: no drop shadow, no ground shadow, no contact shadow - "
    "the area directly under and around the subject is pure flat magenta with "
    "nothing on it. "
)

READS_SMALL = (
    "The object will be displayed very small on screen (about one seventh of the "
    "screen height), so it must read instantly as a bold simple silhouette: few "
    "large shapes, chunky proportions, strong contrast, no fine detail, no tiny "
    "specks, no thin lines. "
)

CRYSTAL_SHAPE = (
    "a chunky faceted crystal cluster: ONE big fat blunt-tipped hexagonal crystal "
    "standing upright in the middle, with exactly two smaller stubby crystals "
    "leaning against its base, one on each side. Each crystal is a simple chunky "
    "gem with a few large flat facets and a bright highlight streak, blunt rounded "
    "points (not sharp or spiky), sitting on a small rounded rocky base"
)

RECOLOR = (
    "Generate an image. Here is a reference picture of a crystal cluster. Redraw "
    "the EXACT SAME crystal cluster - identical shape, identical silhouette, "
    "identical number and position of crystals, identical facets, identical size, "
    "position, outline thickness and shading - but recolored so the crystals are "
    "{color}. Change ONLY the colors of the crystals; do not move, resize, reshape, "
    "add or remove anything. The rocky base stays the same grey stone. "
    f"{PROP_STYLE}Solid flat magenta #FF00FF background."
)

# The CREATURE style, for the one thing in this file that is ALIVE (the glowbug).
# PROP_STYLE's whole job is to strip character cues; a critter needs them back, so
# this is generate_sprites.STYLE's cute-eyes wording plus the same single-object pin
# and no-shadow clause the props are held to.
CREATURE_STYLE = (
    "Chunky toddler-friendly cartoon creature for a preschool game. Thick bold dark "
    "outlines (heavy uniform black outline all the way around the silhouette), bright "
    "saturated colors, soft simple cel shading, rounded friendly chubby shapes, big "
    "cute expressive eyes, adorable and cheerful. Flat 2D game sprite look. Absolutely "
    "no text, no letters, no numbers, no words, no logos, no watermark. EXACTLY ONE "
    "single creature centered in the frame - one creature only, not a set, not a row, "
    "no duplicates, no smaller copies, no extra props of any kind anywhere in the "
    "image. The entire background must be a single solid flat pure magenta color "
    "#FF00FF (RGB 255,0,255) with nothing else on it, no gradient, no vignette. The "
    "subject casts NO shadow at all: no drop shadow, no ground shadow, no contact "
    "shadow - the area directly under and around the subject is pure flat magenta "
    "with nothing on it. "
)

SPECS = {
    # --- crystals: one neutral master, then three 1-hop recolors ---------------
    "dig_crystal_master": dict(
        prompt=(f"Generate an image. {PROP_STYLE}{READS_SMALL}"
                f"Draw {CRYSTAL_SHAPE}. The crystals are a plain neutral pale "
                f"silvery-white quartz color with soft grey shading, so they can be "
                f"recolored later. Solid flat magenta #FF00FF background."),
        out=None),
    "dig_crystal_teal": dict(
        prompt=RECOLOR.format(color=("a bright saturated TEAL / turquoise blue-green, "
                                     "with deeper teal shadow facets and pale mint "
                                     "highlights")),
        ref="dig_crystal_master", out="crystal_teal"),
    "dig_crystal_coral": dict(
        prompt=RECOLOR.format(color=("a bright saturated CORAL pink-orange, with "
                                     "deeper rosy-red shadow facets and pale peach "
                                     "highlights")),
        ref="dig_crystal_master", out="crystal_coral"),
    "dig_crystal_gold": dict(
        prompt=RECOLOR.format(color=("a bright saturated GOLDEN yellow amber, with "
                                     "deeper orange-gold shadow facets and pale "
                                     "cream-yellow highlights")),
        ref="dig_crystal_master", out="crystal_gold"),

    # --- boom geode -----------------------------------------------------------
    "dig_boom_geode": dict(
        prompt=(f"Generate an image. {PROP_STYLE}{READS_SMALL}"
                f"Draw ONE geode bowl: a fat chunky ROUND ball of bumpy grey-brown "
                f"stone whose whole TOP has broken away, leaving an open bowl with a "
                f"chunky rim of a few broad blunt bumps at the top, tipped slightly "
                f"toward the viewer so we "
                f"look down into it. The inside of the bowl is filled with a heaped bed "
                f"of SMOOTH ROUNDED bubbly gems in bright lilac, purple and pink - "
                f"rounded bumps and domes like a pile of shiny marbles or bubbles, "
                f"NEVER pointed spikes, NEVER triangles, NEVER teeth, with a soft pale "
                f"glow over them. Three or four chunky four-pointed cartoon SPARKLE "
                f"stars in cream and pale yellow sit right against the rim, each one "
                f"TOUCHING the stone so everything stays one connected object - no "
                f"sparkle floats away on its own. "
                f"The outer stone surface is completely PLAIN and unmarked: no dots, no "
                f"spots, no dimples, no pits, no curved lines, no arcs, no squiggles, "
                f"no marks of any kind drawn on the rock - just flat grey-brown stone "
                f"with soft simple shading. "
                f"CRITICAL: this must NOT look like a face or a mouth. There is no dark "
                f"hole, no throat, no lips, no tongue, no ring of pointed teeth, no "
                f"eyes, no closed eyes, no eyebrows, no smile, no cheeks, no sleepy "
                f"face - it is a stone bowl heaped with round shiny gems. It looks "
                f"magical, "
                f"happy and inviting like a treasure surprise: not scary, not spooky, no "
                f"explosion, no fire, no smoke. Its outer shape is clearly ROUND and "
                f"ball-like, obviously different from a pointy crystal cluster. "
                f"Solid flat magenta #FF00FF background."),
        out="boom_geode"),

    # --- pinata pot + cracked state ------------------------------------------
    "dig_pinata_pot": dict(
        prompt=(f"Generate an image. {PROP_STYLE}{READS_SMALL}"
                f"Draw ONE ancient clay pot: a fat round-bellied terracotta pot with a "
                f"short wide neck and a small flared rim, warm orange-brown clay with "
                f"soft cel shading. Around its widest part runs a single painted "
                f"decorative band of simple chunky ZIGZAG triangles in cream and teal - "
                f"a plain repeating zigzag pattern only, absolutely no letters, no "
                f"symbols, no glyphs, no hieroglyphs, no writing of any kind. The pot "
                f"is whole and unbroken, cute and cheerful like a party pinata. "
                f"Solid flat magenta #FF00FF background."),
        out="pinata_pot"),
    "dig_pinata_pot_cracked": dict(
        prompt=("Generate an image. Here is a reference picture of a clay pot. Redraw "
                "the EXACT SAME pot - identical clay color, identical painted zigzag "
                "band, identical outline thickness, shading, size and position - but "
                "now it is CRACKED OPEN: the top half of the pot is broken away into a "
                "jagged missing chunk with a few chunky broken shards still attached at "
                "the rim, and the open break shows gold coins and one or two round gems "
                "heaped inside, glinting, with a couple of chunky four-pointed cartoon "
                "sparkle stars right at the opening. The bottom half of the pot with "
                "its zigzag band stays exactly as it is. Everything stays one single "
                "connected object - no loose shards or coins floating away from the "
                "pot, nothing scattered on the ground. "
                f"{PROP_STYLE}Solid flat magenta #FF00FF background."),
        ref="dig_pinata_pot", out="pinata_pot_cracked"),

    # --- dust thump -----------------------------------------------------------
    "dig_dust_thump": dict(
        prompt=(f"Generate an image. {PROP_STYLE}{READS_SMALL}"
                f"Draw ONE simple cartoon DUST CLOUD shape and nothing else: three or "
                f"four fat overlapping round bumps merged into a single low wide cloud "
                f"blob, clearly wider than it is tall, with a flat-ish bottom edge, "
                f"like the little cloud a cartoon foot kicks up. It is drawn LARGE and "
                f"centered so it fills most of the frame. Soft warm dusty beige and "
                f"pale tan, one lighter cream tone across the top bumps, thick dark "
                f"outline around the whole cloud. "
                f"CRITICAL: the ONLY thing in the picture is this one dust cloud blob. "
                f"There is NO animal, NO elephant, NO creature, NO character, NO "
                f"person, NO foot, NO feet, NO dinosaur, NO vehicle, NO second object "
                f"of any kind - just the cloud shape alone on the magenta. Also no "
                f"motion lines, no speed streaks, no pebbles, no rocks, no debris, no "
                f"sparkles, no ground, no smoke wisps. "
                f"Solid flat magenta #FF00FF background."),
        out="dust_thump"),

    # --- the ladder down + its "down" affordance (DinoDigger-n05) --------------
    # The ladder was shipping on the town's striped BARRIER SIGN as a placeholder,
    # which reads as "do not enter" — the exact opposite of the one thing it means.
    # PROMPT SCARS: "ladder going down into a hole" drew a whole ISOMETRIC SCENE
    # (dirt bank, sky, grass) instead of a prop, so the framing is pinned to a flat
    # front-on prop with nothing around it. "Rungs" alone drew a thin lattice that
    # vanished at tile size, hence the explicit fat-rails / few-rungs count.
    "dig_prop_ladder_down": dict(
        prompt=(f"Generate an image. {PROP_STYLE}{READS_SMALL}"
                f"Draw ONE chunky wooden LADDER seen flat from the front, standing "
                f"upright and filling the frame top to bottom. It has exactly TWO very "
                f"thick rounded vertical side rails made of warm honey-brown timber with "
                f"simple wood-grain shading, and exactly FOUR fat chunky horizontal rungs "
                f"evenly spaced between them, each rung a rounded wooden bar. The top of "
                f"each rail is a rounded cap. It is a solid toy-playset ladder: fat, "
                f"stubby and friendly, NOT thin, NOT tall and skinny, NOT a lattice. "
                f"CRITICAL FRAMING: this is a single cut-out PROP on flat magenta - there "
                f"is NO ground, NO hole, NO pit, NO dirt, NO wall, NO sky, NO grass, NO "
                f"scenery, NO scene, NO perspective floor, nothing behind or beside the "
                f"ladder at all. Just the ladder shape alone. "
                f"Solid flat magenta #FF00FF background."),
        out="prop_ladder_down"),

    # The affordance the ladder alone cannot carry: a fat chevron the runtime bobs
    # DOWNWARD under the prop, so "this takes you deeper" is legible with no words.
    # Drawn as a solid chevron rather than a full arrow so it never reads as a UI
    # button or a letter V.
    "dig_prop_arrow_down": dict(
        prompt=(f"Generate an image. {PROP_STYLE}{READS_SMALL}"
                f"Draw ONE fat chunky CHEVRON STROKE pointing straight DOWN and nothing "
                f"else. Picture a single thick rounded BAR, like a fat crayon stroke, "
                f"bent once in the middle into a wide shallow V so that the bend points "
                f"DOWNWARD at the bottom center and the two ends rise up to the left and "
                f"right. The bar has a constant chunky thickness along its whole length "
                f"and both ends are cut off with rounded caps. It is clearly wider than "
                f"it is tall. Bright warm cream-yellow and pale gold with a thick dark "
                f"outline all the way around the bar and one lighter highlight along its "
                f"top edge. "
                f"CRITICAL: it is a BENT BAR, not a filled shape - the whole area ABOVE "
                f"the bar, BELOW the bar and INSIDE the V is empty flat magenta showing "
                f"through. It is NOT a solid triangle, NOT a shield, NOT a pentagon, NOT "
                f"a badge, NOT a plaque, NOT a banner, NOT a filled arrowhead, NOT a "
                f"heart. Exactly ONE bar - not two, not three, not a stack, no second "
                f"smaller chevron, no arrow shaft, no tail, no circle, no button, no "
                f"ring, no border, no box behind it, no text. "
                f"Solid flat magenta #FF00FF background."),
        out="prop_arrow_down"),

    # --- the catchable glowbug (DinoDigger-n05) --------------------------------
    # It was drawing on the STAR PARTICLE, which reads as loot; a thing that PAYS a
    # coin must not look like the coin. So: unmistakably ALIVE — round bug body,
    # little legs, a glowing tail lamp — and green, never gold.
    "dig_critter_glowbug": dict(
        prompt=(f"Generate an image. {CREATURE_STYLE}{READS_SMALL}"
                f"Draw ONE adorable chubby cartoon GLOWBUG (a firefly beetle) seen from "
                f"the side-front, drawn LARGE and centered so it fills most of the frame. "
                f"It has one fat round ball-shaped body in bright fresh lime-green and "
                f"apple-green with a soft cel-shaded belly, two big round friendly eyes "
                f"with white sparkle dots and a tiny happy smile, two short stubby "
                f"antennae with round tips, and THREE tiny stubby legs along the bottom "
                f"so it clearly reads as a living bug. At its back end is a round "
                f"lantern-tail: a SOLID flat pale butter-yellow dome with its own thick "
                f"dark outline all the way around it, like a little glowing bulb. Two "
                f"small rounded mint-green wings sit on its back, each with a thick dark "
                f"outline. "
                f"CRITICAL: every part is SOLID and fully opaque with a hard dark outline. "
                f"There is NO soft glow, NO halo, NO aura, NO haze, NO blur, NO fog, NO "
                f"see-through or semi-transparent parts, NO light rays and NO coloured "
                f"mist around the bug - the magenta comes right up to the outline "
                f"everywhere. "
                f"CRITICAL: it must NOT look like treasure or a collectible - there is NO "
                f"star shape, NO star points, NO gem, NO coin, NO jewel, NO crystal, NO "
                f"gold, NO crown, NO sparkle stars floating around it. It is a cute round "
                f"bug. Everything is one single connected creature. "
                f"Solid flat magenta #FF00FF background."),
        out="critter_glowbug"),

    # --- Glow the lantern bot (DinoDigger-n05) --------------------------------
    # THE actual object in Greg's screenshots: with no art wired, DigGlow fell back
    # to lib.StarParticle, so the child saw a giant smiley STAR perched off the edge
    # of the pit with the (mound-sprite) charge gauge stretched under it. Same family
    # as machines/doodle|sprinkles|tuggy: a face, a bottom-heavy chassis, one job.
    "machine_glow": dict(
        prompt=(f"Generate an image. {CREATURE_STYLE}{READS_SMALL}"
                f"Draw ONE cute little cartoon LANTERN ROBOT for a preschool game, seen "
                f"from the front, standing still. It is a small squat robot whose whole "
                f"BODY is a chunky rounded old-fashioned camping lantern: a fat rounded "
                f"glass lamp housing in the middle glowing warm honey-yellow, a chunky "
                f"teal-blue metal cap on top with a rounded carry handle arching over it, "
                f"and a chunky teal-blue metal base underneath standing on two short "
                f"stubby rounded feet. Two big round friendly cartoon eyes with white "
                f"sparkle dots and a small happy smile sit on the glowing lamp glass. Two "
                f"short stubby rounded arms stick out at its sides. A soft creamy-yellow "
                f"glow hugs the lamp housing. "
                f"CRITICAL: exactly one robot, standing upright, bottom-heavy and stable. "
                f"NO star shape, NO star points, NO light beam, NO rays, NO lightning "
                f"bolt, NO wheels, NO hat, NO scenery, NO ground. "
                f"Solid flat magenta #FF00FF background."),
        out="glow", dir="machines"),
}

ORDER = list(SPECS)


def neutralize(img: Image.Image) -> Image.Image:
    """Kill the leftover magenta hiding in fully-transparent pixels.

    The chroma key only zeroes ALPHA; the RGB under it stays the model's magenta,
    which bleeds back as a pink halo under bilinear filtering / mipmapping. Replace
    the RGB of every fully-transparent pixel with the sprite's own median opaque
    color, so any bleed pulls in the art's own tone instead of magenta."""
    a = np.asarray(img).copy()
    al = a[..., 3]
    solid = al > 200
    if not solid.any():
        return img
    med = np.median(a[solid][:, :3], axis=0).astype(np.uint8)
    a[al == 0, 0:3] = med
    return Image.fromarray(a, mode="RGBA")


def gen(name: str) -> bool:
    spec = SPECS[name]
    ref_b64 = None
    if spec.get("ref"):
        rp = os.path.join(RAW, f"{spec['ref']}.png")
        if not os.path.exists(rp):
            print(f"FAILED {name}: missing ref {rp}", file=sys.stderr)
            return False
        ref_b64 = base64.b64encode(open(rp, "rb").read()).decode()
    b64 = G._attempt(spec["prompt"], ref_b64, name, 2)
    if not b64:
        print(f"FAILED {name}")
        return False
    G._save_raw(b64, os.path.join(RAW, f"{name}.png"))
    return True


def slice_one(name: str, pad: int = 8) -> str | None:
    spec = SPECS[name]
    if not spec.get("out"):
        print(f"[skip] {name} (master, not shipped)")
        return None
    raw = os.path.join(RAW, f"{name}.png")
    if not os.path.exists(raw):
        print(f"[skip] {name}: no raw", file=sys.stderr)
        return None
    a = np.asarray(S.chroma_key(Image.open(raw))).copy()
    r = S.EDGE_RING
    a[:r, :, 3] = 0
    a[-r:, :, 3] = 0
    a[:, :r, 3] = 0
    a[:, -r:, 3] = 0
    img = S.despeckle(Image.fromarray(a, mode="RGBA"))
    img = S.clear_magenta_pockets(img)          # wide-magenta fallback
    img = S.trim(img, pad)
    img = neutralize(img)
    # Almost everything here is a dig prop; a spec may name a different Generated/
    # subfolder (Glow the lantern bot belongs with the other machine friends).
    outdir = os.path.join(REPO, "Assets", "Art", "Generated", spec.get("dir", "dig"))
    os.makedirs(outdir, exist_ok=True)
    out = os.path.join(outdir, f"{spec['out']}.png")
    img.save(out)
    print(f"       {out}  ({img.width}x{img.height})")
    return out


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "list"
    names = sys.argv[2:] or ORDER
    if cmd == "list":
        for n in ORDER:
            print(n, "->", SPECS[n].get("out"))
    elif cmd == "gen":
        ok = all(gen(n) for n in names)
        sys.exit(0 if ok else 1)
    elif cmd == "slice":
        for n in names:
            slice_one(n)
    else:
        print(__doc__)
        sys.exit(2)
