#!/usr/bin/env python3
"""Contact sheets of the actor art AS THE IMPORTER DISPLAYS IT, plus the measured audit
that decides the flip table — and the MANIFEST that table is guarded against
(DinoDigger-3yb).

    python3 Tools/verify_facing_art.py --sheets       # Logs/facing_<actor>_<stage>.png
    python3 Tools/verify_facing_art.py --audit        # the per-cell measurement table
    python3 Tools/verify_facing_art.py --audit --raw  # audit the RAW files, remap ignored
    python3 Tools/verify_facing_art.py --bake         # re-bake FacingManifest.cs
    python3 Tools/verify_facing_art.py --check        # CI guard: every flip table in step

WHY IT EXISTS
-------------
`GeneratedArtImporter.FlippedFacingPairs` remaps the (actor, stage, facing) cells whose
generated art came out mirrored against its own filename (the DinoDigger-bw4 scar). The
table was per-actor and adult-only until DinoDigger-3yb, which shipped adult stegosaurus
and kid ankylosaurus facing backwards. Deciding those cells by eye is what let them slip,
so the decision is a MEASUREMENT here, and the sheets exist to confirm it rather than to
make it.

WHERE THE PIXEL TRUTH LIVES, AND WHY IT LIVES HERE
--------------------------------------------------
It has to live in this file, on the SOURCE PNGs, because the measurement is impossible
anywhere else. The textures Unity ships are 256px tall (the WebGL platform override in
every dino .meta) and crunch-compressed, while these sources are ~800px: at a third of
the raster a pupil no longer survives the erosion the eye-finder is built on, and the
near-white sclera beside it is averaged away, so a runtime re-measurement of the shipped
texture finds NO eye on ANY sprite — not a degraded reading, zero readings. (That is not
a Read/Write-enabled question: the guard reads its pixels through a RenderTexture blit,
which works fine on a non-readable texture. It is purely resolution.)

So the audit runs here, once, and BAKES ITS VERDICT into
`Assets/Scripts/Config/FacingManifest.cs` — measured pixel truth in the same shape
BuilderPropAnchors.cs already ships it in. Three consumers must agree with that manifest:

  * `GeneratedArtImporter.FlippedFacingPairs` — what the game actually wires up.
  * `bake_builder_anchors.FLIPPED_FACINGS`    — what the anchor baker measures through.
  * the `FacingArtNotMirrored` integration case — which reads the sprite the game would
    DISPLAY for every (species, stage, facing) and asserts its source FILE is the one the
    manifest says it must be. That closes the original DinoDigger-3yb hole from the other
    side: it fails not only on a wrong table entry but on a path helper that forgets to
    consult the table at all, which is exactly what StagePath used to do.

`--check` compares all three against a fresh measurement of the art. Edit a table without
re-auditing and the suite fails; regenerate art without re-auditing and this tool fails.

THE THREE LANDMARKS (all signed: + means the actor faces SCREEN-RIGHT)
---------------------------------------------------------------------
head      the eye-pair centre relative to the TRUNK AXIS. A pupil is found as a dark disc
          that survives an erosion (which erases the 1-3px outlines) and has near-white
          sclera beside it (which rejects a parasaurolophus' crest spines and an
          ankylosaurus' shell studs). The trunk axis is the median centre of the widest
          contiguous opaque run across the body's middle rows — unlike the mass centroid,
          a tail cannot drag it.
parallax  the eye-SIZE sign, and the only landmark with real sensitivity on the 3/4
          (SE/NE) frames. Turn a head toward screen-right and the animal's own right side
          rotates into view: the right eye is drawn larger AND, at 45 degrees, lands to
          screen-LEFT of the far one. So the BIGGER EYE ON THE LEFT means facing RIGHT.
          Gated on a real size ratio, because a true front (S) view draws both eyes the
          same and the sign is then meaningless.
tail      the larger mid-height horizontal reach past the trunk axis is the tail; the head
          is opposite it. Weak on species whose silhouette is dominated by something else
          (a spinosaurus' sail, a pteranodon's crest), which is why it only ever votes.

A cell is called FLIPPED when the landmarks that clear their confidence gates agree on
LEFT. Conflicting evidence leaves the cell alone: the table has always listed only
high-confidence flips so that it can never regress an actor that is already correct.

Only E/SE/NE (and their mirrors) can ever be flipped — N and S have no horizontal
component — and W/SW/NW are exact pixel mirrors produced by slice_sprites.py, so auditing
the right-side member of each pair audits both.
"""

from __future__ import annotations

import argparse
import math
import os
import re
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(TOOLS_DIR, ".."))
GEN = os.path.join(REPO, "Assets", "Art", "Generated")

sys.path.insert(0, TOOLS_DIR)
from bake_builder_anchors import (  # noqa: E402  (path set above)
    DIRS, FLIPPED_FACINGS, SPECIES, file_dir, frame_paths)

ACTORS = ["backhoe"] + [f for _, f in SPECIES]
STAGES = [None, "kid", "baby"]
POSES = ["idle", "walkA", "walkB"]

ALPHA_T = 8
DARK = 95            # luminance below this is outline-or-pupil
WHITE = 225          # luminance above this is sclera-or-highlight
PUPIL_R = 0.010      # erosion radius as a fraction of the alpha bbox height
EYE_TOP = 0.60       # eyes ride in the top of the silhouette
EYE_MAX = 0.18       # a pupil is SMALL: cap its box at this fraction of the alpha bbox.
                     # Measured across the cast, no real pupil exceeds 0.15 in either
                     # axis, while the blobs this rejects are 0.19-0.37 — an ankylosaurus'
                     # club (a cluster of cream studs whose dark shading survives the
                     # erosion and whose studs satisfy the sclera gate) and the same
                     # species' shoulder plates. Without this cap the club is the only
                     # "eye" found on kid_NE and the audit reads that head backwards.
CONF = 0.040         # noise floor on the head / tail landmarks
PAR_RATIO = 1.25     # eye-size ratio that makes the parallax sign meaningful
PAR_SEP = 0.10       # eye separation (canvas fractions) parallax needs to be readable

CS_PATH = os.path.join(REPO, "Assets", "Scripts", "Config", "FacingManifest.cs")
IMPORTER_CS = os.path.join(REPO, "Assets", "Scripts", "Editor", "GeneratedArtImporter.cs")

# Species in DinoType declaration order (GameEnums.cs) and stages in GrowthStage order —
# the manifest is indexed by both, so these orders are load-bearing.
STAGE_ORDER = [("Baby", "baby"), ("Kid", "kid"), ("Big", "adult")]

# THE THREE CELLS THE MEASUREMENT CANNOT DECIDE, adjudicated by eye from the contact
# sheets and recorded here so they are part of the baked manifest rather than a silent
# discrepancy between it and the shipped tables. Each is 'ambiguous' to the landmarks for
# a stated, structural reason — and each is CHECKED to still be ambiguous when the
# manifest is baked: the day a regen gives one of these cells a real verdict, this file
# fails loudly rather than letting a hand-entered answer override a measured one.
ADJUDICATED = {
    ("backhoe", "adult"): {
        "SE": "a backhoe has no eyes, so the head landmark never fires and the tail "
              "landmark reads its own boom; decided from Logs/facing_backhoe_adult.png",
        "NE": "same as SE — no eyes, no tail; decided from the contact sheet",
    },
    ("ankylosaurus", "kid"): {
        "NE": "only a sliver of the head clears the shell at this angle, so no pupil "
              "survives the erosion; decided from Logs/facing_ankylosaurus_kid.png",
    },
}


# ------------------------------------------------------------------ pixel primitives
def load(path):
    arr = np.array(Image.open(path).convert("RGBA")).astype(np.int16)
    alpha = arr[:, :, 3] > ALPHA_T
    lum = 0.299 * arr[:, :, 0] + 0.587 * arr[:, :, 1] + 0.114 * arr[:, :, 2]
    return alpha, lum


def _morph(mask, r, filt):
    if r < 1:
        return mask
    img = Image.fromarray((mask * 255).astype(np.uint8)).filter(filt(2 * r + 1))
    return np.array(img) > 127


def erode(m, r):
    return _morph(m, r, ImageFilter.MinFilter)


def dilate(m, r):
    return _morph(m, r, ImageFilter.MaxFilter)


def components(mask):
    """4-connected labelling: size + centroid + box per blob. Small inputs only (the
    eroded pupil mask), so a union-find over the set pixels is plenty."""
    h, w = mask.shape
    lab = np.zeros((h, w), np.int32)
    parent = [0]

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[max(ra, rb)] = min(ra, rb)

    ys, xs = np.nonzero(mask)
    nxt = 1
    for y, x in zip(ys.tolist(), xs.tolist()):
        up = lab[y - 1, x] if y else 0
        left = lab[y, x - 1] if x else 0
        if up and left:
            lab[y, x] = min(up, left)
            union(up, left)
        elif up:
            lab[y, x] = up
        elif left:
            lab[y, x] = left
        else:
            lab[y, x] = nxt
            parent.append(nxt)
            nxt += 1

    acc = {}
    for y, x in zip(ys.tolist(), xs.tolist()):
        c = acc.setdefault(find(lab[y, x]), [0, 0, 0, h, 0, w, 0])
        c[0] += 1
        c[1] += y
        c[2] += x
        c[3] = min(c[3], y)
        c[4] = max(c[4], y)
        c[5] = min(c[5], x)
        c[6] = max(c[6], x)
    return [dict(size=c[0], cy=c[1] / c[0], cx=c[2] / c[0], y0=c[3], y1=c[4] + 1,
                 x0=c[5], x1=c[6] + 1) for c in acc.values()]


def trunk_axis(alpha, y0, y1):
    """Median centre of the widest contiguous opaque run over the body's middle rows."""
    axes = []
    bh = y1 - y0
    for y in range(y0 + int(0.30 * bh), y0 + int(0.80 * bh)):
        idx = np.nonzero(alpha[y])[0]
        if idx.size == 0:
            continue
        brk = np.nonzero(np.diff(idx) > 1)[0]
        starts, ends = np.r_[0, brk + 1], np.r_[brk, idx.size - 1]
        k = int(np.argmax(ends - starts))
        axes.append((idx[starts[k]] + idx[ends[k]]) / 2.0)
    return float(np.median(axes)) if axes else None


def measure_frame(path):
    """The three landmarks for one PNG, each positive when the actor faces screen-right."""
    alpha, lum = load(path)
    ys, xs = np.nonzero(alpha)
    y0, y1 = int(ys.min()), int(ys.max()) + 1
    x0, x1 = int(xs.min()), int(xs.max()) + 1
    bw, bh = x1 - x0, y1 - y0

    axis = trunk_axis(alpha, y0, y1)
    if axis is None:
        axis = (x0 + x1) / 2.0

    # tail: the bigger mid-height reach past the axis is the tail, head is opposite it
    band = alpha[y0 + int(0.30 * bh):y0 + int(0.85 * bh)]
    bxs = np.nonzero(band)[1]
    tail = -((bxs.max() - axis) - (axis - bxs.min())) / bw

    # pupils: eroded dark discs with sclera beside them, high in the silhouette
    r = max(1, int(round(PUPIL_R * bh)))
    survivors = erode(alpha & (lum < DARK), r)
    sclera = dilate(alpha & (lum > WHITE), max(2, r))
    eyes = []
    for c in components(survivors):
        if c["size"] < 0.7 * math.pi * r * r:
            continue
        cw, ch = c["x1"] - c["x0"], c["y1"] - c["y0"]
        if not 0.45 <= cw / max(1, ch) <= 2.2:
            continue
        if (c["cy"] - y0) > EYE_TOP * bh or cw > EYE_MAX * bw or ch > EYE_MAX * bh:
            continue
        if not sclera[c["y0"]:c["y1"], c["x0"]:c["x1"]].any():
            continue
        eyes.append(c)
    eyes.sort(key=lambda c: -c["size"])
    eyes = eyes[:2]

    head = parallax = float("nan")
    if eyes:
        total = sum(c["size"] for c in eyes)
        head = (sum(c["cx"] * c["size"] for c in eyes) / total - axis) / bw
    if len(eyes) == 2:
        big, small = eyes
        sep = (big["cx"] - small["cx"]) / bw
        if big["size"] / small["size"] >= PAR_RATIO and abs(sep) >= PAR_SEP:
            parallax = -sep      # bigger eye to the LEFT => facing RIGHT
    return head, parallax, tail


def measure_cell(folder, stage, d, raw=False):
    """Average the landmarks over a facing's idle + both stride frames."""
    src = d if raw else file_dir(folder, stage, d)
    vals = {"head": [], "parallax": [], "tail": []}
    for p in frame_paths(folder, stage, src):
        if not os.path.exists(p):
            continue
        h, par, t = measure_frame(p)
        for k, v in (("head", h), ("parallax", par), ("tail", t)):
            if not math.isnan(v):
                vals[k].append(v)
    if not vals["tail"]:
        return None
    out = {k: (float(np.mean(v)) if v else float("nan")) for k, v in vals.items()}
    out["file"] = src
    out["par_frames"] = len(vals["parallax"])

    votes = []
    if not math.isnan(out["head"]) and abs(out["head"]) >= CONF:
        votes.append(math.copysign(1, out["head"]))
    if abs(out["tail"]) >= CONF:
        votes.append(math.copysign(1, out["tail"]))
    # parallax is authoritative on 3/4 frames, but only when it fires on most frames
    if not math.isnan(out["parallax"]) and out["par_frames"] >= 2:
        votes.append(math.copysign(1, out["parallax"]))
    if votes and all(v > 0 for v in votes):
        out["verdict"] = "faces-right"
    elif votes and all(v < 0 for v in votes):
        out["verdict"] = "FACES-LEFT"
    else:
        out["verdict"] = "ambiguous"
    return out


# --------------------------------------------------------------------------- reports
def audit(raw: bool) -> int:
    what = "RAW FILES (remap ignored)" if raw else "DISPLAYED sprites (remap applied)"
    print(f"FACING AUDIT of {what}")
    print("landmarks are signed: + faces screen-RIGHT, - faces screen-LEFT\n")
    print(f"{'actor':16s} {'stage':6s} {'dir':>3s} {'file':>4s} "
          f"{'head':>7s} {'parlx':>7s} {'tail':>7s}  verdict")
    bad = 0
    for folder in ACTORS:
        for stage in STAGES:
            for d in ("E", "SE", "NE"):
                if not os.path.exists(frame_paths(folder, stage, d)[0]):
                    continue
                m = measure_cell(folder, stage, d, raw)
                if m is None:
                    continue
                if m["verdict"] == "FACES-LEFT":
                    bad += 1
                print(f"{folder:16s} {(stage or 'adult'):6s} {d:>3s} {m['file']:>4s} "
                      f"{m['head']:+7.3f} {m['parallax']:+7.3f} {m['tail']:+7.3f}  "
                      f"{m['verdict']}")
    if raw:
        print(f"\n{bad} raw cell(s) drawn mirrored against their filename — every one of "
              "them must appear in FlippedFacingPairs.")
        return 0
    print(f"\n{bad} DISPLAYED cell(s) still facing the wrong way.")
    return 1 if bad else 0


# -------------------------------------------------------------------------- manifest
def measure_manifest() -> dict:
    """Measure every (actor, stage) from the RAW files and return
    {(folder, stage_key): sorted[flipped E/SE/NE]} — the audited truth the shipped flip
    tables must equal. Mechanical FACES-LEFT verdicts plus the ADJUDICATED cells, whose
    'ambiguous' status is re-confirmed here."""
    out = {}
    for folder in ACTORS:
        for stage in STAGES:
            key = (folder, stage or "adult")
            flipped, verdicts = set(), {}
            for d in ("E", "SE", "NE"):
                if not os.path.exists(frame_paths(folder, stage, d)[0]):
                    continue
                m = measure_cell(folder, stage, d, raw=True)
                if m is None:
                    continue
                verdicts[d] = m["verdict"]
                if m["verdict"] == "FACES-LEFT":
                    flipped.add(d)

            for d, why in ADJUDICATED.get(key, {}).items():
                got = verdicts.get(d)
                if got is None:
                    raise SystemExit(
                        f"ADJUDICATED CELL HAS NO ART: {key} {d} is listed in "
                        f"ADJUDICATED but no frames were measured. Drop the entry or "
                        f"restore the art.")
                if got != "ambiguous":
                    raise SystemExit(
                        f"ADJUDICATED CELL IS NO LONGER AMBIGUOUS: {key} {d} now measures "
                        f"'{got}', but this file overrides it by hand ({why}). A hand "
                        f"answer must never outrank a measured one — re-check the sheet, "
                        f"then either delete the ADJUDICATED entry (if the measurement is "
                        f"now right) or fix the eye-finder.")
                flipped.add(d)

            if verdicts or key in ADJUDICATED:
                out[key] = sorted(flipped, key=("E", "SE", "NE").index)
    return out


CS_HEADER = """// <auto-generated>
// WHICH (species, stage, facing) CELLS OF THE GENERATED ART ARE DRAWN MIRRORED against
// their own filename — MEASURED FROM SOURCE-PNG PIXELS by Tools/verify_facing_art.py.
// DO NOT EDIT BY HAND. RE-RUN THAT SCRIPT AFTER ANY DINO ART REGEN.
//
// This is the DinoDigger-bw4 / DinoDigger-3yb scar: several generated cells came back
// facing the opposite way from the direction their filename claims, so the importer
// resolves those cells to their MIRROR PARTNER's file
// (GeneratedArtImporter.FlippedFacingPairs). That table is hand-maintained and was wrong
// twice — per-actor when the fault is per (actor, STAGE, facing), and consulted by
// AdultSuffix but not by StagePath — which shipped adult stegosaurus and kid
// ankylosaurus backwards for months.
//
// So the decision is a measurement, and this file is the measurement's verdict. The
// FacingArtNotMirrored integration case reads the sprite the game would DISPLAY for
// every (species, stage, facing) and asserts its source file is the one this manifest
// implies — a wrong table entry, a stale table entry, and a path helper that forgets the
// table all fail there. `verify_facing_art.py --check` is the offline half: it
// re-measures the art and fails if this file, the importer's table, or the anchor
// baker's copy has drifted from it.
//
// WHY THE GUARD READS A BAKED TABLE INSTEAD OF RE-MEASURING PIXELS AT RUNTIME: it cannot
// re-measure. The eye-finder needs the ~800px source raster; the texture Unity ships is
// 256px tall (the WebGL override in every dino .meta) and crunch-compressed, and at that
// size no pupil survives the erosion and no sclera survives the resample — a runtime
// re-measurement finds zero eyes on all 54 E/W sprites. Nothing about texture Read/Write
// changes that (the pixels come back fine through a blit); the resolution does.
//
// Entries are the RIGHT-side member (E/SE/NE) of each mirrored pair; W/SW/NW are exact
// pixel mirrors made by slice_sprites.py, so flipping a pair covers both. N and S have no
// horizontal component and can never be flipped.
// </auto-generated>
using DinoDigger.Core;

namespace DinoDigger.Config
{
    /// <summary>The audited handedness of the generated actor art: for each (species,
    /// growth stage) the facings whose art is drawn mirrored against its filename, and so
    /// the facings the importer must resolve to their mirror partner's FILE.</summary>
    public static class FacingManifest
    {
        // Indexed [(int)DinoType * 3 + (int)GrowthStage] — DinoType and GrowthStage
        // declaration order (GameEnums.cs); Baby, Kid, Big.
        private static readonly Dir8[][] DinoFlips =
        {
"""

CS_FOOTER = """        };

        // The backhoe has one art set (no growth stages).
        private static readonly Dir8[] BackhoeFlips = %(backhoe)s;   // backhoe / adult

        /// <summary>The facing whose FILE the game must display for a logical facing —
        /// the logical facing itself, or its mirror partner where the art is
        /// backwards.</summary>
        public static Dir8 DisplayedFacing(DinoType type, GrowthStage stage, Dir8 logical)
        {
            return Contains(Row(type, stage), logical) ? Mirror(logical) : logical;
        }

        /// <summary>DisplayedFacing for the backhoe, which has no growth stages.</summary>
        public static Dir8 DisplayedBackhoeFacing(Dir8 logical)
        {
            return Contains(BackhoeFlips, logical) ? Mirror(logical) : logical;
        }

        /// <summary>Horizontal mirror of a facing (N/S are their own mirrors).</summary>
        public static Dir8 Mirror(Dir8 d)
        {
            switch (d)
            {
                case Dir8.E: return Dir8.W;
                case Dir8.W: return Dir8.E;
                case Dir8.NE: return Dir8.NW;
                case Dir8.NW: return Dir8.NE;
                case Dir8.SE: return Dir8.SW;
                case Dir8.SW: return Dir8.SE;
                default: return d;
            }
        }

        private static Dir8[] Row(DinoType type, GrowthStage stage)
        {
            int i = (int)type * 3 + (int)stage;
            return i >= 0 && i < DinoFlips.Length ? DinoFlips[i] : System.Array.Empty<Dir8>();
        }

        /// <summary>Is this facing's mirror PAIR flagged? Both members of a pair share the
        /// one entry, stored under its right-side member.</summary>
        private static bool Contains(Dir8[] row, Dir8 d)
        {
            Dir8 key = d == Dir8.W ? Dir8.E : d == Dir8.SW ? Dir8.SE : d == Dir8.NW ? Dir8.NE : d;
            for (int i = 0; i < row.Length; i++)
            {
                if (row[i] == key)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
"""


def _cs_row(dirs) -> str:
    return "new Dir8[] { " + ", ".join(f"Dir8.{d}" for d in dirs) + " }" if dirs \
        else "new Dir8[] { }"


def emit_cs(man: dict) -> str:
    lines = [CS_HEADER]
    for name, folder in SPECIES:
        for stage_name, stage_key in STAGE_ORDER:
            row = _cs_row(man.get((folder, stage_key), []))
            lines.append(f"            {row + ',':38s} // {name} / {stage_name}\n")
    return "".join(lines) + CS_FOOTER % {
        "backhoe": _cs_row(man.get(("backhoe", "adult"), []))}


ROW_RE = re.compile(r"new Dir8\[\] \{([^}]*)\}[,;]?\s*//\s*(\w+) / (\w+)")
DIR_RE = re.compile(r"Dir8\.(\w+)")


def parse_cs(text: str) -> dict:
    """Best-effort read of a generated FacingManifest.cs, for diffing only."""
    out = {}
    folders = dict(SPECIES)
    stages = {n: k for n, k in STAGE_ORDER}
    for dirs, who, stage in ROW_RE.findall(text):
        folder = folders.get(who, who)
        out[(folder, stages.get(stage, stage))] = DIR_RE.findall(dirs)
    return out


def parse_importer() -> dict:
    """GeneratedArtImporter.FlippedFacingPairs, read out of the C# source."""
    with open(IMPORTER_CS, encoding="utf-8") as fh:
        text = fh.read()
    body = text.split("FlippedFacingPairs", 1)[1]
    body = body.split("};", 1)[0]
    out = {}
    for folder, stage_tok, dirs in re.findall(
            r'\(\s*"(\w+)",\s*(AdultStage|"\w+")\s*\),\s*new HashSet<Dir8>\s*\{([^}]*)\}',
            body):
        stage = "adult" if stage_tok == "AdultStage" else stage_tok.strip('"')
        out[(folder, stage)] = DIR_RE.findall(dirs)
    return out


def _norm(table: dict) -> dict:
    """Drop empty rows and sort, so the three tables compare on content alone."""
    order = ("E", "SE", "NE")
    return {k: sorted(v, key=order.index) for k, v in table.items() if v}


def _diff(label: str, want: dict, got: dict) -> int:
    bad = 0
    for key in sorted(set(want) | set(got)):
        w, g = want.get(key, []), got.get(key, [])
        if w != g:
            bad += 1
            print(f"  {label}: {key[0]}/{key[1]} shipped {g or '[]'} "
                  f"but the art measures {w or '[]'}")
    return bad


def bake() -> int:
    man = measure_manifest()
    with open(CS_PATH, "w", encoding="utf-8") as fh:
        fh.write(emit_cs(man))
    cells = sum(len(v) for v in man.values())
    print(f"wrote {os.path.relpath(CS_PATH, REPO)}: {cells} mirrored cell(s) across "
          f"{len([v for v in man.values() if v])} (actor, stage) row(s).")
    print("Now make GeneratedArtImporter.FlippedFacingPairs and "
          "bake_builder_anchors.FLIPPED_FACINGS match it, re-import, and re-run "
          "`python3 Tools/bake_builder_anchors.py`.")
    return 0


def check() -> int:
    man = measure_manifest()
    want = _norm(man)
    bad = 0

    if not os.path.exists(CS_PATH):
        print(f"MISSING: {os.path.relpath(CS_PATH, REPO)} — run --bake.")
        return 1
    with open(CS_PATH, encoding="utf-8") as fh:
        shipped = fh.read()
    if shipped != emit_cs(man):
        bad += max(1, _diff("FacingManifest.cs", want, _norm(parse_cs(shipped))))

    bad += _diff("GeneratedArtImporter.FlippedFacingPairs", want, _norm(parse_importer()))
    bad += _diff("bake_builder_anchors.FLIPPED_FACINGS", want,
                 _norm({k: list(v) for k, v in FLIPPED_FACINGS.items()}))

    if bad:
        print(f"\nSTALE: {bad} disagreement(s) with the current art. Re-run "
              "`python3 Tools/verify_facing_art.py --bake`, bring the importer and the "
              "anchor baker into step with it, re-import, then re-run "
              "`python3 Tools/bake_builder_anchors.py`.")
        return 1

    cells = sum(len(v) for v in want.values())
    print(f"OK: FacingManifest.cs, GeneratedArtImporter.FlippedFacingPairs and "
          f"bake_builder_anchors.FLIPPED_FACINGS all match the measured art "
          f"({cells} mirrored cell(s)).")
    return 0


def sheets(out_dir: str) -> int:
    os.makedirs(out_dir, exist_ok=True)
    tile_w, tile_h, label_h = 300, 300, 24
    written = []
    for folder in ACTORS:
        for stage in STAGES:
            if not os.path.exists(frame_paths(folder, stage, "S")[0]):
                continue
            sheet = Image.new("RGB", (tile_w * 8, tile_h * len(POSES)), (250, 248, 244))
            draw = ImageDraw.Draw(sheet)
            for row, pose in enumerate(POSES):
                for col, d in enumerate(DIRS):
                    src = file_dir(folder, stage, d)
                    path = frame_paths(folder, stage, src)[row]
                    if not os.path.exists(path):
                        continue
                    img = Image.open(path).convert("RGBA")
                    k = min((tile_w - 10) / img.width, (tile_h - label_h - 6) / img.height)
                    img = img.resize((max(1, int(img.width * k)),
                                      max(1, int(img.height * k))), Image.LANCZOS)
                    bg = Image.new("RGBA", img.size, (250, 248, 244, 255))
                    bg.alpha_composite(img)
                    sheet.paste(bg.convert("RGB"),
                                (col * tile_w + (tile_w - img.width) // 2,
                                 row * tile_h + label_h))
                    tag = f"{d} {pose}" + ("" if src == d else f"  [<-{src}]")
                    draw.text((col * tile_w + 6, row * tile_h + 6), tag, fill=(20, 20, 20))
                    draw.rectangle([col * tile_w, row * tile_h,
                                    col * tile_w + tile_w - 1, row * tile_h + tile_h - 1],
                                   outline=(214, 212, 208))
            out = os.path.join(out_dir, f"facing_{folder}_{stage or 'adult'}.png")
            sheet.save(out)
            written.append(out)
    print("\n".join(written))
    print(f"\n{len(written)} sheet(s). Every tile must face the direction its LABEL names; "
          "'[<-X]' marks a cell the remap redirected to its mirror partner.")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--sheets", action="store_true", help="write per-actor contact sheets")
    ap.add_argument("--audit", action="store_true", help="print the measured facing table")
    ap.add_argument("--raw", action="store_true",
                    help="with --audit: measure the raw files, ignoring the remap")
    ap.add_argument("--bake", action="store_true",
                    help="re-bake Assets/Scripts/Config/FacingManifest.cs from the art")
    ap.add_argument("--check", action="store_true",
                    help="fail if any shipped flip table disagrees with the art")
    ap.add_argument("--out", default=os.path.join(REPO, "Logs"),
                    help="directory for --sheets output")
    args = ap.parse_args()
    if args.sheets:
        return sheets(args.out)
    if args.bake:
        return bake()
    if args.check:
        return check()
    return audit(args.raw)


if __name__ == "__main__":
    sys.exit(main())
