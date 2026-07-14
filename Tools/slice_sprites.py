#!/usr/bin/env python3
"""Slice raw Dino Digger art into game-ready transparent PNGs.

Reads raw generations from Tools/raw/ (produced by generate_sprites.py) and writes
trimmed, transparent PNGs under Assets/Art/Generated/<group>/.

Two kinds of input:
  * Turnaround characters -> per-facing files raw/<char>_S.png, _SE.png, _E.png,
    _NE.png, _N.png. Each is chroma-keyed + trimmed and saved as <char>_<DIR>.png.
    The three left-side directions (SW, W, NW) are produced by horizontally MIRRORING
    the right-side facings (SE, E, NE), giving a full 8-direction isometric set with
    front == S (facing camera-south).
  * Item sheets -> a single grid image raw/<name>.png that is split into cells
    (2x2 or 1x3) and each cell chroma-keyed + trimmed (egg_green.png, fruit_apple.png,
    treasure_coin.png, dirt_crack_0.png, particle_star.png, ...).

The background the model paints is a magenta/pink that varies per image, so the
background color is AUTO-DETECTED from each image's border rather than hardcoded to
#FF00FF, then keyed out with a tolerance band and feathered (anti-aliased) edges.

Usage:
    python3 slice_sprites.py                 # slice everything present in raw/
    python3 slice_sprites.py --only trex      # slice one entry
    python3 slice_sprites.py --pad 12         # override border padding (px)
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from generate_sprites import (  # noqa: E402
    SPRITES, SPRITES_BY_NAME, RAW_DIR, GEN_DIRS, MIRROR, STAGES, STRIDES,
    ROLL_POSES, TOWN_STATES)

TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
GENERATED_DIR = os.path.normpath(
    os.path.join(TOOLS_DIR, "..", "Assets", "Art", "Generated"))

# The model paints a flat magenta/pink background that, being close to reds/pinks in
# RGB, cannot be separated from reddish objects (apple, watermelon, heart) by color
# distance alone -- and it shares its hue with the purple characters, so it can't be
# keyed by hue either. Instead we FLOOD-FILL the background: only pixels that are
# background-colored AND connected to the image border are removed, so any enclosed
# interior (kept inside the sprite's thick dark outline) survives regardless of color.
# The connected ground-shadow the model sometimes adds is removed too, since it is
# background-hued and touches the surrounding background.
# A pixel is keyed out if it is EITHER background-connected (flood, catches the
# outer background + connected ground-shadow + feather band) OR near-identical to the
# bg color (catches enclosed true-magenta holes, e.g. the gap between the backhoe's
# arm and cab, which the flood can't reach). Merely-reddish enclosed interiors (heart
# ~83, watermelon ~58, apple ~53 units from bg) sit above T_STRICT and are preserved.
T_FLOOD = 95.0   # RGB distance from bg still "background" when border-connected
T_STRICT = 45.0  # RGB distance below which a pixel is bg even if enclosed


def detect_bg(rgb: np.ndarray) -> np.ndarray:
    """Median color of an image's outer border ring (the flat background)."""
    b = 6
    ring = np.concatenate([
        rgb[:b, :, :].reshape(-1, 3), rgb[-b:, :, :].reshape(-1, 3),
        rgb[:, :b, :].reshape(-1, 3), rgb[:, -b:, :].reshape(-1, 3),
    ])
    return np.median(ring, axis=0)


def _flood_from_border(similar: np.ndarray) -> np.ndarray:
    """Morphological reconstruction: bg-similar pixels reachable from the border."""
    marker = np.zeros_like(similar)
    marker[0, :] |= similar[0, :]
    marker[-1, :] |= similar[-1, :]
    marker[:, 0] |= similar[:, 0]
    marker[:, -1] |= similar[:, -1]
    while True:
        nb = marker.copy()
        nb[1:, :] |= marker[:-1, :]
        nb[:-1, :] |= marker[1:, :]
        nb[:, 1:] |= marker[:, :-1]
        nb[:, :-1] |= marker[:, 1:]
        nb &= similar
        if np.array_equal(nb, marker):
            return marker
        marker = nb


def _blur3(a: np.ndarray) -> np.ndarray:
    """3x3 box blur (1px edge feather)."""
    p = np.pad(a, 1, mode="edge")
    return (p[:-2, :-2] + p[:-2, 1:-1] + p[:-2, 2:] +
            p[1:-1, :-2] + p[1:-1, 1:-1] + p[1:-1, 2:] +
            p[2:, :-2] + p[2:, 1:-1] + p[2:, 2:]) / 9.0


def chroma_key(cell: Image.Image) -> Image.Image:
    """Return an RGBA copy with the flood-filled background keyed out + feathered."""
    rgb = np.asarray(cell.convert("RGB"), dtype=np.float32)
    bg = detect_bg(rgb)
    dist = np.sqrt(((rgb - bg) ** 2).sum(axis=2))
    bg_pixel = _flood_from_border(dist < T_FLOOD) | (dist < T_STRICT)

    # Binary sprite mask (opaque interior, transparent background) then feather the
    # 1px boundary so edges are anti-aliased without touching interior opacity.
    alpha = _blur3((~bg_pixel).astype(np.float32))

    # Light spill suppression on the feathered rim: de-tint any lingering bg-hued
    # pixels (r>g,b>g like the pink bg) weighted by transparency. Sprite interiors
    # are opaque (weight 0) so purple/red bodies are untouched.
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    bg_ish = (r > g) & (b > g) & (bg[0] > bg[1]) & (bg[2] > bg[1])
    detinted = np.dstack([np.minimum(r, np.maximum(g, b)), g,
                          np.minimum(b, np.maximum(g, r))])
    w = ((1.0 - alpha) * bg_ish)[..., None]
    rgb2 = rgb * (1 - w) + detinted * w

    out = np.dstack([rgb2, alpha * 255.0]).clip(0, 255).astype(np.uint8)
    return Image.fromarray(out, mode="RGBA")


def trim(img: Image.Image, pad: int, alpha_floor: int = 16) -> Image.Image:
    """Crop to visible (alpha>floor) bounds plus `pad` px of transparency."""
    a = np.asarray(img)[..., 3]
    ys, xs = np.where(a > alpha_floor)
    if len(xs) == 0:
        return img
    x0, y0 = max(0, xs.min() - pad), max(0, ys.min() - pad)
    x1, y1 = min(img.width, xs.max() + 1 + pad), min(img.height, ys.max() + 1 + pad)
    return img.crop((int(x0), int(y0), int(x1), int(y1)))


def alpha_bbox(img: Image.Image, floor: int = 16):
    """(x0, y0, x1, y1) of the visible (alpha>floor) pixels, or None if empty."""
    a = np.asarray(img)[..., 3]
    ys, xs = np.where(a > floor)
    if len(xs) == 0:
        return None
    return (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)


def crop_to_box(img: Image.Image, box, pad: int) -> Image.Image:
    """Crop to `box` padded by `pad`, clamped to the image. Applying the SAME box to
    every frame keeps them pixel-identical in size and aligned in position."""
    x0 = max(0, box[0] - pad)
    y0 = max(0, box[1] - pad)
    x1 = min(img.width, box[2] + pad)
    y1 = min(img.height, box[3] + pad)
    return img.crop((x0, y0, x1, y1))


def slice_strides(spec: dict, pad: int) -> list[str]:
    """Slice the mid-stride walk frames for a dino (y85.1).

    TRIM ALIGNMENT (critical): the idle frame and its two stride frames must share
    ONE canvas so the dino's body doesn't hop when the runtime swaps frames. For
    each canonical facing we chroma-key all three raws, take the UNION of their
    alpha bounding boxes, and crop all three to that identical (padded) box in raw
    pixel space. Because the model kept the character in the same on-canvas position
    and scale, cropping by a shared raw box keeps the head/body pixel-aligned and the
    feet baseline fixed — only the legs differ. The idle frame is re-sliced to the
    same box (overwriting <base>_<DIR>.png) so all three frames match exactly.

    Left-side facings (SW, W, NW) are the right-side frames mirrored, like the base
    turnaround. Output names: adult -> walkA_<DIR>.png / walkB_<DIR>.png and the
    re-aligned idle <base>_<DIR>.png; a growth stage -> <stage>_walkA_<DIR>.png etc."""
    base, outdir = spec["base"], os.path.join(GENERATED_DIR, spec["outdir"])
    os.makedirs(outdir, exist_ok=True)
    stride_stages = spec.get("stride_stages", [None])
    written = []

    for stage in stride_stages:
        def idle_raw(d):
            return (os.path.join(RAW_DIR, f"{base}_{d}.png") if stage is None
                    else os.path.join(RAW_DIR, f"{base}_{stage}_{d}.png"))

        def stride_raw(pose, d):
            tag = pose if stage is None else f"{stage}_{pose}"
            return os.path.join(RAW_DIR, f"{base}_{tag}_{d}.png")

        def idle_out(d):
            return os.path.join(outdir, (f"{base}_{d}.png" if stage is None
                                         else f"{stage}_{d}.png"))

        def stride_out(pose, d):
            name = pose if stage is None else f"{stage}_{pose}"
            return os.path.join(outdir, f"{name}_{d}.png")

        # cropped keyed frames per canonical dir, for mirroring: d -> {"idle","walkA","walkB"}
        frames_by_dir = {}
        for d in GEN_DIRS:
            paths = {"idle": idle_raw(d),
                     "walkA": stride_raw("walkA", d),
                     "walkB": stride_raw("walkB", d)}
            missing = [k for k, p in paths.items() if not os.path.exists(p)]
            if missing:
                print(f"       SKIP {base} {stage or 'adult'} {d}: missing raw "
                      f"{missing}", file=sys.stderr)
                continue

            keyed = {k: chroma_key(Image.open(p)) for k, p in paths.items()}
            boxes = [alpha_bbox(img) for img in keyed.values()]
            boxes = [b for b in boxes if b is not None]
            if not boxes:
                print(f"       SKIP {base} {stage or 'adult'} {d}: empty alpha",
                      file=sys.stderr)
                continue
            union = (min(b[0] for b in boxes), min(b[1] for b in boxes),
                     max(b[2] for b in boxes), max(b[3] for b in boxes))

            cropped = {k: crop_to_box(img, union, pad) for k, img in keyed.items()}
            frames_by_dir[d] = cropped

            cropped["idle"].save(idle_out(d))
            cropped["walkA"].save(stride_out("walkA", d))
            cropped["walkB"].save(stride_out("walkB", d))
            w, h = cropped["idle"].width, cropped["idle"].height
            written += [idle_out(d), stride_out("walkA", d), stride_out("walkB", d)]
            print(f"       {base} {stage or 'adult'} {d}: idle+walkA+walkB "
                  f"aligned to {w}x{h}")

        # Mirror the right-side facings into the left-side ones (all 3 frames).
        for target, source in MIRROR.items():
            if source not in frames_by_dir:
                print(f"       cannot mirror {stage or 'adult'} {target} "
                      f"(missing {source})", file=sys.stderr)
                continue
            src = frames_by_dir[source]
            idle_f = src["idle"].transpose(Image.FLIP_LEFT_RIGHT)
            a_f = src["walkA"].transpose(Image.FLIP_LEFT_RIGHT)
            b_f = src["walkB"].transpose(Image.FLIP_LEFT_RIGHT)
            idle_f.save(idle_out(target))
            a_f.save(stride_out("walkA", target))
            b_f.save(stride_out("walkB", target))
            written += [idle_out(target), stride_out("walkA", target),
                        stride_out("walkB", target)]
            print(f"       {base} {stage or 'adult'} {target}: mirrored from {source}")

    return written


def slice_roll(spec: dict, pad: int) -> list[str]:
    """Slice the wheel-roll frames for the backhoe (DinoDigger-682).

    Same union-box trim alignment the strides use: for each canonical facing we
    chroma-key the idle frame and its two roll frames, take the UNION of their alpha
    bounding boxes, and crop all three to that identical (padded) box so the body
    stays pixel-stationary while the runtime swaps frames. The idle facing
    (<base>_<DIR>.png) is re-sliced to the same box so all three match exactly.
    Left-side facings (SW, W, NW) are the right-side frames mirrored. Output names:
    rollA_<DIR>.png / rollB_<DIR>.png and the re-aligned idle <base>_<DIR>.png."""
    base, outdir = spec["base"], os.path.join(GENERATED_DIR, spec["outdir"])
    os.makedirs(outdir, exist_ok=True)
    written = []

    def idle_raw(d):
        return os.path.join(RAW_DIR, f"{base}_{d}.png")

    def roll_raw(pose, d):
        return os.path.join(RAW_DIR, f"{base}_{pose}_{d}.png")

    def idle_out(d):
        return os.path.join(outdir, f"{base}_{d}.png")

    def roll_out(pose, d):
        return os.path.join(outdir, f"{pose}_{d}.png")

    frames_by_dir = {}
    for d in GEN_DIRS:
        paths = {"idle": idle_raw(d)}
        for pose in ROLL_POSES:
            paths[pose] = roll_raw(pose, d)
        missing = [k for k, p in paths.items() if not os.path.exists(p)]
        if missing:
            print(f"       SKIP {base} roll {d}: missing raw {missing}",
                  file=sys.stderr)
            continue

        keyed = {k: chroma_key(Image.open(p)) for k, p in paths.items()}
        boxes = [b for b in (alpha_bbox(img) for img in keyed.values()) if b is not None]
        if not boxes:
            print(f"       SKIP {base} roll {d}: empty alpha", file=sys.stderr)
            continue
        union = (min(b[0] for b in boxes), min(b[1] for b in boxes),
                 max(b[2] for b in boxes), max(b[3] for b in boxes))

        cropped = {k: crop_to_box(img, union, pad) for k, img in keyed.items()}
        frames_by_dir[d] = cropped

        cropped["idle"].save(idle_out(d))
        written.append(idle_out(d))
        for pose in ROLL_POSES:
            cropped[pose].save(roll_out(pose, d))
            written.append(roll_out(pose, d))
        w, h = cropped["idle"].width, cropped["idle"].height
        print(f"       {base} roll {d}: idle+{'+'.join(ROLL_POSES)} aligned to {w}x{h}")

    # Mirror the right-side facings into the left-side ones (all frames).
    for target, source in MIRROR.items():
        if source not in frames_by_dir:
            print(f"       cannot mirror roll {target} (missing {source})",
                  file=sys.stderr)
            continue
        src = frames_by_dir[source]
        src["idle"].transpose(Image.FLIP_LEFT_RIGHT).save(idle_out(target))
        written.append(idle_out(target))
        for pose in ROLL_POSES:
            src[pose].transpose(Image.FLIP_LEFT_RIGHT).save(roll_out(pose, target))
            written.append(roll_out(pose, target))
        print(f"       {base} roll {target}: mirrored from {source}")

    return written


def slice_turnaround(spec: dict, pad: int) -> list[str]:
    name, outdir = spec["name"], os.path.join(GENERATED_DIR, spec["outdir"])
    os.makedirs(outdir, exist_ok=True)
    written, keyed_by_dir = [], {}

    for d in GEN_DIRS:
        raw = os.path.join(RAW_DIR, f"{name}_{d}.png")
        if not os.path.exists(raw):
            print(f"       MISSING {raw}", file=sys.stderr)
            continue
        keyed = trim(chroma_key(Image.open(raw)), pad)
        keyed_by_dir[d] = keyed
        out = os.path.join(outdir, f"{name}_{d}.png")
        keyed.save(out)
        written.append(out)
        print(f"       {out}  ({keyed.width}x{keyed.height})")

    # Mirror the right-side facings to make the left-side directions.
    for target, source in MIRROR.items():
        if source not in keyed_by_dir:
            print(f"       cannot mirror {target} (missing {source})", file=sys.stderr)
            continue
        flipped = keyed_by_dir[source].transpose(Image.FLIP_LEFT_RIGHT)
        out = os.path.join(outdir, f"{name}_{target}.png")
        flipped.save(out)
        written.append(out)
        print(f"       {out}  (mirrored from {source})")
    return written


def slice_stages(spec: dict, pad: int) -> list[str]:
    """Slice the baby/kid stage facings for a dino. Reads raw/<base>_<stage>_<DIR>.png,
    chroma-keys + trims each, mirrors the right-side facings, and writes
    Generated/<outdir>/<stage>_<DIR>.png (the adult keeps its <base>_<DIR>.png names)."""
    base, outdir = spec["base"], os.path.join(GENERATED_DIR, spec["outdir"])
    os.makedirs(outdir, exist_ok=True)
    written = []

    for stage in STAGES:
        keyed_by_dir = {}
        for d in GEN_DIRS:
            raw = os.path.join(RAW_DIR, f"{base}_{stage}_{d}.png")
            if not os.path.exists(raw):
                print(f"       MISSING {raw}", file=sys.stderr)
                continue
            keyed = trim(chroma_key(Image.open(raw)), pad)
            keyed_by_dir[d] = keyed
            out = os.path.join(outdir, f"{stage}_{d}.png")
            keyed.save(out)
            written.append(out)
            print(f"       {out}  ({keyed.width}x{keyed.height})")

        for target, source in MIRROR.items():
            if source not in keyed_by_dir:
                print(f"       cannot mirror {stage}_{target} (missing {source})", file=sys.stderr)
                continue
            flipped = keyed_by_dir[source].transpose(Image.FLIP_LEFT_RIGHT)
            out = os.path.join(outdir, f"{stage}_{target}.png")
            flipped.save(out)
            written.append(out)
            print(f"       {out}  (mirrored from {source})")

    return written


def slice_item(spec: dict, pad: int) -> list[str]:
    name = spec["name"]
    raw = os.path.join(RAW_DIR, f"{name}.png")
    if not os.path.exists(raw):
        print(f"[skip] {name}: no raw file at {raw}", file=sys.stderr)
        return []
    sheet = Image.open(raw).convert("RGB")
    rows, cols = spec["grid"]
    cw, ch = sheet.width // cols, sheet.height // rows
    outdir = os.path.join(GENERATED_DIR, spec["outdir"])
    os.makedirs(outdir, exist_ok=True)

    written = []
    for idx, cellname in enumerate(spec["cells"]):
        r, c = divmod(idx, cols)
        cell = sheet.crop((c * cw, r * ch, (c + 1) * cw, (r + 1) * ch))
        keyed = trim(chroma_key(cell), pad)
        out = os.path.join(outdir, f"{spec['prefix']}{cellname}.png")
        keyed.save(out)
        written.append(out)
        print(f"       {out}  ({keyed.width}x{keyed.height})")
    return written


def slice_background(spec: dict) -> list[str]:
    """Full-bleed backdrops are NOT chroma-keyed or trimmed — they must fill the
    frame edge to edge. Just normalize raw/<name>.png to a clean RGB PNG at
    Generated/<outdir>/<outfile>.png."""
    name = spec["name"]
    raw = os.path.join(RAW_DIR, f"{name}.png")
    if not os.path.exists(raw):
        print(f"[skip] {name}: no raw file at {raw}", file=sys.stderr)
        return []
    img = Image.open(raw).convert("RGB")
    outdir = os.path.join(GENERATED_DIR, spec["outdir"])
    os.makedirs(outdir, exist_ok=True)
    out = os.path.join(outdir, f"{spec['outfile']}.png")
    img.save(out)
    print(f"       {out}  ({img.width}x{img.height})  [full-bleed: no key, no trim]")
    return [out]


def slice_imgedit(spec: dict, pad: int) -> list[str]:
    """A single edited sprite on magenta: chroma-key + trim like one item cell,
    writing Generated/<outdir>/<outfile>.png."""
    name = spec["name"]
    raw = os.path.join(RAW_DIR, f"{name}.png")
    if not os.path.exists(raw):
        print(f"[skip] {name}: no raw file at {raw}", file=sys.stderr)
        return []
    keyed = trim(chroma_key(Image.open(raw)), pad)
    outdir = os.path.join(GENERATED_DIR, spec["outdir"])
    os.makedirs(outdir, exist_ok=True)
    out = os.path.join(outdir, f"{spec['outfile']}.png")
    keyed.save(out)
    print(f"       {out}  ({keyed.width}x{keyed.height})")
    return [out]


def slice_town(spec: dict, pad: int) -> list[str]:
    """Slice the finished building + its construction states (DinoDigger-5li.3).

    Each raw state (raw/<name>_{done,s3,s2,s1,s0}.png) is chroma-keyed + trimmed
    like one item cell and written to Generated/town/<name>_<state>.png. Trimming is
    per-state (the silhouette genuinely grows taller each stage); the importer gives
    every state a bottom-center pivot so they share a ground line and grow upward."""
    name = spec["name"]
    outdir = os.path.join(GENERATED_DIR, spec["outdir"])
    os.makedirs(outdir, exist_ok=True)
    written = []
    for state in ["done"] + TOWN_STATES:
        raw = os.path.join(RAW_DIR, f"{name}_{state}.png")
        if not os.path.exists(raw):
            print(f"       MISSING {raw}", file=sys.stderr)
            continue
        keyed = trim(chroma_key(Image.open(raw)), pad)
        out = os.path.join(outdir, f"{name}_{state}.png")
        keyed.save(out)
        written.append(out)
        print(f"       {out}  ({keyed.width}x{keyed.height})")
    return written


def slice_one(spec: dict, pad: int) -> list[str]:
    if spec["category"] == "town":
        return slice_town(spec, pad)
    if spec["category"] == "turnaround":
        return slice_turnaround(spec, pad)
    if spec["category"] == "stages":
        return slice_stages(spec, pad)
    if spec["category"] == "strides":
        return slice_strides(spec, pad)
    if spec["category"] == "roll":
        return slice_roll(spec, pad)
    if spec["category"] == "background":
        return slice_background(spec)
    if spec["category"] == "imgedit":
        return slice_imgedit(spec, pad)
    return slice_item(spec, pad)


def main():
    ap = argparse.ArgumentParser(description="Slice Dino Digger raw art.")
    ap.add_argument("--only", metavar="NAME", help="slice only this entry")
    ap.add_argument("--pad", type=int, default=8, help="transparent border padding px")
    args = ap.parse_args()

    if args.only:
        if args.only not in SPRITES_BY_NAME:
            print(f"ERROR: unknown entry '{args.only}'", file=sys.stderr)
            sys.exit(1)
        todo = [SPRITES_BY_NAME[args.only]]
    else:
        todo = SPRITES

    total = 0
    for spec in todo:
        print(f"[slice] {spec['name']} -> Generated/{spec['outdir']}/")
        total += len(slice_one(spec, args.pad))
    print(f"\nDone: {total} sprites written under {GENERATED_DIR}")


if __name__ == "__main__":
    main()
