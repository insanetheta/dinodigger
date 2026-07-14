# Dino Digger — AI character-art pipeline

Generates all sprite art for the game from text prompts via OpenRouter's Gemini
Flash Image model, then slices/cleans the raw output into transparent, game-ready
PNGs.

**Art direction:** chunky toddler-friendly cartoon, thick dark outlines, bright
saturated colors, soft shading, no text. Everything is generated on a solid
magenta (`#FF00FF`) background which the slicer keys out to transparent alpha.

**Model:** `google/gemini-2.5-flash-image` (the proven model from
`witsandfools/Assets/Art/generate_cards.py`). It is used both for text-to-image
(item sheets, the front view of each character) and image-to-image (rotating the
front view into the other facings — see below).

## Layout

```
Tools/
  generate_sprites.py   # prompts -> OpenRouter -> Tools/raw/*.png   (costs money)
  slice_sprites.py      # Tools/raw/*.png -> Assets/Art/Generated/<group>/*.png
  .openrouter_key       # your OpenRouter API key (untracked, never printed)
  raw/                  # raw generations: <char>_{S,SE,E,NE,N}.png + item sheets
Assets/Art/Generated/
  backhoe/ trex/ triceratops/ brachiosaurus/ stegosaurus/   # <char>_S.png ... _SW.png
  eggs/ fruit/ treasure/ dirt/ particles/                    # item PNGs
  digbg/                                                     # dig_background.png (full-bleed)
```

## Setup

1. Python 3.10+ with **Pillow** and **numpy** (that's all — no scipy/OpenCV):
   ```bash
   pip3 install --user Pillow numpy
   # or, if the system python is externally managed (PEP 668):
   python3 -m venv Tools/venv && Tools/venv/bin/pip install Pillow numpy
   # then run the scripts with Tools/venv/bin/python3 instead of python3
   ```
2. API key. Put your OpenRouter key in `Tools/.openrouter_key` (one line, already
   present) — or export `OPENROUTER_API_KEY`. The key is read the same lambda way
   as the witsandfools card generator and is **never printed or committed**.

## Run order

```bash
cd Tools

# 1. Generate raw images (idempotent — skips anything already in raw/)
python3 generate_sprites.py            # generate everything missing
python3 generate_sprites.py --force    # force regenerate ALL (spends API credits)
python3 generate_sprites.py --only trex # (re)generate one entry
python3 generate_sprites.py --list     # list all entry names

# 2. Slice + chroma-key into transparent, trimmed PNGs (free, safe to re-run)
python3 slice_sprites.py               # process everything in raw/
python3 slice_sprites.py --only trex   # one entry
python3 slice_sprites.py --pad 12      # override transparent border padding
```

If a generation comes back malformed, regenerate just that entry (`--only <name>`,
which forces a fresh render, with up to 3 attempts built in) and re-slice it.

## How the art is produced

**Item sheets** (eggs, fruit, treasure, dirt, particles) are a single text-to-image
generation each, laid out as a small grid, then split into cells.

**Character turnarounds.** The image model reliably renders one clean centered
subject but will *not* honor an 8-view 4×2 grid (it forces a square 3×3 of
repetitive, non-distinct angles). So each character is built from the 5 genuinely
distinct facings and the other 3 are mirrored:

1. Generate the front view **S** (text-to-image).
2. Feed S back as a reference image and rotate it (image-to-image) into **SE, E,
   NE, N** — this keeps the character consistent across angles.
3. The slicer horizontally **mirrors** SE→SW, E→W, NE→NW to complete the 8-way set.

`front == S` (facing camera-south in the isometric view). The 8 directions and how
they map to the generated/mirrored sources:

```
   N        NE / NW       E / W        SE / SW        S
  back    back 3/4     side profile  front 3/4     front
 (gen)  (gen / mirror) (gen / mirror)(gen / mirror) (gen)
```

**Background removal.** The model paints "magenta" as a pink (~RGB 230,40,150) that
varies per image and sits close to reds/pinks, and it shares its hue with the purple
characters — so neither a color-distance nor a hue key works alone. `slice_sprites.py`
instead: auto-detects the bg color from each image's border, then keys a pixel out if
it is **either** background-connected via a flood fill from the border (removes the
outer background, any connected ground-shadow, and the feather band) **or**
near-identical to the bg color (removes enclosed true-magenta holes, e.g. the gap
between the backhoe's arm and cab). Merely-reddish enclosed interiors (heart, apple,
watermelon) are preserved. Edges are feathered (1px) and borders trimmed with a few
px of padding. Tunables live at the top of the file (`T_FLOOD`, `T_STRICT`, `--pad`).

## What gets generated

Turnaround characters (8 directional PNGs each, `<char>_<DIR>.png`):

| entry | subject |
|-------|---------|
| `backhoe` | friendly yellow cartoon backhoe excavator, cute eyes on the cab |
| `trex` | chubby happy baby green T-Rex |
| `triceratops` | chubby happy baby orange Triceratops |
| `brachiosaurus` | chubby happy baby blue Brachiosaurus |
| `stegosaurus` | chubby happy baby purple Stegosaurus |

Item sheets:

| entry | grid | outputs |
|-------|------|---------|
| `eggs` | 2×2 | `egg_green` `egg_orange` `egg_blue` `egg_purple` |
| `fruit` | 2×2 | `fruit_apple` `fruit_banana` `fruit_berries` `fruit_watermelon` |
| `treasure` | 2×2 | `treasure_coin` `treasure_gem` `treasure_boot` `treasure_bone` |
| `dirt` | 1×3 | `dirt_crack_0` `dirt_crack_1` `dirt_crack_2` |
| `particles` | 1×3 | `particle_star` `particle_heart` `particle_crumb` |

Full-bleed backgrounds (`"category": "background"`, single landscape image, **no**
magenta key — the slicer copies/normalizes it straight through, no chroma-key, no
trim, because a backdrop must fill the frame edge to edge):

| entry | output | notes |
|-------|--------|-------|
| `digbg` | `digbg/dig_background` | side-view dig-mode backdrop: sky + sun + clouds, green grass lip, brown soil strata, pebbles, a worm, edge roots |

## Adding a NEW dinosaur (or vehicle)

1. Add one entry to the `SPRITES` list in `generate_sprites.py`. A turnaround entry
   needs only three fields — `name`, `outdir` (use the same lowercase id), and
   `subject` (the one-line character description):

   ```python
   {
       "name": "ankylosaurus", "category": "turnaround", "outdir": "ankylosaurus",
       "subject": ("a chubby happy baby teal Ankylosaurus dinosaur with a round "
                   "armored back and a club tail, big sparkly eyes"),
   },
   ```

2. Regenerate just that entry and slice it:

   ```bash
   python3 generate_sprites.py --only ankylosaurus   # 5 img2img calls
   python3 slice_sprites.py --only ankylosaurus      # -> 8 directional PNGs
   ```

For a new **item** sheet instead, set `"category": "item"`, add `grid` (rows, cols),
`prefix` (usually `""`), and `cells` (full base names in row-major order). The 5
generated facings (`GEN_DIRS`) and the mirror map (`MIRROR`) are defined once at the
top of `generate_sprites.py`, so turnaround output naming stays consistent for every
character automatically.

## Walk-stride frames (y85.1)

Category `strides` img2img-edits each canonical facing's EXISTING raw sprite into
two mid-stride poses (`walkA` = left leg forward, `walkB` = right leg forward).
The prompt pins everything except the legs (head, colors, canvas position, scale,
and — critically — the CAMERA ANGLE, or back views get spun around to face the
camera). Per entry, `stride_stages` picks which age stages get strides
(`None` = adult; pilot: trex `[None]` only, y85.2 flips the rest to
`[None, "kid", "baby"]`).

```bash
python3 generate_sprites.py --only trex_strides   # 5 dirs x 2 poses, resumable
python3 slice_sprites.py   --only trex_strides    # aligned 3-frame sets, 8 dirs
```

Output naming (mirrors the idle convention — adult unprefixed, stages prefixed):

| stage | idle | stride A | stride B |
|-------|------|----------|----------|
| adult | `<dino>_<DIR>.png` | `walkA_<DIR>.png` | `walkB_<DIR>.png` |
| kid/baby | `<stage>_<DIR>.png` | `<stage>_walkA_<DIR>.png` | `<stage>_walkB_<DIR>.png` |

TRIM ALIGNMENT: `slice_strides` chroma-keys idle+walkA+walkB together and crops all
three to the UNION of their alpha boxes in raw-pixel space (the idle PNG is
re-written to the shared canvas too). All three frames of a facing therefore have
identical dimensions and a fixed feet baseline, so the body never hops when the
runtime swaps frames. After slicing, run `DinoDigger/Import Generated Art` — it
imports strides at the actor's shared PPU and fills the DinoDefinition
`WalkA/WalkB` (+ per-stage) arrays; dinos without stride files keep the static
walk (arrays stay null).

Retry workflow: gate every generated frame visually (legs repositioned; head,
colors, angle unchanged). To retry a bad frame, delete its raw PNG and re-run
`generate_sprites.py --only <dino>_strides` — only missing frames regenerate.
