using System.Collections.Generic;
using UnityEngine;

namespace DinoDigger.Config
{
    /// <summary>
    /// A "dig postcard": one themed flavour of dig site. Pure delight variance — tint-only
    /// over the shared dig art, plus a loot skew and item-count override. Every theme stays
    /// strictly generous (no fail states, no text); the tints are gentle MULTIPLIES so the
    /// dirt/background/mound art stays readable. A mound rolls a theme (weighted by
    /// <see cref="RollWeight"/>) when it (re)spawns and tints itself to match, so the colour
    /// telegraphs the flavour before the child even digs.
    /// </summary>
    [System.Serializable]
    public class DigTheme
    {
        public string Name = "Meadow Classic";

        [Tooltip("Multiply tint for the dirt tiles (keep near-white so cracks stay readable).")]
        public Color DirtTint = Color.white;

        [Tooltip("Multiply tint for the full-bleed dig backdrop.")]
        public Color BackgroundTint = Color.white;

        [Tooltip("Multiply tint for the overworld mound sprite + its sparkle — the colour cue.")]
        public Color MoundTint = Color.white;

        [Tooltip("Loot roll weights within this theme: Egg / Fruit / Treasure. The egg-shard " +
                 "nerf (once every egg species is owned) still applies to EggWeight exactly as " +
                 "it does for the default weights.")]
        public float EggWeight = 0.35f;
        public float FruitWeight = 0.40f;
        public float TreasureWeight = 0.25f;

        [Tooltip("Buried-item count range for a site of this theme (inclusive).")]
        public int MinItems = 2;
        public int MaxItems = 4;

        [Tooltip("Base per-tile break-tap hardness range for this theme (inclusive). A tile " +
                 "rolls a LOW-biased value in [MinTaps,MaxTaps] so most tiles crumble at the " +
                 "soft end and the max is rare. Kept generous — read via GetTapRange, which " +
                 "clamps to [1,4] (a 1-tap tile is pure toddler joy; never a slog past 4).")]
        public int MinTaps = 2;
        public int MaxTaps = 3;

        [Tooltip("Relative chance a (re)spawning mound rolls THIS theme. Higher = more common.")]
        public float RollWeight = 1f;

        /// <summary>This theme's break-tap range, clamped defensively to [1,4] with min &lt;= max.
        /// Never exceeds 4 (the toddler-generosity cap); a bad serialized value can't make a
        /// tile a chore.</summary>
        public void GetTapRange(out int min, out int max)
        {
            min = Mathf.Clamp(MinTaps, 1, 4);
            max = Mathf.Clamp(MaxTaps, 1, 4);
            if (max < min)
            {
                max = min;
            }
        }
    }

    /// <summary>How a falling tile's travel is shaped over its (post-stagger) flight time.
    /// <see cref="Accelerate"/> is the shipped feel: a squared ramp, so a tile starts slow and
    /// arrives fast — heavy, never floaty. The others exist so the feel pass can compare the
    /// same cascade under a different curve without a code change.</summary>
    public enum FallEase
    {
        Accelerate = 0,   // u*u — gravity-ish, the default
        Linear = 1,       // constant speed (reads mechanical; a useful contrast)
        EaseOut = 2,      // fast out of the gate, settling in (light + floaty)
        EaseInOut = 3     // soft at both ends (dreamy; good for slow-motion review)
    }

    /// <summary>Which dig-mode excavator ARM ART SET renders (DinoDigger-rrn). The rig
    /// skeleton, IK, joint limits and bite timing are identical either way — this only
    /// selects which sprites mount on the bones, so the two can be A/B'd live by eye.
    /// V1 stays the default until V2 is approved.</summary>
    public enum DigArmVersion
    {
        V1 = 0,   // original gooseneck art (Assets/Art/Generated/digarm)
        V2 = 1    // proportionate slim rebuild (Assets/Art/Generated/digarm2)
    }

    /// <summary>All designer-tunable numbers in one asset.</summary>
    [CreateAssetMenu(menuName = "DinoDigger/Game Config", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        [Header("Dino roster")]
        public List<DinoDefinition> Dinos = new List<DinoDefinition>();

        [Header("Growth")]
        [Tooltip("Uniform scale multiplier per growth stage: baby, kid, big. Kept subtle " +
                 "because per-stage ART (baby/kid/adult sprite sets) now carries most of " +
                 "the visible growth; this only adds a gentle size bump on top.")]
        public float[] StageScales = { 1.0f, 1.15f, 1.3f };

        [Tooltip("Total fruit eaten to reach Kid stage.")]
        public int FruitToKid = 2;

        [Tooltip("Additional fruit (beyond Kid total) to reach Big stage.")]
        public int FruitToBig = 3;

        [Header("Dig mounds")]
        [Tooltip("Seconds after a mound is dug out before it respawns elsewhere.")]
        public float MoundRespawnSeconds = 20f;

        [Header("Dig site contents")]
        public int MinItemsPerSite = 2;
        public int MaxItemsPerSite = 4;

        [Tooltip("Relative weights for Egg / Fruit / Treasure. These are the DEFAULT " +
                 "(Meadow Classic) loot weights; a themed site (DigThemes) overrides them.")]
        public float EggWeight = 0.35f;
        public float FruitWeight = 0.40f;
        public float TreasureWeight = 0.25f;

        [Header("Dig postcards (themes)")]
        [Tooltip("Themed dig sites — tint-only variety over the SAME dig art. A (re)spawning " +
                 "mound rolls one weighted by RollWeight and tints itself so the kid learns the " +
                 "colour language; the dig site reads the theme for its dirt/background tints, " +
                 "loot weights and buried-item count. Empty here falls back to a built-in set " +
                 "(see BuildDefaultThemes) so an older serialized asset still gets all four.")]
        public DigTheme[] DigThemes = BuildDefaultThemes();

        [Tooltip("Number of fruit sprite variants (apple, banana, berry, watermelon).")]
        public int FruitVariants = 4;

        [Tooltip("Number of treasure sprite variants (coin, gem, boot, bone).")]
        public int TreasureVariants = 4;

        [Tooltip("Coins banked per treasure variant when collected (coin, gem, boot, bone). " +
                 "Out-of-range variants safely bank 1 via TreasureValue().")]
        public int[] TreasureValues = { 1, 3, 1, 2 };

        [Tooltip("Chance a dug FRUIT downgrades to a random treasure when NOTHING is hungry, " +
                 "so uneaten fruit can't pile up; the rest stays fruit so the world keeps some.")]
        public float FruitDowngradeFraction = 0.75f;

        [Header("Dig grid")]
        public int DigRows = 5;      // 4-6 layers of dirt
        public int DigColumns = 7;
        [Tooltip("Taps to fully crumble one dirt tile (matches 3 crack states).")]
        public int DirtHealth = 3;

        [Header("Dig arm art (DinoDigger-rrn)")]
        [Tooltip("Which excavator-arm art set the dig minigame renders. V1 = the original " +
                 "gooseneck art (default until V2 is approved). V2 = the proportionate slim " +
                 "rebuild in Assets/Art/Generated/digarm2 — same rig skeleton, IK, joint " +
                 "limits and bite timing, art only. Live-switchable mid-dig via the editor " +
                 "menu DinoDigger/Demo/Dig Arm V2 On|Off, so the two can be A/B'd by eye. " +
                 "If the V2 sprites are missing the rig safely stays on V1.")]
        public DigArmVersion DigArmVersion = DigArmVersion.V1;

        // ---- Dig Loop 2.0 feel knobs (DinoDigger-73a) -------------------------
        // EVERY number that shapes how the cascade and the dig toys MOVE lives here, with the
        // shipped values as defaults, because "it feels smooth and natural" is judged by eye in
        // play mode and nowhere else. The runtime re-reads these on every single use (never
        // caches them into a field at build time), so dragging a slider while the game is
        // running retunes the very next tile that falls — that live loop is the whole point of
        // the block. All of them are read through the clamped helpers at the bottom of this
        // class, so a wild inspector value can slow a fall down or speed it up but can never
        // stall a tile mid-air or divide by zero.
        [Header("Dig cascade feel (Dig Loop 2.0)")]
        [Tooltip("Seconds of travel per ROW a tile drops. The single biggest weight knob: " +
                 "higher = a slower, heavier tumble.")]
        public float DigFallRowSeconds = 0.07f;

        [Tooltip("Floor on a fall's travel time, so even a one-row drop is a readable little " +
                 "hop rather than a teleport.")]
        public float DigFallMinSeconds = 0.05f;

        [Tooltip("Ceiling on a fall's travel time, so a drop down a deep pit never drags.")]
        public float DigFallMaxSeconds = 0.28f;

        [Tooltip("Extra start delay per tile UP a falling column — what turns a column into a " +
                 "tumble instead of a lift. 0 = the whole column starts as one slab.")]
        public float DigFallStaggerSeconds = 0.05f;

        [Tooltip("Cap on that per-column stagger, so a tall column's top tile never hangs.")]
        public float DigFallStaggerMaxSeconds = 0.25f;

        [Tooltip("Shape of a fall's travel over its flight time. Accelerate (default) reads as " +
                 "gravity; the others are here for side-by-side feel comparisons.")]
        public FallEase DigFallEase = FallEase.Accelerate;

        [Tooltip("Landing squash depth: the tile widens by this fraction and flattens by it at " +
                 "peak impact, then springs back to EXACTLY its resting scale.")]
        public float DigSquashAmplitude = 0.18f;

        [Tooltip("Seconds the landing squash takes to spike and spring back to rest.")]
        public float DigSquashRecoverSeconds = 0.22f;

        [Tooltip("Dust particles puffed at the impact line by ONE landing tile.")]
        public int DigDustPerLanding = 4;

        [Tooltip("Minimum seconds between landing thumps, so a ten-tile cascade lands as one " +
                 "thud rather than a rattle.")]
        public float DigLandingThumpGapSeconds = 0.08f;

        [Header("Dig toys — crystals / geode / pot (DinoDigger-z4d)")]
        [Tooltip("Seconds between RINGS of a crystal blob popping outward from the tapped " +
                 "crystal. Small on purpose: the whole blob is gone in a blink, it just " +
                 "ripples outward instead of vanishing as a slab.")]
        public float DigCrystalPopRingSeconds = 0.03f;

        [Tooltip("Seconds one popped crystal takes to sparkle-shrink away once its ring fires.")]
        public float DigCrystalPopFadeSeconds = 0.14f;

        [Tooltip("Sparkle particles burst by ONE popping crystal.")]
        public int DigCrystalSparkleCount = 12;

        [Tooltip("Coins paid PER CRYSTAL in a popped blob — so a bigger blob is a bigger " +
                 "payout, with no maths a toddler has to follow. Kept small; the delight is " +
                 "the pop, the coins are the cherry.")]
        public int DigCrystalCoinBase = 1;

        [Tooltip("Hard cap on the coins one blob pays, so a freak chain can't spray dozens of " +
                 "pickups across the overworld.")]
        public int DigCrystalCoinMax = 12;

        [Tooltip("Seconds a tapped boom geode sparkle-fuses before it goes off — the " +
                 "anticipation beat that makes the whumph land.")]
        public float DigGeodeFuseSeconds = 0.4f;

        [Tooltip("Tiny camera shake amplitude (world units) on a geode whumph. Deliberately " +
                 "small — a toddler game nudges, it never jolts.")]
        public float DigGeodeShakeAmplitude = 0.09f;

        [Tooltip("Seconds the geode's camera nudge decays over.")]
        public float DigGeodeShakeSeconds = 0.28f;

        [Tooltip("Dust particles in the geode's soft ring.")]
        public int DigGeodeDustCount = 18;

        [Tooltip("Coins sprayed by a broken pinata pot (inclusive range, rolled per pot).")]
        public int DigPotCoinMin = 5;
        public int DigPotCoinMax = 8;

        [Tooltip("Seconds one sprayed coin arcs outward before it bounces to rest.")]
        public float DigPotCoinArcSeconds = 0.55f;

        [Tooltip("Seconds a landed pot coin sits and shines before it auto-collects. Nothing " +
                 "to chase and nothing to miss — the child just watches it get banked.")]
        public float DigPotCoinCollectSeconds = 1f;

        [Header("Dig toys — site generation")]
        [Tooltip("Chance a site rolls ANY crystal at all. A site without crystals is still a " +
                 "perfectly good dig; variety is the point.")]
        public float DigCrystalSiteChance = 0.65f;

        [Tooltip("Crystal clusters placed when a site rolls crystals (each its own colour).")]
        public int DigCrystalClusterCount = 2;

        [Tooltip("Cells in one crystal cluster (inclusive). Every cluster is 4-way connected, " +
                 "so a single tap always pops the whole thing.")]
        public int DigCrystalClusterMin = 3;
        public int DigCrystalClusterMax = 6;

        [Tooltip("Chance a site hides one boom geode (rare — it should feel like an event).")]
        public float DigGeodeChance = 0.3f;

        [Tooltip("Chance a site hides one pinata pot.")]
        public float DigPotChance = 0.35f;

        [Header("Dig toys — the 'every dig has a toy' roller (DinoDigger-qhy)")]
        [Tooltip("Draw weights for the site's FEATURED toy, indexed 0 crystal cluster / " +
                 "1 boom geode / 2 pinata pot / 3 surprise pocket / 4 water pocket / " +
                 "5 gem vein / 6 bouncy mushroom / 7 dig critter. Every site gets exactly one " +
                 "featured toy GUARANTEED (the chances above then roll SECONDARY toys on top), " +
                 "and the previous site's feature is excluded from the draw so two digs in a " +
                 "row never lead with the same treat — which, with the wave-2 toys " +
                 "(DinoDigger-u47) in the roster, now also means a DEEPER LAYER never leads " +
                 "with the layer above's treat. A zero weight benches that toy as a feature " +
                 "without removing it from the game.")]
        public int[] DigPrimaryToyWeights = { 3, 2, 2, 3, 2, 2, 2, 2 };

        [Header("Fossil bones (DinoDigger-0z5)")]
        [Tooltip("Chance a site buries a multi-cell bone, once every egg species is owned " +
                 "(the same gate egg shards use). 1 = every site, which is the shipped default: " +
                 "the skeleton board is the late-game collection and it should always tick over.")]
        public float DigBoneSiteChance = 1f;

        // The shard TRADE (a site that buried a bone downgraded its rolled shards to treasure)
        // is gone with the shards themselves: save v5 retires the egg-shard nest outright, so
        // there is no second late-game drip left to trade against.

        [Tooltip("Seconds the assembled bone takes to rise out of the pit when its last cell " +
                 "is uncovered.")]
        public float DigBoneRiseSeconds = 0.5f;

        [Tooltip("World units the assembled bone rises.")]
        public float DigBoneRiseHeight = 1.1f;

        [Tooltip("Rattle: how far the rising bone wobbles (degrees) and for how long.")]
        public float DigBoneRattleDegrees = 22f;
        public float DigBoneRattleSeconds = 0.55f;

        [Tooltip("Seconds the bone hangs at the top of its rise before shrinking away.")]
        public float DigBoneHoldSeconds = 0.8f;

        [Tooltip("Sparkle particles the whole-bone pop throws.")]
        public int DigBoneSparkleCount = 20;

        // ---- Depth layers (DinoDigger-dv1) -----------------------------------
        // A dig is no longer one board: clear enough of the first layer and a big friendly
        // LADDER appears at the bottom of the pit. Tapping it dips the camera and the SAME
        // site rebuilds one stratum deeper — darker, harder, richer. Two layers is the whole
        // ladder (the bible's "depth is time" without ever becoming a grind), and every knob
        // that says how much deeper is deeper lives here so it can be judged by eye.
        [Header("Dig depth layers (DinoDigger-dv1)")]
        [Tooltip("How many strata one dig site can have. 2 = the shipped ladder (surface + " +
                 "one deep layer). 1 disables the ladder entirely without removing the code.")]
        public int DigDepthLayers = 2;

        [Tooltip("Fraction of the layer's tiles that must be CLEARED before the ladder down " +
                 "appears. 0.6 = a bit more than half the board — far enough in that the child " +
                 "has committed to this dig, early enough that they still have digging left.")]
        public float DigLadderRevealFraction = 0.6f;

        [Tooltip("Colour MULTIPLY applied to the dirt tiles for each layer below the first. " +
                 "Deliberately a cool shade rather than a black: deep must read as older and " +
                 "quieter, never as dark-and-scary (that is also why Glow ships with it).")]
        public Color DigDeepDirtMultiply = new Color(0.62f, 0.63f, 0.78f, 1f);

        [Tooltip("Colour MULTIPLY applied to the dig backdrop for each layer below the first.")]
        public Color DigDeepBackgroundMultiply = new Color(0.52f, 0.55f, 0.72f, 1f);

        [Tooltip("Added to every tile's rolled hardness per layer below the first (clamped to " +
                 "a sane 1..6). Deeper dirt is older dirt.")]
        public int DigDeepHardnessBonus = 1;

        [Tooltip("Extra crystal clusters rolled per deep layer — the visible half of 'richer'.")]
        public int DigDeepCrystalClusterBonus = 1;

        [Tooltip("Added to each SECONDARY toy chance (geode, pot, water, vein, mushroom) per " +
                 "deep layer, clamped to 1. Deep boards are busier boards.")]
        public float DigDeepToyChanceBonus = 0.2f;

        [Tooltip("Multiplier on every coin a TOY pays on a deep layer (crystal blobs, pots, " +
                 "veins, critters). The 'bigger treasure' half of the depth promise.")]
        public float DigDeepCoinMultiplier = 2f;

        [Tooltip("Multiplier on the TREASURE weight of the buried-loot roll on a deep layer, " +
                 "so what the child digs up down there is worth more too.")]
        public float DigDeepTreasureWeightMultiplier = 2f;

        [Tooltip("Added to DigBoneSiteChance per deep layer (clamped to 1): bones are the deep " +
                 "layer's headline, so a deep stratum should essentially always bury one.")]
        public float DigDeepBoneChanceBonus = 0.5f;

        [Tooltip("Seconds the camera takes to dip down and back as the ladder is taken. The " +
                 "new layer is built at the BOTTOM of the dip, so the child sees the change " +
                 "happen rather than being teleported into it.")]
        public float DigLadderDipSeconds = 0.6f;

        [Tooltip("World units the camera dips while descending.")]
        public float DigLadderDipUnits = 1.6f;

        // ---- Mega-fossil sites (DinoDigger-84f) -------------------------------
        [Header("Mega-fossil sites (DinoDigger-84f)")]
        [Tooltip("Chance a (re)spawning mound is a MEGA-FOSSIL site: a skull-marked mound that " +
                 "opens a big 7x9 pit burying an ENTIRE remaining skeleton. Gated behind the " +
                 "same all-egg-species rule bones are, so it can never appear in the early game.")]
        public float DigMegaFossilChance = 0.12f;

        [Tooltip("PITY. If the skeleton board still has an incomplete species and the child has " +
                 "not seen a mega-fossil site yet this session, the Nth mound rolled is one " +
                 "GUARANTEED. A rare event a child might never meet is not an event.")]
        public int DigMegaFossilPityMounds = 6;

        [Tooltip("Grid size of a mega-fossil dig (the normal site is DigRows x DigColumns). " +
                 "Big enough to lay a whole six-bone skeleton out with room to dig between the " +
                 "pieces.")]
        public int DigMegaRows = 7;
        public int DigMegaColumns = 9;

        [Tooltip("Camera ortho size for a mega-fossil dig — the bigger pit needs a wider frame.")]
        public float DigMegaOrthoSize = 5.8f;

        // ---- Wave 2 dig toys (DinoDigger-u47) ---------------------------------
        [Header("Dig toys — wave 2: water / critter / vein / mushroom (DinoDigger-u47)")]
        [Tooltip("Chance a site hides a WATER POCKET: cracking it gushes down its column, " +
                 "washing the remaining hardness off every tile below and floating buried loot " +
                 "one row up toward the surface.")]
        public float DigWaterPocketChance = 0.3f;

        [Tooltip("Seconds the gush takes to run down the column. The LOGIC lands on the " +
                 "cracking frame (so nothing can wedge); this is purely how long the splash " +
                 "takes to travel.")]
        public float DigWaterGushSeconds = 0.6f;

        [Tooltip("Splash particles per washed tile.")]
        public int DigWaterSplashCount = 10;

        [Tooltip("Chance a cleared tile releases a DIG CRITTER — a glowbug that scurries from " +
                 "tile to tile and pays a coin if the child can catch it.")]
        public float DigCritterChance = 0.12f;

        [Tooltip("Most critters loose in one dig at a time. They never block anything, but a " +
                 "swarm would compete with the digging itself.")]
        public int DigCritterMax = 2;

        [Tooltip("Seconds between a critter's scurries from one tile to the next.")]
        public float DigCritterHopSeconds = 1.5f;

        [Tooltip("Seconds an uncaught critter stays out before it burrows away. Missing one is " +
                 "never a loss — there is always another.")]
        public float DigCritterLifeSeconds = 10f;

        [Tooltip("Coins a caught critter giggles out.")]
        public int DigCritterCoins = 2;

        [Tooltip("Chance a site hides a GEM VEIN: a connected run of gem cells that chain-pops " +
                 "segment by segment when either end is hit.")]
        public float DigGemVeinChance = 0.3f;

        [Tooltip("Cells in one gem vein (inclusive). Always a connected run, so a single hit " +
                 "always takes the whole thing.")]
        public int DigGemVeinMin = 3;
        public int DigGemVeinMax = 5;

        [Tooltip("Seconds between vein SEGMENTS popping as the spark travels along the run.")]
        public float DigGemVeinStaggerSeconds = 0.12f;

        [Tooltip("Coins one vein segment pays.")]
        public int DigGemVeinCoinsPerSegment = 1;

        [Tooltip("Chance a site hides a BOUNCY MUSHROOM: the arm's first bite boings off it " +
                 "(no damage, big squash) and flings dirt, clearing 1-2 random neighbours " +
                 "instead. The second bite pops the mushroom itself.")]
        public float DigMushroomChance = 0.3f;

        [Tooltip("Neighbouring tiles a boing flings loose (inclusive range).")]
        public int DigMushroomFlingMin = 1;
        public int DigMushroomFlingMax = 2;

        [Tooltip("How far the mushroom squashes on a boing, and for how long. Big and springy " +
                 "on purpose — the squash IS the joke.")]
        public float DigMushroomSquash = 0.5f;
        public float DigMushroomSquashSeconds = 0.4f;

        // ---- Glow the lantern bot (DinoDigger-6tc) -----------------------------
        [Header("Glow the lantern bot (DinoDigger-6tc)")]
        [Tooltip("Peek alpha a buried outline is lifted to inside Glow's beam. The baseline " +
                 "buried hint is 0.55, so anything above that reads as 'the lamp found this'.")]
        public float DigGlowPeekAlpha = 0.85f;

        [Tooltip("Peek alpha for the 3-cell cone Glow throws ONE TILE AHEAD of a crack — the " +
                 "preview that turns a dig tap into a choice. Softer than the beam itself.")]
        public float DigGlowConePeekAlpha = 0.7f;

        [Tooltip("Seconds between Glow's belly-beam sweeps (it re-aims at the deepest uncleared " +
                 "part of the pit).")]
        public float DigGlowSweepSeconds = 0.5f;

        [Tooltip("Body alpha Glow dims to on the bright first layer, where it has no work to " +
                 "do — a cute night-light idle rather than a switched-off machine.")]
        public float DigGlowDimAlpha = 0.55f;

        [Header("Movement")]
        public float BackhoeSpeed = 3.5f;
        public float BackhoeArriveDistance = 0.15f;
        public float DinoFollowSpeed = 3.0f;
        public float DinoFollowSlack = 0.4f;   // deadzone before a dino chases
        public float DinoWanderRadius = 1.2f;
        public float DinoEatSpeed = 4.0f;

        // ---- Camera framing (DinoDigger-kgm) ---------------------------------
        // EVERY ORTHO NUMBER BELOW IS A LANDSCAPE BASELINE, NOT A FRAMING. Ortho size is half
        // the VERTICAL extent, so one number is only correct at one aspect — see
        // Core.CameraFraming. The camera derives its actual size from CONTENT plus the live
        // aspect and uses these as MINIMUMS, which is what keeps the desktop/landscape look
        // pixel-identical to what shipped while portrait stops clipping the playfield.
        [Header("Camera")]
        public float RoamOrthoSize = 5.5f;
        // Dig view frames the close-up 2.4-unit backhoe body ABOVE the surface
        // plus all grid rows below it (see DigModeController.DigCenter): rows=5
        // needs a half-height of ~4.2.
        public float DigOrthoSize = 4.2f;
        [Tooltip("Ortho size the camera zooms to for the Dino-Matic revival ceremony " +
                 "(a gentle push-in, framing the machine + the new baby dino).")]
        public float CeremonyOrthoSize = 4.0f;
        public float CameraFollowLerp = 3.0f;
        public Vector2 CameraDeadzone = new Vector2(1.2f, 0.8f);
        public float TransitionSeconds = 0.5f;

        [Tooltip("OVERWORLD TARGET VISIBLE WORLD WIDTH, in units. The roam camera keeps this " +
                 "much world across the screen at every aspect, floored at RoamOrthoSize. " +
                 "TUNED AT 2x THE BASELINE ON PURPOSE: half of 11 is exactly 5.5, so every " +
                 "LANDSCAPE aspect frames precisely as it always did (5.5/aspect <= 5.5 once " +
                 "aspect >= 1, so the floor wins outright), while portrait ends up showing the " +
                 "SAME AMOUNT OF ISLAND as landscape — turning the phone changes the shape of " +
                 "the view, not how much world is in it. Today portrait shows 5.1 units of " +
                 "width against landscape's 23.8, which is the bug.")]
        public float RoamViewWidth = 11f;

        [Tooltip("Absurdity ceiling on the roam framing — a browser window dragged into a " +
                 "sliver must not turn the island into a map. Set clear of real handsets " +
                 "(9:19.5 asks for 11.9, 9:21 for 12.8), so it only ever catches nonsense.")]
        public float RoamMaxOrthoSize = 14f;

        [Tooltip("Target visible world WIDTH for the revival ceremony / attract-tour push-in " +
                 "(the machine plus the new baby, with room around them). Same 2x-the-baseline " +
                 "rule as RoamViewWidth, so it only bites in portrait — where CeremonyOrthoSize " +
                 "alone would crop the pair.")]
        public float CeremonyViewWidth = 8f;

        [Tooltip("Absurdity ceiling on the ceremony framing.")]
        public float CeremonyMaxOrthoSize = 10f;

        [Tooltip("Absurdity ceiling on the dig framing. Deliberately generous: the dig's " +
                 "content (grid + body + arm reach) MUST fit, so this only exists to stop a " +
                 "degenerate window from producing a nonsense camera.")]
        public float DigMaxOrthoSize = 16f;

        /// <summary>The overworld framing request: a target visible world width, floored at the
        /// landscape baseline. Derived here rather than in CameraFollow so the numbers and the
        /// tooltips that explain them live together.</summary>
        public Core.CameraFit RoamFit()
        {
            float baseline = Mathf.Max(0.5f, RoamOrthoSize);
            return Core.CameraFit.Content(
                Mathf.Max(0f, RoamViewWidth) * 0.5f, baseline, 0f,
                baseline, Mathf.Max(baseline, RoamMaxOrthoSize));
        }

        /// <summary>The ceremony / attract-tour push-in framing request.</summary>
        public Core.CameraFit CeremonyFit()
        {
            float baseline = Mathf.Max(0.5f, CeremonyOrthoSize);
            return Core.CameraFit.Content(
                Mathf.Max(0f, CeremonyViewWidth) * 0.5f, baseline, 0f,
                baseline, Mathf.Max(baseline, CeremonyMaxOrthoSize));
        }

        // THE EGG-SHARD NEST IS RETIRED (save v5, DinoDigger-5ve). Its escalating requirement
        // curve lived here; it now lives — frozen — as Managers.SaveData.LegacyShardsPerHatch,
        // because the only thing that still needs it is the one-shot v4->v5 migration that
        // converts a returning player's leftover shards into banked bones, and a migration must
        // be reproducible from the save alone rather than from a live design number.

        [Header("Dino Town (idle builder)")]
        /// <summary>Build-order index of the Fruit Stand — the second building (price 25),
        /// the first FUNCTIONAL one: once finished it turns surplus fruit into coins. A
        /// compile-time const (not a serialized field) so an existing GameConfig asset
        /// can't silently deserialize it to 0.</summary>
        public const int FruitStandIndex = 1;

        [Tooltip("Curated build-price curve for the town, buildings 1..N (indexed 0-based). " +
                 "Coins auto-spend on the next building the moment the wallet clears its price. " +
                 "All nine entries build in order, one per town plot. Values from the design " +
                 "doc's roster: 10/25/50/90/150/240/380/490/600 (~x1.6 step).")]
        public int[] TownBuildingPrices = { 10, 25, 50, 90, 150, 240, 380, 490, 600 };

        [Tooltip("Seconds of builder WORK time to advance one construction state (0->1->2->3->finished). " +
                 "Timing is driven by the crew, not a clock: worked time only accrues while builders are " +
                 "on site. A bigger — or BIGGER-GROWN — crew banks it faster (dt * the crew's summed " +
                 "BuildSpeed* growth-stage multipliers).")]
        public float TownSecondsPerBuildState = 8f;

        // Growth-stage build speed (DinoDigger-s90). Design-doc rationale: feeding is the
        // toddler's core verb, so the growth it buys must pay a VISIBLE dividend somewhere
        // outside the meadow. A builder contributes work scaled by how grown it is, which
        // makes "feed the dinos, the town rises faster" a loop a 3-year-old can feel without
        // reading a number. The curve is deliberately super-linear (1 -> 1.6 -> 2.5) so the
        // last step to Big is the most exciting one.
        private const float DefaultBuildSpeedBaby = 1f;
        private const float DefaultBuildSpeedKid = 1.6f;
        private const float DefaultBuildSpeedBig = 2.5f;

        [Tooltip("Build-speed multiplier for a BABY builder — the baseline (x1.0). Design doc: a baby " +
                 "on site still helps, it just helps least, so an unfed town still finishes eventually.")]
        public float BuildSpeedBaby = DefaultBuildSpeedBaby;

        [Tooltip("Build-speed multiplier for a KID builder (x1.6). Design doc: the first feeding " +
                 "milestone already reads as a noticeably busier site.")]
        public float BuildSpeedKid = DefaultBuildSpeedKid;

        [Tooltip("Build-speed multiplier for an ADULT/Big builder (x2.5). Design doc: a fully-grown " +
                 "crew builds more than twice as fast as babies — the payoff that makes feeding feel " +
                 "like it built the town.")]
        public float BuildSpeedBig = DefaultBuildSpeedBig;

        [Tooltip("Seconds of build WORK a single fruit banks when fed to a builder on an active site " +
                 "(the builder-snack payoff). Defaults to TownSecondsPerBuildState, so ONE fruit == ONE " +
                 "construction state — the building visibly jumps ahead a step per snack.")]
        public float SnackWorkSeconds = 8f;

        [Tooltip("Max NON-BUDDY resident dinos drafted onto one construction site. If none are available " +
                 "the build simply waits — buddies and the player backhoe are NEVER drafted.")]
        public int TownMaxBuilders = 2;

        [Tooltip("Walk-speed multiplier for a resident commuting from the meadow to a build site.")]
        public float TownBuilderCommuteSpeed = 1.1f;

        [Tooltip("Tap-to-cheer: how much faster the crew works while a cheer is running (x2 = " +
                 "double the whole crew's banked work). Applied ONCE however many times the site " +
                 "is tapped — a re-tap refreshes the timer, it never compounds.")]
        public float TownCheerMultiplier = 2f;

        [Tooltip("Tap-to-cheer: seconds one cheer lasts after tapping the ACTIVE construction " +
                 "site. NON-STACKING — re-tapping refreshes this window back to full rather than " +
                 "adding to it, so hammering the site is generous but never exploitable.")]
        public float TownCheerSeconds = 3f;

        [Tooltip("Recess Time: seconds a tapped FINISHED building throws a dino party (residents " +
                 "trot over and orbit it) before everyone does a final dance and heads home.")]
        public float RecessSeconds = 15f;

        [Tooltip("Max NON-BUDDY residents recruited to one recess party (min 2 implicit — recruit " +
                 "up to this many, then party with whoever showed up, even 1).")]
        public int RecessMaxDinos = 4;

        [Header("Dino Town life (townsfolk visits)")]
        [Tooltip("Seconds between ambient visit attempts: every so often a free resident strolls " +
                 "to a random FINISHED building and plays its little scene (slide, coffee sip, " +
                 "spa soak...). The countdown only runs once at least one building is finished, " +
                 "and a failed attempt (nobody free) just waits for the next one.")]
        public float TownVisitIntervalSeconds = 18f;

        [Tooltip("Max ambient building visits running at once (one per building either way). " +
                 "Kept low so the plaza reads as gentle life, not a crowd.")]
        public int TownMaxVisits = 2;

        [Tooltip("Seconds per BEAT of a building's interaction loop (waddle up / whoosh down / " +
                 "line up again). The whole scene is a handful of beats, so this sets how long " +
                 "a visit lasts; the Fossil Fountain finale simply runs more beats.")]
        public float TownVisitBeatSeconds = 0.9f;

        [Header("Rock Smash (Ankylosaurus)")]
        [Tooltip("A buddy Ankylosaurus must be at least this close to a tapped rock to " +
                 "smash it open (same reach as the Brachio tree-shake).")]
        public float RockSmashRange = 3f;

        [Tooltip("Per-rock cooldown after a smash: the same rock can't pay out again " +
                 "until this many seconds pass (a tap still wiggles for feedback).")]
        public float RockCooldownSeconds = 15f;

        // A smashed rock used to roll an egg shard some of the time to keep the nest ticking
        // over. The nest is retired (save v5) and the fossil species come out of dig sites as
        // BONES, so a rock is always coins now and the chance knob is gone with the mechanic.

        [Header("Berry Patch (garden)")]
        [Tooltip("Seconds a budding sprout takes to swell into a ripe, harvestable berry.")]
        public float SproutRipenSeconds = 25f;

        [Tooltip("Seconds after a ripe sprout is harvested before it buds again and " +
                 "re-enters the ripen cycle.")]
        public float SproutRegrowSeconds = 25f;

        [Header("Machine Friends (mossy sleepers)")]
        [Tooltip("PACING GUARD. When on (the default), at most ONE undiscovered machine may " +
                 "stand in the world at a time: if a second discovery gate trips while a " +
                 "sleeper is still waiting to be found, the second machine queues and arrives " +
                 "the moment the first is woken. That is the bible's 'one friend at a time' " +
                 "arc made mechanical, and it stops two glinting strangers competing for the " +
                 "same pair of eyes. Turn off only to stage several machines at once.")]
        public bool MachineOneDiscoveryAtATime = true;

        [Tooltip("Doodle the music-box bot: seconds between plaza dance parties. The visible " +
                 "gauge under him fills back up over exactly this long, so the wait is never " +
                 "wordless-mysterious — the child can SEE when the next crank is ready.")]
        public float DoodleCooldownSeconds = 20f;

        [Tooltip("How long the townsfolk keep dancing around Doodle before drifting home.")]
        public float DoodlePartySeconds = 6f;

        [Tooltip("How many nearby residents Doodle can call to a party at once. Deliberately " +
                 "small: the plaza should read as a party, never as a stampede.")]
        public int DoodleMaxDancers = 4;

        [Tooltip("Radius (world units) around Doodle that residents are summoned from.")]
        public float DoodleGatherRadius = 6f;

        [Tooltip("Sprinkles the watering bot: how many sprays its belly tank holds. Each tap " +
                 "spends one; the tank level is drawn on its belly.")]
        public int SprinklesTankCharges = 3;

        [Tooltip("Seconds Sprinkles takes to sip ONE charge back into its tank.")]
        public float SprinklesRechargeSeconds = 45f;

        [Tooltip("World units/sec Sprinkles trundles at on its way to a thirsty sprout.")]
        public float SprinklesTrundleSpeed = 1.1f;

        [Tooltip("Tuggy the tugboat: seconds between toots (each toot tows a fresh duckling line).")]
        public float TuggyCooldownSeconds = 40f;

        [Tooltip("Seconds a towed duckling line stays out. The ducklings are REAL ducks — they " +
                 "keep drifting (and stay catchable) until they reach the stream mouth, so this " +
                 "is only how long Tuggy's tow-line pose lasts.")]
        public float TuggyTowSeconds = 30f;

        [Tooltip("How many ducklings one toot tows (rolled between min and max, inclusive).")]
        public int TuggyDucklingsMin = 2;
        public int TuggyDucklingsMax = 3;

        [Tooltip("World units/sec Tuggy chugs along its stream. Slower than a duck (0.5) so the " +
                 "boat reads as the big steady one and the ducks still overtake it.")]
        public float TuggyChugSpeed = 0.32f;

        [Header("Feel")]
        public float IdleAttractSeconds = 15f;
        public float ParentGateHoldSeconds = 3f;

        // ----- Dig feel helpers -----
        // The cascade calls these EVERY time it moves a tile (never caches the result), so an
        // inspector tweak lands on the very next fall. They also clamp, which is what makes the
        // live-tuning loop safe: a designer can drag any of the knobs above to a silly value
        // mid-play and the worst that happens is a silly-looking (still finishing) cascade.

        /// <summary>Travel seconds for a tile dropping <paramref name="rowsDropped"/> rows:
        /// the per-row cost over a floor, under the ceiling. Never zero (a zero-second fall is
        /// a teleport) and never more than 2s (a stuck-looking tile).</summary>
        public float DigFallSeconds(int rowsDropped)
        {
            float ceiling = Mathf.Clamp(DigFallMaxSeconds, 0.02f, 2f);
            float floor = Mathf.Clamp(DigFallMinSeconds, 0.01f, ceiling);
            float t = floor + Mathf.Max(0f, DigFallRowSeconds) * Mathf.Max(0, rowsDropped);
            return Mathf.Clamp(t, 0.01f, ceiling);
        }

        /// <summary>Start delay for the <paramref name="order"/>-th mover up a falling column
        /// (0 = the bottom one, which never waits), capped so a tall column's top tile can't
        /// hang in the air.</summary>
        public float DigFallStagger(int order)
        {
            float cap = Mathf.Clamp(DigFallStaggerMaxSeconds, 0f, 1f);
            return Mathf.Clamp(Mathf.Max(0, order) * Mathf.Max(0f, DigFallStaggerSeconds), 0f, cap);
        }

        /// <summary>Apply <see cref="DigFallEase"/> to a normalized 0..1 travel fraction.</summary>
        public float DigFallCurve(float u)
        {
            u = Mathf.Clamp01(u);
            switch (DigFallEase)
            {
                case FallEase.Linear: return u;
                case FallEase.EaseOut: return 1f - Mathf.Pow(1f - u, 3f);
                case FallEase.EaseInOut:
                    return u < 0.5f ? 4f * u * u * u : 1f - Mathf.Pow(-2f * u + 2f, 3f) / 2f;
                default: return u * u; // Accelerate
            }
        }

        /// <summary>Draw weight of featured-toy <paramref name="index"/> for the site roller
        /// (DinoDigger-qhy). Falls back to an even-ish default for an index the serialized array
        /// does not cover — a stale asset must never bench a toy by accident — and clamps
        /// negatives to 0 so a typo in the inspector cannot make a weight subtract.</summary>
        public int DigPrimaryToyWeight(int index)
        {
            if (DigPrimaryToyWeights == null || index < 0 || index >= DigPrimaryToyWeights.Length)
            {
                return 2;
            }

            return Mathf.Max(0, DigPrimaryToyWeights[index]);
        }

        /// <summary>Coins one popped crystal blob of <paramref name="blobSize"/> pays: the
        /// per-crystal base, clamped to at least 1 (a pop ALWAYS pays — toddler rule) and to
        /// the configured maximum.</summary>
        public int DigCrystalCoins(int blobSize)
        {
            int per = Mathf.Max(0, DigCrystalCoinBase);
            int cap = Mathf.Max(1, DigCrystalCoinMax);
            return Mathf.Clamp(per * Mathf.Max(1, blobSize), 1, cap);
        }

        /// <summary>The pot's coin-spray range, clamped defensively to [1,12] with min &lt;= max,
        /// so a bad serialized value can never break a pot into zero coins.</summary>
        public void GetPotCoinRange(out int min, out int max)
        {
            min = Mathf.Clamp(DigPotCoinMin, 1, 12);
            max = Mathf.Clamp(DigPotCoinMax, 1, 12);
            if (max < min)
            {
                max = min;
            }
        }

        /// <summary>The crystal-cluster size range, clamped to [2,10] with min &lt;= max. Two is
        /// the floor because a "cluster" of one has no blob to pop.</summary>
        public void GetCrystalClusterRange(out int min, out int max)
        {
            min = Mathf.Clamp(DigCrystalClusterMin, 2, 10);
            max = Mathf.Clamp(DigCrystalClusterMax, 2, 10);
            if (max < min)
            {
                max = min;
            }
        }

        /// <summary>The gem-vein length range, clamped to [2,8] with min &lt;= max. Two is the
        /// floor for the same reason a cluster's is: a one-cell "vein" has nothing to chain.</summary>
        public void GetGemVeinRange(out int min, out int max)
        {
            min = Mathf.Clamp(DigGemVeinMin, 2, 8);
            max = Mathf.Clamp(DigGemVeinMax, 2, 8);
            if (max < min)
            {
                max = min;
            }
        }

        /// <summary>How many neighbours one mushroom boing flings loose, clamped to [1,4] with
        /// min &lt;= max — a boing ALWAYS clears something (that is the whole payoff of a bite
        /// that deals no damage), and never enough to read as a geode.</summary>
        public void GetMushroomFlingRange(out int min, out int max)
        {
            min = Mathf.Clamp(DigMushroomFlingMin, 1, 4);
            max = Mathf.Clamp(DigMushroomFlingMax, 1, 4);
            if (max < min)
            {
                max = min;
            }
        }

        /// <summary>Grid size for a dig site: the mega-fossil pit when <paramref name="mega"/>,
        /// else the normal board. The normal board keeps its historical 4-6 row clamp; a mega
        /// site is deliberately allowed to be bigger (it has a whole skeleton to lay out) and is
        /// clamped only against absurdity.</summary>
        public void GetDigGridSize(bool mega, out int rows, out int cols)
        {
            if (mega)
            {
                rows = Mathf.Clamp(DigMegaRows, 5, 12);
                cols = Mathf.Clamp(DigMegaColumns, 5, 14);
                return;
            }

            rows = Mathf.Clamp(DigRows, 4, 6);
            cols = Mathf.Max(3, DigColumns);
        }

        // ----- Derived helpers -----

        /// <summary>Coins banked when the treasure <paramref name="variant"/> is collected
        /// (clamped). An out-of-range or unconfigured variant safely banks 1.</summary>
        public int TreasureValue(int variant)
        {
            if (TreasureValues == null || TreasureValues.Length == 0)
            {
                return 1;
            }

            variant = Mathf.Clamp(variant, 0, TreasureValues.Length - 1);
            return Mathf.Max(1, TreasureValues[variant]);
        }

        public float StageScale(GrowthStage stage)
        {
            int i = (int)stage;
            if (StageScales == null || StageScales.Length == 0)
            {
                return 1f;
            }

            i = Mathf.Clamp(i, 0, StageScales.Length - 1);
            return StageScales[i];
        }

        /// <summary>Total cumulative fruit required to be at <paramref name="stage"/>.</summary>
        public int FruitThreshold(GrowthStage stage)
        {
            switch (stage)
            {
                case GrowthStage.Kid: return FruitToKid;
                case GrowthStage.Big: return FruitToKid + FruitToBig;
                default: return 0;
            }
        }

        /// <summary>Curated price of the town building at <paramref name="index"/> in build
        /// order (clamped). Returns a huge value if the curve is empty so nothing ever
        /// auto-starts without a configured price.</summary>
        public int TownBuildingPrice(int index)
        {
            if (TownBuildingPrices == null || TownBuildingPrices.Length == 0)
            {
                return int.MaxValue;
            }

            index = Mathf.Clamp(index, 0, TownBuildingPrices.Length - 1);
            return Mathf.Max(0, TownBuildingPrices[index]);
        }

        /// <summary>Build-work multiplier a builder at <paramref name="stage"/> contributes on a
        /// town site (DinoDigger-s90): Baby x1.0, Kid x1.6, Big x2.5. A non-positive serialized
        /// value (an asset saved before these fields existed, or a designer typo) falls back to the
        /// design-doc default rather than stalling construction at zero work per second.</summary>
        public float BuildSpeedFor(GrowthStage stage)
        {
            switch (stage)
            {
                case GrowthStage.Big:
                    return BuildSpeedBig > 0f ? BuildSpeedBig : DefaultBuildSpeedBig;
                case GrowthStage.Kid:
                    return BuildSpeedKid > 0f ? BuildSpeedKid : DefaultBuildSpeedKid;
                default:
                    return BuildSpeedBaby > 0f ? BuildSpeedBaby : DefaultBuildSpeedBaby;
            }
        }

        // ----- Dig themes -----

        // Cached fallback so an older serialized GameConfig.asset (saved before the
        // DigThemes field existed, so it deserializes empty) still gets the full four
        // themes at runtime. A designer-populated array in the inspector always wins.
        private DigTheme[] _fallbackThemes;

        /// <summary>The theme set actually in effect: the serialized array when populated,
        /// otherwise the built-in default four. Never null or empty.</summary>
        public DigTheme[] EffectiveThemes =>
            (DigThemes != null && DigThemes.Length > 0)
                ? DigThemes
                : (_fallbackThemes ??= BuildDefaultThemes());

        public int DigThemeCount => EffectiveThemes.Length;

        /// <summary>The theme at <paramref name="index"/> in the effective set (clamped).
        /// Never null — falls back to a fresh Meadow Classic if a slot is somehow null.</summary>
        public DigTheme GetTheme(int index)
        {
            DigTheme[] themes = EffectiveThemes;
            index = Mathf.Clamp(index, 0, themes.Length - 1);
            return themes[index] ?? new DigTheme();
        }

        /// <summary>Pick a theme index weighted by each theme's <see cref="DigTheme.RollWeight"/>.
        /// A (re)spawning mound calls this. Returns 0 when the set is somehow empty.</summary>
        public int PickThemeIndex()
        {
            DigTheme[] themes = EffectiveThemes;
            if (themes.Length == 0)
            {
                return 0;
            }

            float total = 0f;
            for (int i = 0; i < themes.Length; i++)
            {
                if (themes[i] != null)
                {
                    total += Mathf.Max(0f, themes[i].RollWeight);
                }
            }

            if (total <= 0.0001f)
            {
                return Random.Range(0, themes.Length); // all zero -> uniform
            }

            float roll = Random.value * total;
            for (int i = 0; i < themes.Length; i++)
            {
                if (themes[i] == null)
                {
                    continue;
                }

                roll -= Mathf.Max(0f, themes[i].RollWeight);
                if (roll <= 0f)
                {
                    return i;
                }
            }

            return themes.Length - 1;
        }

        /// <summary>The built-in four "dig postcards". Doubles as the field initializer (so a
        /// freshly generated asset bakes them in) AND the runtime fallback for a stale asset.
        /// Tints are gentle multiplies; item counts + weights follow the design doc.</summary>
        private static DigTheme[] BuildDefaultThemes()
        {
            return new[]
            {
                // Meadow Classic: the neutral default look — identical to the flat config
                // weights, common (roll weight 4).
                new DigTheme
                {
                    Name = "Meadow Classic",
                    DirtTint = Color.white,
                    BackgroundTint = Color.white,
                    MoundTint = Color.white,
                    EggWeight = 0.35f, FruitWeight = 0.40f, TreasureWeight = 0.25f,
                    MinItems = 2, MaxItems = 4,
                    MinTaps = 2, MaxTaps = 3,
                    RollWeight = 4f,
                },
                // Berry Bog: muddy brown dirt, fruit-heavy.
                new DigTheme
                {
                    Name = "Berry Bog",
                    DirtTint = new Color(0.72f, 0.55f, 0.40f),
                    BackgroundTint = new Color(0.82f, 0.78f, 0.66f),
                    MoundTint = new Color(0.72f, 0.55f, 0.40f),
                    EggWeight = 0.25f, FruitWeight = 0.60f, TreasureWeight = 0.15f,
                    MinItems = 2, MaxItems = 4,
                    MinTaps = 1, MaxTaps = 2,   // soft mud — the fastest crumble
                    RollWeight = 2f,
                },
                // Sparkle Cave: purple dirt + a slightly darker/cooler backdrop, treasure-heavy.
                new DigTheme
                {
                    Name = "Sparkle Cave",
                    DirtTint = new Color(0.75f, 0.62f, 0.92f),
                    BackgroundTint = new Color(0.72f, 0.70f, 0.85f),
                    MoundTint = new Color(0.78f, 0.62f, 0.95f),
                    EggWeight = 0.20f, FruitWeight = 0.20f, TreasureWeight = 0.60f,
                    MinItems = 2, MaxItems = 4,
                    MinTaps = 3, MaxTaps = 4,   // crystal-hard
                    RollWeight = 2f,
                },
                // Golden Mound: warm gold everywhere, ALL treasure, always 4 items — rare
                // (roll weight 1 -> ~1-in-9 with the weights above, close enough to 1-in-8).
                new DigTheme
                {
                    Name = "Golden Mound",
                    DirtTint = new Color(1.0f, 0.85f, 0.45f),
                    BackgroundTint = new Color(1.0f, 0.92f, 0.70f),
                    MoundTint = new Color(1.0f, 0.82f, 0.35f),
                    EggWeight = 0f, FruitWeight = 0f, TreasureWeight = 1.0f,
                    MinItems = 4, MaxItems = 4,
                    MinTaps = 2, MaxTaps = 3,   // keep the jackpot site snappy, not a slog
                    RollWeight = 1f,
                },
            };
        }

        public DinoDefinition GetDino(DinoType type)
        {
            if (Dinos == null)
            {
                return null;
            }

            for (int i = 0; i < Dinos.Count; i++)
            {
                if (Dinos[i] != null && Dinos[i].Type == type)
                {
                    return Dinos[i];
                }
            }

            return Dinos.Count > 0 ? Dinos[0] : null;
        }

        // ======================================================================= ENV
        // Environment dressing (DinoDigger-y1g). Append-only region: everything the
        // Jurassic-earth ground/decal pass needs to be TUNED lives here, and nothing
        // else in this file references it. Read by SceneBuilder at build time only —
        // no runtime system consults these, and none of them can affect walkability,
        // colliders or spawn logic (the dressing only chooses which equally-walkable
        // tile asset is painted, plus a purely decorative decal layer).
        //
        // Defaults mirror EnvDressing's, which in turn mirror the density grammar the
        // art's own approved contact sheet was composed with.

        [Header("ENV dressing (DinoDigger-y1g)")]
        [Tooltip("Master switch for the environment dressing pass. Off = SceneBuilder " +
                 "paints the flat placeholder tiles exactly as it did before the env set " +
                 "landed (the guaranteed no-regression path).")]
        public bool EnvDressingEnabled = true;

        [Range(0f, 1f)]
        [Tooltip("Chance a plain grass cell gets a fern/moss/clover decal.")]
        public float EnvGrassDecalChance = EnvDressing.DefaultGrassDecalChance;

        [Range(0f, 1f)]
        [Tooltip("Chance a path cell gets a footprints/pebbles decal.")]
        public float EnvPathDecalChance = EnvDressing.DefaultPathDecalChance;

        [Range(0f, 1f)]
        [Tooltip("Chance a water cell gets a lily decal.")]
        public float EnvWaterDecalChance = EnvDressing.DefaultWaterDecalChance;

        [Range(0f, 1f)]
        [Tooltip("Share of path decals that are the RARE warm-stone accent rather than " +
                 "the everyday footprints/pebbles (style rule 4: clusters, not carpets).")]
        public float EnvAccentShare = EnvDressing.DefaultAccentShare;

        /// <summary>The decal density for a biome, or 0 for biomes that take no scatter.</summary>
        public float EnvDecalChance(EnvBiome biome)
        {
            switch (biome)
            {
                case EnvBiome.Grass: return EnvGrassDecalChance;
                case EnvBiome.Path: return EnvPathDecalChance;
                case EnvBiome.Water: return EnvWaterDecalChance;
                default: return 0f;
            }
        }
        // =================================================================== end ENV
    }
}
