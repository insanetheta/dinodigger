#!/usr/bin/env python3
"""Generate the pond-duck sprites for Dino Digger (ticket DinoDigger-0qr.2).

Standalone companion to generate_sprites.py (which another agent owns — this
script never imports from or edits it). It reuses the same OpenRouter Gemini
Flash Image API pattern and the same chunky toddler-cartoon / solid-magenta
style, and it borrows only the pure image helpers (chroma_key, trim) from
slice_sprites.py so the keying matches every other sprite.

It produces two frames for a cute white duck:
    duck_E    — right-facing side view (the drift pose; mirrored to W at runtime)
    duck_fly  — an airborne flapping frame (the catch / flap-away pose)

Raw generations land in Tools/raw/duck_*.png; the sliced, transparent,
game-ready PNGs land in Assets/Art/Generated/duck/duck_E.png and duck_fly.png.

Usage:
    python3 generate_ducks.py            # generate the missing frames
    python3 generate_ducks.py --force    # regenerate both frames
"""

import argparse
import base64
import json
import os
import sys
import time
import urllib.request

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
# Reuse ONLY the pure image helpers — no manifest coupling, no edits to either file.
from slice_sprites import chroma_key, trim  # noqa: E402

# --- API config (mirrors generate_sprites.py) ------------------------------------
TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
API_KEY = (lambda _p: open(_p).read().strip() if os.path.exists(_p)
           else os.environ.get("OPENROUTER_API_KEY", ""))(
    os.path.join(TOOLS_DIR, ".openrouter_key"))
MODEL = "google/gemini-2.5-flash-image"
API_URL = "https://openrouter.ai/api/v1/chat/completions"

RAW_DIR = os.path.join(TOOLS_DIR, "raw")
OUT_DIR = os.path.normpath(
    os.path.join(TOOLS_DIR, "..", "Assets", "Art", "Generated", "duck"))

# Shared toddler-cartoon style (identical language to generate_sprites.STYLE).
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

DUCK_SUBJECT = (
    "a chubby happy little white cartoon duck with a rounded body, soft white "
    "feathers, a small orange beak, orange webbed feet, and one big cute sparkly eye"
)

# Two frames: a drift side view and an airborne flap frame.
FRAMES = {
    "duck_E": (
        f"A single full-body picture of {DUCK_SUBJECT}, shown as a right side "
        f"profile view: the duck faces directly to the RIGHT, floating calmly on "
        f"the water surface, wings tucked against its body, feet visible below. "
        f"One centered duck."
    ),
    "duck_fly": (
        f"A single full-body picture of {DUCK_SUBJECT}, shown as a right side "
        f"profile view flying to the RIGHT: the duck is airborne with BOTH wings "
        f"spread wide and lifted UP mid-flap, neck stretched happily forward, feet "
        f"tucked up under its body. One centered duck."
    ),
}


def request_image(prompt: str) -> str | None:
    """Return base64 PNG payload (no data: prefix) or None."""
    payload = {"model": MODEL, "messages": [{"role": "user", "content": prompt}]}
    headers = {
        "Authorization": f"Bearer {API_KEY}",
        "Content-Type": "application/json",
        "HTTP-Referer": "https://dinodigger.game",
        "X-Title": "Dino Digger Duck Generator",
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

    for img in message.get("images", []):
        if isinstance(img, dict):
            url_data = ""
            if img.get("type") == "image_url":
                url_data = img.get("image_url", {}).get("url", "")
            elif "url" in img:
                url_data = img["url"]
            if url_data.startswith("data:image"):
                return url_data.split(",", 1)[1]

    content = message.get("content", "")
    if isinstance(content, list):
        for part in content:
            if isinstance(part, dict) and part.get("type") == "image_url":
                url_data = part.get("image_url", {}).get("url", "")
                if url_data.startswith("data:image"):
                    return url_data.split(",", 1)[1]

    print(f"  Could not extract image. Text: {str(content)[:200]}", file=sys.stderr)
    return None


def frame_prompt(subject: str) -> str:
    return (f"Generate an image. {STYLE}{subject} Solid flat magenta #FF00FF background.")


def _attempt(prompt: str, label: str, retries: int = 2) -> str | None:
    for attempt in range(1, retries + 2):
        print(f"[gen ] {label} (attempt {attempt})...")
        b64 = request_image(prompt)
        if b64:
            return b64
        if attempt <= retries:
            time.sleep(3)
    return None


def generate_frame(name: str, subject: str, force: bool) -> str:
    """Generate + slice one duck frame. Returns 'saved' | 'skipped' | 'failed'."""
    raw_path = os.path.join(RAW_DIR, f"{name}.png")
    out_path = os.path.join(OUT_DIR, f"{name}.png")

    if os.path.exists(raw_path) and not force:
        print(f"[skip] {name} (raw exists — re-slicing)")
    else:
        b64 = _attempt(frame_prompt(subject), name)
        if not b64:
            print(f"       FAILED {name}")
            return "failed"
        os.makedirs(RAW_DIR, exist_ok=True)
        img_bytes = base64.b64decode(b64)
        with open(raw_path, "wb") as f:
            f.write(img_bytes)
        print(f"       saved {raw_path} ({len(img_bytes)/1024:.0f} KB)")

    # Slice: chroma-key the magenta background out and trim to the visible duck.
    os.makedirs(OUT_DIR, exist_ok=True)
    keyed = trim(chroma_key(Image.open(raw_path)), pad=8)
    keyed.save(out_path)
    print(f"       {out_path}  ({keyed.width}x{keyed.height})")
    return "saved"


def main():
    ap = argparse.ArgumentParser(description="Generate Dino Digger duck sprites.")
    ap.add_argument("--force", action="store_true", help="regenerate even if raw exists")
    args = ap.parse_args()

    if not API_KEY:
        print("ERROR: no API key (Tools/.openrouter_key missing and "
              "OPENROUTER_API_KEY unset)", file=sys.stderr)
        sys.exit(1)

    os.makedirs(RAW_DIR, exist_ok=True)
    tally = {"saved": 0, "skipped": 0, "failed": 0}
    failures = []
    for i, (name, subject) in enumerate(FRAMES.items()):
        result = generate_frame(name, subject, force=args.force)
        tally[result] += 1
        if result == "failed":
            failures.append(name)
        if i < len(FRAMES) - 1:
            time.sleep(2)  # gentle rate limit

    print(f"\nDone: {tally['saved']} generated, {tally['skipped']} skipped, "
          f"{tally['failed']} failed.")
    if failures:
        print("Failed: " + ", ".join(failures))
        sys.exit(2)


if __name__ == "__main__":
    main()
