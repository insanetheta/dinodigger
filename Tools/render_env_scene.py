#!/usr/bin/env python3
"""Offline composite of the REAL island, from the REAL scene data (DinoDigger-l9g).

The env art was signed off on `verify` sheets that show every tile ALONE. That is
exactly how a connectivity bug survives review: each tile is fine, the joins are not.
This tool closes that hole. It reads the actual cell grid out of Assets/Scenes/Main.unity
— the same streams the A* carved, the same path bands, the same pond, the same bridges —
and composes it with the shipped PNGs and the SAME tile-selection maths SceneBuilder
uses, so what comes out is what the game will draw.

    python3 Tools/render_env_scene.py            # all views -> Tools/render/
    python3 Tools/render_env_scene.py --flat     # force the OLD flat-variant painter
                                                 # (side-by-side before/after)

Views are chosen for the joins that were broken, not for the pretty bits: diagonal
stream runs, stream junctions, the pond mouth, bridge crossings, path crossroads.
"""
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO, "Tools", "venv", "lib", "python3.13",
                                "site-packages"))
sys.path.insert(0, os.path.join(REPO, "Tools"))

import numpy as np                                             # noqa: E402
from PIL import Image, ImageDraw                               # noqa: E402
import generate_env as E                                       # noqa: E402

SCENE = os.path.join(REPO, "Assets", "Scenes", "Main.unity")
TILES_META = os.path.join(REPO, "Assets", "Art", "Placeholder", "Tiles")
OUT = os.path.join(REPO, "Tools", "render")
TW, TH = E.TILE_W, E.TILE_H
N = 48

# ------------------------------------------------------------------ scene parsing

ENV_TILES_META = os.path.join(REPO, "Assets", "Art", "Generated", "env", "Tiles")


def tile_guids() -> dict:
    """guid -> tile asset name, over BOTH tile folders.

    The placeholder set (Grass/Path/Water/Bridge/Tree/Rock/Mound) AND the generated env
    set, because the scene on disk has already been rebuilt with the env tiles — it is
    the very scene Greg screenshotted, which is exactly what we want to reproduce."""
    out = {}
    for d in (TILES_META, ENV_TILES_META):
        if not os.path.isdir(d):
            continue
        for f in sorted(os.listdir(d)):
            if not f.endswith(".asset.meta"):
                continue
            with open(os.path.join(d, f)) as fh:
                m = re.search(r"^guid:\s*([0-9a-f]+)", fh.read(), re.M)
            if m:
                out[m.group(1)] = f[: -len(".asset.meta")]
    return out


def parse_tilemaps() -> list:
    """Every Tilemap component in the scene as {name_at_cell: {(x,y): tilename}}.

    Deliberately a line scanner, not a YAML load: Unity scene YAML carries custom tags
    that trip PyYAML, and all we need is m_Tiles (cell -> index) plus m_TileAssetArray
    (index -> guid), both of which are strictly ordered and trivially recognisable.
    """
    guids = tile_guids()
    maps, cur, mode = [], None, None
    cell, pending = None, []
    with open(SCENE) as fh:
        for line in fh:
            if line.startswith("Tilemap:"):
                cur = {"cells": [], "guids": []}
                maps.append(cur)
                mode = None
                continue
            if cur is None:
                continue
            if line.startswith("  m_Tiles:"):
                mode = "tiles"
                continue
            if line.startswith("  m_TileAssetArray:"):
                mode = "assets"
                continue
            if line.startswith("  m_TileSpriteArray:") or line.startswith("  m_Origin:"):
                mode = None
                continue
            if mode == "tiles":
                m = re.match(r"\s*- first: \{x: (-?\d+), y: (-?\d+)", line)
                if m:
                    cell = (int(m.group(1)), int(m.group(2)))
                    continue
                m = re.match(r"\s*m_TileIndex: (\d+)", line)
                if m and cell is not None:
                    cur["cells"].append((cell, int(m.group(1))))
                    cell = None
            elif mode == "assets":
                m = re.search(r"guid: ([0-9a-f]+)", line)
                if m:
                    cur["guids"].append(guids.get(m.group(1), "?" + m.group(1)[:6]))
    out = []
    for mp in maps:
        d = {}
        for (c, idx) in mp["cells"]:
            if idx < len(mp["guids"]):
                d[c] = mp["guids"][idx]
        if d:
            out.append(d)
    return out


def load_map():
    """The island as a char grid, in SceneBuilder's own legend."""
    grid = [["~"] * N for _ in range(N)]
    layers = parse_tilemaps()

    def classify(name):
        """Tile asset name -> map char, for BOTH the placeholder and env tile sets."""
        if name in ("Tree", "Rock"):
            return {"Tree": "T", "Rock": "R"}[name]
        if name in ("Grass", "Path", "Water", "Bridge"):
            return {"Grass": "G", "Path": "P", "Water": "W", "Bridge": "B"}[name]
        if name.startswith("bridge_"):
            return "B"
        if name.startswith("tile_grass") or name.startswith("edge_grass_"):
            return "G"          # incl. the old grass-side transitions
        if name.startswith("tile_path") or name.startswith("path_b"):
            return "P"
        if name.startswith("tile_water") or name.startswith("water_b"):
            return "W"
        if name.startswith("tile_bed") or name.startswith("bed_b"):
            return "A"
        return None             # decals and anything else: not ground

    obst, ground, water = None, None, None
    for d in layers:
        chars = {classify(n) for n in set(d.values())}
        if {"T", "R"} & chars:
            obst = d
        elif "W" in chars and not ({"G", "P"} & chars):
            water = d
        elif {"G", "P", "B", "A"} & chars:
            ground = d
    for d in (ground, water, obst):
        if not d:
            continue
        for (x, y), name in d.items():
            c = classify(name)
            if c and 0 <= x < N and 0 <= y < N:
                grid[x][y] = c
    return grid


# ------------------------------------------------- painter maths (mirrors C#)

SALT = {"grass": 0x51ED, "path": 0x2C0F, "water": 0x7A31, "bed": 0x1B95,
        "decal_place": 0x3F6B, "decal_pick": 0x6D2A}
M32 = 0xFFFFFFFF


def _i32(v):
    v &= M32
    return v - (1 << 32) if v >= (1 << 31) else v


def cell_hash(x, y, salt):
    """Bit-identical to Config/EnvDressing.Hash — the painter and this renderer MUST
    agree or the composite is a picture of something the game will never draw."""
    h = (_i32(x * 73856093) & M32) ^ (_i32(y * 19349663) & M32) ^ (_i32(salt * 83492791) & M32)
    h &= M32
    h ^= h >> 16
    h = (h * 0x7FEB352D) & M32
    h ^= h >> 15
    h = (h * 0x846CA68B) & M32
    h ^= h >> 16
    return h


def biome_of(ch):
    return {"G": "grass", "M": "grass", "D": "grass", "T": "grass", "R": "grass",
            "P": "path", "B": "path", "W": "water", "S": "water",
            "A": "bed"}.get(ch)


# Cardinal offsets in blob-bit order, then the diagonals between consecutive cardinals.
CARD = [(0, -1), (1, 0), (0, 1), (-1, 0)]
DIAG = [(1, -1), (1, 1), (-1, 1), (-1, -1)]


def blob_key(grid, x, y, members):
    """The 8-neighbour blob key for a cell, over the set of chars counted as same-biome."""
    key = 0
    for i, (dx, dy) in enumerate(CARD):
        nx, ny = x + dx, y + dy
        if 0 <= nx < N and 0 <= ny < N and grid[nx][ny] in members:
            key |= 1 << i
    for i, (dx, dy) in enumerate(DIAG):
        nx, ny = x + dx, y + dy
        if 0 <= nx < N and 0 <= ny < N and grid[nx][ny] in members:
            key |= 1 << (4 + i)
    return E.blob_normalise(key)


# Bridges read as BOTH: the channel continues under the deck and the path continues
# over it, so a stream connects toward a bridge and so does a path.
#
# '~' (off-island open sea) counts as WATER, and that is not cosmetic. The sea is
# water; without this every coastal water cell decides it borders land and grows a
# grass bank with a sand rim, which bakes a bright green-and-cream ribbon floating in
# the middle of the ocean along the whole coastline (visible in the first render of
# this fix). It is NOT counted as path or bed — a path that runs out at the coast
# should end in a grass shoulder, which is exactly what it does.
MEMBERS = {"water": set("WSB~"), "path": set("PB"), "bed": set("A")}


# ---------------------------------------------------------------------- rendering

_CACHE = {}


def art(path):
    if path not in _CACHE:
        _CACHE[path] = Image.open(path).convert("RGBA") if os.path.exists(path) else None
    return _CACHE[path]


def ground_tile(grid, x, y, flat):
    ch = grid[x][y]
    b = biome_of(ch)
    if ch == "B":
        return art(os.path.join(E.DECOR, f"bridge_{'ab'[cell_hash(x, y, SALT['path']) % 2]}.png"))
    if b == "grass":
        if flat:
            return art(os.path.join(E.GROUND,
                                    f"tile_grass_{cell_hash(x, y, SALT['grass']) % 16:02d}.png"))
        # Connected painter: grass stays a plain variant; the biome next door owns the
        # transition. Flat painter: same, plus edge_grass_* melts (added below).
        return art(os.path.join(E.GROUND,
                                f"tile_grass_{cell_hash(x, y, SALT['grass']) % 16:02d}.png"))
    if b is None:
        return None
    if flat:
        n = 4 if b == "bed" else 16
        return art(os.path.join(E.GROUND, f"tile_{b}_{cell_hash(x, y, SALT[b]) % n:02d}.png"))
    return art(os.path.join(E.GROUND, f"{b}_b{blob_key(grid, x, y, MEMBERS[b]):03d}.png"))


def flat_grass_edge(grid, x, y):
    """The OLD painter's grass-side transition, so the before/after is honest."""
    best, mask = None, 0
    for bit, (dx, dy) in enumerate(CARD):
        nx, ny = x + dx, y + dy
        if not (0 <= nx < N and 0 <= ny < N):
            continue
        nb = biome_of(grid[nx][ny])
        nb = "water" if grid[nx][ny] == "B" else nb
        if nb and nb != "grass":
            if best is None or nb == best:
                best, mask = nb, mask | (1 << bit)
    if mask and best:
        return art(os.path.join(E.GROUND, f"edge_grass_{best}_{mask}.png"))
    return None


DECALS = {"grass": ["decal_fern", "decal_moss", "decal_clover"],
          "path": ["decal_footprints", "decal_pebbles"],
          "water": ["decal_lily", "decal_lily_blossom"]}
DENSITY = {"grass": 0.20, "path": 0.34, "water": 0.22}


def decal_for(grid, x, y):
    ch = grid[x][y]
    if ch not in "GMPWS":
        return None
    b = biome_of(ch)
    if (cell_hash(x, y, SALT["decal_place"]) % 100000) / 100000 >= DENSITY[b]:
        return None
    # Lilies only where the water is genuinely open, never on a 1-cell channel where a
    # pad would sit on the bank (DinoDigger-l9g).
    if b == "water" and blob_key(grid, x, y, MEMBERS["water"]) != 255:
        return None
    names = DECALS[b]
    name = names[cell_hash(x, y, SALT["decal_pick"]) % len(names)]
    im = art(os.path.join(E.DECAL, f"{name}.png"))
    if im is None:
        return None
    w = int(round(E.DECAL_WORLD_W[name] * TW))
    return im.resize((max(1, w), max(1, int(im.height * w / im.width))), Image.LANCZOS)


def prop_for(grid, x, y):
    ch = grid[x][y]
    if ch == "T":
        return art(os.path.join(E.PROP, "tree_cycad.png"))
    if ch == "R":
        return art(os.path.join(E.PROP, "rock_boulder.png"))
    return None


def render(grid, x0, y0, w, h, flat=False, decals=True, sky=(140, 196, 226)):
    """Compose a rectangular cell window as the game would draw it."""
    cw, ch_ = w + h, w + h
    img = Image.new("RGBA", (cw * TW // 2 + TW, ch_ * TH // 2 + 3 * TH), sky + (255,))

    def centre(x, y):
        return ((x - x0) - (y - y0)) * TW // 2 + cw * TW // 4 + TW // 2, \
               ((x - x0) + (y - y0)) * TH // 2 + TH

    def paste(im, x, y, dy=0):
        if im is None:
            return
        cx, cy = centre(x, y)
        img.alpha_composite(im, (cx - im.width // 2, cy - im.height // 2 + dy))

    cells = [(x, y) for x in range(x0, x0 + w) for y in range(y0, y0 + h)]
    cells.sort(key=lambda c: (c[0] - x0) + (c[1] - y0))

    for (x, y) in cells:                                    # 1) ground + water
        paste(ground_tile(grid, x, y, flat), x, y)
        if flat and biome_of(grid[x][y]) == "grass" and grid[x][y] != "B":
            paste(flat_grass_edge(grid, x, y), x, y)
    if decals:
        for (x, y) in cells:                                # 2) scatter
            paste(decal_for(grid, x, y), x, y)
    for (x, y) in cells:                                    # 3) props, back to front
        paste(prop_for(grid, x, y), x, y, dy=-TH // 2)
    return img


def label(img, text):
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, img.width, 34], fill=(20, 24, 30, 235))
    d.text((10, 9), text, fill=(255, 255, 255, 255), font=E._font(20, True))
    return img


# ----------------------------------------------------------------------- views

def find_views(grid):
    """Locate the joins that were reported broken, from the real grid."""
    views = []

    def count(x0, y0, w, h, pred):
        return sum(1 for x in range(x0, x0 + w) for y in range(y0, y0 + h)
                   if 0 <= x < N and 0 <= y < N and pred(grid[x][y]))

    # Diagonal stream run: the window with the most stream cells that is NOT the pond.
    best = None
    for x in range(0, N - 12):
        for y in range(0, N - 12):
            wet = count(x, y, 12, 12, lambda c: c == "W")
            if 12 <= wet <= 34:
                score = wet + count(x, y, 12, 12, lambda c: c == "B") * 6
                if best is None or score > best[0]:
                    best = (score, x, y)
    if best:
        views.append(("stream_run", best[1], best[2], 12, 12))

    # Bridge crossing.
    bridges = [(x, y) for x in range(N) for y in range(N) if grid[x][y] == "B"]
    if bridges:
        bx, by = bridges[len(bridges) // 2]
        views.append(("bridge_crossing", max(0, bx - 5), max(0, by - 5), 11, 11))

    # Pond mouth: the pond edge nearest an incoming stream.
    pond = [(x, y) for x in range(N) for y in range(N)
            if grid[x][y] == "W" and count(x, y, 1, 1, lambda c: True)]
    if pond:
        cx = sum(p[0] for p in pond) // len(pond)
        cy = sum(p[1] for p in pond) // len(pond)
        views.append(("pond_mouth", max(0, cx - 6), max(0, cy - 7), 14, 14))

    # Path crossroads: the window with the most path cells.
    best = None
    for x in range(0, N - 12):
        for y in range(0, N - 12):
            p = count(x, y, 12, 12, lambda c: c == "P")
            g = count(x, y, 12, 12, lambda c: c == "G")
            if p > 16 and g > 30 and (best is None or p > best[0]):
                best = (p, x, y)
    if best:
        views.append(("path_crossroads", best[1], best[2], 12, 12))

    # The Berry Patch: the only garden-bed biome on the island, and the third biome that
    # had to grow connected pieces.
    bed = [(x, y) for x in range(N) for y in range(N) if grid[x][y] == "A"]
    if bed:
        bx = sum(p[0] for p in bed) // len(bed)
        by = sum(p[1] for p in bed) // len(bed)
        views.append(("garden_bed", max(0, bx - 5), max(0, by - 5), 11, 11))

    # One wide shot with everything in it, for the ticket.
    views.append(("island_wide", 8, 18, 26, 22))
    return views


REVIEW_W = 1500     # per-panel width in the combined review sheet


def review_sheet(panels) -> Image.Image:
    """One stacked artifact for review, so a reviewer looks at the JOINS in context
    rather than at a grid of tiles in isolation — which is exactly the review that let
    DinoDigger-l9g through."""
    scaled = []
    for img in panels:
        k = REVIEW_W / img.width
        scaled.append(img.convert("RGB").resize(
            (REVIEW_W, max(1, int(img.height * k))), Image.LANCZOS))
    gap = 8
    sheet = Image.new("RGB", (REVIEW_W, sum(i.height for i in scaled) + gap * len(scaled)),
                      (18, 20, 24))
    y = 0
    for i in scaled:
        sheet.paste(i, (0, y))
        y += i.height + gap
    return sheet


def main():
    flat_too = "--flat" in sys.argv
    os.makedirs(OUT, exist_ok=True)
    grid = load_map()
    tally = {}
    for x in range(N):
        for y in range(N):
            tally[grid[x][y]] = tally.get(grid[x][y], 0) + 1
    print("island parsed from Main.unity:", tally)

    panels = []
    for name, x0, y0, w, h in find_views(grid):
        for flat in ((True, False) if flat_too else (False,)):
            img = render(grid, x0, y0, w, h, flat=flat)
            tag = "BEFORE (flat variants + grass-side edges)" if flat else \
                  "AFTER (connected topology-keyed pieces)"
            label(img, f"{name}  cells ({x0},{y0}) {w}x{h}   {tag}")
            p = os.path.join(OUT, f"{name}{'_before' if flat else ''}.png")
            img.convert("RGB").save(p)
            print("  wrote", os.path.relpath(p, REPO), img.size)
            panels.append(img)

    sheet = review_sheet(panels)
    sp = os.path.join(OUT, "connectivity_review.png")
    sheet.save(sp)
    print("  wrote", os.path.relpath(sp, REPO), sheet.size, "<- THE review artifact")


if __name__ == "__main__":
    main()
