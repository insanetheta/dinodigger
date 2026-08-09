#!/usr/bin/env python3
"""Concept art for the terrain pass pitch (DinoDigger-cdt, docs/terrain-art-pitch.md).

These are CONCEPTS, not game assets. Raw gens land in Tools/raw/terr_*.png and
processed outputs land in Assets/Art/Concepts/terrain/ — deliberately NOT under
Assets/Art/Generated/ so the sprite importer/slicer manifests ignore them.

What gets made:
    terr_grass_plate   full-bleed top-down mottled Jurassic grass swatch (1024^2)
    terr_path_plate    full-bleed top-down warm earth path swatch w/ mineral veins
    terr_water_plate   full-bleed top-down calm water swatch w/ lily accents
    terr_decals        2x2 magenta-keyed decal sheet: fern / moss / footprints / stones
    mockup_meadow_before_after.png   real current tiles vs concept re-dress, meadow+path
    mockup_stream_before_after.png   same, stream+bridge region — actors on both

STYLE CONTRACT (the pitch's rules, enforced here, not just prompted):
  * Terrain gets NO dark outlines — outlines are the tap language, reserved for
    actors/tappables. TERRAIN_STYLE inverts the character STYLE on purpose.
  * Saturation and value contrast are CAPPED IN POST (quiet()): the model always
    over-saturates; we never trust the prompt to keep terrain quieter than actors.
  * Big soft shapes only; interior texture is 2-3 close tones, no noise.

PROMPT SCARS (inherited — do not simplify away):
  * INHERITED from generate_dig_props: lone small marks invite an inventor — the
    footprint decal pins "flat painted marks, no creature anywhere".
  * INHERITED from generate_dig_props: pit/ring shapes read as FACES; every prompt
    carries the faceless clause.
  * INHERITED from generate_sprites BG_STYLE: full-bleed art must forbid frames,
    borders, vignettes; plates additionally forbid horizon/sky/perspective so the
    swatch stays a flat overhead texture, not a landscape painting.
  * NEW — "volcanic" is poison for a toddler palette (lava, smoke, danger). The
    warm accent is phrased as "smooth warm amber stones", never named volcanic.

Usage:
    python3 generate_terrain_concepts.py list
    python3 generate_terrain_concepts.py gen [<name>...]    # (re)generate raw
    python3 generate_terrain_concepts.py slice [<name>...]  # raw -> Concepts/terrain
    python3 generate_terrain_concepts.py mock               # before/after sheets
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

RAW = G.RAW_DIR
OUT = os.path.join(REPO, "Assets", "Art", "Concepts", "terrain")
PLACEHOLDER = os.path.join(REPO, "Assets", "Art", "Placeholder", "Sprites")
BACKHOE = os.path.join(REPO, "Assets", "Art", "Generated", "backhoe",
                       "backhoe_SE.png")
DINO = os.path.join(REPO, "Assets", "Art", "Generated", "parasaurolophus",
                    "kid_SE.png")

# --- Style ------------------------------------------------------------------------
# Deliberate inverse of generate_sprites.STYLE: terrain is the quiet layer UNDER the
# outlined actors, so outlines/saturation/sparkle are all forbidden here.
TERRAIN_STYLE = (
    "Soft quiet cartoon GROUND TEXTURE for a preschool game, meant to sit BEHIND "
    "bold black-outlined characters without competing with them. Gentle slightly "
    "muted colors, LOW contrast, soft flat painterly shapes with absolutely NO "
    "outlines anywhere: no dark lines, no black edges, no line art. Big simple "
    "rounded readable shapes only — no fine noise, no busy small detail, no "
    "high-frequency texture, no obvious repeating pattern. Perfectly flat overhead "
    "top-down view looking straight down at the ground, like a texture swatch: NO "
    "horizon, NO sky, NO perspective, NO landscape, no hills, no depth. The texture "
    "fills the ENTIRE frame edge to edge: no border, no frame, no margin, no "
    "vignette, no rounded corners. No creatures, no animals, no insects, no faces, "
    "no eyes, no characters of any kind. Absolutely no text, no letters, no "
    "numbers, no words, no logos, no watermark. "
)

DECAL_STYLE = (
    "Soft quiet cartoon ground-decoration decals for a preschool game, meant to be "
    "scattered on grass BEHIND bold black-outlined characters. Gentle slightly "
    "muted colors, soft flat painterly shapes with NO dark outlines: no black "
    "edges, no line art. Seen from overhead, flat against the ground. Each decal "
    "is a plain faceless object: no creatures, no animals, no insects, no faces, "
    "no eyes, no mouths, no characters. Absolutely no text, no letters, no "
    "numbers, no words, no logos, no watermark. The entire background must be a "
    "single solid flat pure magenta color #FF00FF with nothing else on it, no "
    "gradient, no vignette, and the decals cast NO shadows. "
)

SPECS = {
    "terr_grass_plate": dict(
        kind="plate",
        prompt=(
            f"Generate an image. {TERRAIN_STYLE}"
            "The texture: a lush calm prehistoric meadow of soft spring-green "
            "grass. The ground is gently mottled from only two or three CLOSE "
            "shades of leaf green, in big soft irregular blotches that melt into "
            "each other with soft edges. Scattered sparsely across it: a few soft "
            "clusters of small light yellow-green fern fronds lying flat against "
            "the grass, two or three tiny pale-green clover patches, and one or "
            "two slightly darker soft moss patches. Most of the swatch is plain "
            "calm grass — the accents are few, small and far apart."),
    ),
    "terr_path_plate": dict(
        kind="plate",
        # The raw gen leans salmon; pull its mean hard onto the shipped path tan
        # (slightly lifted so the re-dress stays airy).
        quiet_kw=dict(sat=0.72, tint=(206, 176, 128), tint_amt=0.9),
        prompt=(
            f"Generate an image. {TERRAIN_STYLE}"
            "The texture: a warm packed-earth dirt path surface in soft sandy "
            "tans. The ground is gently mottled from only two or three CLOSE "
            "shades of warm tan and light earthy brown, in big soft irregular "
            "blotches that melt into each other. Scattered sparsely across it: a "
            "few faint slightly-paler cream mineral veins meandering gently like "
            "thin soft ribbons, and a few small smooth rounded pebbles in a "
            "slightly warmer soft amber tone. Most of the swatch is plain calm "
            "packed earth — the veins and pebbles are faint, few and far apart."),
    ),
    "terr_water_plate": dict(
        kind="plate",
        # Water may keep more chroma than land — it must still read as WATER (a
        # hard walkability boundary) at toddler glance-speed.
        quiet_kw=dict(sat=0.95, con=0.95, lift=0.0,
                      tint=(80, 150, 230), tint_amt=0.20),
        prompt=(
            f"Generate an image. {TERRAIN_STYLE}"
            "The texture: calm shallow fresh water in soft friendly blues. The "
            "water is gently mottled from only two or three CLOSE shades of soft "
            "blue, with a few wide soft lighter ripple bands curving gently "
            "across it. Floating on it: exactly TWO round soft-green lily pads, "
            "one with a single tiny pale-pink blossom. Most of the swatch is "
            "plain calm water — the lily pads are small and the ripples are "
            "faint and wide."),
    ),
    "terr_decals": dict(
        kind="decals",
        names=["decal_fern", "decal_moss", "decal_footprints", "decal_stones"],
        prompt=(
            f"Generate an image. {DECAL_STYLE}"
            "A 2x2 grid of four separate small ground decals, evenly spaced and "
            "the same size, one per cell, each floating alone on the flat "
            "magenta. Top-left: ONE small cluster of soft light yellow-green "
            "fern fronds fanning out from a center, lying flat. Top-right: ONE "
            "soft irregular moss patch in two close soft greens with three tiny "
            "round pale-green clover dots on it. Bottom-left: ONE short walking "
            "trail of exactly three small round soft warm-tan footprint marks in "
            "a gentle zigzag line — they are simple flat painted oval marks on "
            "nothing, there is NO creature, NO foot and NO animal anywhere in "
            "the picture. Bottom-right: ONE small cluster of exactly three "
            "smooth rounded stones in warm soft amber-orange, nestled together, "
            "with a faint warm cream glow between them — plain stones, no "
            "cracks, no lava, no fire, no smoke."),
    ),
}

ORDER = list(SPECS)

# --- Post: the quiet pass -----------------------------------------------------------
# The pitch's rule "terrain saturation/contrast is capped below actors" is enforced
# here in the pipeline. Never trust the prompt for this — the model over-saturates.


def quiet(img: Image.Image, sat: float = 0.80, con: float = 0.85,
          lift: float = 4.0, tint=None, tint_amt: float = 0.0) -> Image.Image:
    """Cap saturation, compress value contrast around the mean, gently lift, and
    optionally pull the palette toward the shipped flat tile's base color so the
    re-dress stays continuous with the game's existing palette."""
    a = np.asarray(img.convert("RGB")).astype(np.float32)
    mean = a.mean(axis=(0, 1), keepdims=True)
    a = mean + (a - mean) * con                      # compress contrast
    grey = a.mean(axis=2, keepdims=True)
    a = grey + (a - grey) * sat                      # cap saturation
    if tint is not None and tint_amt > 0:
        # Reinhard-style: move the image mean toward the shipped flat tile's base
        # color by tint_amt (1.0 = exact mean match). Keeps palette continuity.
        t = np.asarray(tint, dtype=np.float32).reshape(1, 1, 3)
        cur = a.mean(axis=(0, 1), keepdims=True)
        a = a + (t - cur) * tint_amt
    a = np.clip(a + lift, 0, 255)                    # keep it airy, never muddy
    return Image.fromarray(a.astype(np.uint8), "RGB")


# --- gen / slice --------------------------------------------------------------------

def gen(name: str) -> bool:
    b64 = G._attempt(SPECS[name]["prompt"], None, name, 2)
    if not b64:
        print(f"FAILED {name}")
        return False
    G._save_raw(b64, os.path.join(RAW, f"{name}.png"))
    return True


def neutralize(img: Image.Image) -> Image.Image:
    """Same scar as generate_machine_concepts.neutralize: the chroma key only zeroes
    ALPHA, so transparent RGB stays magenta and bleeds back as a pink halo under
    bilinear filtering. Replace transparent RGB with the median opaque color."""
    a = np.asarray(img).copy()
    al = a[..., 3]
    solid = al > 200
    if not solid.any():
        return img
    med = np.median(a[solid][:, :3], axis=0).astype(np.uint8)
    a[al == 0, 0:3] = med
    return Image.fromarray(a, mode="RGBA")


def _unmix(keyed: Image.Image, cell: Image.Image) -> Image.Image:
    """De-magenta the feathered pixels. These decals have soft unoutlined edges and
    one has a glow, so the chroma key leaves a lot of semi-transparent pixels whose
    RGB is still partly magenta (pink halo). Treat each such pixel as fg composited
    over the cell's border-median bg and solve for the foreground color."""
    a = np.asarray(keyed).astype(np.float32)
    src = np.asarray(cell.convert("RGB")).astype(np.float32)
    border = np.concatenate([src[0], src[-1], src[:, 0], src[:, -1]])
    bg = np.median(border, axis=0)
    al = a[..., 3:4] / 255.0
    mid = (al[..., 0] > 0.02) & (al[..., 0] < 0.98)
    fg = np.clip((src - (1.0 - al) * bg) / np.maximum(al, 0.02), 0, 255)
    a[mid, 0:3] = fg[mid]
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def _slice_decals(name: str, pad: int = 6) -> None:
    """Quadrant-slice the 2x2 decal sheet, chroma-key each cell."""
    spec = SPECS[name]
    raw = Image.open(os.path.join(RAW, f"{name}.png")).convert("RGB")
    w, h = raw.size
    cells = [raw.crop((0, 0, w // 2, h // 2)), raw.crop((w // 2, 0, w, h // 2)),
             raw.crop((0, h // 2, w // 2, h)), raw.crop((w // 2, h // 2, w, h))]
    os.makedirs(OUT, exist_ok=True)
    for cell, out_name in zip(cells, spec["names"]):
        img = S.despeckle(S.chroma_key(cell))
        img = _unmix(img, cell)
        img = S.trim(img, pad)
        img = neutralize(img)
        out = os.path.join(OUT, f"{out_name}.png")
        img.save(out)
        print(f"       {out}  ({img.width}x{img.height})")


def slice_one(name: str) -> None:
    spec = SPECS[name]
    raw = os.path.join(RAW, f"{name}.png")
    if not os.path.exists(raw):
        print(f"[skip] {name}: no raw", file=sys.stderr)
        return
    if spec["kind"] == "decals":
        _slice_decals(name)
        return
    os.makedirs(OUT, exist_ok=True)
    img = quiet(Image.open(raw), **spec.get("quiet_kw", {}))
    out = os.path.join(OUT, name.replace("terr_", "") + ".png")
    img.save(out)
    print(f"       {out}  ({img.width}x{img.height})")


# --- Mockups ------------------------------------------------------------------------
# Iso convention matching the shipped tilemap (cell 1 x 0.5, tiles 128x64):
# cell (x,y) diamond center lands at  X = (x-y)*64 + rows*64,  Y = (x+y)*32 + 32.

CELL = 128          # map-space pixels per cell (plates are 1024 = 8 cells)

# Region layouts. G grass, P path, W water, B bridge; props: T tree, R rock, M mound
# (props sit on grass). Both mockups share the actor placement code so BEFORE and
# AFTER differ ONLY in terrain.
MEADOW = [
    "GGGGGGGGPGGG",
    "GGT GGGGPPGG".replace(" ", "G"),
    "GGGGGGGPPGTG",
    "GGPPPPPPGGGG",
    "GPPGGGGGGRGG",
    "GPGGGMGGGGGG",
    "GPGGGGGGGTGG",
    "GGGGRGGGGGGG",
    "GGGGGGGGGGGG",
]
STREAM = [
    "GGGGGTWWGGGG",
    "GGGGGGWWGGGG",
    "GGPGGGWWGGRG",
    "GGPPPPBBPPGG",
    "GGGGGGWWGPGG",
    "GRGGGGWWGGGG",
    "GGGGGWWGGTGG",
    "GGTGGWWGGGGG",
    "GGGGGWWGGGGG",
]
PROPS = set("TRM")
GROUND_OF_PROP = "G"        # props stand on grass


def _tile(name: str) -> Image.Image:
    return Image.open(os.path.join(PLACEHOLDER, f"{name}.png")).convert("RGBA")


def _center(x: int, y: int, rows: int) -> tuple[int, int]:
    return (x - y) * 64 + rows * 64, (x + y) * 32 + 32


def _canvas_size(cols: int, rows: int) -> tuple[int, int]:
    return (cols + rows) * 64, (cols + rows) * 32 + 32


def _mirror_tile(plate: Image.Image, w: int, h: int) -> Image.Image:
    """Cover (w,h) with the plate, mirror-tiled so edges stay continuous."""
    pw, ph = plate.size
    big = Image.new("RGB", (w, h))
    for j in range(0, h, ph):
        for i in range(0, w, pw):
            t = plate
            if (i // pw) % 2:
                t = t.transpose(Image.FLIP_LEFT_RIGHT)
            if (j // ph) % 2:
                t = t.transpose(Image.FLIP_TOP_BOTTOM)
            big.paste(t, (i, j))
    return big


def _soft_mask(layout, rows, cols, kinds: str, blur: int = 18) -> Image.Image:
    m = Image.new("L", (cols * CELL, rows * CELL), 0)
    px = m.load()
    for y in range(rows):
        for x in range(cols):
            k = layout[y][x]
            if k in kinds or (k in PROPS and GROUND_OF_PROP in kinds):
                for v in range(y * CELL, (y + 1) * CELL):
                    for u in range(x * CELL, (x + 1) * CELL):
                        px[u, v] = 255
    return m.filter(ImageFilter.GaussianBlur(blur))


def _after_ground(layout) -> Image.Image:
    """Compose the whole re-dressed ground in map space, then project to iso."""
    rows, cols = len(layout), len(layout[0])
    w, h = cols * CELL, rows * CELL
    grass = _mirror_tile(Image.open(os.path.join(OUT, "grass_plate.png")), w, h)
    path = _mirror_tile(Image.open(os.path.join(OUT, "path_plate.png")), w, h)
    water = _mirror_tile(Image.open(os.path.join(OUT, "water_plate.png")), w, h)

    ground = grass.copy()
    # Path melts into grass along a soft irregular edge. The blur is kept tight
    # and the alpha curve steepened: a wide half-blend ring of tan over the
    # grass's darker olive blotches reads as a muddy rust halo (v1 lesson).
    pmask = _soft_mask(layout, rows, cols, "P", blur=12).point(
        lambda v: max(0, min(255, (v - 110) * 3 + 128)))
    ground.paste(path, (0, 0), pmask)
    # Water gets a pale sandy shore rim, then a slightly crisper waterline.
    wmask = _soft_mask(layout, rows, cols, "WB", blur=14)
    shore = wmask.point(lambda v: min(255, v * 3)).filter(
        ImageFilter.GaussianBlur(8))
    sand = Image.new("RGB", (w, h), (214, 196, 150))
    ground.paste(sand, (0, 0), shore.point(lambda v: v // 2))
    ground.paste(water, (0, 0), wmask.point(lambda v: 255 if v > 110 else 0)
                 .filter(ImageFilter.GaussianBlur(3)))

    # Project map space -> iso screen space in one affine transform.
    W, H = _canvas_size(cols, rows)
    off = len(layout) * 64
    iso = ground.convert("RGBA").transform(
        (W, H), Image.AFFINE, (1, 2, -off * 1.0, -1, 2, off * 1.0),
        resample=Image.BILINEAR, fillcolor=(0, 0, 0, 0))
    return iso


def _before_ground(layout) -> Image.Image:
    rows, cols = len(layout), len(layout[0])
    W, H = _canvas_size(cols, rows)
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    tiles = {"G": _tile("tile_grass"), "P": _tile("tile_path"),
             "W": _tile("tile_water"), "B": _tile("tile_bridge")}
    for y in range(rows):
        for x in range(cols):
            k = layout[y][x]
            t = tiles[GROUND_OF_PROP if k in PROPS else k]
            cx, cy = _center(x, y, rows)
            img.alpha_composite(t, (cx - 64, cy - 32))
    return img


def _paste_bottom(img, sprite, cx, cy, width):
    """Paste sprite scaled to `width`, bottom-center anchored at (cx, cy)."""
    s = width / sprite.width
    sp = sprite.resize((width, max(1, int(sprite.height * s))), Image.LANCZOS)
    img.alpha_composite(sp, (cx - sp.width // 2, cy - sp.height + 8))


def _props_and_actors(img, layout, dressed: bool, rng):
    rows, cols = len(layout), len(layout[0])
    tree, rock, mound = _tile("tile_tree"), _tile("tile_rock"), _tile("tile_mound")
    decals = {}
    if dressed:
        for n in ["decal_fern", "decal_moss", "decal_footprints", "decal_stones"]:
            p = os.path.join(OUT, f"{n}.png")
            if os.path.exists(p):
                decals[n] = Image.open(p).convert("RGBA")
        # Decal language (readability rule: nothing on grass may look pickable):
        #   grass  -> fern + moss only, sparse
        #   path   -> footprint trails only
        #   stones -> exactly ONE warm accent per region, beside the path
        def put(name, x, y, wpx):
            d = decals.get(name)
            if d is None:
                return
            s = wpx / d.width
            dd = d.resize((wpx, max(1, int(d.height * s * 0.62))),
                          Image.LANCZOS)   # squash toward ground plane
            cx, cy = _center(x, y, rows)
            img.alpha_composite(dd, (cx - dd.width // 2, cy - dd.height // 2))

        stones_done = False
        for y in range(rows):
            for x in range(cols):
                k = layout[y][x]
                if k == "G":
                    nbrs = [layout[j][i] for i, j in
                            ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1))
                            if 0 <= i < cols and 0 <= j < rows]
                    if not stones_done and "P" in nbrs and x > 2 and y > 2:
                        put("decal_stones", x, y, 52)
                        stones_done = True
                    elif rng.random() < 0.11:
                        put(rng.choice(["decal_fern", "decal_moss"]), x, y,
                            72 if rng.random() < 0.5 else 58)
                elif k == "P" and rng.random() < 0.18:
                    put("decal_footprints", x, y, 40)
    # Bridge planks stay in both versions (structures are out of scope), but the
    # placeholder bridge tile has the blue water diamond BAKED IN — on the dressed
    # ground that baked blue clashes with the soft waterline, so extract just the
    # grey plank pixels (low blue-dominance) for the AFTER panel.
    bridge = _tile("tile_bridge")
    if dressed:
        b = np.asarray(bridge).copy()
        blue_bg = b[..., 2].astype(int) - b[..., 0].astype(int) > 40
        b[blue_bg, 3] = 0
        bridge = Image.fromarray(b, "RGBA")
    for y in range(rows):
        for x in range(cols):
            if layout[y][x] == "B":
                cx, cy = _center(x, y, rows)
                img.alpha_composite(bridge, (cx - 64, cy - 32))
    # Props, painter order.
    for y in range(rows):
        for x in range(cols):
            k = layout[y][x]
            if k not in PROPS:
                continue
            cx, cy = _center(x, y, rows)
            spr = {"T": tree, "R": rock, "M": mound}[k]
            img.alpha_composite(spr, (cx - spr.width // 2,
                                      cy + 16 - spr.height))
    # Actors: identical placement in BEFORE and AFTER — the whole test.
    backhoe = Image.open(BACKHOE).convert("RGBA")
    dino = Image.open(DINO).convert("RGBA")
    rows_ = rows
    bx, by = _center(4, 6, rows_)
    dx, dy = _center(7, 5, rows_)
    _paste_bottom(img, backhoe, bx, by + 16, 240)
    _paste_bottom(img, dino, dx, dy + 12, 120)


def _compose(layout, name: str) -> str:
    import random
    rows, cols = len(layout), len(layout[0])
    panels = []
    for dressed in (False, True):
        g = _after_ground(layout) if dressed else _before_ground(layout)
        _props_and_actors(g, layout, dressed, random.Random(7))
        panels.append(g)
    W, H = panels[0].size
    pad, label_h = 24, 0
    sheet = Image.new("RGB", (W * 2 + pad * 3, H + pad * 2), (247, 244, 238))
    for i, p in enumerate(panels):
        sheet.paste(p, (pad + i * (W + pad), pad), p)
    out = os.path.join(OUT, f"mockup_{name}_before_after.png")
    sheet.save(out)
    print(f"       {out}  ({sheet.width}x{sheet.height})")
    return out


def mock():
    _compose(MEADOW, "meadow")
    _compose(STREAM, "stream")


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "list"
    names = sys.argv[2:] or ORDER
    if cmd == "list":
        for n in ORDER:
            print(n)
    elif cmd == "gen":
        ok = all(gen(n) for n in names)
        sys.exit(0 if ok else 1)
    elif cmd == "slice":
        for n in names:
            slice_one(n)
    elif cmd == "mock":
        mock()
    else:
        print(__doc__)
        sys.exit(2)
