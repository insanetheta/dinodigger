#!/usr/bin/env python3
"""Bake the builder-prop (hard-hat + mallet) anchor table from the CURRENT sprite art.

WHY THIS FILE EXISTS (DinoDigger-rip)
-------------------------------------
`Assets/Scripts/Config/BuilderPropAnchors.cs` is a GENERATED table of per-species,
per-facing head/hand anchors measured from sprite pixels. It was originally baked by a
throw-away script that lived in a session scratchpad; when the limb-freeze pass
(DinoDigger-n4b) and the follow-up regen (DinoDigger-awr) re-sliced ~80 dino frames with
new union boxes, the table silently went stale and every builder's hard hat floated off
its head. This script is the PERMANENT replacement: re-run it after ANY dino art regen.

    python3 Tools/bake_builder_anchors.py            # re-bake BuilderPropAnchors.cs
    python3 Tools/bake_builder_anchors.py --check    # CI guard: fail if the .cs is stale
    python3 Tools/bake_builder_anchors.py --report   # print measured-vs-shipped drift
    python3 Tools/bake_builder_anchors.py --verify   # composite hat+mallet over the art

MEASUREMENT MODEL (must stay in lockstep with DinoController.UpdateHat/UpdateMallet)
------------------------------------------------------------------------------------
Every value is a fraction of the sprite's FULL CANVAS (the PNG's own width), because the
dino sprites are FullRect meshes, so `SpriteRenderer.bounds` == the whole canvas, and the
runtime applies these fractions straight to `bounds` with no further correction.

* The three frames of one facing (idle, walkA, walkB) are cropped by `slice_sprites.py`
  to ONE shared union box, so they are pixel-aligned and share a canvas. We measure the
  UNION of their alpha so the anchor is valid for whichever frame is on screen.
* HeadCx / HeadW: the horizontal span of opaque pixels inside the TOP `HEAD_BAND` of the
  alpha bounding box. The runtime seats the hat at the TOP of the bounds (`b.max.y`), so
  the head measurement is deliberately "whatever silhouette reaches the top", not an
  anatomical head detection.
* FrontX: the facing-side extreme of the silhouette on the scanline `FRONT_ROW` down the
  alpha bounding box (the runtime puts the mallet at `b.max.y - 0.45 * b.size.y`).
  East-ish facings take the RIGHTmost pixel, west-ish the LEFTmost. N and S have no
  facing side, so the mallet is parked at a fixed `NS_FRONT` fraction of the bbox, out to
  the dino's own left (N) / right (S).

MIRRORS AND THE bw4 FLIP REMAP
------------------------------
`slice_sprites.py` generates only S/SE/E/NE/N and produces SW/W/NW by horizontally
flipping SE/E/NE (`MIRROR` below — re-checked against Tools/generate_sprites.py). We
still MEASURE every one of the 8 files rather than deriving the mirrors arithmetically:
it costs nothing and it catches a broken mirror instead of hiding it.

The table is keyed by the DISPLAYED sprite. `GeneratedArtImporter.FlippedFacingPairs`
records actors whose ADULT art came out mirrored vs its filename, and the importer loads
the mirror-partner FILE for those facings. We fold that remap in identically: HeadCx /
HeadW come from the file the importer actually loads, while FrontX is placed on the side
the dino VISUALLY faces (i.e. the logical Dir8). NOTE the asymmetry, it is real: the
importer's `StagePath` does NOT apply the remap, so baby/kid facings always read their
own file. This script mirrors the importer exactly rather than "fixing" it.

STAGES
------
The table is indexed [DinoType][GrowthStage][Dir8]. It used to be adult-only on the
assumption that baby/kid silhouettes agreed with the adult in normalised terms; measuring
the regenerated stage art (`--report`) showed they disagree by up to 0.37 of the sprite
width, and half a world unit of prop displacement — which is most of why the hats floated.
Baby/kid are separate img2img generations, not scaled adults, so they get their own rows.

THE OTHER HALF OF THE GUARD
---------------------------
`--check` compares this table against the source PNGs. The `BuilderAnchorsMatchArt`
integration case does the same comparison from inside the engine, against the textures the
importer actually shipped (which are downsampled to 256px tall), so it also catches an
import-setting change that this script cannot see. Keep the constants above in step with
the ones at the top of that case — each file names the other.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys

import numpy as np
from PIL import Image, ImageDraw

TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(TOOLS_DIR, ".."))
GEN = os.path.join(REPO, "Assets", "Art", "Generated")
CS_PATH = os.path.join(REPO, "Assets", "Scripts", "Config", "BuilderPropAnchors.cs")
JSON_PATH = os.path.join(TOOLS_DIR, "builder_prop_anchors.json")

# --------------------------------------------------------------- measurement constants
ALPHA_T = 8          # alpha above this counts as opaque (kills the chroma-key feather)
HEAD_BAND = 0.11     # top slice of the alpha bbox that counts as "head"
FRONT_ROW = 0.45     # scanline for the mallet hand, as a fraction DOWN the alpha bbox
NS_FRONT = 0.28      # N/S mallet x as a bbox fraction (S uses 1 - NS_FRONT)

# HORN / CREST GUARD. The band above assumes the top of the silhouette IS the head, which
# is true for most of the cast. It is not true for a triceratops' brow horn or a
# parasaurolophus' crest: there the band measures a narrow spike, the hat comes out the
# size of a thimble, and — because its brim only dips HatCrownOverlap of its own height
# below the crown — a hat that small never reaches the head at all and reads as floating.
# So a span narrower than MIN_HEAD_W of the canvas is taken as evidence we measured a
# spike, and the band is deepened (never past MAX_HEAD_BAND) until it clears the floor.
# The threshold is ABSOLUTE rather than relative to the body on purpose: relative rules
# also fire on genuinely wide-backed species (ankylosaurus' spiked shell) whose narrow
# crown measurement is correct.
#
# THE DEPTH THIS PICKS IS BAKED INTO THE TABLE, and that is not incidental. The span is a
# step function of the band depth — a stegosaurus' back plates enter the band all at once,
# so its span jumps 0.285 -> 0.366 between two adjacent depths — and MIN_HEAD_W is a hard
# threshold sitting inside that step. Re-deriving the depth on a DIFFERENT raster (the
# runtime guard reads the 256px-tall texture Unity ships, not this 720px source) can land
# on the other side of the step and then deepen when we didn't, moving HeadW by 0.055 for
# no real reason. Measuring the population showed there is no "safer" reference depth to
# decide at either: parasaurolophus sits within 0.01 of the floor at every depth tried. So
# the decision is made ONCE, here, and shipped as BuilderPropAnchor.HeadBand — the guard
# re-measures at that depth instead of re-running this rule.
MIN_HEAD_W = 0.30
MAX_HEAD_BAND = 0.20

# AMBIGUOUS-DEPTH GUARD. The span is a STEP function of the band depth wherever the art has
# a horizontal edge near the crown — a stegosaurus' back plates start on one scanline, so
# its span jumps 0.291 -> 0.366 between two adjacent rows, and a triceratops baby's brow
# jumps 0.171 in one row. Land the band on such a row and the number is not a property of
# the silhouette at all, it is a property of the raster: the source PNG is ~790px tall and
# the texture Unity ships is 256px, so `round(bh * depth)` picks row 85 on one and the
# equivalent of row 84 on the other, and the two disagree by the whole height of the step.
# (That is exactly how the runtime guard reported 0.296 where this baker had 0.366.)
#
# So after the horn guard picks a depth we check the neighbourhood the two rasters could
# each land in, and if a step lives inside it we nudge the depth just far enough to clear
# it — keeping MIN_HEAD_W if it was already satisfied. Across all 216 anchors this moves 17
# (stegosaurus' plates, triceratops' brow) by at most 0.006 of depth, and drops the worst
# single-row step under any chosen band from 0.171 to MAX_JUMP.
JUMP_WINDOW = 0.005   # normalised depth the two rasters may disagree by (~1 texture row)
MAX_JUMP = 0.015      # a single-row span step bigger than this makes the depth ambiguous
JUMP_NUDGE = 0.002
MAX_NUDGE = 0.070

# Dir8 declaration order in Assets/Scripts/Config/GameEnums.cs.
DIRS = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]
# slice_sprites.py: left-side facings are h-flips of their right-side partner.
MIRROR = {"SW": "SE", "W": "E", "NW": "NE"}
EASTISH = {"E", "NE", "SE"}
WESTISH = {"W", "NW", "SW"}

# DinoType enum order (GameEnums.cs) -> generated-art folder (GeneratedArtImporter.Dinos).
SPECIES = [
    ("TRex", "trex"),
    ("Triceratops", "triceratops"),
    ("Brachiosaurus", "brachiosaurus"),
    ("Stegosaurus", "stegosaurus"),
    ("Pteranodon", "pteranodon"),
    ("Ankylosaurus", "ankylosaurus"),
    ("Spinosaurus", "spinosaurus"),
    ("Parasaurolophus", "parasaurolophus"),
    ("Velociraptor", "velociraptor"),
]

# Mirror of GeneratedArtImporter.FlippedFacingPairs, keyed by the right-side member of
# each pair. Adult sets ONLY (StagePath does not consult it) — see module docstring.
FLIPPED_FACING_PAIRS = {
    "triceratops": {"SE"},
    "stegosaurus": {"SE"},
    "ankylosaurus": {"E", "SE", "NE"},
}

# GrowthStage enum order (GameEnums.cs) -> generated-art stage prefix (None == adult/Big).
STAGE_ORDER = [("Baby", "baby"), ("Kid", "kid"), ("Big", None)]
STAGES = [None, "kid", "baby"]     # None == the adult set
CHAR_TARGET_H = 1.30               # GeneratedArtImporter.CharTargetH (world units)
STAGE_SCALE = {None: 1.30, "kid": 1.15, "baby": 1.00}   # GameConfig.StageScales


def mirror_dir(d: str) -> str:
    return {"E": "W", "W": "E", "NE": "NW", "NW": "NE", "SE": "SW", "SW": "SE"}.get(d, d)


def pair_key(d: str) -> str:
    """The right-side representative (E/SE/NE) identifying a facing's mirror pair."""
    return {"W": "E", "SW": "SE", "NW": "NE"}.get(d, d)


def file_dir(folder: str, stage, d: str) -> str:
    """The facing whose FILE the importer actually displays for logical facing `d`."""
    if stage is None and pair_key(d) in FLIPPED_FACING_PAIRS.get(folder, ()):
        return mirror_dir(d)
    return d


def frame_paths(folder: str, stage, d: str) -> list[str]:
    if stage is None:
        names = [f"{folder}_{d}.png", f"walkA_{d}.png", f"walkB_{d}.png"]
    else:
        names = [f"{stage}_{d}.png", f"{stage}_walkA_{d}.png", f"{stage}_walkB_{d}.png"]
    return [os.path.join(GEN, folder, n) for n in names]


def union_alpha(paths: list[str]) -> np.ndarray:
    """OR of the opaque masks of the idle + both stride frames of one facing."""
    acc = None
    for p in paths:
        mask = np.array(Image.open(p).convert("RGBA"))[:, :, 3] > ALPHA_T
        if acc is None:
            acc = mask
        elif acc.shape != mask.shape:
            raise SystemExit(
                f"FRAME CANVASES DISAGREE for {p} ({mask.shape} vs {acc.shape}). "
                "The idle/walkA/walkB set must share one union box — re-run "
                "slice_sprites.py for this actor before baking anchors.")
        else:
            acc |= mask
    return acc


def measure(folder: str, stage, d: str) -> dict:
    """Measure one DISPLAYED (species, stage, facing) in canvas fractions."""
    src = file_dir(folder, stage, d)
    mask = union_alpha(frame_paths(folder, stage, src))
    h, w = mask.shape
    ys, xs = np.nonzero(mask)
    y0, y1 = int(ys.min()), int(ys.max()) + 1
    x0, x1 = int(xs.min()), int(xs.max()) + 1

    # Per-row running extent of the union, so every band-depth query below is a lookup:
    # cum_l[r] / cum_r[r] are the leftmost / rightmost opaque columns over rows y0..y0+r.
    bh = y1 - y0
    body = mask[y0:y1, :]
    has = body.any(axis=1)
    idx = np.arange(w)
    row_l = np.where(has, np.argmax(body, axis=1), w)
    row_r = np.where(has, w - 1 - np.argmax(body[:, ::-1], axis=1), -1)
    cum_l = np.minimum.accumulate(row_l)
    cum_r = np.maximum.accumulate(row_r)

    def rows_at(depth: float) -> int:
        return min(bh, max(1, int(round(bh * depth))))

    def head_span(depth: float):
        r = rows_at(depth) - 1
        return int(cum_l[r]), int(cum_r[r]) + 1

    def span_frac(depth: float) -> float:
        head_l, head_r = head_span(depth)
        return (head_r - head_l) / w

    # Widest single-row step anywhere the two rasters could land for this depth.
    step = np.abs(np.diff((cum_r - cum_l + 1) / w))

    def worst_step(depth: float) -> float:
        lo = max(1, rows_at(depth - JUMP_WINDOW))
        hi = min(bh - 1, max(rows_at(depth + JUMP_WINDOW), lo + 1))
        return float(step[lo - 1:hi].max()) if hi >= lo else 0.0

    # 1) The horn/crest guard: look past a spike until the span reads like a skull.
    depth = HEAD_BAND
    while span_frac(depth) < MIN_HEAD_W and depth < MAX_HEAD_BAND:
        depth = min(depth + 0.01, MAX_HEAD_BAND)

    # 2) The ambiguity guard: if a step lives in the window the two rasters share, step off
    #    it — nearest depth first, and never giving up a MIN_HEAD_W we had already met.
    if worst_step(depth) > MAX_JUMP:
        need_min = span_frac(depth) >= MIN_HEAD_W
        nudge = JUMP_NUDGE
        while nudge <= MAX_NUDGE:
            for cand in (depth + nudge, depth - nudge):
                if not 0.06 <= cand <= 0.32:
                    continue
                if worst_step(cand) <= MAX_JUMP and \
                        (not need_min or span_frac(cand) >= MIN_HEAD_W):
                    depth = cand
                    nudge = MAX_NUDGE  # done
                    break
            else:
                nudge += JUMP_NUDGE
                continue
            break

    head_l, head_r = head_span(depth)

    if d in EASTISH or d in WESTISH:
        row = np.nonzero(mask[int(round(y0 + FRONT_ROW * (y1 - y0)))])[0]
        # An empty scanline can only mean a silhouette with a hole at hand height; fall
        # back to the bbox edge so the mallet still lands on the correct side.
        if row.size == 0:
            front = x1 if d in EASTISH else x0
        else:
            front = int(row.max()) + 1 if d in EASTISH else int(row.min())
    else:
        f = NS_FRONT if d == "N" else 1.0 - NS_FRONT
        front = x0 + f * (x1 - x0)

    return {
        "file_dir": src,
        "canvas": [w, h],
        "bbox": [x0, y0, x1, y1],
        "HeadCx": (head_l + head_r) / 2.0 / w,
        "HeadW": (head_r - head_l) / w,
        "HeadBand": depth,
        "FrontX": front / w,
    }


def measure_all() -> dict:
    out = {}
    for _, folder in SPECIES:
        out[folder] = {}
        for stage in STAGES:
            key = stage or "adult"
            out[folder][key] = {d: measure(folder, stage, d) for d in DIRS}
    return out


def adult_world_width(folder: str, d: str, stage=None) -> float:
    """SpriteRenderer.bounds.size.x in WORLD UNITS, reproducing the importer's PPU
    (one PPU per actor = tallest ADULT facing / CharTargetH, shared by every stage)
    times the growth-stage transform scale."""
    max_h = max(Image.open(frame_paths(folder, None, dd)[0]).size[1] for dd in DIRS)
    ppu = max_h / CHAR_TARGET_H
    w = Image.open(frame_paths(folder, stage, file_dir(folder, stage, d))[0]).size[0]
    return w / ppu * STAGE_SCALE[stage]


# ------------------------------------------------------------------- shipped .cs table
ROW_RE = re.compile(
    r"new BuilderPropAnchor\(([-\d.]+)f,\s*([-\d.]+)f,\s*([-\d.]+)f,\s*([-\d.]+)f\)")


def read_shipped() -> dict:
    """Parse the generated table out of BuilderPropAnchors.cs, in its declaration order
    (DinoType x GrowthStage x Dir8) -> {folder: {stage_key: {dir: (cx, w, fx)}}}."""
    with open(CS_PATH) as fh:
        text = fh.read()
    vals = [tuple(float(g) for g in m.groups()) for m in ROW_RE.finditer(text)]
    want = len(SPECIES) * len(STAGE_ORDER) * 8
    if len(vals) != want:
        raise SystemExit(f"expected {want} anchors in {CS_PATH}, got {len(vals)}")
    out, k = {}, 0
    for _, folder in SPECIES:
        out[folder] = {}
        for _, stage in STAGE_ORDER:
            out[folder][stage or "adult"] = {d: vals[k + j] for j, d in enumerate(DIRS)}
            k += 8
    return out


# ------------------------------------------------------------------------ code emitter
HEADER = '''// <auto-generated>
// Builder-prop (hard-hat + mallet) head/hand anchors, MEASURED FROM SPRITE PIXELS.
// Baked by Tools/bake_builder_anchors.py — DO NOT EDIT BY HAND. RE-RUN THAT SCRIPT
// AFTER ANY DINO ART REGEN: a re-slice moves each facing's union box and silently
// invalidates every number below. That is DinoDigger-rip, where a limb-freeze re-bake
// left the hard hats hovering off the crews' heads and the mallets beside their hands.
// `bake_builder_anchors.py --check` is the guard; BuilderAnchorsMatchArt is its runtime
// twin (it re-measures live sprites and fails on drift).
//
// Per (species, stage, facing): the head's horizontal center + width taken from the top
// 11% of the opaque bounding box, and the facing-side body extreme on the scanline 45%
// down that box, all as fractions of that facing's idle+walkA+walkB union canvas.
//
// PER GROWTH STAGE, because baby/kid are separate img2img generations rather than scaled
// adults — their measurements differ from the adult's by up to 0.37 of the sprite width
// (half a world unit of prop displacement), which is most of a hard hat.
//
// The table is keyed by LOGICAL facing (Dir8) and its values are already in
// CURRENT-FRAME renderer-bounds (canvas) coordinates, with the bw4 mirror
// correction (GeneratedArtImporter.FlippedFacingPairs) FOLDED IN: for a facing
// whose displayed sprite is the mirror-partner FILE, HeadCx/HeadW come from that
// displayed file and FrontX is placed on the visual facing side. That remap applies to
// the ADULT rows only — GeneratedArtImporter.StagePath does not consult it, so the
// baby/kid rows are measured straight off their own files. Sprites are FullRect meshes
// so renderer.bounds == the full canvas, and consumers apply the fractions directly to
// renderer.bounds with NO further mirroring. There is no runtime flipX.
// </auto-generated>
using DinoDigger.Core;

namespace DinoDigger.Config
{
    /// <summary>One head/hand anchor for a (species, stage, facing): all fractions of the
    /// current SpriteRenderer.bounds. HeadCx = head horizontal center (0=left edge);
    /// HeadW = head width; FrontX = facing-side body extreme at 45% height (mallet).</summary>
    public readonly struct BuilderPropAnchor
    {
        public readonly float HeadCx;
        public readonly float HeadW;
        public readonly float FrontX;

        /// <summary>Fraction of the opaque bbox height the head span was measured over —
        /// 0.11 normally, deeper where the baker had to look past a horn or a crest.
        /// The RUNTIME PLACEMENT NEVER READS THIS. It is shipped so the BuilderAnchorsMatchArt
        /// guard can re-measure the same band instead of re-deriving it: the span is a step
        /// function of this depth, so re-deciding it against the downsampled runtime texture
        /// can tip to the other side of the step and invent drift that isn't there.</summary>
        public readonly float HeadBand;

        public BuilderPropAnchor(float headCx, float headW, float frontX, float headBand)
        {
            HeadCx = headCx; HeadW = headW; FrontX = frontX; HeadBand = headBand;
        }
    }

    /// <summary>Generated lookup of builder-gear anchors + the tuned placement
    /// multipliers, keyed [DinoType][GrowthStage][Dir8].</summary>
    public static class BuilderPropAnchors
    {
        /// <summary>Hat world width = HatWidthMul * HeadW * bounds.width.</summary>
        public const float HatWidthMul = 1.00f;
        /// <summary>Hat brim dips this fraction of the hat height below the crown.</summary>
        public const float HatCrownOverlap = 0.30f;
        /// <summary>Mallet world height = MalletHeightMul * bounds.height.</summary>
        public const float MalletHeightMul = 0.30f;
        /// <summary>Mallet grip rides this fraction DOWN from the top of the bounds.</summary>
        public const float MalletHeightFrac = 0.45f;

        /// <summary>Anchors for the sprite a dino of this species/stage/facing is
        /// ACTUALLY showing. Every index is clamped, so a future enum value degrades to
        /// the first row rather than throwing under a builder's LateUpdate.</summary>
        public static BuilderPropAnchor Get(DinoType type, GrowthStage stage, Dir8 dir)
        {
            int t = (int)type;
            int s = (int)stage;
            int d = (int)dir;
            if (t < 0 || t >= Table.Length) t = 0;
            var species = Table[t];
            if (s < 0 || s >= species.Length) s = species.Length - 1;
            var row = species[s];
            if (d < 0 || d >= row.Length) d = 0;
            return row[d];
        }

        // [DinoType][GrowthStage][Dir8] = Baby,Kid,Big x N,NE,E,SE,S,SW,W,NW
        private static readonly BuilderPropAnchor[][][] Table =
        {
'''

FOOTER = '''        };
    }
}
'''


def emit_cs(m: dict) -> str:
    lines = [HEADER]
    for name, folder in SPECIES:
        lines.append(f"            // ---- {name}\n            new[]\n            {{\n")
        for stage_name, stage in STAGE_ORDER:
            row = m[folder][stage or "adult"]
            cells = ", ".join(
                "new BuilderPropAnchor("
                f"{row[d]['HeadCx']:.3f}f, {row[d]['HeadW']:.3f}f, "
                f"{row[d]['FrontX']:.3f}f, {row[d]['HeadBand']:.3f}f)"
                for d in DIRS)
            lines.append(f"                // {name} / {stage_name}\n"
                         f"                new[] {{ {cells} }},\n")
        lines.append("            },\n")
    lines.append(FOOTER)
    return "".join(lines)


# --------------------------------------------------------------------------- reporting
def report(m: dict) -> int:
    shipped = read_shipped()
    print("DRIFT: shipped BuilderPropAnchors.cs vs CURRENT art")
    print(f"{'species':16s} {'stage':6s} {'dir':>3s}  {'dHeadCx':>8s} {'dHeadW':>8s} "
          f"{'dFrontX':>8s}  {'hat dx':>7s} {'mallet dx':>9s}   (world units)")
    worst = 0.0
    for _, folder in SPECIES:
        for stage in STAGES:
            key = stage or "adult"
            for d in DIRS:
                cur, old = m[folder][key][d], shipped[folder][key][d]
                dcx = cur["HeadCx"] - old[0]
                dw = cur["HeadW"] - old[1]
                dfx = cur["FrontX"] - old[2]
                ww = adult_world_width(folder, d, stage)
                hat, mal = abs(dcx) * ww, abs(dfx) * ww
                worst = max(worst, hat, mal)
                flag = "  <<<" if max(hat, mal) > 0.05 else ""
                print(f"{folder:16s} {key:6s} {d:>3s}  {dcx:+8.3f} {dw:+8.3f} {dfx:+8.3f}"
                      f"  {hat:7.3f} {mal:9.3f}{flag}")
    print(f"\nworst prop displacement: {worst:.3f} world units "
          f"(a Big dino is {CHAR_TARGET_H * STAGE_SCALE[None]:.2f} units tall)")

    print("\nSTAGE DIVERGENCE: how far each stage's own measurement sits from the adult's "
          "(this is what a single shared table would cost)")
    print(f"{'species':16s} {'stage':6s}  {'max|dHeadCx|':>12s} {'max|dHeadW|':>11s} "
          f"{'max|dFrontX|':>12s}  {'worst world dx':>14s}")
    for _, folder in SPECIES:
        for stage in ("kid", "baby"):
            a = b = c = wmax = 0.0
            for d in DIRS:
                cur, ad = m[folder][stage][d], m[folder]["adult"][d]
                ww = adult_world_width(folder, d, stage)
                a = max(a, abs(cur["HeadCx"] - ad["HeadCx"]))
                b = max(b, abs(cur["HeadW"] - ad["HeadW"]))
                c = max(c, abs(cur["FrontX"] - ad["FrontX"]))
                wmax = max(wmax, abs(cur["HeadCx"] - ad["HeadCx"]) * ww,
                           abs(cur["FrontX"] - ad["FrontX"]) * ww)
            print(f"{folder:16s} {stage:6s}  {a:12.3f} {b:11.3f} {c:12.3f}  {wmax:14.3f}")
    return 0


def check(m: dict, tol: float) -> int:
    shipped = read_shipped()
    bad = []
    for _, folder in SPECIES:
        for stage in STAGES:
            key = stage or "adult"
            for d in DIRS:
                cur, old = m[folder][key][d], shipped[folder][key][d]
                # HeadBand is held to its own, far tighter bound: it is not an anchor but
                # the DEPTH the other three were read at, so slop there silently changes
                # what the runtime guard measures. (A 2-decimal emitter once rounded a
                # nudged 0.114 back to 0.11 and this check waved it through.)
                for k, i in (("HeadCx", 0), ("HeadW", 1), ("FrontX", 2), ("HeadBand", 3)):
                    lim = 0.0006 if k == "HeadBand" else tol
                    if abs(cur[k] - old[i]) > lim:
                        bad.append(f"  {folder} {key} {d} {k}: shipped {old[i]:.3f} "
                                   f"measured {cur[k]:.3f} (drift {cur[k] - old[i]:+.3f})")
    if bad:
        print(f"STALE: BuilderPropAnchors.cs disagrees with the current art "
              f"(tolerance {tol}):", file=sys.stderr)
        print("\n".join(bad), file=sys.stderr)
        print("\nFix: python3 Tools/bake_builder_anchors.py", file=sys.stderr)
        return 1
    print(f"OK: BuilderPropAnchors.cs matches the current art within {tol} "
          f"({len(SPECIES) * len(STAGE_ORDER) * 8} anchors).")
    return 0


# ------------------------------------------------------------------- offline composite
def composite(out_path: str, stage=None, anchors: dict | None = None,
              art_root: str | None = None, frame: str = "idle") -> str:
    """Draw the hat + mallet at the SHIPPED anchors over the real sprites, reproducing
    DinoController.UpdateHat/UpdateMallet in canvas space. Every tile is eyeballed.

    `art_root` lets a caller point at an OLD checkout of Assets/Art/Generated so the
    before/after of an art regen can be compared with one table.

    Runtime equivalence: renderer.bounds == the whole canvas (FullRect), so bounds.min.x
    is canvas x=0 and bounds.max.y is canvas y=0 with y flipped. The hat's world height
    follows its own aspect off the target WIDTH, and its centre sits (0.5 - overlap) hat
    heights ABOVE the canvas top — which is why the tiles need headroom above the sprite.
    """
    global GEN
    old_gen = GEN
    if art_root:
        GEN = art_root
    try:
        if anchors is None:
            key, shipped = stage or "adult", read_shipped()
            anchors = {f: shipped[f][key] for _, f in SPECIES}
        hat_img = Image.open(os.path.join(old_gen, "town", "prop_hardhat.png")).convert("RGBA")
        mal_img = Image.open(os.path.join(old_gen, "town", "prop_tool_hammer.png")).convert("RGBA")
        overlap, mallet_h_mul, mallet_h_frac = (0.30, 0.30, 0.45)

        tile_w, tile_h, pad, label_h = 210, 260, 6, 18
        sheet = Image.new("RGBA", (tile_w * 8 + pad, tile_h * len(SPECIES) + pad),
                          (250, 248, 244, 255))
        draw = ImageDraw.Draw(sheet)

        for r, (name, folder) in enumerate(SPECIES):
            for c, d in enumerate(DIRS):
                cx_f, hw_f, fx_f = anchors[folder][d][:3]
                src = file_dir(folder, stage, d)
                idx = {"idle": 0, "walkA": 1, "walkB": 2}[frame]
                sprite = Image.open(frame_paths(folder, stage, src)[idx]).convert("RGBA")
                W, H = sprite.size

                hw = max(2, int(round(hw_f * W)))
                hh = max(2, int(round(hw * hat_img.height / hat_img.width)))
                # Headroom so a hat floating above the canvas top is VISIBLE, not clipped.
                top = int(round((0.5 + overlap) * hh)) + 4
                canvas = Image.new("RGBA", (W, H + top), (0, 0, 0, 0))
                canvas.alpha_composite(sprite, (0, top))
                # Guide line = the sprite's top edge (where the runtime seats the crown).
                ImageDraw.Draw(canvas).line([(0, top), (W, top)], fill=(0, 160, 255, 90))

                hat = hat_img.resize((hw, hh), Image.LANCZOS)
                if d in WESTISH:
                    hat = hat.transpose(Image.FLIP_LEFT_RIGHT)
                hat_cy = top - (0.5 - overlap) * hh          # y grows DOWN
                canvas.alpha_composite(hat, (int(round(cx_f * W - hw / 2)),
                                             int(round(hat_cy - hh / 2))))

                mh = max(2, int(round(mallet_h_mul * H)))
                mw = max(2, int(round(mh * mal_img.width / mal_img.height)))
                mal = mal_img.resize((mw, mh), Image.LANCZOS)
                if fx_f < 0.5:
                    mal = mal.transpose(Image.FLIP_LEFT_RIGHT)
                canvas.alpha_composite(mal, (int(round(fx_f * W - mw / 2)),
                                             int(round(top + mallet_h_frac * H - mh / 2))))

                s = min((tile_w - 10) / canvas.width, (tile_h - label_h - 6) / canvas.height)
                canvas = canvas.resize((max(1, int(canvas.width * s)),
                                        max(1, int(canvas.height * s))), Image.LANCZOS)
                ox = pad + c * tile_w + (tile_w - canvas.width) // 2
                oy = pad + r * tile_h + label_h
                sheet.alpha_composite(canvas, (ox, oy))
                draw.text((pad + c * tile_w + 6, pad + r * tile_h + 3),
                          f"{name[:13]} {d}", fill=(30, 30, 30, 255))

        sheet.convert("RGB").save(out_path)
        return out_path
    finally:
        GEN = old_gen


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--check", action="store_true",
                    help="exit 1 if the shipped .cs disagrees with the current art")
    ap.add_argument("--report", action="store_true", help="print drift + stage agreement")
    ap.add_argument("--verify", metavar="PNG", nargs="?", const="", default=None,
                    help="write a hat+mallet composite over the real sprites")
    ap.add_argument("--verify-stage", choices=["adult", "kid", "baby"], default="adult")
    ap.add_argument("--verify-frame", choices=["idle", "walkA", "walkB"], default="idle")
    ap.add_argument("--art-root", default=None,
                    help="composite against another Assets/Art/Generated (before/after)")
    ap.add_argument("--tol", type=float, default=0.02,
                    help="--check tolerance in canvas fractions (default 0.02 ~ 2%%)")
    args = ap.parse_args()

    m = measure_all()

    if args.report:
        return report(m)
    if args.check:
        return check(m, args.tol)
    if args.verify is not None:
        stage = None if args.verify_stage == "adult" else args.verify_stage
        out = args.verify or os.path.join(
            REPO, "Logs", f"anchor_verify_shipped_{args.verify_stage}.png")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        print("wrote " + composite(out, stage, art_root=args.art_root,
                                   frame=args.verify_frame))
        return 0

    with open(CS_PATH, "w") as fh:
        fh.write(emit_cs(m))
    with open(JSON_PATH, "w") as fh:
        json.dump(m, fh, indent=1, sort_keys=True)
    print(f"baked {CS_PATH}\nmeasurements {JSON_PATH}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
