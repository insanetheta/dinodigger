#!/usr/bin/env python3
"""Generate all Dino Digger character/item art via OpenRouter's Gemini Flash Image.

Art style: chunky toddler-friendly cartoon, thick dark outlines, bright saturated
colors, soft shading, no text. Every image is rendered on a solid magenta (#FF00FF)
background so slice_sprites.py can chroma-key it to transparent alpha.

Usage:
    python3 generate_sprites.py               # generate everything missing
    python3 generate_sprites.py --force       # regenerate everything
    python3 generate_sprites.py --only trex   # (re)generate a single entry
    python3 generate_sprites.py --list        # list entry names and exit

Raw generations are saved to Tools/raw/<name>.png. Slicing is a separate step
(slice_sprites.py), which imports the SPRITES manifest from this module so the
grid layout / naming can never drift from what was generated.

The API key is read from Tools/.openrouter_key (never printed, never committed).
"""

import argparse
import base64
import json
import os
import sys
import time
import urllib.request

# --- API config (mirrors the proven witsandfools/Assets/Art/generate_cards.py) ---
API_KEY = (lambda _p: open(_p).read().strip() if os.path.exists(_p)
           else os.environ.get("OPENROUTER_API_KEY", ""))(
    os.path.join(os.path.dirname(os.path.abspath(__file__)), ".openrouter_key"))
MODEL = "google/gemini-2.5-flash-image"
API_URL = "https://openrouter.ai/api/v1/chat/completions"

RAW_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "raw")

# Shared toddler-cartoon style applied to every prompt.
STYLE = (
    "Chunky toddler-friendly cartoon style for a preschool game. Thick bold dark "
    "outlines, bright saturated colors, soft simple cel shading, rounded friendly "
    "chubby shapes, big cute expressive eyes, adorable and cheerful. Flat 2D game "
    "sprite look. Absolutely no text, no letters, no numbers, no words, no logos, "
    "no watermark. The entire background must be a single solid flat pure magenta "
    "color #FF00FF (RGB 255,0,255) with nothing else on it, no gradient, no vignette. "
    "The subject casts NO shadow at all: no drop shadow, no ground shadow, no contact "
    "shadow - the area directly under and around the subject is pure flat magenta with "
    "nothing on it. "
)

# Style for full-bleed background art. Unlike STYLE this has NO magenta key color:
# a dig-mode backdrop must fill the frame edge-to-edge, so the slicer copies it
# straight through (no chroma-key, no trim) rather than cutting a subject out.
BG_STYLE = (
    "Chunky toddler-friendly cartoon style for a preschool game. Soft simple cel "
    "shading, bright saturated colors, flat simple rounded shapes. Absolutely no "
    "text, no letters, no numbers, no words, no logos, no watermark. No people and "
    "no characters (the one tiny worm described is the only creature). A full-bleed "
    "illustration that fills the ENTIRE frame edge to edge: NO border, NO frame, no "
    "matte, no margin, no vignette, no rounded corners. Wide landscape orientation, "
    "roughly 3:2 aspect ratio. "
)

# --- Turnaround strategy ---------------------------------------------------------
# The Gemini image model reliably renders ONE clean centered subject per call, and
# (being an image-editing model) can rotate a supplied reference image while keeping
# the character consistent. So we generate the 5 genuinely-distinct facings via
# image-to-image (front first, then rotate it) and MIRROR them in the slicer to get
# the left-hand 3 directions. front == S (facing camera-south in the isometric view).
GEN_DIRS = ["S", "SE", "E", "NE", "N"]          # directions we actually generate
# The slicer builds each left-hand facing by HORIZONTALLY FLIPPING its right-hand
# counterpart. This mapping is geometrically correct (a h-flip of a SE/E/NE pose yields
# a SW/W/NW pose), BUT it only produces a correct 8-set if each SOURCE facing actually
# points to the frame's RIGHT — otherwise a mirrored source silently swaps the whole
# pair (this was the DinoDigger-bw4 diagonal bug). Source orientation is enforced by the
# screen-relative ROTATE prompts + NO_MIRROR clause below; do NOT "fix" it here.
MIRROR = {"SW": "SE", "W": "E", "NW": "NE"}     # slicer h-flips source -> target

# How each facing is described to the model.
#
# CRITICAL — SCREEN-RELATIVE, NOT CHARACTER-RELATIVE (DinoDigger-bw4). Earlier wording
# ("we see its front AND its RIGHT side") was character-relative and AMBIGUOUS: the
# model often read "its right side" as the character's ANATOMICAL right, which lands on
# SCREEN-LEFT, so many diagonal (and some side) facings came out horizontally MIRRORED
# vs their filename. Since the slicer builds the left-hand set (SW/W/NW) by flipping the
# right-hand set (SE/E/NE), a mirrored source silently swaps a whole pair. So EVERY
# facing below is phrased in terms of where the character's HEAD/FRONT must point ON THE
# SCREEN (the viewer's/frame's left-right), the rotation is pinned so it always sweeps
# S -> right-hand-side -> N through the frame's RIGHT, and mirroring is explicitly
# forbidden. Screen map: S = toward camera, N = away, E = frame-RIGHT, SE = toward the
# frame's lower-right, NE = up-and-away toward the frame's upper-right.
ROTATE = {
    "S":  "a FRONT view facing straight toward the camera: we clearly see its face and "
          "belly, head pointing toward the viewer",
    "SE": "a three-quarter FRONT view rotated so the character's head and the front of "
          "its body point toward the LOWER-RIGHT of the frame (the viewer's right): we "
          "still see its face and chest, turned about 45 degrees to the frame's right. "
          "Its snout/beak/front must be on the RIGHT-hand side of the image",
    "E":  "a side profile facing the RIGHT edge of the frame (the viewer's right): its "
          "head, face and front point to the image's RIGHT, tail/back to the image's "
          "left; we see its complete side silhouette",
    "NE": "a three-quarter BACK view rotated so the character's head and front point AWAY "
          "and toward the UPPER-RIGHT of the frame (the viewer's right): we mostly see "
          "its back and the back of its head, turned about 135 degrees to the frame's "
          "right, with its snout/beak just visible on the RIGHT-hand side of the image",
    "N":  "a BACK view seen from directly behind: we see its back and the back of its "
          "head, not its face",
}
# Anti-mirror clause appended to every rotate prompt so the model never flips the pose.
NO_MIRROR = ("Do NOT mirror or horizontally flip the character; keep the rotation "
             "toward the RIGHT side of the frame exactly as described. ")

# --- Growth-stage strategy -------------------------------------------------------
# The existing per-dino sprites ARE the ADULT ("big") stage. To get real per-stage
# art (not just scale), we img2img-transform the adult FRONT (S) view down the age
# ladder: adult_S -> kid_S -> baby_S. Because every stage derives from the same
# source image, the character's identity/colors stay consistent as it "de-ages".
# Then each stage's S view is rotated into the other facings by the SAME proven
# rotation chain the base turnaround uses (5 gens + 3 slicer mirrors).
#
# STAGE_SEED maps a stage to the stage whose S view seeds it (None == the adult's
# existing raw S). Order matters: kid must exist before baby is transformed from it.
STAGES = ["kid", "baby"]
STAGE_SEED = {"kid": None, "baby": "kid"}

# img2img transform instruction per stage (keeps pose + colors, changes age/shape).
STAGE_TRANSFORM = {
    "kid": ("Redraw the EXACT SAME character — identical design, colors, markings, "
            "outline thickness, shading, pose and camera angle — but now as a "
            "slightly younger KID version of the very same creature: a little "
            "smaller overall, a rounder chubbier body, a bigger head-to-body ratio, "
            "and slightly shorter stubbier limbs. Keep the same species, the same "
            "colors and the same art style; only change its age and proportions."),
    "baby": ("Redraw the EXACT SAME character — identical design, colors, markings, "
             "outline thickness, shading, pose and camera angle — but now as a "
             "newborn BABY version of the very same creature, with EXAGGERATED baby "
             "proportions: make the HEAD dramatically OVERSIZED (roughly HALF the "
             "whole body, chibi style) sitting on a TINY short round pot-belly body, "
             "very short stubby little limbs, and enormous sparkly baby eyes that "
             "take up most of the face. It should look obviously much younger and "
             "littler than the reference. Keep the same species, the same colors "
             "and the same art style; only change its age and proportions."),
}

# --- Walk-stride strategy (y85.1) ------------------------------------------------
# Two mid-stride WALK frames per canonical facing, produced by image-to-image
# editing the EXISTING per-facing sprite (raw/<base>_<DIR>.png for the adult, or
# raw/<base>_<stage>_<DIR>.png for a growth stage). The pose edit repositions ONLY
# the legs/feet (a slight body bob is allowed) — head, face, body, arms, tail,
# colors, outline, camera angle, size AND on-canvas position all stay identical, so
# the runtime cycle (idle -> A -> idle -> B) never flickers the character's
# identity. The slicer then aligns each stride frame to its idle frame's trim box
# (shared canvas + feet baseline) so the dino's body never hops when frames swap.
#
# The left-side facings (SW, W, NW) are produced by the slicer mirroring the
# right-side stride frames, exactly like the base turnaround.
STRIDES = ["walkA", "walkB"]
STRIDE_TRANSFORM = {
    "walkA": ("its LEFT leg striding FORWARD and its RIGHT leg pushing BACK behind "
              "it, knees bent, caught in the middle of a walking step"),
    "walkB": ("its RIGHT leg striding FORWARD and its LEFT leg pushing BACK behind "
              "it, knees bent, caught in the middle of a walking step"),
}

# --- Four-legged species (DinoDigger-n4b) ----------------------------------------
# Some idle turnarounds stand on ALL FOURS. The default two-legged stride wording
# above ("its LEFT leg forward, its RIGHT leg back") has no reading for a quadruped,
# so the model resolved it by REARING THE DINO UP onto its hind legs and redrawing
# its front legs as ARMS — an 0-arms -> 2-arms limb change that popped every time
# the runtime swapped idle -> walk. Quadruped (base, stage) pairs get a four-legged
# diagonal-gait description plus a hard stance freeze instead. Stage matters: the
# brachiosaurus BABY is drawn bipedal even though its adult and kid are not.
QUADRUPED_STAGES = {
    ("brachiosaurus", None), ("brachiosaurus", "kid"),
    ("stegosaurus", None), ("stegosaurus", "kid"), ("stegosaurus", "baby"),
    ("ankylosaurus", None), ("ankylosaurus", "kid"), ("ankylosaurus", "baby"),
}
QUAD_STRIDE_TRANSFORM = {
    "walkA": ("its front-LEFT foot and its back-RIGHT foot each lifted just a little "
              "way off the ground and swung slightly FORWARD, while its front-RIGHT "
              "and back-LEFT feet stay planted on the ground — a tiny four-legged "
              "diagonal walking step"),
    "walkB": ("its front-RIGHT foot and its back-LEFT foot each lifted just a little "
              "way off the ground and swung slightly FORWARD, while its front-LEFT "
              "and back-RIGHT feet stay planted on the ground — a tiny four-legged "
              "diagonal walking step"),
}
QUAD_STANCE_FREEZE = (
    "READ THIS FIRST — THE REFERENCE IS A FOUR-LEGGED ANIMAL STANDING ON ALL FOURS, "
    "AND IT MUST STAY THAT WAY. Its body is low and horizontal with its belly close "
    "to the ground and FOUR legs underneath it: two FRONT legs and two BACK legs, "
    "four feet on the ground. This creature has NO arms and NO hands whatsoever. "
    "Keep the body at the SAME low horizontal angle as the reference. Do NOT stand it "
    "up on its hind legs, do NOT rear it up, do NOT tilt the chest upright, do NOT "
    "make it a two-legged cartoon walk. Its two FRONT LEGS stay FRONT LEGS reaching "
    "down to the ground — never redraw them as arms, hands, fists, paws held up, or "
    "limbs bent across the chest. All four feet stay at the bottom of the figure near "
    "the ground line. This is only a small step, not a new pose. ")

# KNOWN RESIDUAL (DinoDigger-awr): the stegosaurus BABY still rears up on its
# front-ish facings — walkA_S, walkB_S and walkB_SE come back with the chest
# tilted up and one front leg bent to chest height, reading as a raised arm. It
# reproduced on THREE independent rolls, including one with a stricter variant
# clause that froze the front legs entirely and moved only the hind feet, so it is
# a model limitation on this asset (chibi baby proportions on a front view), not a
# missing clause. Do not re-attempt by weakening or rewording the freezes above —
# the same wording renders the ankylosaurus and brachiosaurus babies correctly.

# --- Limb freeze (DinoDigger-n4b) -------------------------------------------------
# img2img pose/age edits hallucinate ANATOMY: a two-armed dino comes back with four
# arms, a QUADRUPED idle comes back standing on two legs with its front legs redrawn
# as arms, a folded-wing pteranodon unfolds a wing, an extra hind leg sprouts between
# the two real ones. All of it reads as a limb POP when the runtime swaps frames.
# This clause is appended to BOTH the stride prompt and the stage (de-age) prompt so
# no generated frame can ever change the character's limb inventory again. Keep it
# next to the other freeze clauses; do not weaken the "EXACTLY the same" wording.
LIMB_FREEZE = (
    "EXTRA CRITICAL — LIMB COUNT IS FROZEN: the character has EXACTLY the same limbs "
    "as the reference — the SAME number of arms and the SAME number of legs, in the "
    "same places on the body. Count the arms and legs in the reference and draw "
    "EXACTLY that many. Do NOT add, duplicate, invent, split, hide or remove any "
    "limb; do NOT grow a third arm, a spare hand, an extra leg or an extra foot; do "
    "NOT turn a leg into an arm or an arm into a leg; do NOT unfold, spread or open "
    "a wing or a flipper that is folded in the reference. If the reference stands on "
    "ALL FOURS with four legs on the ground, the new image must ALSO stand on all "
    "fours with those same four legs on the ground — do NOT rear it up onto two legs "
    "and do NOT redraw its front legs as arms. Every other feature is frozen too: the "
    "same single tail (same length, same side, and keeping any club, spikes or fan on "
    "its end), the same single head crest / frill / horns / back plates / spine "
    "ridge / sail — never a second one, never a new one, and never removed. "
)

# Per-species extra freeze clauses appended to the stride prompt. Long-necked and
# winged species drift their signature feature when only the legs are asked to move,
# so we pin those features to the reference on top of the generic freeze.
SPECIES_STRIDE_FREEZE = {
    "brachiosaurus": (
        "EXTRA CRITICAL — LONG NECK: the long neck and head must stay in the EXACT "
        "same pose, curve, length and angle as the reference; do NOT raise, lower, "
        "lengthen, shorten or re-curve the neck. Only the legs move. "),
    "pteranodon": (
        "EXTRA CRITICAL — WINGS STAY FOLDED: this is a little waddling walk on two "
        "legs; the wings must remain FOLDED and tucked against the body EXACTLY as in "
        "the reference — do NOT spread, open, raise or unfold the wings, and keep the "
        "head crest in the same pose. Only take a small waddle step with the legs. "),
}


# --- Sprite manifest -------------------------------------------------------------
# Turnaround entries (category "turnaround") need only: name, outdir, subject.
#   They are generated as 5 per-facing files raw/<name>_{S,SE,E,NE,N}.png via
#   image-to-image (see GEN_DIRS), and the slicer mirrors them into the full 8.
# Item entries (category "item") also need: grid=(rows,cols), cells (base names in
#   row-major order), prefix (prepended to each cell name; "" for items).
SPRITES = [
    # ---- Character turnarounds (8 isometric directions, front == S) ----
    {
        "name": "backhoe", "category": "turnaround", "outdir": "backhoe",
        "subject": ("a friendly yellow cartoon backhoe excavator digger with a big "
                    "digging arm and bucket, and a pair of big cute happy eyes on the "
                    "cab window like a face, bright yellow body, chunky black tires"),
    },
    {
        "name": "trex", "category": "turnaround", "outdir": "trex",
        "subject": ("a chubby happy baby green Tyrannosaurus Rex dinosaur, round "
                    "belly, tiny arms, big friendly smile, big sparkly eyes"),
    },
    {
        "name": "triceratops", "category": "turnaround", "outdir": "triceratops",
        "subject": ("a chubby happy baby orange Triceratops dinosaur with three small "
                    "rounded horns and a frill, round belly, big friendly smile, big "
                    "sparkly eyes"),
    },
    {
        "name": "brachiosaurus", "category": "turnaround", "outdir": "brachiosaurus",
        "subject": ("a chubby happy baby blue Brachiosaurus long-neck dinosaur, "
                    "cute long neck, round belly, big friendly smile, big sparkly eyes"),
    },
    {
        "name": "stegosaurus", "category": "turnaround", "outdir": "stegosaurus",
        "subject": ("a chubby happy baby purple Stegosaurus dinosaur with rounded "
                    "back plates and a soft spiky tail, round belly, big friendly "
                    "smile, big sparkly eyes"),
    },

    # ---- Shard-exclusive species (5 new, bl6.2) ----
    # Same "chubby happy baby <species>" framing as the original four. Each is drawn
    # standing/waddling on legs so the 8-dir ground sprites make sense. Pteranodon
    # (a flyer) is deliberately grounded for v1: on two legs with wings FOLDED.
    {
        "name": "pteranodon", "category": "turnaround", "outdir": "pteranodon",
        "subject": ("a chubby happy baby teal Pteranodon dinosaur standing upright "
                    "and waddling on two little legs, with a long pointed head crest "
                    "sweeping back and big wings FOLDED and tucked against its body "
                    "(not spread), round belly, big friendly smile, big sparkly eyes"),
    },
    {
        "name": "ankylosaurus", "category": "turnaround", "outdir": "ankylosaurus",
        "subject": ("a chubby happy baby red Ankylosaurus dinosaur, a low rounded "
                    "body covered in bumpy armor plates with a big heavy round club "
                    "on the end of its tail, stubby little legs, round belly, big "
                    "friendly smile, big sparkly eyes"),
    },
    {
        "name": "spinosaurus", "category": "turnaround", "outdir": "spinosaurus",
        "subject": ("a chubby happy baby yellow-green Spinosaurus dinosaur standing "
                    "on two legs. Its defining feature is a HUGE tall rounded SAIL "
                    "FIN on its back — a big semicircular fan of skin as tall as the "
                    "body itself, running the whole length of the back from the neck "
                    "all the way to the tail, impossible to miss. It also has a long "
                    "friendly crocodile-like snout, round belly, big friendly smile, "
                    "big sparkly eyes"),
    },
    {
        "name": "parasaurolophus", "category": "turnaround", "outdir": "parasaurolophus",
        "subject": ("a chubby happy baby pink Parasaurolophus duck-billed dinosaur "
                    "with one long curved tube crest sweeping back from the top of "
                    "its head and a wide flat duck bill, round belly, big friendly "
                    "smile, big sparkly eyes"),
    },
    {
        "name": "velociraptor", "category": "turnaround", "outdir": "velociraptor",
        "subject": ("a chubby happy baby sky-grey Velociraptor dinosaur, a small "
                    "speedy raptor standing on two legs with tiny clawed hands and a "
                    "fluffy feathered tail, round belly, big friendly smile, big "
                    "sparkly eyes"),
    },

    # ---- Item sheets ----
    {
        "name": "eggs", "category": "item", "outdir": "eggs",
        "grid": (2, 2), "prefix": "",
        "cells": ["egg_green", "egg_orange", "egg_blue", "egg_purple"],
        "subject": ("a 2x2 grid of 4 cute cartoon dinosaur eggs, evenly spaced and "
                    "the same size, one per cell. Top-left: a green egg with darker "
                    "green spots. Top-right: an orange egg with darker orange spots. "
                    "Bottom-left: a blue egg with darker blue spots. Bottom-right: a "
                    "purple egg with darker purple spots. Each egg's color clearly "
                    "telegraphs which dino is inside"),
    },
    # ---- Shard-exclusive eggs (5 new, bl6.2). One egg per generation (1x1) so each
    # shell's color + pattern can be dialed in individually; all land in eggs/ next
    # to the original four (egg_<color>.png), same naming convention. ----
    {
        "name": "egg_teal", "category": "item", "outdir": "eggs",
        "grid": (1, 1), "prefix": "", "cells": ["egg_teal"],
        "subject": ("a single cute cartoon dinosaur egg, centered: a teal egg with "
                    "thin darker swept wing-like streaks curving across the shell. "
                    "Its teal color and wing streaks telegraph a Pteranodon inside"),
    },
    {
        "name": "egg_red", "category": "item", "outdir": "eggs",
        "grid": (1, 1), "prefix": "", "cells": ["egg_red"],
        "subject": ("a single cute cartoon dinosaur egg, centered: a brick-red egg "
                    "with a bumpy armored pebble texture all over the shell. Its red "
                    "color and armored texture telegraph an Ankylosaurus inside"),
    },
    {
        "name": "egg_olive", "category": "item", "outdir": "eggs",
        "grid": (1, 1), "prefix": "", "cells": ["egg_olive"],
        "subject": ("a single cute cartoon dinosaur egg, centered: an olive "
                    "yellow-green egg with one tall darker sail-stripe running down "
                    "the middle of the shell. Its olive color and sail-stripe "
                    "telegraph a Spinosaurus inside"),
    },
    {
        "name": "egg_pink", "category": "item", "outdir": "eggs",
        "grid": (1, 1), "prefix": "", "cells": ["egg_pink"],
        "subject": ("a single cute cartoon dinosaur egg, centered: a pink egg with "
                    "one long curved darker crest-stripe sweeping across the shell. "
                    "Its pink color and curved crest-stripe telegraph a "
                    "Parasaurolophus inside"),
    },
    {
        "name": "egg_grey", "category": "item", "outdir": "eggs",
        "grid": (1, 1), "prefix": "", "cells": ["egg_grey"],
        "subject": ("a single cute cartoon dinosaur egg, centered: a sky-grey egg "
                    "with quick little darker zig-zag feather flecks scattered across "
                    "the shell. Its grey color and zig-zag flecks telegraph a "
                    "Velociraptor inside"),
    },
    {
        "name": "fruit", "category": "item", "outdir": "fruit",
        "grid": (2, 2), "prefix": "",
        "cells": ["fruit_apple", "fruit_banana", "fruit_berries", "fruit_watermelon"],
        "subject": ("a 2x2 grid of 4 cute cartoon fruits, evenly spaced and the same "
                    "size, one per cell. Top-left: a shiny red apple. Top-right: a "
                    "yellow banana. Bottom-left: a cluster of blue berries. "
                    "Bottom-right: a juicy pink watermelon slice"),
    },
    {
        "name": "treasure", "category": "item", "outdir": "treasure",
        "grid": (2, 2), "prefix": "",
        "cells": ["treasure_coin", "treasure_gem", "treasure_boot", "treasure_bone"],
        "subject": ("a 2x2 grid of 4 cute cartoon buried-treasure items, evenly "
                    "spaced and the same size, one per cell. Top-left: a shiny gold "
                    "coin. Top-right: a sparkly cut gemstone jewel. Bottom-left: a "
                    "worn old brown boot. Bottom-right: a white dinosaur bone"),
    },
    {
        "name": "dirt", "category": "item", "outdir": "dirt",
        "grid": (1, 3), "prefix": "",
        "cells": ["dirt_crack_0", "dirt_crack_1", "dirt_crack_2"],
        "subject": ("a 1x3 horizontal row of 3 square brown dirt dig tiles, same size, "
                    "showing 3 progressive damage states left to right. Left: a solid "
                    "intact mound of brown dirt, no cracks. Middle: the same dirt tile "
                    "with a few medium cracks starting to break apart. Right: the same "
                    "dirt tile heavily cracked and crumbling into chunks, almost dug "
                    "away"),
    },
    {
        "name": "particles", "category": "item", "outdir": "particles",
        "grid": (1, 3), "prefix": "",
        "cells": ["particle_star", "particle_heart", "particle_crumb"],
        "subject": ("a 1x3 horizontal row of 3 small simple cartoon particle icons, "
                    "same size, one per cell. Left: a bright yellow five-pointed star. "
                    "Middle: a red heart. Right: a small brown dirt crumb clod"),
    },

    # ---- Dig-mode excavator arm parts (sliced into 3 separate rig pieces) ----
    # A single row of three chunky yellow arm parts, each drawn HORIZONTALLY pointing
    # to the right so the slicer's left-edge pivot (set by GeneratedArtImporter) puts
    # the hinge at each piece's base. Assembled at runtime into a two-bone IK arm.
    {
        "name": "digarm", "category": "item", "outdir": "digarm",
        "grid": (1, 3), "prefix": "",
        "cells": ["digarm_boom", "digarm_stick", "digarm_bucket"],
        "subject": ("3 separate chunky cartoon excavator arm parts arranged in one "
                    "horizontal row, matching a bright yellow construction digger with "
                    "thick black outlines. Divide the square image into three equal "
                    "vertical columns and place exactly ONE part, SMALL and CENTERED, "
                    "inside each column with a LARGE empty magenta margin around it; the "
                    "parts must be well separated and must NEVER touch or cross into a "
                    "neighboring column. All three lie flat and horizontal pointing to "
                    "the right. In the LEFT column: a thick straight short yellow BOOM "
                    "arm segment (the sturdiest piece) with a round dark bolt hole at "
                    "each end. In the MIDDLE column: a slimmer straight short yellow "
                    "STICK arm segment with a round dark bolt hole at each end. In the "
                    "RIGHT column: a chunky yellow digging BUCKET scoop seen from the "
                    "side, a curved metal scoop with 3 or 4 pointed teeth, its OPENING "
                    "facing LEFT and a round dark hinge bolt at its top-left. Each part "
                    "is a solid single object, not connected to the others"),
    },

    # ---- Anatomical arm segments (REPLACE the boom/stick cells of the digarm sheet).
    # Real-excavator side profiles drawn near their DISPLAY aspect so they render 1:1
    # with a uniform scale — no 9-slice, no stretching, pin bosses stay perfect
    # circles. The rig positions the joints on the drawn pin bosses (centroids are
    # measured by Tools and hardcoded in GeneratedArtImporter/DigModeController).
    # NOTE: these must stay listed AFTER the "digarm" sheet entry — both write
    # digarm_boom/digarm_stick.png and later entries win on a full pipeline re-run.
    {
        "name": "digboom", "category": "item", "outdir": "digarm",
        "grid": (1, 1), "prefix": "", "style": "part",
        "cells": ["digarm_boom"],
        "subject": ("a side profile of a cartoon backhoe excavator BOOM arm segment "
                    "shaped like a REAL excavator boom: one smoothly CURVED gooseneck "
                    "banana shape, thick and deep at its mounting end at the far LEFT "
                    "of the image, arcing gently and TAPERING toward its narrower tip "
                    "end at the far RIGHT of the image. It spans the ENTIRE image "
                    "width from the left edge to the right edge, and is long and "
                    "slender: about 6 times as long as it is thick. One LARGE round "
                    "pivot pin boss (a perfect circle with a dark round pin hole in "
                    "its center) at the left mounting end, and one SMALLER round "
                    "pivot pin boss (a perfect circle with a dark round pin hole) at "
                    "the right tip end. The bosses are perfectly circular, never "
                    "oval. Everything between the two bosses is smooth plain bright "
                    "yellow with soft cel shading and NO other details: no hydraulic "
                    "hoses, no cylinders, no rivets, no bolts, no panel lines"),
    },
    {
        "name": "digstick", "category": "item", "outdir": "digarm",
        "grid": (1, 1), "prefix": "", "style": "part",
        "cells": ["digarm_stick"],
        "subject": ("a side profile of a cartoon excavator DIPPER STICK arm segment "
                    "shaped like a real excavator dipper: one nearly STRAIGHT slim "
                    "box-section bar, slightly deeper at its pivot end at the far "
                    "LEFT of the image and tapering a little toward its narrower "
                    "bucket end at the far RIGHT of the image. It spans the ENTIRE "
                    "image width from the left edge to the right edge, and is long "
                    "and slender: about 7 times as long as it is thick. One round "
                    "pivot pin boss (a perfect circle with a dark round pin hole in "
                    "its center) at the left end, and one round pivot pin boss (a "
                    "perfect circle with a dark round pin hole) at the right end. "
                    "The bosses are perfectly circular, never oval. Everything "
                    "between the two bosses is smooth plain bright yellow with soft "
                    "cel shading and NO other details: no hydraulic hoses, no "
                    "cylinders, no rivets, no bolts, no panel lines"),
    },

    # ---- Image-to-image edits (start from an existing raw sprite) ----
    # digbody: take the side-view backhoe and delete the rear excavator boom+arm so the
    # armless tractor can carry the separately-built rig. category "imgedit" attaches
    # raw/<ref>.png as the input image and chroma-keys the result like an item.
    {
        "name": "digbody", "category": "imgedit", "outdir": "digbody",
        "outfile": "digbody", "ref": "backhoe_E",
        "instruction": ("Remove the entire rear excavator arm, boom, and its digging "
                        "bucket on the LEFT side of the machine, completely erasing "
                        "every part of that back arm. KEEP the tractor body, the cab "
                        "with its window and cute eyes, the exhaust pipe, all the wheels, "
                        "and the front loader bucket on the right EXACTLY the same. Fill "
                        "the space where the rear arm was with clean flat magenta "
                        "background so nothing of the old arm remains. Keep the identical "
                        "art style, outline thickness, colors and proportions."),
    },

    # ---- Full-bleed backgrounds (no chroma key, no trim; copied straight) ----
    # A single landscape image saved to raw/<name>.png and normalized by the slicer
    # to Assets/Art/Generated/<outdir>/<outfile>.png. Needs: name, outdir, outfile,
    # subject.
    {
        "name": "digbg", "category": "background", "outdir": "digbg",
        "outfile": "dig_background",
        "subject": ("a side-view underground cross-section: the top fifth of the "
                    "image is a cheerful blue sky with a smiling sun and two puffy "
                    "white clouds, then a bright green grass lip/edge running straight "
                    "across, and below it a warm brown soil cross-section filling all "
                    "the rest of the image down to the bottom, with subtle horizontal "
                    "strata bands in slightly varying browns, a few small pebbles, one "
                    "cute little pink worm, and pale tree roots reaching in from the "
                    "left and right edges"),
    },

    # ---- Growth-stage art (baby/kid) for each dino ----
    # category "stages": img2img the dino's existing ADULT front (raw/<base>_S.png)
    # down the age ladder, then rotate each stage's S view like a turnaround. Raw
    # files land at raw/<base>_<stage>_<DIR>.png; the slicer writes them to
    # Generated/<outdir>/<stage>_<DIR>.png. The adult keeps its existing filenames.
    {"name": "trex_stages",          "category": "stages", "outdir": "trex",          "base": "trex"},
    {"name": "triceratops_stages",   "category": "stages", "outdir": "triceratops",   "base": "triceratops"},
    {"name": "brachiosaurus_stages", "category": "stages", "outdir": "brachiosaurus", "base": "brachiosaurus"},
    {"name": "stegosaurus_stages",   "category": "stages", "outdir": "stegosaurus",   "base": "stegosaurus"},
    # Shard-exclusive species stage art (bl6.2), same de-age chain as the four above.
    {"name": "pteranodon_stages",     "category": "stages", "outdir": "pteranodon",     "base": "pteranodon"},
    {"name": "ankylosaurus_stages",   "category": "stages", "outdir": "ankylosaurus",   "base": "ankylosaurus"},
    {"name": "spinosaurus_stages",    "category": "stages", "outdir": "spinosaurus",    "base": "spinosaurus"},
    {"name": "parasaurolophus_stages","category": "stages", "outdir": "parasaurolophus","base": "parasaurolophus"},
    {"name": "velociraptor_stages",   "category": "stages", "outdir": "velociraptor",   "base": "velociraptor"},

    # ---- Walk-stride frames (y85.1). category "strides": img2img each canonical
    # per-facing sprite into two mid-stride poses (see STRIDE_TRANSFORM). Listed
    # LAST so a full pipeline run has already produced every stage's S/rotated raws
    # before strides edit them. "stride_stages" picks which age stages to build
    # strides for (None = adult raw <base>_<DIR>.png). The PILOT (y85.1) is trex
    # adult only ([None]); y85.2 will flip the rest to [None, "kid", "baby"].
    {"name": "trex_strides",          "category": "strides", "outdir": "trex",          "base": "trex",          "stride_stages": [None, "kid", "baby"]},
    {"name": "triceratops_strides",   "category": "strides", "outdir": "triceratops",   "base": "triceratops",   "stride_stages": [None, "kid", "baby"]},
    {"name": "brachiosaurus_strides", "category": "strides", "outdir": "brachiosaurus", "base": "brachiosaurus", "stride_stages": [None, "kid", "baby"]},
    {"name": "stegosaurus_strides",   "category": "strides", "outdir": "stegosaurus",   "base": "stegosaurus",   "stride_stages": [None, "kid", "baby"]},
    {"name": "pteranodon_strides",     "category": "strides", "outdir": "pteranodon",     "base": "pteranodon",     "stride_stages": [None, "kid", "baby"]},
    {"name": "ankylosaurus_strides",   "category": "strides", "outdir": "ankylosaurus",   "base": "ankylosaurus",   "stride_stages": [None, "kid", "baby"]},
    {"name": "spinosaurus_strides",    "category": "strides", "outdir": "spinosaurus",    "base": "spinosaurus",    "stride_stages": [None, "kid", "baby"]},
    {"name": "parasaurolophus_strides","category": "strides", "outdir": "parasaurolophus","base": "parasaurolophus","stride_stages": [None, "kid", "baby"]},
    {"name": "velociraptor_strides",   "category": "strides", "outdir": "velociraptor",   "base": "velociraptor",   "stride_stages": [None, "kid", "baby"]},

    # ---- Backhoe roll animation (DinoDigger-682). category "roll": a vehicle
    # variant of the stride pipeline. img2img each canonical backhoe facing into two
    # wheel-roll frames (spokes rotated + a small suspension bob; everything else
    # frozen). Raw files land at raw/backhoe_<pose>_<DIR>.png; the slicer aligns
    # each roll frame to the idle backhoe facing via the same union-box crop the
    # strides use, and mirrors the right-side facings. ----
    {"name": "backhoe_roll", "category": "roll", "outdir": "backhoe", "base": "backhoe"},

    # ---- Dino Town buildings (DinoDigger-5li.3). category "town": generate ONE
    # finished building (fresh gen, toy-box style, NO face on the structure), then
    # "un-build" it down a construction ladder by chained img2img edits — the same
    # proven technique the de-age/stride pipelines use, pointed at construction. Each
    # earlier state removes more of the structure (walls -> frame -> foundation ->
    # bare dirt). Raw files land at raw/<name>_{done,s3,s2,s1,s0}.png; the slicer
    # chroma-keys + trims each to Generated/town/<name>_<state>.png. ----
    {
        "name": "pebble_playground", "category": "town", "outdir": "town",
        "finished_subject": (
            "a finished cute stone-age Flintstones-style toddler PLAYGROUND, a single "
            "structure centered in the frame, built entirely from natural prehistoric "
            "materials: a ring of big smooth rounded grey boulders enclosing it, a "
            "slide made from one big flat tilted polished stone slab, a see-saw made "
            "of a long pale dinosaur bone balanced across a round rock, and big bright "
            "green leaf canopies on wooden poles for shade. Chunky, rounded, colorful, "
            "sturdy and inviting. It is a BUILDING/playset, an object only — it has NO "
            "face, no eyes, no mouth and is not a character"),
        "kind": "playground",
        "s3_frame": "the boulder ring and the main frame",
        "s3_missing": ("one or two leaf canopies not yet added, the slide slab only "
                       "half mounted"),
        "s2_frame": ("the ring of foundation boulders and a few bare upright wooden "
                     "support poles / partial stone walls"),
        "s2_gaps": "the slide, see-saw and leaf canopies",
        "s1_none": "NO slide, NO see-saw, NO canopies",
    },
    {
        "name": "boulder_brew", "category": "town", "outdir": "town",
        "finished_subject": (
            "a finished cute stone-age Flintstones-style COFFEE SHOP, a single small "
            "structure centered in the frame, built entirely from natural prehistoric "
            "materials: a long thick grey stone-slab serving counter across the front, "
            "a big bright green leaf awning stretched over it on two chunky log posts, "
            "and behind the counter a fat round clay pot of bubbling tar-black brew "
            "steaming on a nest of glowing hot orange stones, with a couple of stacked "
            "stone cups on the counter. Chunky, rounded, cozy, warm and inviting. It "
            "is a BUILDING, an object only — it has NO face, no eyes, no mouth and is "
            "not a character, and there is NO writing or sign lettering anywhere"),
        # The leaf awning seals two pockets of background magenta above the brew
        # pot that the border flood cannot reach; without this they survive the key
        # as pink blobs that read as a pair of eyes. See clear_magenta_pockets.
        "pocket_states": ["done"],
        "kind": "coffee shop",
        "s3_frame": "the counter slab and the awning posts",
        "s3_missing": ("the leaf awning only half draped over one post, the brew pot "
                       "not yet set on its hot stones"),
        "s2_frame": ("the low stone foundation blocks and two bare upright log posts "
                     "with a half-built counter slab"),
        "s2_gaps": "the leaf awning, the brew pot and the hot stones",
        "s1_none": "NO counter, NO awning, NO brew pot",
    },
    {
        "name": "slate_library", "category": "town", "outdir": "town",
        "finished_subject": (
            "a finished cute stone-age Flintstones-style LIBRARY, a single tall "
            "structure centered in the frame, built entirely from natural prehistoric "
            "materials: chunky stacked grey stone-slab walls with big open square "
            "window openings, and through those openings you can see tall shelves of "
            "neatly stacked flat stone tablets inside. A tall ladder made of a log "
            "frame with log rungs leans against the front wall. A big bright green "
            "leaf roof caps it, and a large upright carved stone tablet stands out "
            "front on two little rock feet as a sign — the tablet face is completely "
            "BLANK and smooth with absolutely nothing carved or written on it. Chunky, "
            "rounded, scholarly and inviting. It is a BUILDING, an object only — it "
            "has NO face, no eyes, no mouth and is not a character, and there is NO "
            "writing, no letters and no symbols anywhere in the picture"),
        "kind": "library",
        "s3_frame": "the stone-slab walls and the roof beams",
        "s3_missing": ("part of the leaf roof not yet laid, the blank tablet sign not "
                       "yet stood up out front, the shelves only half filled"),
        "s2_frame": ("the foundation slabs and half-height bare stone walls with a few "
                     "upright log roof posts"),
        "s2_gaps": "the leaf roof, the shelves, the log ladder and the tablet sign",
        "s1_none": "NO shelves, NO roof, NO ladder, NO sign",
    },
    {
        "name": "bedrock_bijou", "category": "town", "outdir": "town",
        "finished_subject": (
            "a finished cute stone-age Flintstones-style MOVIE THEATER, a single grand "
            "structure centered in the frame, built entirely from natural prehistoric "
            "materials: a wide heavy grey stone-slab marquee canopy jutting out over "
            "the entrance, its underside and rim studded with rows of round glowing "
            "warm-yellow firefly bulbs in little stone cups. The marquee board itself "
            "is completely BLANK smooth stone with absolutely nothing written on it. "
            "The entrance below is a tall arched opening hung with a heavy draped "
            "brown-and-tan animal hide curtain, tied back at one side. Chunky stone "
            "block walls and a big green leaf roof above. Chunky, rounded, grand, "
            "festive and inviting. It is a BUILDING, an object only — it has NO face, "
            "no eyes, no mouth and is not a character, and there is absolutely NO "
            "text, NO letters and NO numbers anywhere in the picture"),
        "kind": "movie theater",
        "s3_frame": "the stone walls and the marquee slab",
        "s3_missing": ("about half the firefly bulbs not yet fitted into their stone "
                       "cups, the hide curtain not yet hung in the doorway"),
        "s2_frame": ("the foundation slabs and half-height bare stone block walls with "
                     "bare log posts where the marquee will sit"),
        "s2_gaps": "the marquee slab, the firefly bulbs, the hide curtain and the roof",
        "s1_none": "NO marquee, NO bulbs, NO curtain",
    },
    {
        "name": "boneanza_bowling", "category": "town", "outdir": "town",
        "finished_subject": (
            "a finished cute stone-age Flintstones-style BOWLING ALLEY, a single long "
            "low structure centered in the frame seen at a friendly three-quarter "
            "angle, built entirely from natural prehistoric materials: one long "
            "polished flat grey stone lane running away from the viewer with smooth "
            "log gutter rails down both sides, sheltered under an open roof of thick "
            "log beams topped with big green leaves. At the far end of the lane stands "
            "a neat triangular rack of ten chunky pale white dinosaur-bone pins, and a "
            "big round grey stone bowling ball sits at the near end. Chunky, rounded, "
            "playful and inviting. It is a BUILDING, an object only — it has NO face, "
            "no eyes, no mouth and is not a character, and there is NO writing or "
            "scoreboard lettering anywhere"),
        "kind": "bowling alley",
        "s3_frame": "the lane slab and the log roof beams",
        "s3_missing": ("part of the leaf roof not yet laid over the beams, one gutter "
                       "rail log missing, the bone pins not yet racked at the end"),
        "s2_frame": ("the long foundation of rough unpolished stone blocks and a few "
                     "bare upright log posts along the sides"),
        "s2_gaps": "the polished lane surface, the roof, the gutter rails and the bone pins",
        "s1_none": "NO lane, NO roof, NO rails, NO pins",
    },
    {
        "name": "dino_daycare", "category": "town", "outdir": "town",
        "finished_subject": (
            "a finished cute stone-age Flintstones-style baby NURSERY hut, a single "
            "small low round structure centered in the frame, built entirely from "
            "natural prehistoric materials: a squat rounded dome of smooth pastel-grey "
            "river stones, extra cozy and pillowy, capped with a thick shaggy roof of "
            "overlapping big green leaves. Two tiny low arched doorways in the front "
            "are hung with soft little flaps of pale cream hide. Through a low round "
            "side opening you can see soft nests woven from green leaves and dry golden "
            "grass inside. A couple of speckled round eggs rest in the nearest nest. "
            "Chunky, rounded, soft, warm and inviting. It is a BUILDING, an object "
            "only — it has NO face, no eyes, no mouth and is not a character, there "
            "are NO baby animals or creatures in it, and NO writing anywhere"),
        "kind": "nursery hut",
        "s3_frame": "the round stone dome walls",
        "s3_missing": ("part of the leaf roof still open at the top, the little hide "
                       "door-flaps not yet hung, only one leaf-nest placed inside"),
        "s2_frame": ("the low circular ring of foundation stones and a half-height "
                     "curved bare stone wall with bent log ribs arching over it"),
        "s2_gaps": "the leaf roof, the door-flaps and the soft leaf-nests",
        "s1_none": "NO dome, NO roof, NO door-flaps, NO nests",
    },
    {
        "name": "tarpit_springs", "category": "town", "outdir": "town",
        "finished_subject": (
            "a finished cute stone-age Flintstones-style SPA, a single sunken bathing "
            "pool centered in the frame, built entirely from natural prehistoric "
            "materials: a round sunken basin of warm creamy caramel-brown bubbling mud "
            "with fat glossy bubbles doming up on its surface and soft curls of white "
            "steam rising, ringed all the way around by a rim of big smooth rounded "
            "soaking stones in soft greys and warm tans, with two or three wide flat "
            "stones set as seats along the rim and a small leafy potted fern beside it. "
            "Chunky, rounded, relaxing, warm and inviting. It is a BUILDING/structure, "
            "an object only — it has NO face, no eyes, no mouth and is not a character, "
            "there are NO people or animals bathing in it, and NO writing anywhere"),
        "kind": "spa bathing pool",
        "s3_frame": "the dug basin and most of the stone rim",
        "s3_missing": ("a gap in the ring of soaking stones where two rocks are not "
                       "yet set, the basin only part filled so the mud is not bubbling "
                       "yet"),
        "s2_frame": ("the empty dug-out earth basin lined with a few rough foundation "
                     "stones and a couple of upright marker stakes"),
        "s2_gaps": "the smooth soaking-stone rim, the seats and the warm bubbling mud",
        "s1_none": "NO pool, NO mud, NO stone rim, NO seats",
    },
    {
        "name": "gronks_grocer", "category": "town", "outdir": "town",
        "finished_subject": (
            "a finished cute stone-age Flintstones-style open-air MARKET stall, a "
            "single structure centered in the frame, built entirely from natural "
            "prehistoric materials: two or three open-fronted stalls of flat grey "
            "stone-slab tables on chunky rock legs, heaped high with giant colorful "
            "prehistoric produce — oversized red and purple berries, big orange and "
            "yellow gourds, fat green melons and bunches of leafy greens spilling out "
            "of woven baskets. Overhead a wide canopy of big leaves in alternating "
            "bright green and pale yellow-green STRIPES is slung on chunky log poles. "
            "Chunky, rounded, abundant, colorful and inviting. It is a BUILDING/market "
            "stall, an object only — it has NO face, no eyes, no mouth and is not a "
            "character, there are NO shoppers or vendors, and there is NO writing, no "
            "price signs and no lettering anywhere"),
        "kind": "market stall",
        "s3_frame": "the slab stall tables and the canopy poles",
        "s3_missing": ("the striped leaf canopy only half slung across the poles, one "
                       "stall table still bare and empty of produce"),
        "s2_frame": ("the foundation stones and a few bare upright log poles with one "
                     "half-laid slab table"),
        "s2_gaps": "the striped leaf canopy, the stall tables and all the produce",
        "s1_none": "NO stalls, NO canopy, NO produce, NO baskets",
    },
    {
        "name": "fossil_fountain", "category": "town", "outdir": "town",
        "finished_subject": (
            "a finished cute stone-age Flintstones-style town-plaza FOUNTAIN, a single "
            "structure centered in the frame, built entirely from natural prehistoric "
            "materials: a wide round basin of chunky grey stone blocks holding clear "
            "bright blue water, and rising from its middle a carved centerpiece of "
            "pale cream dinosaur bones — a tall curving rib-bone arch and a big round "
            "skull-less bone bowl — spouting arcs of sparkling water with little white "
            "sparkle dots and droplets splashing back into the basin. Around the "
            "outside sits a ring of big smooth rounded sitting-stones in soft greys "
            "and tans. Chunky, rounded, cheerful and inviting. It is a FOUNTAIN "
            "structure, an object only — it has NO face, no eyes, no mouth, no skull "
            "and is not a character, and there is NO writing anywhere"),
        "kind": "plaza fountain",
        "s3_frame": "the round stone basin and the bone centerpiece",
        "s3_missing": ("the basin still empty and dry with no water and no spouting "
                       "yet, two of the ring of sitting-stones not yet rolled into "
                       "place"),
        "s2_frame": ("the circular ring of rough foundation blocks forming a low "
                     "half-height basin wall with a bare stub post in the middle"),
        "s2_gaps": "the bone centerpiece, the water and the sitting-stones",
        "s1_none": "NO basin, NO bone centerpiece, NO water, NO sitting-stones",
    },
]

SPRITES_BY_NAME = {s["name"]: s for s in SPRITES}


# Style for plain MECHANICAL parts (arm segments): same chunky outline language as
# STYLE but with the character cues stripped — "big cute expressive eyes" in STYLE
# is what kept turning bolt holes into googly eyes on the excavator arm pieces.
PART_STYLE = (
    "Chunky toddler-friendly cartoon style mechanical part for a preschool game. "
    "Thick bold dark outlines, bright saturated colors, soft simple cel shading, "
    "flat 2D game sprite look. It is a plain mechanical part: absolutely no eyes, "
    "no face, no mouth, no character features. Absolutely no text, no letters, no "
    "numbers, no words, no logos, no watermark. The entire background must be a "
    "single solid flat pure magenta color #FF00FF (RGB 255,0,255) with nothing "
    "else on it, no gradient, no vignette. The subject casts NO shadow at all: no "
    "drop shadow, no ground shadow, no contact shadow. "
)


# Style for BUILDINGS/props (town). Same chunky toy-box outline language as STYLE,
# but the "big cute expressive eyes" character cue is stripped (it turned buildings
# into faces) and the subject is pinned to be an inanimate structure.
BUILDING_STYLE = (
    "Chunky toddler-friendly cartoon style building for a preschool game. Thick bold "
    "dark outlines, bright saturated colors, soft simple cel shading, rounded "
    "friendly chunky shapes, cheerful and inviting. Flat 2D game sprite look. It is "
    "an inanimate BUILDING / playset structure, an object only: absolutely NO face, "
    "no eyes, no mouth, no character features. Absolutely no text, no letters, no "
    "numbers, no words, no logos, no watermark. The entire background must be a "
    "single solid flat pure magenta color #FF00FF (RGB 255,0,255) with nothing else "
    "on it, no gradient, no vignette. The subject casts NO shadow at all: no drop "
    "shadow, no ground shadow, no contact shadow - the area directly under and around "
    "the subject is pure flat magenta with nothing on it. "
)

# --- Town construction-ladder strategy (DinoDigger-5li.3) -----------------------
# Un-build the finished building down four earlier construction states by chained
# img2img edits (each seeded from the next-more-complete state, so removal is
# gradual and identity/site stay put — the de-age chain, pointed at construction).
# Order matters: s3 is edited from the finished image, then s2 from s3, etc.
TOWN_STATES = ["s3", "s2", "s1", "s0"]
TOWN_SEED = {"s3": "done", "s2": "s3", "s1": "s2", "s0": "s1"}

# Town buildings sit on the ISOMETRIC overworld tilemap, so every state must be
# drawn from the same overhead three-quarter camera the Pebble Playground landed on
# by luck. Left unsaid, the model draws a flat straight-on elevation, which reads as
# a different game next to the isometric map. Applied to the finished gen AND every
# un-build rung so the camera can't drift down the chain.
TOWN_CAMERA = (
    "Drawn in a three-quarter overhead ISOMETRIC view for an isometric game map: the "
    "camera looks DOWN at the structure from about 45 degrees above and off to one "
    "side, so you clearly see the TOP of the building, its front and one side at "
    "once, and the ground footprint it stands on recedes as a diamond/rhombus shape. "
    "Not a flat straight-on front elevation. The structure sits on its own small "
    "patch of ground (grass or packed dirt) cut out in that same isometric diamond "
    "footprint, and nothing extends beyond that patch. "
)
#
# The four rungs are TEMPLATES: each building fills in its own kind/frame/parts
# wording from its spec, so an un-build reads about the right structure (the
# library's shelves and ladder, not the playground's slide and see-saw). The
# playground's spec supplies the original wording verbatim, so its chain is
# unchanged.
TOWN_UNBUILD = {
    "s3": ("Show this SAME {kind} at an EARLIER construction stage, NEARLY "
           "FINISHED but still a building site: {s3_frame} "
           "are up and most pieces are in place, but a few final parts are still "
           "MISSING ({s3_missing}), and a couple of wooden scaffolding poles and stacked "
           "loose stones sit beside it. Keep the EXACT same location, footprint, "
           "camera angle, art style, colors and outline; only make it look slightly "
           "less complete, like it is almost done being built."),
    "s2": ("Show this SAME {kind} HALF BUILT: only the basic skeleton FRAME is "
           "standing — {s2_frame} — with big obvious GAPS where "
           "{s2_gaps} will go (those are NOT built yet). A few "
           "scaffolding poles and piles of loose stones and logs lie around the site. "
           "Keep the EXACT same location, footprint, camera angle, art style, colors "
           "and outline; only strip it back to a half-finished frame."),
    "s1": ("Show this SAME building site at the FOUNDATION stage: nothing is standing "
           "up yet — just flat grey rectangular stone FOUNDATION SLABS laid out on the "
           "ground in the building's footprint, with a few stacked stones and logs and "
           "one or two short corner stakes. NO walls, NO frame, {s1_none}. "
           "Keep the EXACT same location, footprint, camera angle, art "
           "style, colors and outline; only show bare foundation slabs on the ground."),
    "s0": ("Show this SAME spot at GROUND-BREAKING, before any building: just a bare "
           "brown DIRT PATCH cleared on the grass in the building's footprint, marked "
           "out with a few thin wooden corner STAKES and string between them, plus a "
           "small pile of dirt and one dropped stone tool. Absolutely NOTHING is built "
           "— no slabs, no walls, no frame, no structure at all, only a marked-out "
           "dirt patch with stakes. Keep the EXACT same location, footprint, camera "
           "angle, art style, colors and outline."),
}


def item_prompt(spec: dict) -> str:
    style = PART_STYLE if spec.get("style") == "part" else STYLE
    return (f"Generate an image. {style}Draw {spec['subject']}. "
            f"Solid flat magenta #FF00FF background.")


def background_prompt(spec: dict) -> str:
    return (f"Generate a wide landscape image. {BG_STYLE}Draw {spec['subject']}. "
            f"CRITICAL COMPOSITION: the artwork must bleed to all four edges with "
            f"absolutely NO white bands, letterbox, or empty margins anywhere - blue "
            f"sky touches the very top edge, brown soil touches the very bottom edge, "
            f"and soil touches the left and right edges. The blue sky is a THIN strip "
            f"across only the TOP 20 percent of the image. The green grass lip is a "
            f"thin band right below the sky. The warm brown soil fills the ENTIRE "
            f"lower 75 percent of the image, all the way down to the bottom edge. Do "
            f"not center a small scene inside a white frame; the scene fills the whole "
            f"canvas.")


def turnaround_base_prompt(spec: dict) -> str:
    """Prompt for the front (S) view - a fresh single-subject generation."""
    return (f"Generate an image. {STYLE}A single full-body picture of {spec['subject']}. "
            f"One centered character in {ROTATE['S']}, feet visible. "
            f"Solid flat magenta #FF00FF background.")


def turnaround_rotate_prompt(spec: dict, direction: str) -> str:
    """Prompt that rotates the supplied reference image to `direction`."""
    return (f"Generate an image. Here is a reference picture of a character. Redraw the "
            f"EXACT SAME character - identical design, proportions, colors, outline "
            f"thickness, shading and size - but now shown as {ROTATE[direction]}. "
            f"Do not change the character in any way, only change the camera angle. "
            f"{NO_MIRROR}"
            f"Keep it a single centered full-body figure with feet visible. "
            f"Remove any shadow: NO drop shadow or ground shadow under the character. {STYLE}"
            f"Solid flat magenta #FF00FF background.")


def request_image(prompt: str, ref_b64: str | None = None) -> str | None:
    """Return base64 PNG payload (no data: prefix) or None.

    If ref_b64 (base64 PNG, no prefix) is given, it is attached as an input image so
    the model edits/rotates it (image-to-image)."""
    if ref_b64:
        content = [
            {"type": "text", "text": prompt},
            {"type": "image_url",
             "image_url": {"url": f"data:image/png;base64,{ref_b64}"}},
        ]
    else:
        content = prompt
    payload = {"model": MODEL, "messages": [{"role": "user", "content": content}]}
    headers = {
        "Authorization": f"Bearer {API_KEY}",
        "Content-Type": "application/json",
        "HTTP-Referer": "https://dinodigger.game",
        "X-Title": "Dino Digger Sprite Generator",
    }
    req = urllib.request.Request(API_URL, data=json.dumps(payload).encode(),
                                headers=headers, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=180) as resp:
            data = json.load(resp)
    except Exception as e:
        print(f"  ERROR: {e}", file=sys.stderr)
        return None

    choices = data.get("choices", [])
    if not choices:
        print(f"  No choices in response: {json.dumps(data)[:300]}", file=sys.stderr)
        return None
    message = choices[0].get("message", {})

    # OpenRouter returns generated images in message.images[]
    for img in message.get("images", []):
        if isinstance(img, dict):
            url_data = ""
            if img.get("type") == "image_url":
                url_data = img.get("image_url", {}).get("url", "")
            elif "url" in img:
                url_data = img["url"]
            if url_data.startswith("data:image"):
                return url_data.split(",", 1)[1]

    # Fallback: inline image parts in content
    content = message.get("content", "")
    if isinstance(content, list):
        for part in content:
            if isinstance(part, dict) and part.get("type") == "image_url":
                url_data = part.get("image_url", {}).get("url", "")
                if url_data.startswith("data:image"):
                    return url_data.split(",", 1)[1]

    print(f"  Could not extract image. Text: {str(content)[:200]}", file=sys.stderr)
    return None


def _attempt(prompt: str, ref_b64: str | None, label: str, retries: int) -> str | None:
    """Request one image with retries. Returns base64 PNG or None."""
    for attempt in range(1, retries + 2):
        print(f"[gen ] {label} (attempt {attempt})...")
        b64 = request_image(prompt, ref_b64)
        if b64:
            return b64
        if attempt <= retries:
            time.sleep(3)
    return None


def _save_raw(b64: str, path: str) -> None:
    img_bytes = base64.b64decode(b64)
    os.makedirs(RAW_DIR, exist_ok=True)
    with open(path, "wb") as f:
        f.write(img_bytes)
    print(f"       saved {path} ({len(img_bytes)/1024:.0f} KB)")


def generate_item(spec: dict, force: bool, retries: int = 2) -> str:
    """Generate a single item sheet. Returns 'saved' | 'skipped' | 'failed'."""
    name = spec["name"]
    out_path = os.path.join(RAW_DIR, f"{name}.png")
    if os.path.exists(out_path) and not force:
        print(f"[skip] {name} (raw exists)")
        return "skipped"
    b64 = _attempt(item_prompt(spec), None, name, retries)
    if not b64:
        print(f"       FAILED {name}")
        return "failed"
    _save_raw(b64, out_path)
    return "saved"


def generate_turnaround(spec: dict, force: bool, retries: int = 2) -> str:
    """Generate the 5 facings (S,SE,E,NE,N) for a character via image-to-image.

    Raw files land at raw/<name>_<DIR>.png. The slicer mirrors these to produce the
    left-side directions. Returns 'saved' | 'skipped' | 'failed'."""
    name = spec["name"]
    paths = {d: os.path.join(RAW_DIR, f"{name}_{d}.png") for d in GEN_DIRS}

    if all(os.path.exists(p) for p in paths.values()) and not force:
        print(f"[skip] {name} (all {len(GEN_DIRS)} facings exist)")
        return "skipped"

    # 1) Front (S) is the reference for every rotation - generate/load it first.
    if force or not os.path.exists(paths["S"]):
        base_b64 = _attempt(turnaround_base_prompt(spec), None, f"{name}_S", retries)
        if not base_b64:
            print(f"       FAILED {name}_S (base)")
            return "failed"
        _save_raw(base_b64, paths["S"])
    else:
        with open(paths["S"], "rb") as f:
            base_b64 = base64.b64encode(f.read()).decode()
        print(f"[skip] {name}_S (exists, used as reference)")

    # 2) Rotate the reference into the other facings.
    ok = True
    for d in [d for d in GEN_DIRS if d != "S"]:
        if os.path.exists(paths[d]) and not force:
            print(f"[skip] {name}_{d} (exists)")
            continue
        time.sleep(2)
        b64 = _attempt(turnaround_rotate_prompt(spec, d), base_b64, f"{name}_{d}", retries)
        if not b64:
            print(f"       FAILED {name}_{d}")
            ok = False
            continue
        _save_raw(b64, paths[d])
    return "saved" if ok else "failed"


def stage_transform_prompt(stage: str) -> str:
    """img2img prompt that de-ages the supplied reference to `stage`."""
    return (f"Generate an image. Here is a reference picture of a character. "
            f"{STAGE_TRANSFORM[stage]} Do NOT turn it into a different animal. "
            f"{LIMB_FREEZE}Keep it "
            f"a single centered full-body figure in a front view facing the camera, "
            f"feet visible. Remove any shadow: NO drop shadow or ground shadow under "
            f"the character. {STYLE}Solid flat magenta #FF00FF background.")


def generate_stages(spec: dict, force: bool, retries: int = 2) -> str:
    """Generate baby+kid facings for a dino by de-aging its adult front then
    rotating each stage. Raw files land at raw/<base>_<stage>_<DIR>.png. The slicer
    mirrors these into the left-side directions. Returns 'saved'|'skipped'|'failed'."""
    base = spec["base"]

    def raw(stage: str, d: str) -> str:
        return os.path.join(RAW_DIR, f"{base}_{stage}_{d}.png")

    all_paths = [raw(st, d) for st in STAGES for d in GEN_DIRS]
    if all(os.path.exists(p) for p in all_paths) and not force:
        print(f"[skip] {base} stages (all {len(all_paths)} facings exist)")
        return "skipped"

    ok = True

    # 1) Build each stage's S view by chained img2img transforms down the age ladder.
    for stage in STAGES:
        out_s = raw(stage, "S")
        if os.path.exists(out_s) and not force:
            print(f"[skip] {base}_{stage}_S (exists)")
            continue

        seed_stage = STAGE_SEED[stage]
        seed_path = (os.path.join(RAW_DIR, f"{base}_S.png") if seed_stage is None
                     else raw(seed_stage, "S"))
        if not os.path.exists(seed_path):
            print(f"       FAILED {base}_{stage}_S: seed {seed_path} missing", file=sys.stderr)
            ok = False
            continue

        with open(seed_path, "rb") as f:
            seed_b64 = base64.b64encode(f.read()).decode()
        b64 = _attempt(stage_transform_prompt(stage), seed_b64, f"{base}_{stage}_S", retries)
        if not b64:
            print(f"       FAILED {base}_{stage}_S")
            ok = False
            continue
        _save_raw(b64, out_s)
        time.sleep(2)

    # 2) Rotate each stage's S view into the other facings (mirrors done by slicer).
    for stage in STAGES:
        s_path = raw(stage, "S")
        if not os.path.exists(s_path):
            print(f"       SKIP {base}_{stage} rotations (no S view)", file=sys.stderr)
            ok = False
            continue

        with open(s_path, "rb") as f:
            base_b64 = base64.b64encode(f.read()).decode()
        for d in [d for d in GEN_DIRS if d != "S"]:
            out_d = raw(stage, d)
            if os.path.exists(out_d) and not force:
                print(f"[skip] {base}_{stage}_{d} (exists)")
                continue
            time.sleep(2)
            b64 = _attempt(turnaround_rotate_prompt(spec, d), base_b64,
                           f"{base}_{stage}_{d}", retries)
            if not b64:
                print(f"       FAILED {base}_{stage}_{d}")
                ok = False
                continue
            _save_raw(b64, out_d)

    return "saved" if ok else "failed"


def stride_prompt(pose: str, direction: str = "S", base: str | None = None,
                  stage: str | None = None) -> str:
    """img2img prompt that repositions ONLY the legs of the supplied reference into
    a mid-stride walking pose. Everything else (identity, colors, camera, position,
    scale) must stay bit-for-bit consistent so the walk cycle doesn't flicker.

    For back-facing views (N, and the back-view three-quarter NE) an EXTRA tightened
    back-detail freeze is appended: on those angles the model tends to redraw spine
    bumps / back ridges / tail placement, which pops against the idle frame. We pin
    those features to be identical to the reference. A per-species freeze (neck /
    wings) from SPECIES_STRIDE_FREEZE is appended for `base` when present."""
    back_freeze = ""
    if direction in ("N", "NE"):
        back_freeze = (
            "EXTRA CRITICAL — THIS IS A BACK VIEW: the back ridges, spine bumps, back "
            "plates/fin/frill/crest and the TAIL must be IDENTICAL to the reference — "
            "the SAME shapes, the SAME positions, and the tail on the SAME side as the "
            "reference. Do NOT add, remove, move, re-curve, resize or re-detail any "
            "back or tail feature; copy the entire back and tail exactly as drawn and "
            "change ONLY the legs. ")
    species_freeze = SPECIES_STRIDE_FREEZE.get(base, "")
    is_quad = (base, stage) in QUADRUPED_STAGES
    move = (QUAD_STRIDE_TRANSFORM if is_quad else STRIDE_TRANSFORM)[pose]
    quad_freeze = QUAD_STANCE_FREEZE if is_quad else ""
    return (
        f"Generate an image. Here is a reference picture of a character standing. "
        f"{quad_freeze}"
        f"Redraw the EXACT SAME character — identical design, proportions, colors, "
        f"markings, outline thickness, shading and overall SIZE — but now captured "
        f"MID-STRIDE while walking, with {move}. "
        f"CRITICAL: change ONLY the legs and feet — a tiny up/down body bob is fine, "
        f"but the HEAD, face, body, belly, arms and tail must stay EXACTLY as in the "
        f"reference, drawn in the SAME position on the canvas at the SAME scale. "
        f"{LIMB_FREEZE}{back_freeze}"
        f"THE CAMERA ANGLE MUST BE IDENTICAL TO THE REFERENCE: if the reference shows "
        f"the character from BEHIND (its back, no face visible), the new image must "
        f"ALSO show it from behind with NO face visible; if it is a side profile, keep "
        f"the exact same side profile; if it faces the camera, keep it facing the "
        f"camera. Do NOT turn, rotate, resize, flip or re-center the character; do NOT "
        f"change its outline or colors; every body part visible in the reference stays "
        f"visible (including the tail) and nothing new becomes visible. Only its legs "
        f"move, as if it is taking a walking step. {species_freeze}Keep it a single "
        f"centered full-body figure with both feet visible. Remove any shadow: NO drop "
        f"shadow or ground shadow under the character. {STYLE}"
        f"Solid flat magenta #FF00FF background.")


def generate_strides(spec: dict, force: bool, retries: int = 2) -> str:
    """Generate the two mid-stride walk frames for each canonical facing by editing
    the existing per-facing sprite. Raw files land at raw/<base>_<pose>_<DIR>.png
    (adult) or raw/<base>_<stage>_<pose>_<DIR>.png (a growth stage). The slicer
    mirrors the right-side facings. Resumes: only missing frames are regenerated
    unless --force. Returns 'saved' | 'skipped' | 'failed'."""
    base = spec["base"]
    stride_stages = spec.get("stride_stages", [None])

    def seed_path(stage, d):
        return (os.path.join(RAW_DIR, f"{base}_{d}.png") if stage is None
                else os.path.join(RAW_DIR, f"{base}_{stage}_{d}.png"))

    def out_path(stage, pose, d):
        tag = pose if stage is None else f"{stage}_{pose}"
        return os.path.join(RAW_DIR, f"{base}_{tag}_{d}.png")

    all_paths = [out_path(st, pose, d)
                 for st in stride_stages for pose in STRIDES for d in GEN_DIRS]
    if all(os.path.exists(p) for p in all_paths) and not force:
        print(f"[skip] {base} strides (all {len(all_paths)} frames exist)")
        return "skipped"

    ok = True
    for stage in stride_stages:
        label_stage = "adult" if stage is None else stage
        for d in GEN_DIRS:
            seed = seed_path(stage, d)
            if not os.path.exists(seed):
                print(f"       FAILED {base} {label_stage} {d} strides: "
                      f"seed {seed} missing", file=sys.stderr)
                ok = False
                continue
            with open(seed, "rb") as f:
                seed_b64 = base64.b64encode(f.read()).decode()

            for pose in STRIDES:
                op = out_path(stage, pose, d)
                if os.path.exists(op) and not force:
                    print(f"[skip] {base}_{label_stage}_{pose}_{d} (exists)")
                    continue
                time.sleep(2)
                b64 = _attempt(stride_prompt(pose, d, base, stage), seed_b64,
                               f"{base}_{label_stage}_{pose}_{d}", retries)
                if not b64:
                    print(f"       FAILED {base}_{label_stage}_{pose}_{d}")
                    ok = False
                    continue
                _save_raw(b64, op)

    return "saved" if ok else "failed"


# --- Backhoe roll strategy (DinoDigger-682) -------------------------------------
# A vehicle variant of the stride pipeline: two wheel-roll frames per canonical
# facing, produced by img2img editing the EXISTING backhoe facing. The pose edit
# rotates ONLY the wheels' spokes/hubs and applies a tiny suspension bob — the body,
# cab, arm, bucket, eyes, colors, outline, camera angle, scale and on-canvas position
# all stay identical, so the drive cycle (idle -> A -> idle -> B) never flickers the
# machine's identity. The slicer aligns each roll frame to its idle facing's union
# trim box (same as the strides) so the body never hops when frames swap.
ROLL_POSES = ["rollA", "rollB"]
ROLL_TRANSFORM = {
    "rollA": ("its wheels/tyres rotated to a VISIBLY DIFFERENT spoke and hub angle "
              "(as if they have turned a little while rolling), and the whole body "
              "dipped DOWN about 2% lower on its suspension"),
    "rollB": ("its wheels/tyres rotated to a VISIBLY DIFFERENT spoke and hub angle "
              "the OTHER way (as if rolling on), and the whole body bobbed UP about "
              "2% higher on its suspension"),
}


def roll_prompt(pose: str, direction: str = "S") -> str:
    """img2img prompt that spins ONLY the wheels + adds a small suspension bob to the
    supplied backhoe facing. Everything else must stay bit-for-bit consistent so the
    roll cycle doesn't flicker the vehicle's identity."""
    return (
        f"Generate an image. Here is a reference picture of a cartoon backhoe digger "
        f"vehicle. Redraw the EXACT SAME vehicle — identical design, proportions, "
        f"colors, markings, outline thickness, shading and overall SIZE, at the SAME "
        f"camera angle and the SAME position on the canvas — but now with "
        f"{ROLL_TRANSFORM[pose]}. "
        f"CRITICAL: change ONLY the wheels' spoke/hub rotation and the tiny up/down "
        f"suspension bob. The cab, window, cute eyes, exhaust, digging arm, bucket, "
        f"body panels and every other part must stay EXACTLY as in the reference, "
        f"drawn in the SAME position at the SAME scale and the SAME camera angle. "
        f"NOTHING else changes. Do NOT turn, rotate, resize, flip or re-center the "
        f"vehicle; do NOT change its outline or colors. Keep it a single centered "
        f"vehicle with all wheels visible. Remove any shadow: NO drop shadow or "
        f"ground shadow under the vehicle. {STYLE}Solid flat magenta #FF00FF "
        f"background.")


def generate_roll(spec: dict, force: bool, retries: int = 2) -> str:
    """Generate the two wheel-roll frames for each canonical backhoe facing by
    editing the existing per-facing sprite. Raw files land at
    raw/<base>_<pose>_<DIR>.png. The slicer mirrors the right-side facings. Resumes:
    only missing frames are regenerated unless --force. Returns
    'saved' | 'skipped' | 'failed'."""
    base = spec["base"]

    def seed_path(d):
        return os.path.join(RAW_DIR, f"{base}_{d}.png")

    def out_path(pose, d):
        return os.path.join(RAW_DIR, f"{base}_{pose}_{d}.png")

    all_paths = [out_path(pose, d) for pose in ROLL_POSES for d in GEN_DIRS]
    if all(os.path.exists(p) for p in all_paths) and not force:
        print(f"[skip] {base} roll (all {len(all_paths)} frames exist)")
        return "skipped"

    ok = True
    for d in GEN_DIRS:
        seed = seed_path(d)
        if not os.path.exists(seed):
            print(f"       FAILED {base} {d} roll: seed {seed} missing",
                  file=sys.stderr)
            ok = False
            continue
        with open(seed, "rb") as f:
            seed_b64 = base64.b64encode(f.read()).decode()

        for pose in ROLL_POSES:
            op = out_path(pose, d)
            if os.path.exists(op) and not force:
                print(f"[skip] {base}_{pose}_{d} (exists)")
                continue
            time.sleep(2)
            b64 = _attempt(roll_prompt(pose, d), seed_b64,
                           f"{base}_{pose}_{d}", retries)
            if not b64:
                print(f"       FAILED {base}_{pose}_{d}")
                ok = False
                continue
            _save_raw(b64, op)

    return "saved" if ok else "failed"


def generate_background(spec: dict, force: bool, retries: int = 2) -> str:
    """Generate a single full-bleed background. Returns 'saved'|'skipped'|'failed'."""
    name = spec["name"]
    out_path = os.path.join(RAW_DIR, f"{name}.png")
    if os.path.exists(out_path) and not force:
        print(f"[skip] {name} (raw exists)")
        return "skipped"
    b64 = _attempt(background_prompt(spec), None, name, retries)
    if not b64:
        print(f"       FAILED {name}")
        return "failed"
    _save_raw(b64, out_path)
    return "saved"


def imgedit_prompt(spec: dict) -> str:
    return (f"Generate an image. Here is a reference picture. {spec['instruction']} {STYLE}"
            f"Solid flat magenta #FF00FF background.")


def generate_imgedit(spec: dict, force: bool, retries: int = 2) -> str:
    """Edit an existing raw sprite (image-to-image). Returns 'saved'|'skipped'|'failed'."""
    name = spec["name"]
    out_path = os.path.join(RAW_DIR, f"{name}.png")
    if os.path.exists(out_path) and not force:
        print(f"[skip] {name} (raw exists)")
        return "skipped"

    ref_path = os.path.join(RAW_DIR, f"{spec['ref']}.png")
    if not os.path.exists(ref_path):
        print(f"       FAILED {name}: reference {ref_path} missing", file=sys.stderr)
        return "failed"
    with open(ref_path, "rb") as f:
        ref_b64 = base64.b64encode(f.read()).decode()

    b64 = _attempt(imgedit_prompt(spec), ref_b64, name, retries)
    if not b64:
        print(f"       FAILED {name}")
        return "failed"
    _save_raw(b64, out_path)
    return "saved"


def town_finished_prompt(spec: dict) -> str:
    """Fresh single-subject generation of the finished building (no reference)."""
    return (f"Generate an image. {BUILDING_STYLE}{TOWN_CAMERA}"
            f"Draw {spec['finished_subject']}. "
            f"One single centered structure, its whole base/footprint visible. "
            f"Solid flat magenta #FF00FF background.")


def town_unbuild_prompt(state: str, spec: dict) -> str:
    """img2img prompt that un-builds the supplied reference to an earlier state.

    The rung template is filled from the spec so each building is un-built in its
    own vocabulary (see TOWN_UNBUILD)."""
    kind = spec.get("kind", "building")
    rung = TOWN_UNBUILD[state].format(
        kind=kind,
        s3_frame=spec.get("s3_frame", "the foundation and the main frame"),
        s3_missing=spec.get("s3_missing", "a couple of final pieces not yet added"),
        s2_frame=spec.get("s2_frame", "the foundation stones and a few bare upright "
                                      "wooden support poles / partial stone walls"),
        s2_gaps=spec.get("s2_gaps", "the roof and the finishing pieces"),
        s1_none=spec.get("s1_none", "NO roof, NO fittings"),
    )
    return (f"Generate an image. Here is a reference picture of a cartoon stone-age "
            f"{kind} building. {rung} It stays a single centered "
            f"structure with its base/footprint in the same place, seen from the "
            f"EXACT same camera as the reference. {BUILDING_STYLE}{TOWN_CAMERA}"
            f"Solid flat magenta #FF00FF background.")


def generate_town(spec: dict, force: bool, retries: int = 2) -> str:
    """Generate the finished building then un-build it down the construction ladder
    by chained img2img edits. Raw files land at raw/<name>_{done,s3,s2,s1,s0}.png.
    Resumes: only missing states are regenerated unless --force (delete a bad state
    and re-run to redo just that link + everything downstream of it). Returns
    'saved' | 'skipped' | 'failed'."""
    name = spec["name"]

    def raw(state: str) -> str:
        return os.path.join(RAW_DIR, f"{name}_{state}.png")

    all_paths = [raw("done")] + [raw(st) for st in TOWN_STATES]
    if all(os.path.exists(p) for p in all_paths) and not force:
        print(f"[skip] {name} town (finished + {len(TOWN_STATES)} states exist)")
        return "skipped"

    ok = True

    # 1) Finished building — fresh generation, reference for the un-build ladder.
    done_path = raw("done")
    if force or not os.path.exists(done_path):
        b64 = _attempt(town_finished_prompt(spec), None, f"{name}_done", retries)
        if not b64:
            print(f"       FAILED {name}_done (finished building)")
            return "failed"
        _save_raw(b64, done_path)
        time.sleep(2)
    else:
        print(f"[skip] {name}_done (exists, used as reference)")

    # 2) Chained un-build down the ladder (done -> s3 -> s2 -> s1 -> s0).
    for state in TOWN_STATES:
        out = raw(state)
        if os.path.exists(out) and not force:
            print(f"[skip] {name}_{state} (exists)")
            continue

        seed_state = TOWN_SEED[state]
        seed_path = raw(seed_state)
        if not os.path.exists(seed_path):
            print(f"       FAILED {name}_{state}: seed {seed_path} missing",
                  file=sys.stderr)
            ok = False
            continue

        with open(seed_path, "rb") as f:
            seed_b64 = base64.b64encode(f.read()).decode()
        b64 = _attempt(town_unbuild_prompt(state, spec), seed_b64,
                       f"{name}_{state}", retries)
        if not b64:
            print(f"       FAILED {name}_{state}")
            ok = False
            continue
        _save_raw(b64, out)
        time.sleep(2)

    return "saved" if ok else "failed"


def generate_one(spec: dict, force: bool) -> str:
    if spec["category"] == "town":
        return generate_town(spec, force)
    if spec["category"] == "turnaround":
        return generate_turnaround(spec, force)
    if spec["category"] == "stages":
        return generate_stages(spec, force)
    if spec["category"] == "strides":
        return generate_strides(spec, force)
    if spec["category"] == "roll":
        return generate_roll(spec, force)
    if spec["category"] == "background":
        return generate_background(spec, force)
    if spec["category"] == "imgedit":
        return generate_imgedit(spec, force)
    return generate_item(spec, force)


def main():
    ap = argparse.ArgumentParser(description="Generate Dino Digger sprite sheets.")
    ap.add_argument("--force", action="store_true", help="regenerate even if raw exists")
    ap.add_argument("--only", metavar="NAME", help="generate only this entry")
    ap.add_argument("--list", action="store_true", help="list entry names and exit")
    args = ap.parse_args()

    if args.list:
        for s in SPRITES:
            if s["category"] == "turnaround":
                detail = f"facings={'+'.join(GEN_DIRS)} (+mirror)"
            elif s["category"] == "stages":
                detail = f"stages={'+'.join(STAGES)} de-age({s['base']}_S) +rotate +mirror"
            elif s["category"] == "strides":
                sstages = [("adult" if st is None else st)
                           for st in s.get("stride_stages", [None])]
                detail = f"strides={'+'.join(STRIDES)} x[{'+'.join(sstages)}] img2img +mirror"
            elif s["category"] == "roll":
                detail = f"roll={'+'.join(ROLL_POSES)} img2img({s['base']}_<DIR>) +mirror"
            elif s["category"] == "town":
                detail = f"done + un-build[{'+'.join(TOWN_STATES)}] img2img chain"
            elif s["category"] == "background":
                detail = f"single -> {s['outfile']}"
            elif s["category"] == "imgedit":
                detail = f"img2img({s['ref']}) -> {s['outfile']}"
            else:
                detail = f"grid={s['grid']}"
            print(f"{s['name']:14s} {s['category']:10s} {detail}")
        return

    if not API_KEY:
        print("ERROR: no API key (Tools/.openrouter_key missing and "
              "OPENROUTER_API_KEY unset)", file=sys.stderr)
        sys.exit(1)

    if args.only:
        if args.only not in SPRITES_BY_NAME:
            print(f"ERROR: unknown entry '{args.only}'. Options: "
                  f"{', '.join(SPRITES_BY_NAME)}", file=sys.stderr)
            sys.exit(1)
        todo = [SPRITES_BY_NAME[args.only]]
    else:
        todo = SPRITES

    os.makedirs(RAW_DIR, exist_ok=True)
    tally = {"saved": 0, "skipped": 0, "failed": 0}
    failures = []
    for i, spec in enumerate(todo):
        # --only regenerates a targeted single-shot entry, but the stage/stride
        # chains are multi-image sequences with per-file retries — force only on an
        # explicit --force so a re-run RESUMES from the failed image (letting me
        # delete a bad frame and re-run to regenerate just that one) instead of
        # redoing the whole set.
        force = args.force or (bool(args.only) and
                               spec["category"] not in ("stages", "strides", "roll", "town"))
        result = generate_one(spec, force)
        tally[result] += 1
        if result == "failed":
            failures.append(spec["name"])
        if i < len(todo) - 1 and result != "skipped":
            time.sleep(2)  # gentle rate limit

    print(f"\nDone: {tally['saved']} generated, {tally['skipped']} skipped, "
          f"{tally['failed']} failed.")
    if failures:
        print("Failed: " + ", ".join(failures))
        sys.exit(2)


if __name__ == "__main__":
    main()
