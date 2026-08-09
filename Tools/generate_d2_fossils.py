#!/usr/bin/env python3
"""D2 fossil art for Dino Digger (ticket DinoDigger-cwr).

Standalone companion to generate_sprites.py, same pattern as generate_dig_props.py:
it never edits that module, it only borrows the pure helpers — the OpenRouter
request/save path from generate_sprites and chroma_key/despeckle/trim/EDGE_RING
from slice_sprites — so keying matches every other sprite in the game.

Three groups:

  BONES (10)  -> Assets/Art/Generated/dig/bones/bone_<shape>.png
      Lone white/cream fossil bones on magenta, one per shape (femur, rib, skull,
      vertebra, claw, tooth, jaw, pelvis, toe, horn). Fresh single-subject gens
      using the same lone-prop guards generate_dig_props.py landed on, because a
      "bone" prompt otherwise comes back as a matched SET of bones laid out in a
      row, or as a cute skull with eyeballs.

  BOARDS (5)  -> Assets/Art/Generated/dig/boards/board_<species>.png
      The dark silhouette outline a fossil skeleton is assembled onto. These are
      NOT drawn from scratch: each is a single img2img hop off that species'
      EXISTING adult S-facing raw (Tools/raw/<species>_S.png), so the board's
      outline is the real in-game dino's silhouette rather than a generic
      stock-art dinosaur that would not match the creature the player digs up.

  DINOMATIC (5) -> Assets/Art/Generated/town/dinomatic_{done,s3,s2,s1,s0}.png
      The game-scale Dino-Matic 3000 plus its 5-state chain, following the town
      building conventions (TOWN_CAMERA isometric 3/4, magenta key, per-state
      trim, <name>_<state>.png naming). The finished machine is an img2img hop
      off the approved concept at Assets/Art/Concepts/machines/dinomatic_3000.png
      so the game sprite keeps the concept's exact design, re-shot on the
      isometric overworld camera.

      INVERTED FICTION (do not "fix" this to match TOWN_UNBUILD): the other nine
      town entries are BUILT, so their chain removes structure down to bare dirt.
      The Dino-Matic is EXCAVATED — it is dug OUT of the ground, not assembled —
      so its chain BURIES it instead: s3 sits in a shallow pit, s2 is half sunk
      and crusting over with moss, s1 is a mound with only the dome shoulder out,
      and s0 is a plain dirt mound with a single glinting sliver of the dome
      corner showing. Same done->s0 order, same chained img2img (each state is
      seeded from the next-more-excavated one) so the site never moves.

PROMPT SCARS (each one bought a retry; do not simplify away):
  * see inline notes on SKULL_GUARD and LONE_BONE.

Raw generations land in Tools/raw/d2_*.png (gitignored); sliced, transparent,
game-ready PNGs land under Assets/Art/Generated/.

Usage:
    python3 generate_d2_fossils.py list
    python3 generate_d2_fossils.py gen [<name>...]      # (re)generate raw
    python3 generate_d2_fossils.py slice [<name>...]    # raw -> Generated/
"""
import base64
import os
import sys

REPO = "/Users/greg/projects/DinoDigger"
sys.path.insert(0, os.path.join(REPO, "Tools"))

import numpy as np                                            # noqa: E402
from PIL import Image                                         # noqa: E402
import generate_sprites as G                                  # noqa: E402
import slice_sprites as S                                     # noqa: E402

RAW = G.RAW_DIR
GEN = os.path.join(REPO, "Assets", "Art", "Generated")
CONCEPTS = os.path.join(REPO, "Assets", "Art", "Concepts")

# ---------------------------------------------------------------------------
# Shared style blocks
# ---------------------------------------------------------------------------

# Lifted from generate_dig_props.PROP_STYLE — the lone-object pin ("EXACTLY ONE
# single object ... not a set, not a row, not a grid") is load-bearing here: a
# bare "draw a fossil bone" prompt reliably returns a tidy MUSEUM LAYOUT of six
# bones, which slices to one sprite containing six bones.
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

# The bones share one palette so a dug-up set reads as one skeleton.
BONE_COLOR = (
    "It is a dry old fossil BONE: creamy off-white ivory with warm pale sand-beige "
    "shading in the hollows and a soft cream highlight on top, no other colors. "
)

# A bone is not a creature. Without this the model gives the skull cartoon
# eyeballs and a grin (it is drawing a "cute dinosaur skull" character), and it
# sprouts little legs on the pelvis and jaw pieces.
LONE_BONE = (
    "CRITICAL: this is a lifeless piece of BONE lying on its own, NOT a character "
    "and NOT an animal. It has NO eyeballs, NO pupils, NO iris, NO cartoon eyes, NO "
    "eyebrows, NO smile, NO frown, NO tongue, NO expression, NO arms, NO legs, NO "
    "feet, NO body, NO flesh, NO skin. There is no dirt, no rock, no ground, no "
    "sand, no pedestal, no display stand and no second bone anywhere in the image - "
    "just the one bare bone floating alone on the flat magenta. "
)

# Skulls and jaws are allowed the anatomy a skull actually has, but only as dark
# empty holes — the moment the socket is described neutrally the model fills it
# with a glossy eyeball.
SKULL_GUARD = (
    "Its eye sockets are simple dark EMPTY holes in the bone with nothing inside "
    "them - no eyeball, no pupil, no glint, no shine, no colored disc, they are "
    "hollow openings you can see straight through. "
)


def bone(shape: str, extra: str = "") -> str:
    return (f"Generate an image. {PROP_STYLE}{READS_SMALL}{BONE_COLOR}"
            f"Draw ONE single {shape}. {extra}{LONE_BONE}"
            f"Solid flat magenta #FF00FF background.")


# ---------------------------------------------------------------------------
# Skeleton board silhouettes
# ---------------------------------------------------------------------------
# One img2img hop off the species' existing adult S raw. Flat fill, no interior
# detail: this is the "slot the bones in here" outline on the fossil board, so
# any interior shading would fight the bone sprites laid on top of it.
BOARD_PROMPT = (
    "Generate an image. Here is a reference picture of a cartoon dinosaur "
    "character. Redraw it as a FLAT SOLID DARK NAVY SILHOUETTE of this EXACT "
    "character: keep the outline shape, the pose, the proportions, the size and "
    "the position on the canvas EXACTLY the same as the reference, but fill the "
    "whole body in with one single flat solid dark navy blue color and NOTHING "
    "else. NO interior detail whatsoever: no eyes, no face, no mouth, no teeth, no "
    "nostrils, no smile, no pupils, no belly patch, no spots, no stripes, no "
    "scales, no plates, no claws drawn in, no shading, no highlights, no gradients, "
    "no lighter areas, no outline of a different color, no lines inside the shape - "
    "it is one single uniform dark navy shape, like a shadow puppet or a cookie "
    "cutter stamp of the character. The silhouette must still be instantly "
    "recognisable as this same species from its outline alone, so keep every horn, "
    "crest, frill, spike, back plate, wing, tail and limb exactly where the "
    "reference has them, at the same size. "
    # SCAR: the spinosaurus came back correct in the body but with its striped
    # orange/teal/yellow SAIL still fully coloured — the model reads a big patterned
    # appendage as decoration rather than as part of the character. Every appendage
    # has to be named as in-silhouette.
    "EVERY part of the creature is filled with that ONE same dark navy, including "
    "its back sail, fin, crest, frill, plates, spikes, wings, tail and every "
    "marking on them - the sail/fin must be a solid dark navy shape with NO "
    "stripes, NO rays, NO orange, NO teal, NO yellow, NO pattern and no colour of "
    "any kind left in it. There must be exactly TWO colours in the whole picture: "
    "the dark navy of the silhouette and the magenta of the background. "
    "Do not turn, rotate, mirror, resize or "
    "re-centre the character. Absolutely no text, no letters, no numbers, no logos, "
    "no watermark. The entire background must be a single solid flat pure magenta "
    "color #FF00FF (RGB 255,0,255) with nothing else on it, no gradient, no "
    "vignette. NO shadow of any kind on the background. "
    "Solid flat magenta #FF00FF background."
)

BOARD_SPECIES = ["pteranodon", "ankylosaurus", "spinosaurus",
                 "parasaurolophus", "velociraptor"]


# ---------------------------------------------------------------------------
# Dino-Matic 3000: game sprite + excavation chain
# ---------------------------------------------------------------------------
# The machine is a MACHINE, not a building, so BUILDING_STYLE's blanket "no face"
# would delete the concept's friendly eye panel — which IS the design. Instead we
# pin the machine to the concept and forbid only the things that drift.
MACHINE_STYLE = (
    "Chunky toddler-friendly cartoon style machine prop for a preschool game. "
    "Thick bold dark outlines, bright saturated colors, soft simple cel shading, "
    "rounded friendly chunky shapes, cheerful and inviting. Flat 2D game sprite "
    "look. Absolutely no text, no letters, no numbers, no words, no logos, no "
    "watermark, no labels, no gauges with markings. EXACTLY ONE single machine "
    "centered in the frame - not a set, not a row, no duplicates, no smaller "
    "copies. The entire background must be a single solid flat pure magenta color "
    "#FF00FF (RGB 255,0,255) with nothing else on it, no gradient, no vignette. "
    "The subject casts NO shadow at all: no drop shadow, no ground shadow, no "
    "contact shadow - the area directly under and around the subject is pure flat "
    "magenta with nothing on it. "
)

# Borrowed verbatim from generate_sprites.TOWN_CAMERA so the machine lands on the
# same isometric overworld camera as the nine town buildings.
TOWN_CAMERA = G.TOWN_CAMERA

# Keeps the concept's identity across the whole chain.
MACHINE_ID = (
    "The machine is a chunky rounded cube-shaped contraption in cream and teal "
    "with a thick dark outline, standing on four stubby orange peg feet. A big "
    "clear glass DOME sits on its top. Its front has a rounded panel with two "
    "large friendly cartoon eyes and a small simple smile, a row of fat colorful "
    "round push buttons (blue, orange, yellow, green) down its left side and "
    "front, and a wide slot on its right side with a pale cream bone sticking out "
    "of it. A little sprig of green leaves sprouts from its top left corner. "
)

DINOMATIC_DONE = (
    f"Generate an image. Here is a reference picture of a cartoon machine. Redraw "
    f"the EXACT SAME machine - identical design, identical cream and teal colors, "
    f"identical glass dome, identical friendly face panel, identical colored "
    f"buttons, identical bone slot, identical orange peg feet, identical little "
    f"green leaf sprig - but re-drawn as a finished game prop for an isometric "
    f"overworld map. {TOWN_CAMERA}"
    f"It stands FULLY DUG OUT and clean on its own small patch of packed brown dirt "
    f"and grass, freshly excavated and working: the glass dome is clear and "
    f"sparkling, the panels are wiped clean, and a small pile of loose dug dirt and "
    f"one dropped stone shovel sit beside it on the patch. Keep the small green "
    f"cartoon dinosaur sitting inside the glass dome. Do not change the machine's "
    f"design, colors or proportions in any way; only change the camera to the "
    f"isometric three-quarter overhead view and set it on its dirt patch. "
    f"{MACHINE_STYLE}Solid flat magenta #FF00FF background."
)

# done -> s0, each seeded from the previous (more-excavated) state.
DINOMATIC_STATES = ["s3", "s2", "s1", "s0"]
DINOMATIC_SEED = {"s3": "done", "s2": "s3", "s1": "s2", "s0": "s1"}

DINOMATIC_CHAIN = {
    "s3": (
        "Show this SAME machine EARLIER in its excavation, only MOSTLY dug out: it "
        "still sits down inside a shallow scooped DIRT PIT so its four peg feet and "
        "the very bottom rim of its body are still under the soil, and crumbly brown "
        "earth is banked up against its lower edge. Dry dirt is smeared across the "
        "lower panels and dusted over the glass dome so the dome is a little cloudy, "
        "and one stone pick and a small heap of dug soil lie on the rim of the pit. "
        "Everything above the dirt line is still the same clean machine, and the "
        "little dinosaur is still visible inside the dome."),
    "s2": (
        "Show this SAME machine EARLIER still, only HALF EXCAVATED: the bottom HALF "
        "of the machine is buried in a mound of brown earth so the feet, the button "
        "panel and the bone slot are completely gone under the soil, and only its "
        "upper half and the glass dome stand clear. The exposed metal is crusted "
        "with dried dirt and patches of soft green MOSS creeping over the panels, "
        "the dome is grimy and clouded so what is inside is only a vague shape, and "
        "a couple of thin roots and tufts of grass grow across it. It clearly looks "
        "like something OLD being dug up out of the ground, not something new."),
    "s1": (
        "Show this SAME spot even EARLIER, only just BROKEN INTO: almost the whole "
        "machine is buried under a big rounded mound of brown earth and grass. Only "
        "the very TOP of it breaks the surface - the upper curve of the glass dome "
        "and one small corner of a teal panel poke out of the dirt, thickly furred "
        "with green moss, with roots and grass tufts growing over them and loose "
        "clods of soil around the hole. NO face panel, NO buttons, NO bone slot, NO "
        "feet are visible at all - they are all under the ground. A stone pick "
        "leans against the mound where someone started digging."),
    "s0": (
        "Show this SAME spot before the dig has really begun: just a rounded MOUND "
        "of brown dirt and patchy grass sitting on the ground, with a couple of thin "
        "wooden marker STAKES pushed into it. Absolutely NOTHING of the machine is "
        "visible except ONE tiny sliver: a single small curved GLINT of pale "
        "blue-green glass, no bigger than a pebble, just breaking the surface at the "
        "top of the mound with one little cartoon sparkle on it - a hint that "
        "something is buried under there. NO dome, NO panels, NO buttons, NO face, "
        "NO feet, NO machine shape of any kind - only a dirt mound, stakes and that "
        "one small glass glint."),
}


def dinomatic_chain_prompt(state: str) -> str:
    return (f"Generate an image. Here is a reference picture of a cartoon machine "
            f"being excavated out of the ground on an isometric game map. "
            f"{DINOMATIC_CHAIN[state]} Keep the EXACT same location, the same ground "
            f"patch in the same place, the same camera angle, the same art style, "
            f"the same colors and the same outline weight as the reference; the "
            f"machine (whatever is still showing of it) must stay exactly where it "
            f"is on the canvas at the same scale. {TOWN_CAMERA}{MACHINE_STYLE}"
            f"Solid flat magenta #FF00FF background.")


# ---------------------------------------------------------------------------
# Spec table
# ---------------------------------------------------------------------------
# out = (subdirectory under Generated/, filename stem); ref = raw stem to img2img.
SPECS: dict[str, dict] = {}

_BONES = [
    ("femur", "a long dinosaur thigh bone (femur) lying at a slight diagonal: a "
              "thick straight shaft with a big knobbly rounded double-lobed knuckle "
              "on each end, fat and chunky like a cartoon dog bone but longer and "
              "slightly curved", ""),
    ("rib", "a curved dinosaur RIB bone: one long smooth crescent-shaped bone that "
            "arcs like a big letter C, thick and rounded at the top end where it "
            "joined the spine and tapering to a blunt point at the bottom", ""),
    ("skull", "a chunky cartoon dinosaur SKULL seen from a three-quarter angle: a "
              "big rounded braincase, two large round EMPTY eye sockets, a blunt "
              "snout with a couple of chunky nostril holes, and an upper jaw with a "
              "row of short blunt rounded teeth", SKULL_GUARD),
    ("vertebra", "a single dinosaur VERTEBRA (one backbone segment): a fat rounded "
                 "spool-shaped block of bone with a hole through its middle and one "
                 "chunky blunt fin sticking up from the top and two small blunt "
                 "wings out to the sides", ""),
    ("claw", "a single big dinosaur CLAW: one fat curved sickle-shaped talon, thick "
             "and blunt-tipped, wide and knobbly at the base where it attached and "
             "curving to a rounded point", ""),
    # SCAR: "a fat cone-shaped fang" first came back as a flat two-rooted HUMAN
    # MOLAR. The single-root / crocodile-fang wording is what pins it.
    ("tooth", "a single big dinosaur FANG TOOTH: one tall smooth cone, widest at "
              "the bottom and tapering up to a blunt rounded point, like one "
              "crocodile fang. It has ONE single tapering root, NOT two roots and "
              "NOT a flat squat human molar, and it is clearly a simple pointed "
              "cone shape with a soft cream highlight down one side", ""),
    # SCAR: asking for "a lower jawbone" returned a whole SKULL with its mouth
    # hanging open (a duplicate of bone_skull). The jaw has to be described as a
    # detached piece with the skull explicitly excluded.
    ("jaw", "a dinosaur LOWER JAWBONE all on its own, detached from the skull: ONE "
            "long shallow boomerang-shaped bar of bone, like a wide flattened "
            "letter V lying on its side, with a row of short blunt rounded teeth "
            "standing up along its top edge and a chunky rounded hinge knob at the "
            "back end. CRITICAL: this is ONLY the bottom jaw bar by itself - there "
            "is NO skull, NO braincase, NO upper jaw, NO snout, NO eye socket, NO "
            "nostril and NO head shape of any kind in the picture, just the single "
            "detached lower jaw bar", ""),
    ("pelvis", "a chunky dinosaur HIP BONE (pelvis): one broad flat plate of bone "
               "shaped a bit like a wide blunt butterfly, with a big round socket "
               "hole through the middle and two thick rounded blades spreading out "
               "to the sides", ""),
    # SCAR: "a small stubby toe bone" came back as a second classic long dog-bone,
    # visually identical to bone_femur. Smallness cannot be conveyed by adjectives
    # in a lone-subject frame (everything fills the frame), so the SHAPE has to be
    # squat: the shaft is named as almost absent.
    ("toe", "a dinosaur TOE KNUCKLE BONE: one very SHORT squat little bone, WIDER "
            "than it is long, basically just two fat rounded knuckle knobs pressed "
            "right up against each other with almost no shaft between them at all. "
            "CRITICAL: it must NOT be a long bone - there is no long straight shaft, "
            "no narrow middle section, and it must NOT look like the classic long "
            "cartoon dog-bone shape. It is a stubby little pebble-sized lump of "
            "bone, roughly as tall as it is wide", ""),
    ("horn", "a dinosaur HORN CORE: one long thick tapering cone of bone that "
             "curves gently, wide and slightly ridged at the base and narrowing to "
             "a blunt rounded tip, like a cartoon triceratops horn", ""),
]
for _n, _desc, _extra in _BONES:
    SPECS[f"d2_bone_{_n}"] = dict(prompt=bone(_desc, _extra),
                                  out=("dig/bones", f"bone_{_n}"))

for _sp in BOARD_SPECIES:
    SPECS[f"d2_board_{_sp}"] = dict(prompt=BOARD_PROMPT, ref=f"{_sp}_S",
                                    out=("dig/boards", f"board_{_sp}"))

SPECS["d2_dinomatic_done"] = dict(prompt=DINOMATIC_DONE,
                                  ref_file=os.path.join(
                                      CONCEPTS, "machines", "dinomatic_3000.png"),
                                  out=("town", "dinomatic_done"))
for _st in DINOMATIC_STATES:
    SPECS[f"d2_dinomatic_{_st}"] = dict(
        prompt=dinomatic_chain_prompt(_st),
        ref=f"d2_dinomatic_{DINOMATIC_SEED[_st]}",
        out=("town", f"dinomatic_{_st}"))

ORDER = list(SPECS)


# ---------------------------------------------------------------------------
# gen / slice
# ---------------------------------------------------------------------------

def neutralize(img: Image.Image) -> Image.Image:
    """Replace the RGB hiding under fully-transparent pixels with the sprite's own
    median opaque color, so bilinear filtering cannot bleed magenta back in.
    (Same helper generate_dig_props.py uses.)"""
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
    rp = spec.get("ref_file") or (
        os.path.join(RAW, f"{spec['ref']}.png") if spec.get("ref") else None)
    if rp:
        if not os.path.exists(rp):
            print(f"FAILED {name}: missing ref {rp}", file=sys.stderr)
            return False
        ref_b64 = base64.b64encode(open(rp, "rb").read()).decode()
    out_raw = os.path.join(RAW, f"{name}.png")
    b64 = G._attempt(spec["prompt"], ref_b64, name, 2)
    if not b64:
        print(f"FAILED {name}")
        return False
    G._save_raw(b64, out_raw)
    return True


def slice_one(name: str, pad: int = 8) -> str | None:
    spec = SPECS[name]
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
    img = S.trim(img, pad)
    img = neutralize(img)
    sub, stem = spec["out"]
    outdir = os.path.join(GEN, *sub.split("/"))
    os.makedirs(outdir, exist_ok=True)
    out = os.path.join(outdir, f"{stem}.png")
    img.save(out)
    print(f"       {out}  ({img.width}x{img.height})")
    return out


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "list"
    names = sys.argv[2:] or ORDER
    bad = [n for n in names if n not in SPECS]
    if bad:
        print(f"unknown: {bad}", file=sys.stderr)
        sys.exit(2)
    if cmd == "list":
        for n in ORDER:
            print(f"{n:24s} -> {'/'.join(SPECS[n]['out'])}.png")
    elif cmd == "gen":
        ok = all(gen(n) for n in names)
        sys.exit(0 if ok else 1)
    elif cmd == "slice":
        for n in names:
            slice_one(n)
    else:
        print(__doc__)
        sys.exit(2)
