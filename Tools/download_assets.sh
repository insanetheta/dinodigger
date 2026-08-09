#!/usr/bin/env bash
#
# download_assets.sh — re-downloads and re-organizes all free CC0 art/audio
# assets used by the "Dino Digger" toddler game.
#
# All packs are Kenney (kenney.nl, CC0) plus one CC0 music track from
# OpenGameArt. Re-runnable and idempotent: it re-fetches every zip, verifies
# each is a real zip, extracts only the useful PNG/audio payload (stripping
# vector sources, previews, fonts and junk files) into Assets/.
#
# Usage:  bash Tools/download_assets.sh
#
set -euo pipefail

# Project root = parent of this script's directory.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

ART="$ROOT/Assets/Art/Kenney"
AUD="$ROOT/Assets/Audio/Kenney"
MUS="$ROOT/Assets/Audio/Music"
TMP="$ROOT/Tools/.asset_dl_tmp"

UA="Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36"

mkdir -p "$ART" "$AUD" "$MUS" "$TMP"

# fetch <outfile> <url>
fetch() {
  echo ">> downloading $1"
  curl -fSL -A "$UA" "$2" -o "$TMP/$1"
  if ! file "$TMP/$1" | grep -qi 'Zip archive'; then
    echo "ERROR: $1 is not a valid zip archive" >&2
    file "$TMP/$1" >&2
    exit 1
  fi
}

clean_junk() {
  find "$1" \( -name 'Thumbs.db' -o -name 'desktop.ini' -o -name '.DS_Store' \) -delete 2>/dev/null || true
}

# ---------------------------------------------------------------------------
# Kenney zip URLs (CC0). If a hash-path 404s, open the asset page and grab the
# fresh kenney_<slug>.zip link under /media/pages/assets/<slug>/<hash>/.
# ---------------------------------------------------------------------------
URL_BLOCKS="https://kenney.nl/media/pages/assets/isometric-blocks/86a0152f5b-1677662261/kenney_isometric-blocks.zip"
URL_UI="https://kenney.nl/media/pages/assets/ui-pack/f651646eab-1718203990/kenney_ui-pack.zip"
URL_FARM="https://kenney.nl/media/pages/assets/isometric-miniature-farm/abd0274182-1670690319/kenney_isometric-miniature-farm.zip"
URL_DIGITAL="https://kenney.nl/media/pages/assets/digital-audio/216eac4753-1677590265/kenney_digital-audio.zip"
URL_IFACE="https://kenney.nl/media/pages/assets/interface-sounds/fa43c1dd4d-1677589452/kenney_interface-sounds.zip"
URL_IMPACT="https://kenney.nl/media/pages/assets/impact-sounds/87b4ddecda-1677589768/kenney_impact-sounds.zip"
URL_JINGLES="https://kenney.nl/media/pages/assets/music-jingles/f37e530b9e-1677590399/kenney_music-jingles.zip"

# OpenGameArt CC0 music (Bluebonnet by Kistol — gentle/happy, looped OGG).
URL_MUSIC="https://opengameart.org/sites/default/files/bluebonnet_in_b_major_looped_0.ogg"

# ---------------------------------------------------------------------------
# Download + verify
# ---------------------------------------------------------------------------
fetch isometric-blocks.zip          "$URL_BLOCKS"
fetch ui-pack.zip                   "$URL_UI"
fetch isometric-miniature-farm.zip  "$URL_FARM"
fetch digital-audio.zip             "$URL_DIGITAL"
fetch interface-sounds.zip          "$URL_IFACE"
fetch impact-sounds.zip             "$URL_IMPACT"
fetch music-jingles.zip             "$URL_JINGLES"

# ---------------------------------------------------------------------------
# Extract + organize (keep only useful payload)
# ---------------------------------------------------------------------------
rm -rf "$TMP/stage"; mkdir -p "$TMP/stage"

# Isometric Blocks -> individual PNG sprites + tilesheets (drop Vector/previews)
unzip -q -o "$TMP/isometric-blocks.zip" -d "$TMP/stage/ib"
rm -rf "$ART/IsometricBlocks"; mkdir -p "$ART/IsometricBlocks"
cp -R "$TMP/stage/ib/PNG"        "$ART/IsometricBlocks/"
cp -R "$TMP/stage/ib/Tilesheet"  "$ART/IsometricBlocks/"
cp "$TMP/stage/ib/License.txt"   "$ART/IsometricBlocks/" 2>/dev/null || true
clean_junk "$ART/IsometricBlocks"

# UI Pack -> PNG buttons/panels only (drop Vector SVG source, Font, Sounds, previews)
unzip -q -o "$TMP/ui-pack.zip" -d "$TMP/stage/ui"
rm -rf "$ART/UIPack"; mkdir -p "$ART/UIPack"
cp -R "$TMP/stage/ui/PNG"       "$ART/UIPack/"
cp "$TMP/stage/ui/License.txt"  "$ART/UIPack/" 2>/dev/null || true
clean_junk "$ART/UIPack"

# Isometric Miniature Farm -> Isometric + Angle PNG renders (drop samples/previews)
unzip -q -o "$TMP/isometric-miniature-farm.zip" -d "$TMP/stage/farm"
rm -rf "$ART/IsometricMiniatureFarm"; mkdir -p "$ART/IsometricMiniatureFarm"
cp -R "$TMP/stage/farm/Isometric"  "$ART/IsometricMiniatureFarm/"
cp -R "$TMP/stage/farm/Angle"      "$ART/IsometricMiniatureFarm/"
cp "$TMP/stage/farm/License.txt"   "$ART/IsometricMiniatureFarm/" 2>/dev/null || true
clean_junk "$ART/IsometricMiniatureFarm"

# Digital Audio -> ogg only
unzip -q -o "$TMP/digital-audio.zip" -d "$TMP/stage/da"
rm -rf "$AUD/DigitalAudio"; mkdir -p "$AUD/DigitalAudio"
cp "$TMP/stage/da/Audio/"*.ogg     "$AUD/DigitalAudio/"
cp "$TMP/stage/da/License.txt"     "$AUD/DigitalAudio/" 2>/dev/null || true

# Interface Sounds -> ogg only
unzip -q -o "$TMP/interface-sounds.zip" -d "$TMP/stage/is"
rm -rf "$AUD/InterfaceSounds"; mkdir -p "$AUD/InterfaceSounds"
cp "$TMP/stage/is/Audio/"*.ogg     "$AUD/InterfaceSounds/"
cp "$TMP/stage/is/License.txt"     "$AUD/InterfaceSounds/" 2>/dev/null || true

# ---------------------------------------------------------------------------
# Dig-audio packs (DinoDigger-7c4). These two ship 130 + 85 oggs; the dig pass
# uses a curated handful, so unlike the packs above we copy ONLY the files the
# AudioConfig actually references. Keeping the whitelist here (rather than
# copying everything) is what keeps Assets/Audio small — if you wire a new clip
# in GeneratedArtImporter, add its filename to the matching list below or the
# importer will report it missing after a fresh download.
# See Tools/ASSET_SOURCES.md for the clip -> game-moment mapping.
# ---------------------------------------------------------------------------

# copy_picks <src-audio-dir> <dst-dir> <file...>
copy_picks() {
  local src="$1"; local dst="$2"; shift 2
  mkdir -p "$dst"
  local f
  for f in "$@"; do
    if [ ! -f "$src/$f" ]; then
      echo "ERROR: expected clip '$f' is not in the downloaded pack ($src)" >&2
      exit 1
    fi
    cp "$src/$f" "$dst/"
  done
}

# Impact Sounds -> the dig one-shots: tile cracks, cascade thumps, crystal and
# glass pops, bone knocks, the pinata pot, and the machine's wake bell.
unzip -q -o "$TMP/impact-sounds.zip" -d "$TMP/stage/imp"
rm -rf "$AUD/ImpactSounds"
copy_picks "$TMP/stage/imp/Audio" "$AUD/ImpactSounds" \
  impactMining_000.ogg impactMining_001.ogg impactMining_002.ogg \
  impactSoft_heavy_000.ogg impactSoft_heavy_001.ogg \
  impactSoft_medium_000.ogg \
  impactGlass_light_000.ogg impactGlass_medium_000.ogg \
  impactWood_light_000.ogg \
  impactTin_medium_000.ogg \
  impactBell_heavy_002.ogg
cp "$TMP/stage/imp/License.txt" "$AUD/ImpactSounds/" 2>/dev/null || true

# Music Jingles -> two pizzicato phrases: the coin-spray flourish and the short
# music-box vamp the dance party loops.
unzip -q -o "$TMP/music-jingles.zip" -d "$TMP/stage/mj"
rm -rf "$AUD/MusicJingles"
copy_picks "$TMP/stage/mj/Audio/Pizzicato jingles" "$AUD/MusicJingles" \
  jingles_PIZZI00.ogg jingles_PIZZI03.ogg
cp "$TMP/stage/mj/License.txt" "$AUD/MusicJingles/" 2>/dev/null || true

# ---------------------------------------------------------------------------
# Background music (OpenGameArt, CC0)
# ---------------------------------------------------------------------------
echo ">> downloading background music (Bluebonnet, looped OGG)"
curl -fSL -A "$UA" "$URL_MUSIC" -o "$TMP/bluebonnet.ogg"
if ! file "$TMP/bluebonnet.ogg" | grep -qi 'Ogg data'; then
  echo "ERROR: music file is not a valid Ogg file" >&2
  exit 1
fi
cp "$TMP/bluebonnet.ogg" "$MUS/Bluebonnet_looped.ogg"

# ---------------------------------------------------------------------------
# Cleanup temp
# ---------------------------------------------------------------------------
rm -rf "$TMP"

echo ""
echo "All assets downloaded and organized:"
echo "  $ART/IsometricBlocks         ($(find "$ART/IsometricBlocks" -name '*.png' | wc -l | tr -d ' ') png)"
echo "  $ART/UIPack                  ($(find "$ART/UIPack" -name '*.png' | wc -l | tr -d ' ') png)"
echo "  $ART/IsometricMiniatureFarm  ($(find "$ART/IsometricMiniatureFarm" -name '*.png' | wc -l | tr -d ' ') png)"
echo "  $AUD/DigitalAudio            ($(find "$AUD/DigitalAudio" -name '*.ogg' | wc -l | tr -d ' ') ogg)"
echo "  $AUD/InterfaceSounds         ($(find "$AUD/InterfaceSounds" -name '*.ogg' | wc -l | tr -d ' ') ogg)"
echo "  $AUD/ImpactSounds            ($(find "$AUD/ImpactSounds" -name '*.ogg' | wc -l | tr -d ' ') ogg, curated)"
echo "  $AUD/MusicJingles            ($(find "$AUD/MusicJingles" -name '*.ogg' | wc -l | tr -d ' ') ogg, curated)"
echo "  $MUS/Bluebonnet_looped.ogg   (background music)"
