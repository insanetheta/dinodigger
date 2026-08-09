#!/usr/bin/env python3
"""Dig-arm V2 art (ticket DinoDigger-rrn): a proportionate excavator arm.

Standalone companion to generate_sprites.py (same pattern as
generate_dig_props.py): it never edits that module, only borrows the pure
helpers — the OpenRouter request/save path from generate_sprites and
chroma_key/despeckle/trim from slice_sprites — so keying matches every other
sprite in the game.

WHY A V2 (Greg, DinoDigger-rrn): the V1 arm reads GIANT next to the vehicle.
Measured from the shipped art: the V1 boom is drawn 1.80 world units deep over
its 3.4-unit pin span (1:1.9 — a slab), and its base pin HOLE alone is 1.37
units wide — wider than a whole dirt tile — while the entire vehicle body is
only 2.4 units tall. V2 targets a 1:5–6 boom, 1:6–7 stick, pin bosses under
~0.5 units, and matching boss diameters at the elbow so the joint reads as one
clean knuckle. Same rig skeleton (bone lengths / IK / limits unchanged); this
is a proportion + quality pass on the ART only.

V1 art in Assets/Art/Generated/digarm/ is NOT touched. V2 slices land in
Assets/Art/Generated/digarm2/ plus a pins.json holding the measured pin
centroids (normalized, bottom-left origin, matching AssignSegmentPins) and the
pin-to-pin pixel distances the importer needs.

Usage:
    python3 generate_digarm2.py list
    python3 generate_digarm2.py gen [<name>...]      # (re)generate raw
    python3 generate_digarm2.py slice [<name>...]    # raw -> Generated/digarm2/
    python3 generate_digarm2.py measure              # pin centroids -> pins.json + C# consts
"""
import json
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
OUT = os.path.join(REPO, "Assets", "Art", "Generated", "digarm2")

# PART_STYLE (no faces on parts) + the hard slenderness contract. The V1 prompts
# already asked for "6 times as long as thick" and the model shipped 1:1.9, so
# V2 spells the proportion out against the IMAGE dimensions (the one frame of
# reference the model respects) and pins the boss sizes to the strip depth.
SLENDER = (
    "The part is VERY SLENDER: a long thin strip spanning the ENTIRE image "
    "width from the far left edge to the far right edge, but its yellow body "
    "is only about ONE SIXTH of the image height deep. Most of the image "
    "above and below the part is empty flat magenta. It must look like a "
    "long thin crane arm strip, NEVER a fat slab. "
)

BOSS = (
    "It has EXACTLY ONE round pivot pin boss at each end: a perfect circle, "
    "never oval, only a LITTLE larger than the strip's own depth (about 1.3 "
    "times the strip depth in diameter, no bigger), with a small dark round "
    "pin hole exactly in its center. BOTH bosses are exactly the SAME size. "
    "Everything between the two bosses is smooth plain bright yellow with one "
    "soft cel-shading highlight and NO other details: no hydraulic hoses, no "
    "cylinders, no pistons, no rivets, no bolts, no panel lines, no vents. "
    "One thick uniform dark outline around the whole silhouette. "
)

# The stick's first pass came back orange-bossed with a navy outline — pin the
# palette explicitly to the boom's (bright yellow, black outline, charcoal holes).
COLORS = (
    "COLOR: the whole part, INCLUDING both pin bosses, is the same bright "
    "construction-digger YELLOW with soft darker yellow-orange cel shading "
    "only. The bosses are NEVER orange, NEVER grey, never any other color "
    "than the strip itself. The outline is pure black; the pin holes are "
    "dark charcoal, nearly black. "
)

SPECS = {
    "digarm2_boom": dict(
        prompt=(f"Generate an image. {G.PART_STYLE}"
                "a side profile of a cartoon excavator BOOM arm segment for a "
                "cute toy digger, lying horizontally. It is one gently CURVED "
                "slender strip like a shallow banana: it rises softly in the "
                "middle and settles back down, the hump rising only a LITTLE "
                "above the straight line between its two ends — a gentle arc, "
                "not a steep gooseneck. Slightly deeper at its left mounting "
                f"end, tapering a little toward the right tip end. {SLENDER}"
                f"{BOSS}"
                "Solid flat magenta #FF00FF background."),
        out="digarm2_boom"),
    "digarm2_stick": dict(
        prompt=(f"Generate an image. {G.PART_STYLE}"
                "a side profile of a cartoon excavator DIPPER STICK arm "
                "segment for a cute toy digger, lying horizontally. It is one "
                "nearly STRAIGHT slim box-section bar, slightly deeper at its "
                "left pivot end and tapering a little toward its right bucket "
                f"end. {SLENDER}{BOSS}{COLORS}"
                "Solid flat magenta #FF00FF background."),
        out="digarm2_stick"),
    "digarm2_bucket": dict(
        prompt=(f"Generate an image. {G.PART_STYLE}"
                "a side profile of a cartoon excavator digging BUCKET scoop "
                "for a cute toy digger, drawn LARGE and centered, filling most "
                "of the frame. A chunky curved metal scoop seen from the side, "
                "bright yellow with a deeper yellow-orange inside, its OPENING "
                "facing LEFT, with exactly 3 broad blunt rounded teeth along "
                "its lower-left cutting edge (short and friendly, never sharp "
                "or spiky). At its TOP-LEFT corner sits one round hinge lug: a "
                "perfect small circle with a small dark round pin hole exactly "
                "in its center. The bucket is a single solid object with one "
                "thick uniform dark outline; simple smooth surfaces, no rivets, "
                "no bolts, no panel lines, no hydraulic parts. "
                "Solid flat magenta #FF00FF background."),
        # The model reliably mirrors the requested opening direction, so the
        # slice step flips it: shipped art opens LEFT with the hinge lug at its
        # top-RIGHT (the wrist socket side, facing the stick), like V1's.
        out="digarm2_bucket", flip=True),
}

ORDER = list(SPECS)


def neutralize(img: Image.Image) -> Image.Image:
    """Kill the leftover magenta hiding in fully-transparent pixels (same fix
    as generate_dig_props.neutralize: bilinear filtering bleeds the RGB that
    hides under alpha 0, so give it the sprite's own median tone instead)."""
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
    if spec.get("flip"):
        img = img.transpose(Image.FLIP_LEFT_RIGHT)
    os.makedirs(OUT, exist_ok=True)
    out = os.path.join(OUT, f"{spec['out']}.png")
    img.save(out)
    print(f"       {out}  ({img.width}x{img.height})")
    return out


# ---- pin measurement ---------------------------------------------------------
def _round_blobs(a: np.ndarray):
    """All compact round dark blobs in the sprite: connected components of the
    dark mask (via slice_sprites._label4) filtered to disc-like shapes — the
    pin HOLES, not the outline (thin, low fill) or the shading."""
    vis = a[..., 3] > 128
    rgb = a[..., :3].astype(int)
    dark = vis & (rgb.max(axis=2) < 100)
    labels, count = S._label4(dark)
    blobs = []
    for i in range(1, count + 1):
        ys, xs = np.nonzero(labels == i)
        if len(xs) < 60:
            continue
        bw = xs.max() - xs.min() + 1
        bh = ys.max() - ys.min() + 1
        if bw < 12 or bh < 12:
            continue
        aspect = bw / bh if bw > bh else bh / bw
        fill = len(xs) / (bw * bh)
        # a pin hole is either a filled dark disc (fill ~0.785) or a dark RING
        # around a lighter center (fill ~0.3-0.6); either way it is compact and
        # near-square. The silhouette outline is one giant stringy component
        # (huge bbox, tiny fill) and fails both gates. Center = bbox center,
        # which is exact for rings and discs alike.
        if aspect < 1.6 and fill > 0.25:
            blobs.append(((xs.min() + xs.max()) / 2.0,
                          (ys.min() + ys.max()) / 2.0, float(max(bw, bh))))
    return blobs


def _dark_centroid(a: np.ndarray, x0: float, x1: float, label: str):
    """The round pin-hole blob whose center falls inside the horizontal band
    [x0,x1) (fractions of width). Returns (cx, cy) px (top-left origin) and
    the blob diameter, or None."""
    h, w = a.shape[:2]
    cands = [b for b in _round_blobs(a) if x0 * w <= b[0] < x1 * w]
    if not cands:
        print(f"   !! no round pin hole found in {label} band {x0}-{x1}")
        return None
    # largest disc in the band is the pin hole
    return max(cands, key=lambda b: b[2])


def measure() -> None:
    """Measure pin centroids from the sliced V2 art and write pins.json (used
    by the offline pose composer) + print the C# constants for
    GeneratedArtImporter / DigArmV2."""
    res = {}

    for part, key in (("digarm2_boom", "boom"), ("digarm2_stick", "stick")):
        p = os.path.join(OUT, f"{part}.png")
        a = np.asarray(Image.open(p).convert("RGBA"))
        h, w = a.shape[:2]
        base = _dark_centroid(a, 0.0, 0.30, f"{part} base")
        tip = _dark_centroid(a, 0.70, 1.0, f"{part} tip")
        if base is None or tip is None:
            continue
        bx, by, bext = base
        tx, ty, text_ = tip
        dist = ((tx - bx) ** 2 + (ty - by) ** 2) ** 0.5
        # normalized, BOTTOM-left origin (AssignSegmentPins convention)
        res[f"{key}_base"] = [round(bx / w, 4), round(1 - by / h, 4)]
        res[f"{key}_tip"] = [round(tx / w, 4), round(1 - ty / h, 4)]
        res[f"{key}_pin_dist_px"] = round(dist, 1)
        res[f"{key}_size"] = [w, h]
        res[f"{key}_hole_px"] = [round(bext, 1), round(text_, 1)]
        print(f"{part}: {w}x{h}  base=({res[f'{key}_base'][0]},"
              f"{res[f'{key}_base'][1]}) tip=({res[f'{key}_tip'][0]},"
              f"{res[f'{key}_tip'][1]})  pinDist={dist:.1f}px  "
              f"holes {bext:.0f}/{text_:.0f}px")

    p = os.path.join(OUT, "digarm2_bucket.png")
    a = np.asarray(Image.open(p).convert("RGBA"))
    h, w = a.shape[:2]
    # hinge lug: the round hole in the top-RIGHT quadrant (shipped art is
    # flipped at slice time, so the lug faces the stick)
    lugs = [b for b in _round_blobs(a)
            if b[1] < 0.5 * h and b[0] > 0.45 * w]
    if lugs:
        cx, cy, ext = max(lugs, key=lambda b: b[2])
        res["bucket_pivot"] = [round(cx / w, 4), round(1 - cy / h, 4)]
        res["bucket_size"] = [w, h]
        print(f"digarm2_bucket: {w}x{h}  hinge=({res['bucket_pivot'][0]},"
              f"{res['bucket_pivot'][1]})  hole {ext:.0f}px")
    else:
        print("   !! no bucket hinge hole found")

    # design constant, not a measurement: V2 renders the bucket 1.0 units tall
    # (vs V1's 0.72) so the business end reads as the star of the arm — on a
    # toy digger the bucket is the biggest mass after the body, never smaller
    # than a joint knuckle. Kept beside the measured pins so the offline pose
    # composer and the C# importer share one source.
    res["bucket_h"] = 1.0
    with open(os.path.join(OUT, "pins.json"), "w") as f:
        json.dump(res, f, indent=2)
    print("wrote", os.path.join(OUT, "pins.json"))

    if "boom_base" in res and "stick_base" in res:
        print("\n// C# constants (GeneratedArtImporter + DigArmV2):")
        print(f"BoomPinDistPx  = {res['boom_pin_dist_px']}f;")
        print(f"StickPinDistPx = {res['stick_pin_dist_px']}f;")
        print(f"BoomBasePin  = new Vector2({res['boom_base'][0]}f, {res['boom_base'][1]}f);")
        print(f"BoomTipPin   = new Vector2({res['boom_tip'][0]}f, {res['boom_tip'][1]}f);")
        print(f"StickBasePin = new Vector2({res['stick_base'][0]}f, {res['stick_base'][1]}f);")
        print(f"StickTipPin  = new Vector2({res['stick_tip'][0]}f, {res['stick_tip'][1]}f);")
        if "bucket_pivot" in res:
            print(f"BucketPivot  = new Vector2({res['bucket_pivot'][0]}f, {res['bucket_pivot'][1]}f);")


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
    elif cmd == "measure":
        measure()
    else:
        print(__doc__)
        sys.exit(2)
