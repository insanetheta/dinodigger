#!/usr/bin/env python3
"""Concept art for the machine-character roster in docs/backstory.md.

These are CONCEPTS, not game assets. Raw gens land in Tools/raw/mach_*.png and
sliced transparent PNGs land in Assets/Art/Concepts/machines/ — deliberately NOT
under Assets/Art/Generated/ so the sprite importer/slicer manifests ignore them.

Roster (backstory.md "Machine character roster"), plus the Dino-Matic 3000 as the
family centerpiece so the lineup reads complete:
    mach_sprinkles   Sprinkles the watering bot   — mid-sprinkle over one sprout
    mach_tuggy       Tuggy the tugboat            — towing a lily pad with a duck
    mach_glow        Glow the lantern bot         — belly lamp lighting a buried find
    mach_zippy       Zippy the parcel drone       — hovering, basket + hard hat
    mach_doodle      Doodle the music-box bot     — mid-crank, notes floating
    mach_dinomatic   Dino-Matic 3000              — dome with a baby dino inside

These are CHARACTERS (like the backhoe), so they use generate_sprites.STYLE (big
cute expressive eyes, chunky outlines, toy-box) — NOT the faceless PART_STYLE /
PROP_STYLE used for buildings and props.

PROMPT SCARS (inherited + newly earned — do not "simplify" these clauses away):
  * INHERITED from generate_sprites (DinoDigger-bw4): pose wording must be
    SCREEN-RELATIVE, never character-relative. "we see its right side" gets read
    as the character's ANATOMICAL right and silently mirrors the pose. Every
    facing here is phrased as "its face/front points toward the LOWER-RIGHT of
    the frame", plus the NO_MIRROR clause. The whole family shares ONE facing so
    the lineup sheet reads as a single turnaround row.
  * INHERITED from generate_dig_props: a lone small object described as "a small
    soft puff of dust" made the model invent an ELEPHANT to puff it. Anything
    these machines EMIT (water arc, heart puffs, light rays, music notes) is
    therefore pinned with an explicit "emitted by this machine, there is no other
    creature or object anywhere" clause.
  * INHERITED from generate_dig_props: rings of shapes and stray pit marks read
    as FACES/MOUTHS on objects that should not have them. The one prop each
    machine interacts with (sprout, lily pad, hard hat, bone) is explicitly
    declared faceless — the MACHINE is the only thing with eyes.
  * NEW — "post-human" wording is poison. Any hint of "abandoned", "ancient",
    "left behind", "rusty", "weathered" drags the model toward decay palettes and
    sad droopy faces, which violates the backstory's "no extinction imagery" rule.
    The lore beat is carried ONLY by ONE_MOSS: a single tiny moss tuft + leaf
    sprout on an otherwise bright, clean, intact machine. Never name the apocalypse.
  * NEW — "glowing in a dark frame" is unbuildable against a mandatory flat
    magenta key. Glow's light is instead sold intrinsically: a blazing warm lens,
    a soft yellow glow ring, chunky light rays, and warm light spilling onto its
    own legs — with an explicit "background stays bright flat magenta, NOT dark".
  * NEW — music notes are shapes, not letters, but STYLE's blanket "no text, no
    letters, no numbers" makes the model drop them. Doodle's prompt carves out an
    explicit exception ("simple music-note shapes are allowed; they are not text").
  * NEW — vehicles summon their habitat. A tugboat prompt without a guard draws
    water/waves, which the chroma key cannot cut and the trim box cannot survive.
    Tuggy and Zippy both carry an explicit "no water, no waves, no ground, no
    scenery" clause; the boat and the lily pad simply float on the magenta.

Usage:
    python3 generate_machine_concepts.py list
    python3 generate_machine_concepts.py gen [<name>...]     # (re)generate raw
    python3 generate_machine_concepts.py slice [<name>...]   # raw -> Concepts/
    python3 generate_machine_concepts.py sheet               # lineup sheet
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
OUT = os.path.join(REPO, "Assets", "Art", "Concepts", "machines")
BACKHOE_ANCHOR = os.path.join(REPO, "Assets", "Art", "Generated", "backhoe",
                              "backhoe_SE.png")

# --- Shared clauses ---------------------------------------------------------------

# The whole family faces the frame's LOWER-RIGHT (same convention as the existing
# SE facings), phrased screen-relative per the bw4 scar.
FACING = (
    "Draw it in a front three-quarter view, rotated so the machine's face and the "
    "front of its body point toward the LOWER-RIGHT of the frame (the viewer's "
    "right): we clearly see its whole face and its front, turned about 45 degrees "
    "to the frame's right, with the front of the machine on the RIGHT-hand side of "
    "the image. Do NOT mirror or horizontally flip it; keep the turn toward the "
    "RIGHT side of the frame exactly as described. "
    # NEW scar (Sprinkles v1): "three-quarter" alone collapses to a flat side
    # profile on wheeled machines, because a vehicle's most iconic view is its
    # side. Naming what must be visible (front face AND one flank) holds the angle
    # and keeps the whole family on one consistent facing for the lineup sheet.
    "We must see BOTH its front face AND one side flank of its body at the same "
    "time: this is a turned three-quarter view, NOT a flat side profile and NOT a "
    "straight-on front view. "
)

# Family resemblance: the backhoe's design language, stated as a rule.
FAMILY = (
    "It is a friendly machine CHARACTER from the same toy-box family as a cartoon "
    "yellow backhoe digger with eyes on its cab window: it must still clearly look "
    "like the REAL machine it is — a recognizable real-world machine silhouette "
    "first, a character second. Give it a simple happy face with two big cute "
    "expressive eyes and a small friendly smile set into its machine body (like "
    "headlights or a window becoming eyes). Rounded chunky sturdy shapes, thick "
    "bold black outlines all the way around, bright saturated toy colors, soft cel "
    "shading. It is cheerful and safe: NO sharp teeth, NO red or glowing angry "
    "eyes, NO angry eyebrows, NO menacing or scary look, NO sad or droopy "
    "expression, not damaged, not broken. "
    # NEW scar (Tuggy v1): a machine with several round windows/panels gets its
    # features SCATTERED — v1 put the eyes in the wheelhouse window and a separate
    # smile down on the hull, reading as two faces stacked on one boat.
    "It has exactly ONE face, and the two eyes and the one smile sit together on "
    "the SAME single panel, close to each other like a real face: do NOT draw a "
    "second face, and do NOT draw extra eyes, a mouth or a smile anywhere else on "
    "the body. "
    # NEW scar (Zippy v1): translucent "motion blur" parts survive the chroma key as
    # PINK GHOSTS — keying can only feather alpha, so the magenta bleeds through any
    # semi-transparent art. Everything must be opaque and outlined.
    "Every part of the machine is FULLY OPAQUE and enclosed by a bold dark outline: "
    "no see-through parts, no translucent parts, no ghosting, no motion blur, no "
    "faded or soft edges, no swirl arcs, no speed rings. (A glass dome or a lit "
    "lens, where the machine is described as having one, is still fine — draw it as "
    "solid painted glass with a bold outline, not as see-through transparency.) "
)

# The lore beat, carried by ONE accent only. Never name the apocalypse (NEW scar).
ONE_MOSS = (
    "As the ONLY weathering detail, add exactly ONE small soft tuft of bright green "
    "moss with a single tiny green leaf sprout growing on one corner of its chassis "
    "— one small cheerful accent, nothing more. The machine itself is bright, "
    "clean, freshly painted, intact and in perfect happy condition: NO rust, NO "
    "dirt, NO grime, NO cracks, NO dents, NO peeling paint, NO decay, NO vines "
    "covering it, NOT old-looking, NOT abandoned-looking. "
    # NEW scar (Tuggy/Doodle v1): "one tuft" without a count-lock gets sprinkled —
    # v1 grew moss on the roof AND the deck AND an extra loose sprout on the flank.
    "That is ONE moss tuft in ONE place only: no second tuft, no other moss, no "
    "other leaves or sprouts anywhere else on the machine. "
)

# One-subject pin. Adapted from PROP_STYLE, widened to allow the single small prop
# that makes each machine's job readable, and nothing else.
def only(extra: str) -> str:
    return (
        "EXACTLY ONE machine character in the frame, centered and drawn large so it "
        f"fills most of the image, together with {extra} and nothing else at all: no "
        "second machine, no duplicates, no smaller copies, no extra props, no tools, "
        "no crates, no boxes, no plants, no flowers, no rocks, no pebbles, no "
        "sparkles other than those described, no ground, no floor line, no horizon, "
        "no grass, no water, no waves, no sky, no scenery, no background objects of "
        "any kind. "
    )

FACELESS_PROP = ("It has NO eyes, no face and no mouth — the machine is the only "
                 "thing in the picture with a face. ")


def prompt(subject: str, extra_allowed: str) -> str:
    return (f"Generate an image. {G.STYLE}{only(extra_allowed)}{FAMILY}{FACING}"
            f"{ONE_MOSS}The machine: {subject} "
            f"Solid flat pure magenta #FF00FF background, filling every part of the "
            f"image that is not the machine.")


# --- Roster -----------------------------------------------------------------------
SPECS = {
    "mach_sprinkles": dict(
        out="sprinkles_watering_bot",
        # Function pose: mid-sprinkle. Emission pinned (elephant scar).
        prompt=prompt(
            subject=(
                "a squat little garden WATERING BOT — a rounded mint-green and "
                "sky-blue sprinkler body shaped like a fat watering can, riding on "
                "two chunky little rubber wheels, with a big cute happy face on the "
                "front of its tank and a long upturned watering-can spout with a "
                "round sprinkler rose sticking out above the face like a nose. It is "
                "CAUGHT IN THE ACT OF WATERING: a single clean arc of sparkling "
                "blue water sprays out of its spout toward the lower right and "
                "showers down onto ONE tiny green seedling sprout with two little "
                "round leaves standing on the lower-right side of the image, just in "
                "front of the bot. The sprout is a plain little seedling. "
                # NEW scar (Sprinkles v2): an emitted stream drawn "toward" a target
                # lands NEXT TO it — the model treats spray and sprout as two
                # unrelated objects. The contact has to be stated as the subject.
                "The water must clearly LAND ON the sprout and soak it: the falling "
                "end of the arc comes down directly on top of the little sprout, "
                "splashing over its two leaves, with the sprout sitting at the very "
                "end of the arc — not beside it, not behind it, and the water must "
                "not fall onto empty background next to the sprout. "
                f"{FACELESS_PROP}The water arc is a single simple ribbon of blue "
                "droplets sprayed by this bot — nothing else emits or holds it, and "
                "there is no watering can, no hose, no bucket and no creature "
                "anywhere in the picture."),
            extra_allowed=("ONE single arc of sparkling water coming out of its own "
                           "spout and ONE tiny two-leaf green sprout it is watering")),
    ),
    "mach_tuggy": dict(
        out="tuggy_tugboat",
        # Habitat scar: a boat prompt draws an ocean unless forbidden outright.
        prompt=prompt(
            subject=(
                "a small chunky red-and-white TUGBOAT — a fat rounded tugboat hull "
                "with a white wheelhouse cabin, a stubby round smokestack on top, "
                "and a fat black rubber bumper ring around its bow, with its ONE and "
                "ONLY face — two big cute eyes and one small smile, all together — "
                "set into the single big front window of the white wheelhouse cabin. "
                "The red hull below is plain painted metal with NO face, NO eyes and "
                "NO smile on it. It is CAUGHT IN THE ACT OF TOWING: one short thick "
                "rope runs from its bow down toward the lower right to ONE round "
                "green lily pad floating just in front of it, and ONE tiny yellow "
                "cartoon duckling sits happily on that lily pad. Three tiny "
                f"heart-shaped puffs of white smoke rise from its smokestack. The "
                f"lily pad is a plain flat green leaf pad. "
                "IMPORTANT: the boat and the lily pad simply float on the empty flat "
                "magenta background — draw NO water, NO sea, NO river, NO waves, NO "
                "ripples, NO splashes, NO shoreline and NO horizon anywhere."),
            extra_allowed=("ONE short tow rope from its bow, ONE round green lily "
                           "pad, ONE tiny yellow duckling sitting on that lily pad, "
                           "and three tiny heart-shaped smoke puffs from its own "
                           "smokestack")),
    ),
    "mach_glow": dict(
        out="glow_lantern_bot",
        # Dark-frame scar: sell the light intrinsically, keep the key bright.
        prompt=prompt(
            subject=(
                "a round little LANTERN BOT — a fat rounded camping-lantern body in "
                "deep teal metal with a chunky carry handle hoop on top and two "
                "stubby jointed little legs with big round feet, and a big cute "
                "happy face on its upper casing. Its whole round belly is a huge "
                "glass lantern lens that is BLAZING with warm golden-yellow light: "
                "the lens glows brilliant white-hot yellow, a soft round halo of "
                "warm yellow glow surrounds it, several chunky simple yellow light "
                "rays fan out from it toward the lower right, and warm golden light "
                "spills onto its own legs and feet. The light is CLEARLY DOING ITS "
                "JOB: the rays land on ONE small pale dinosaur bone half-buried in a "
                "little mound of brown dirt on the lower-right side of the image, "
                "and that bone is lit up bright and sparkling in the beam. The bone "
                f"and dirt mound are plain objects. {FACELESS_PROP}"
                "IMPORTANT: the picture is NOT dark — the background stays a bright "
                "flat magenta, no darkness, no night, no black vignette, no shadows, "
                "no cave, no gradient."),
            extra_allowed=("the glow, halo and simple light rays coming out of its "
                           "own belly lens, and ONE small pale bone in a small mound "
                           "of dirt that its light is shining on")),
    ),
    "mach_zippy": dict(
        out="zippy_parcel_drone",
        prompt=prompt(
            subject=(
                "a small rounded four-rotor PARCEL DRONE — a fat rounded bee-striped "
                "yellow-and-black body with four stubby arms, each ending in a small "
                "chunky rotor drawn as TWO short solid blade shapes in a flat "
                "pale-grey, each blade fully opaque with a bold dark outline (do NOT "
                "draw the rotors as blurred, spinning, translucent, ghosted or "
                "see-through discs, and draw NO swirl rings around them), and a big "
                "cute happy "
                "face on the front of its body. Slung underneath it on two short "
                "straps hangs ONE small round woven basket, and ONE bright yellow "
                "construction hard hat sits in that basket, clearly being delivered. "
                "It is HOVERING in mid-air, tilted slightly forward toward the lower "
                "right as if flying that way. "
                # NEW scar (Zippy v2): a radially symmetric body (quad-drone) has no
                # silhouette cue for facing, so the model settles it at random and
                # v2 came out turned toward the frame's LEFT. The FACE has to be
                # pinned to a screen side explicitly, per subject, not just globally.
                "Its FACE must be on the RIGHT-hand half of its body and must look "
                "out toward the LOWER-RIGHT corner of the image, with its striped "
                "tail end at the upper LEFT — its nose points right, never left. "
                f"The basket and hard hat are plain "
                f"objects. {FACELESS_PROP}"
                "Draw NO ground, NO landing pad, NO sky, NO clouds, NO motion lines "
                "and NO speed streaks."),
            extra_allowed=("ONE small woven basket hanging under it and ONE yellow "
                           "hard hat sitting in that basket")),
    ),
    "mach_doodle": dict(
        out="doodle_music_box_bot",
        # Text-guard carve-out: notes are shapes, and STYLE otherwise deletes them.
        prompt=prompt(
            subject=(
                "a little wind-up MUSIC BOX BOT — a chunky rounded wooden music box "
                "on a boxy body, painted warm red and cream with a polished brass "
                "trim, rolling on two small chunky wheels, with a big cute happy "
                "face on the front panel of the box, and a big friendly brass "
                "wind-up CRANK handle sticking out of its side. It is CAUGHT IN THE "
                "ACT OF PLAYING: the crank is turned mid-spin, its lid is popped "
                "open a little, and three simple chunky musical notes float up out "
                "of the open lid toward the upper right. "
                "The musical notes are simple solid note SHAPES with round heads and "
                "straight stems — they are decorative shapes, not writing: draw NO "
                "letters, NO numbers and NO words anywhere."),
            extra_allowed=("three simple floating musical-note shapes rising out of "
                           "its own open lid")),
    ),
    "mach_dinomatic": dict(
        out="dinomatic_3000",
        prompt=prompt(
            subject=(
                "the biggest machine of the family, a friendly FOSSIL-REVIVING "
                "MACHINE — a chunky rounded cream-and-teal retro-futuristic machine "
                "cabinet standing on four stubby legs, with rounded corners, big "
                "chunky colorful knobs and round dials across its front, and a big "
                "cute happy face made of two round lamp eyes and a wide smile on its "
                "front panel. On its top sits ONE big clear round glass dome, and "
                "inside that dome stands ONE tiny happy round baby green dinosaur "
                "looking out, freshly made, with a few sparkles around it. On the "
                "lower-right front of the cabinet is one chunky open hopper slot "
                "with ONE pale fossil bone half-slid into it, feeding the machine. "
                "The dome, the bone and the hopper are parts of this one machine. "
                "The tiny baby dinosaur inside the dome is happy and smiling, never "
                "trapped or sad."),
            extra_allowed=("its own glass dome with ONE tiny happy baby dinosaur "
                           "standing inside it, a few sparkles inside the dome, and "
                           "ONE pale fossil bone half-inserted into its own front "
                           "hopper slot")),
    ),
}

ORDER = list(SPECS)

# Relative on-sheet heights (fraction of the tallest). Keeps the lineup readable as
# one world: the Dino-Matic towers, the drone and lantern are pocket-sized.
#
# SHEET SCAR: these are tuned against the trimmed bbox, NOT the machine body, and
# the two differ a lot — Doodle's music notes and Tuggy's heart puffs float well
# above their chassis, so a naive equal-bbox scale shrinks those two machines to
# toys next to the others. Each value below is pre-inflated by roughly the share of
# its bbox that is emitted stuff rather than machine. Re-tune if a prompt changes
# how far the emissions reach.
SHEET_SCALE = {
    "backhoe": 0.72,
    "mach_sprinkles": 0.50,
    "mach_tuggy": 0.62,        # top ~18% of bbox is heart puffs
    "mach_glow": 0.44,
    "mach_zippy": 0.46,
    "mach_doodle": 0.68,       # top ~30% of bbox is floating notes
    "mach_dinomatic": 1.00,
}
# Machines that fly sit this fraction of their own height above the ground line.
HOVER = {"mach_zippy": 0.10}
SHEET_ORDER = ["backhoe", "mach_sprinkles", "mach_tuggy", "mach_glow",
               "mach_zippy", "mach_doodle", "mach_dinomatic"]


def neutralize(img: Image.Image) -> Image.Image:
    """Kill the leftover magenta hiding in fully-transparent pixels.

    Same helper as generate_dig_props.neutralize: the chroma key only zeroes ALPHA,
    so the RGB under it stays magenta and bleeds back as a pink halo under bilinear
    filtering. Replace transparent RGB with the sprite's own median opaque color."""
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
    b64 = G._attempt(spec["prompt"], None, name, 2)
    if not b64:
        print(f"FAILED {name}")
        return False
    G._save_raw(b64, os.path.join(RAW, f"{name}.png"))
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
    img = S.clear_magenta_pockets(img)
    img = S.trim(img, pad)
    img = neutralize(img)
    os.makedirs(OUT, exist_ok=True)
    out = os.path.join(OUT, f"{spec['out']}.png")
    img.save(out)
    print(f"       {out}  ({img.width}x{img.height})")
    return out


def sheet(path: str | None = None) -> str:
    """One name-free lineup sheet on white, all machines at consistent scale, with
    the shipped backhoe sprite at far left as the family anchor."""
    imgs = []
    for key in SHEET_ORDER:
        src = (BACKHOE_ANCHOR if key == "backhoe"
               else os.path.join(OUT, f"{SPECS[key]['out']}.png"))
        if not os.path.exists(src):
            print(f"[warn] missing {src}", file=sys.stderr)
            continue
        im = Image.open(src).convert("RGBA")
        im = S.trim(im, 0)
        imgs.append((key, im))

    tall = 520                      # px height of the tallest (Dino-Matic)
    gap, margin, base_pad = 46, 60, 40
    scaled = []
    for key, im in imgs:
        h = max(1, int(tall * SHEET_SCALE[key]))
        w = max(1, int(im.width * h / im.height))
        scaled.append((key, im.resize((w, h), Image.LANCZOS)))

    W = margin * 2 + sum(i.width for _, i in scaled) + gap * (len(scaled) - 1)
    H = margin * 2 + tall + base_pad
    canvas = Image.new("RGBA", (W, H), (255, 255, 255, 255))
    baseline = margin + tall            # feet line, everyone stands on it
    x = margin
    for key, im in scaled:
        lift = int(im.height * HOVER.get(key, 0.0))
        canvas.alpha_composite(im, (x, baseline - im.height - lift))
        x += im.width + gap

    out = path or os.path.join(OUT, "machine_lineup.png")
    canvas.convert("RGB").save(out)
    print(f"       sheet {out}  ({canvas.width}x{canvas.height})")
    return out


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "list"
    names = sys.argv[2:] or ORDER
    if cmd == "list":
        for n in ORDER:
            print(n, "->", SPECS[n]["out"])
    elif cmd == "gen":
        ok = all(gen(n) for n in names)
        sys.exit(0 if ok else 1)
    elif cmd == "slice":
        for n in names:
            slice_one(n)
    elif cmd == "sheet":
        sheet()
    else:
        print(__doc__)
        sys.exit(2)
