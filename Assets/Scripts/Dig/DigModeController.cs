using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;
using DinoDigger.Overworld;   // ItemPickup (Big Bone value override)

namespace DinoDigger.Dig
{
    /// <summary>
    /// Builds and runs the side-view digging mini-game at a fixed world offset
    /// (the "dig root", far from the overworld). The single main camera is moved
    /// here by CameraFollow. Tapping a dirt tile swings the scoop and crumbles it;
    /// revealing a buried item pops it out and hands control back to the overworld.
    /// </summary>
    // PARTIAL: the V2 arm-art selection (DinoDigger-rrn) lives in DigArmV2.cs.
    public partial class DigModeController : MonoBehaviour
    {
        private struct Buried
        {
            public ItemType Type;
            public DinoType Dino;
            public int Variant;
        }

        /// <summary>One walk buddy that came along on the dig, distilled to just what a
        /// dig superpower needs: its species and growth stage. Built by GameManager from
        /// the live <c>_buddies</c> roster and handed to <see cref="Open(DigTheme, IReadOnlyList{DigBuddy})"/>.</summary>
        public struct DigBuddy
        {
            public DinoType Type;
            public GrowthStage Stage;

            public DigBuddy(DinoType type, GrowthStage stage)
            {
                Type = type;
                Stage = stage;
            }
        }

        /// <summary>A helper dino shown at the pit edge, plus the per-round state its
        /// superpower needs. One per buddy (up to two), fed from the buddies' species art.</summary>
        private class Crew
        {
            public DinoType Type;
            public GrowthStage Stage;
            public SpriteRenderer Sprite;
            public Vector3 RestPos;
            public bool BonusDropped; // Brachiosaurus one-shot bonus-fruit guard
        }

        [SerializeField] private Transform _root;
        [SerializeField] private SpriteRenderer _backhoeBody;
        [SerializeField] private SpriteRenderer _helperDino;
        [SerializeField] private ParticleSystem _crumbs;
        // Full-bleed dig backdrop (the "Background" child of DigRoot). Wired by
        // SceneBuilder; a legacy baked scene with no wiring resolves it by name in
        // BuildGrid so the theme's background tint still lands.
        [SerializeField] private SpriteRenderer _background;

        // Two-bone excavator rig: ArmPivot(shoulder) -> Boom -> Elbow -> Stick ->
        // Wrist -> Bucket. Joint nodes rotate; the sprite renderers hang off them.
        [SerializeField] private Transform _armPivot;
        [SerializeField] private SpriteRenderer _boom;
        [SerializeField] private Transform _elbow;
        [SerializeField] private SpriteRenderer _stick;
        [SerializeField] private Transform _wrist;
        [SerializeField] private SpriteRenderer _bucket;

        private GameConfig _config;
        private PlaceholderLibrary _lib;

        private readonly List<DirtTile> _tiles = new List<DirtTile>();
        private readonly Dictionary<DirtTile, Buried> _buried = new Dictionary<DirtTile, Buried>();
        private readonly List<DugItemInfo> _found = new List<DugItemInfo>();
        private DirtTile[,] _grid;

        private int _rows;
        private int _cols;
        private bool _open;
        private bool _finished;

        // ---- Buddy Dig Crew --------------------------------------------------
        // The buddies that came along (species + stage), the live helper sprites shown
        // at the pit edge, and the per-round cadence counter that fires the powers. All
        // powers are STRICTLY ADDITIVE (the child's tap always resolves normally first)
        // and fire automatically on the child's own bites — the child never triggers them.
        private IReadOnlyList<DigBuddy> _crewBuddies;
        private readonly List<Crew> _crew = new List<Crew>();
        private SpriteRenderer _helperDino2; // runtime second-slot helper renderer
        private bool _trexBigHelps;          // a Big T-Rex buddy is present (adjacent clear)
        private int _bites;                  // player bites this round (drives cadences)
        private int _bonusFruitDropped;      // test-observable Brachio bonus-fruit count
        private int _headbuttCount;          // test-observable Trike column-clear count
        private int _headbuttColumn = -1;    // column cleared by the last Trike headbutt

        // Power cadences (in player bites). Big-stage buddies get a slightly stronger
        // cadence so a grown pet visibly helps more (toddler rule: never worse, only
        // more generous). One knob per power, in the existing hardcoded-tuning style.
        private const int TrikeCadence = 5;        // headbutt every 5th bite...
        private const int TrikeCadenceBig = 4;     // ...or every 4th when Big
        private const int BrachioBonusBite = 8;    // bonus fruit after the 8th bite...
        private const int BrachioBonusBiteBig = 6; // ...or the 6th when Big
        private const int CheerCadence = 6;        // powerless species cheer every 6th bite
        private const float HeadbuttStagger = 0.06f; // per-row crumble delay (top-to-bottom cascade)
        // Active dig postcard theme (tints + loot skew + item count). Null = the flat
        // default config weights/counts + no tint (identical to Meadow Classic).
        private DigTheme _theme;

        // ---- Surprise Pockets -------------------------------------------------
        // Exactly one non-item tile per site is marked as a wiggling mystery pocket. When
        // it is FULLY CLEARED by ANY path (a player bite, a crew clear, or a geode chain) it
        // fires a single delightful one-shot from a small weighted pool, then is done. It
        // never shows a peek and never gates FinishDig (an uncracked pocket just vanishes
        // with the site). All coin output rides the existing reward/bank path; no eggs/shards
        // ever drop from a surprise (progression pacing is untouched).
        private const bool SurprisePocketEnabled = true;

        // The pool, drawn per site with the LAST-SEEN kind excluded so two sites in a row
        // never surprise the same way. Weights are Giggle 4 / Duck 3 / Geode 2 / BigBone 1.
        private enum SurpriseKind { Giggle, Duck, Geode, BigBone }
        private static readonly int[] SurpriseWeights = { 4, 3, 2, 1 };
        private static int _lastSurprise = -1; // transient across sessions (static is fine)

        private const int GiggleCoins = 3;           // coins that arc out of a Giggle Pocket
        private const float GiggleCoinStagger = 0.15f; // one after another
        private const float GeodeStagger = 0.06f;    // per-neighbour radial crumble delay
        private const int BigBoneVariant = 3;        // the bone treasure sprite
        private const int BigBoneCoins = 5;          // banked via a value override (not a fake variant)

        private DirtTile _surpriseTile;
        private SurpriseKind _surpriseKind;
        private bool _surpriseFired;
        private int _surpriseFireCount; // test-observable: must stay 1 across every clear path

        // Bumped every time a site is built or closed. The STAGGERED helper cascades (the
        // Trike column, the geode ring) address tiles by GRID COORDINATE through a delayed
        // callback, so they must prove the site they were fired for is still the one on
        // screen: `_open`/`_finished` alone do NOT, because a site can close and a NEW one
        // open inside the cascade's own stagger window (0.42s of scaled time for the geode).
        // A callback that outlives its site would then crumble whatever now sits at that
        // row/col in the NEXT site — including its untouched surprise pocket, which is
        // exactly the "surprise fired even though it was never cracked" flake (DinoDigger-38r).
        //
        // The gravity cascade extends the same discipline: its LOGIC is synchronous (no
        // deferred step can cross a site boundary at all), and the only deferred parts left —
        // the per-tile fall tween's landing flourish — capture the generation and check it
        // before touching anything site-owned. The settle loop itself re-checks the generation
        // every pass, so a site that closes mid-cascade aborts it cleanly.
        private int _siteGeneration;

        // ---- Gravity cascade (Dig Loop 2.0) -----------------------------------
        // When ANY tile clears — a bite, a superpower, a geode chain, a landing crack —
        // every tile above it in its column falls to rest on the next occupied cell (or
        // the pit floor), and each landing deals ONE hardness tick to the tile it lands
        // on, which can complete that tile and cascade further.
        //
        // DATA FLOW of one cascade:
        //   clear path -> ClearTile (vacate the cell + collect what it hid)
        //              -> SettleGrid: repeat SettlePass until a pass moves nothing
        //                 SettlePass: per column, bottom-up, compact every alive tile
        //                             down to the lowest free cell (logical: _grid +
        //                             DirtTile.SetCell), record (faller -> victim)
        //                             landings, then apply one Damage() tick per landing;
        //                             a tick that crumbles a victim vacates + collects it,
        //                             which the NEXT pass picks up as a fresh hole.
        // The whole board is therefore resolved SYNCHRONOUSLY in one call — tests can assert
        // the final state on the same frame — while each mover carries a staggered travel
        // tween so the chain still reads as a chunky top-to-bottom tumble.
        //
        // ITEMS FALL WITH THEIR TILE. The buried bookkeeping is keyed by tile reference and
        // the peek sprite is a child of the tile renderer, so a buried item riding its own
        // dirt is both free and the only readable option: a peek pinned to a fixed cell would
        // end up glowing through a different tile than the one hiding it.
        private const int MaxSettlePasses = 64;   // see SettleGrid: the real bound is ~1 + rows*cols

        // MOTION LIVES IN GameConfig (DinoDigger-73a). Per-row travel time, the stagger up a
        // column, the squash, the dust count, the thump throttle — all of it is designer-tunable
        // and read through the helpers below EVERY time a tile moves, never cached, so Greg can
        // drag a slider mid-cascade and watch the next one land differently. The literals here
        // are only the no-config fallback (a bare scene), and they match the shipped defaults.
        private const float FallbackFallRowTime = 0.07f;
        private const float FallbackFallMinTime = 0.05f;
        private const float FallbackFallMaxTime = 0.28f;
        private const float FallbackFallStagger = 0.05f;
        private const float FallbackFallStaggerMax = 0.25f;
        private const float FallbackThumpGap = 0.08f;
        private const int FallbackDustPerLanding = 4;

        /// <summary>Travel seconds for a tile dropping <paramref name="drop"/> rows.</summary>
        private float FallSeconds(int drop)
        {
            return _config != null
                ? _config.DigFallSeconds(drop)
                : Mathf.Min(FallbackFallMaxTime, FallbackFallMinTime + FallbackFallRowTime * drop);
        }

        /// <summary>Start delay for the <paramref name="order"/>-th mover up a falling column.</summary>
        private float FallStaggerFor(int order)
        {
            return _config != null
                ? _config.DigFallStagger(order)
                : Mathf.Min(order * FallbackFallStagger, FallbackFallStaggerMax);
        }

        /// <summary>Minimum seconds between landing thumps (one per beat, not one per tile).</summary>
        private float ThumpGap =>
            _config != null ? Mathf.Max(0f, _config.DigLandingThumpGapSeconds) : FallbackThumpGap;

        /// <summary>Dust particles one landing tile puffs.</summary>
        private int DustPerLanding =>
            _config != null ? Mathf.Clamp(_config.DigDustPerLanding, 0, 40) : FallbackDustPerLanding;

        /// <summary>One faller and the tile it came to rest on (null = the pit floor).</summary>
        private struct Landing
        {
            public DirtTile Faller;
            public DirtTile Victim;
        }

        private readonly List<Landing> _landings = new List<Landing>();

        // ---- Dig toys (DinoDigger-z4d) ---------------------------------------
        // Crystals, boom geodes and pinata pots are all just DirtTiles with a Kind, so they fall,
        // crack and clear through the engine above with no special cases inside it. Everything
        // that makes them TOYS lives in this file, below: the flood-fill pop, the auto-pop pass
        // that rides the settle loop, the geode's fuse-then-whumph, and the pot's coin fountain.
        //
        // Same-colour crystal contacts as they were BEFORE the current settle started (see
        // SnapshotCrystalPairs). Only contacts that are NEW when the board goes quiet auto-pop —
        // a cluster that merely rode a column down together is untouched.
        private readonly HashSet<long> _crystalPairs = new HashSet<long>();
        private readonly List<DirtTile> _blob = new List<DirtTile>();      // flood-fill scratch
        private readonly List<int> _blobRing = new List<int>();            // ...and its ring depths
        private readonly HashSet<DirtTile> _blobSeen = new HashSet<DirtTile>();
        private ParticleSystem _dust;   // landing/geode dust emitter (built on first use)

        private int _crystalsPopped;    // test-observable: crystals popped this site
        private int _crystalBlobs;      // test-observable: blob pops (taps + auto-pops)
        private int _lastBlobSize;      // test-observable: crystals in the last blob popped
        private int _autoPops;          // test-observable: auto-pop passes that popped something
        private int _geodeBooms;        // test-observable: geodes detonated this site
        private int _potsBroken;        // test-observable: pots broken this site
        private int _lastPotCoins;      // test-observable: coins the last pot sprayed
        private int _toyCoins;          // test-observable: coins ALL toys paid this site

        private bool _settling;        // re-entrancy: a clear DURING a settle rides the running loop
        private float _gridHalfW;      // column 0's x offset from the dig origin
        private float _lastThump;
        private int _settlePasses;     // test-observable: passes the last settle took
        private int _settleFalls;      // test-observable: tiles moved by the last settle
        private int _landingCracks;    // test-observable: landing ticks dealt this site

        // TEST BREADCRUMB (DinoDigger-38r). Which path cracked the pocket, on which tile and
        // frame. Purely diagnostic and set nowhere else: the case that asserts the pocket was
        // never cracked prints it, so any future spurious fire names its own trigger instead
        // of leaving the next gate run to guess.
        private string _surpriseFiredBy = "";
        private string _clearCause = "player bite";

        // TEST HOOK. Force the next site's surprise kind (>= 0 selects a SurpriseKind and
        // updates the last-seen index; -1 = roll normally). Reset by the test after use.
        internal static int TestForceSurpriseKind = -1;

        // TEST HOOK. Staff NO helper crew at the next site, whatever buddies came along.
        // Every non-tap clear path (the Big T-Rex adjacent clear, the Trike headbutt column,
        // the geode chain) is a crew superpower, so a case that must prove "the pocket was
        // never cracked" pins this true and any spurious fire is then a real bug rather than
        // a buddy the previous case left behind. Default false = normal play.
        internal static bool TestSuppressCrew;

        // ---- The toy roller: every dig has a toy (DinoDigger-qhy) -------------
        // THE ANTI-DULL GUARANTEE. Rolling each toy on its own independent chance meant a site
        // could legitimately roll nothing at all — no crystals, no geode, no pot — and a toddler
        // who digs two of those in a row has learned that digging is sometimes boring. So site
        // generation now picks ONE FEATURED toy first and places it unconditionally, and the
        // per-toy chances above it become SECONDARY rolls layered on top. A site can still be a
        // quiet one-treat site or a riot of four; it can never be nothing.
        //
        // NO TWO DIGS IN A ROW LEAD WITH THE SAME TREAT: the previous site's feature is excluded
        // from the draw (the same trick the surprise pool uses). The exclusion is what turns "you
        // always get a toy" into "you always get a DIFFERENT toy", which is the part that
        // actually keeps a child digging.
        //
        // The surprise pocket is a first-class member of the roster: it already exists on every
        // site, so "the pocket is this site's feature" means the crystals/geode/pot are left to
        // the secondary rolls and the mystery tile carries the dig. That is a real texture
        // difference, not a null result.
        private enum PrimaryToy { CrystalCluster = 0, Geode = 1, Pot = 2, Pocket = 3 }
        private const int PrimaryToyCount = 4;
        private static readonly int[] FallbackPrimaryWeights = { 3, 2, 2, 3 };

        // The last site's feature, remembered ACROSS SITES for the whole session (static, like
        // _lastSurprise) and ALSO across sessions: GameManager mirrors it into SaveData (stored
        // index+1 so an old save's absent field reads as "no history") and pushes it back in
        // here on load. No save version bump — the field is purely additive.
        private static int _lastPrimary = -1;
        private int _primaryToy = -1; // this site's feature (-1 = toys suppressed / no room)

        // ---- Multi-cell fossil bones (DinoDigger-0z5) -------------------------
        // A bone spans 2-4 CELLS and lives UNDER the tiles, in its own layer: each covering tile
        // shows a bone-ish peek (the buried-item peek renderer, reused), and when the LAST of its
        // cells is uncovered the whole bone rises out of the pit with a rattle and banks to
        // GameManager. It is not a toy — it is the reward layer that takes over from egg shards
        // once every egg species is owned — so TestSuppressToys deliberately does NOT suppress it
        // (TestSuppressBones does).
        //
        // BONES vs GRAVITY — the coherence rule, decided here and enforced by UpdateBones:
        //
        //   A buried ITEM rides its tile: the item IS the tile's secret, so when the tile falls
        //   the secret goes with it. A BONE cannot work that way — it spans several cells and
        //   those cells fall independently, so a bone that rode its tiles would tear apart the
        //   first time a column dropped under half of it.
        //
        //   So: BONE CELLS ARE FIXED TO THE GRID. The bone never moves. A covering tile that
        //   falls away UNCOVERS its cell, and — this is the toddler promise — AN UNCOVERED CELL
        //   STAYS UNCOVERED. A tile that later tumbles into that cell hides the bone again
        //   visually (and picks up the peek, so the hint follows it), but the PROGRESS does not
        //   regress: uncovering three cells of a femur and then watching gravity fill one back in
        //   would be the game taking something away, which this game does not do. Progress toward
        //   a bone only ever goes up, and the pop fires the instant the last cell is first seen.
        private class Bone
        {
            public DinoType Species;
            public int BoneIndex;    // (int)BoneType — what gets banked
            public int[] Rows;       // absolute grid cells; never reassigned
            public int[] Cols;
            public bool[] Uncovered; // monotonic: set true once, never back to false
            public bool Popped;
        }

        // Cell-offset templates, flattened (dRow, dCol) pairs from the shape's top-left anchor.
        // Every shape fits in a 3x1 / 1x3 / 2x2 box, so a 5x7 board always has somewhere to put
        // one. Index into BoneTemplateType for the BoneType each template represents.
        private static readonly int[][] BoneTemplates =
        {
            new[] { 0, 0, 0, 1 },                   // small bone, 1x2 laid flat
            new[] { 0, 0, 0, 1, 0, 2 },             // femur, 1x3 horizontal
            new[] { 0, 0, 1, 0, 2, 0 },             // femur, 3x1 vertical
            new[] { 0, 0, 0, 1, 1, 1 },             // rib, a 3-cell arc inside a 2x2
            new[] { 0, 0, 0, 1, 1, 0, 1, 1 },       // skull, 2x2 blocky
        };

        private static readonly int[] BoneTemplateType =
        {
            (int)BoneType.SmallBone,
            (int)BoneType.Femur,
            (int)BoneType.Femur,
            (int)BoneType.Rib,
            (int)BoneType.Skull,
        };

        /// <summary>TEST HOOK. Template index of the 1x3 horizontal femur, so a case can place
        /// the exact shape the spec names without hard-coding the table's order.</summary>
        internal const int BoneTemplateFemurH = 1;

        private readonly List<Bone> _bones = new List<Bone>();
        private bool _boneAssigned;        // this site buried a bone (drives the shard trade)
        private int _bonesPopped;          // test-observable: whole bones popped this site
        private int _boneCellsUncovered;   // test-observable: bone cells first uncovered this site

        // The bone peek's tint: bleached ivory, deliberately unlike any fruit/egg/treasure peek
        // so "a bone is under here" reads differently from "loot is under here".
        private static readonly Color BonePeekTint = new Color(0.96f, 0.94f, 0.86f);

        // Built once, lazily, and only when neither the real bone art nor the treasure bone
        // exists: a plain white silhouette scaled to the bone's footprint. A bone must ALWAYS
        // pop something visible — the D2 art ticket replaces the sprite, never the beat.
        private static Sprite _whiteBoneFallback;

        // TEST HOOK. Bury NO bone at the next site, whatever the ownership gate says — the bone
        // twin of TestSuppressToys, kept separate on purpose: bones are not toys, so a case
        // pinning the toy roller off must not also silently disable the reward layer (and vice
        // versa). Default false = normal play.
        internal static bool TestSuppressBones;

        // ---- Excavator rig geometry + timing --------------------------------
        // DIG-VIEW STAGING (close-up cutaway): the body renders BIG (2.4 units
        // tall vs 1.3 in the overworld), parked at the LEFT end of the surface
        // line, MIRRORED (flipX) so its rear arm-mount faces the grid — a real
        // backhoe digs over its rear. The camera frames body + grid via DigCenter
        // (computed in BuildGrid) and GameConfig.DigOrthoSize 4.2:
        // y in [-5.7, +2.7], x in +-6.72 at 16:10.
        private const float SurfaceY = 0.1f;   // surface line above the dig origin
        private const float DigBodyH = 2.4f;   // dig-view body height (close-up scale)
        private const float BodyRestX = -3.0f;  // parked body center, left of the grid
        private const float MountX = 0.95f;  // shoulder offset from body center
        private const float MountY = 0.15f;  //   (rear-top of the mirrored body)

        // Segment lengths (world units). Reach 6.5 from the shoulder (~1.45 above
        // the surface) covers the deepest row (aim y -4.75 => 6.2 drop); for far
        // columns the body TRAVERSES along the surface so the shoulder tracks the
        // target column (see UpdateBodyLean). Arm : body ratio ~2.7 reads like a
        // proper excavator now that the body itself is big.
        private const float BoomLen = 3.4f;    // shoulder -> elbow
        private const float StickLen = 3.1f;    // elbow -> wrist (bucket hinge)
        // FALLBACK-ONLY thickness (placeholder square drawn as a thin bar when no
        // generated art exists). The real segments render 1:1 from anatomical art
        // via AssignSegmentPins — their thickness is whatever the art drew.
        private const float BoomThick = 0.34f;
        private const float StickThick = 0.30f;
        private const float BucketH = 0.72f;   // bucket keeps its aspect, sized by height

        private const float ReachTime = 0.32f;
        private const float BiteTime = 0.20f;
        private const float RetractTime = 0.28f;
        private const float RestScoop = 70f;     // bucket curled up when parked
        private const float ReachScoop = 8f;     // bucket opened, ready to bite
        private const float BiteScoop = 120f;    // full scooping bite at the tile

        // Parked pose as explicit joint angles (deg, world, 0 = +x, CCW+). The
        // rest wrist target is the FK of these angles, so the IK settles into
        // EXACTLY this fold: boom out low (8 deg — the gooseneck art's hump rides
        // ~1.0 unit above the pin line, so 8 deg keeps its crest at ~2.63, under
        // the 2.7 frame top), stick folded back down, bucket curled resting on
        // the dirt just in front of the machine.
        private const float RestBoomDeg = 8f;
        private const float RestStickDeg = -115f;

        // Body traverse toward the target column (the excavator scoots along
        // the surface). The shoulder parks slightly ABOVE-LEFT of the tile
        // (x = 0.9*tileX - 0.6): with every target below-right of the shoulder,
        // ONE fixed elbow-up bend side is always geometrically correct (no side
        // switching = no pinwheel) and the joint limits below stay satisfiable.
        // Clamps keep the body inside the frame.
        private const float ShoulderTrackGain = 0.9f;
        private const float ShoulderTrackBias = -0.6f;
        private const float LeanMin = -1.4f;
        private const float LeanMax = 4.4f;

        // ---- Joint limits (a backhoe arm, not a pinwheel) --------------------
        // Boom absolute angle (deg from horizontal-toward-grid, CCW+): a 90-deg
        // arc. The floor must be -70 (not -15): the deepest aim row sits 6.2
        // units below the 1.45-high shoulder, and with a -15 floor the arm's
        // maximum drop would be only 0.9 + 3.1 = 4.0 units.
        private const float BoomMinDeg = -70f;
        private const float BoomMaxDeg = 20f;
        // Stick angle RELATIVE to the boom (elbow bend, negative = bends down/
        // clockwise, the elbow-up way): never straight (>= 30 deg of bend),
        // never folded through (<= 150).
        private const float StickRelMinDeg = -150f;
        private const float StickRelMaxDeg = -30f;
        // Bucket curl relative to the stick.
        private const float ScoopMinDeg = 0f;
        private const float ScoopMaxDeg = 120f;
        // Per-frame angular velocity caps so retargets ROTATE smoothly instead
        // of snapping. The bucket cap is higher on purpose: the 112-deg bite in
        // 0.2s (560 deg/s) must still read as a deliberate snap.
        private const float ArmMaxDegPerSec = 300f;
        private const float BucketMaxDegPerSec = 700f;

        private enum ArmState { Idle, Reaching, Biting, Retracting }
        private ArmState _arm = ArmState.Idle;
        private readonly Queue<DirtTile> _digQueue = new Queue<DirtTile>();
        private DirtTile _activeTile;
        private float _phaseT;
        private float _scoopDeg = RestScoop;
        private Vector3 _effTarget;   // eased end-effector (wrist) world target
        private Vector3 _effFrom;     // start of the current ease
        private bool _biteFired;
        private Vector3 _origin;      // dig-root origin captured at BuildGrid
        private Vector3 _bodyBase;    // body rest world position (center)
        private float _leanX;         // current horizontal body traverse offset
        // Displayed joint angles (deg), rate-limited toward the IK solution each
        // frame. Boom is absolute (ArmPivot local z), stick is RELATIVE to the
        // boom (Elbow local z), scoop is relative to the stick (Wrist local z).
        private float _boomShownDeg = RestBoomDeg;
        private float _stickRelShownDeg = RestStickDeg - RestBoomDeg;
        private float _scoopShownDeg = RestScoop;

        public Vector3 DigCenter { get; private set; }
        public bool IsOpen => _open;

        // ------------------------------------------------------------ TEST HOOKS
        // Marked internal for the integration test runner. Read-only views over the
        // dig grid + buried bookkeeping; no behavior change for real players.
        internal int TestTileCount => _tiles.Count;
        internal IReadOnlyList<DirtTile> TestTiles => _tiles;
        internal int TestRows => _rows;
        internal int TestCols => _cols;
        internal int TestBuriedCount => _buried.Count;
        internal bool TestHelperEnabled => _helperDino != null && _helperDino.enabled;
        internal ParticleSystem TestCrumbs => _crumbs;

        // ---- Buddy Dig Crew test hooks ----
        internal int TestCrewCount => _crew.Count;
        internal bool TestCrewHas(DinoType type) => FindCrew(type) != null;
        internal int TestBonusFruitDropped => _bonusFruitDropped;
        internal int TestHeadbuttCount => _headbuttCount;
        internal int TestHeadbuttColumn => _headbuttColumn;
        internal int TestBites => _bites;
        internal int TestFoundCount => _found.Count;

        // ---- Surprise Pocket test hooks ----
        internal DirtTile TestSurpriseTile => _surpriseTile;
        internal int TestSurpriseKind => (int)_surpriseKind;
        internal bool TestSurpriseFired => _surpriseFired;
        internal int TestSurpriseFireCount => _surpriseFireCount;
        internal static int TestLastSurprise => _lastSurprise;

        /// <summary>TEST BREADCRUMB. Empty until the pocket fires; then the path that cracked
        /// it, the tile, and the frame — so a spurious fire reports its own cause.</summary>
        internal string TestSurpriseFiredBy => _surpriseFiredBy;

        /// <summary>TEST HOOK. Fully clear the surprise tile through the SAME crew-clear
        /// chokepoint the Trike headbutt / geode chain use (ClearTileFully -> CollectIfBuried),
        /// so a test can prove the pocket fires on a non-tap path and never fires twice.</summary>
        internal void TestClearSurpriseTile()
        {
            if (_surpriseTile != null)
            {
                ClearTileFully(_surpriseTile, "test crew-clear hook");
            }
        }

        // ---- Dig toy test hooks (DinoDigger-z4d) ----
        internal int TestCrystalsPopped => _crystalsPopped;
        internal int TestCrystalBlobs => _crystalBlobs;
        internal int TestLastBlobSize => _lastBlobSize;
        internal int TestAutoPops => _autoPops;
        internal int TestGeodeBooms => _geodeBooms;
        internal int TestPotsBroken => _potsBroken;
        internal int TestLastPotCoins => _lastPotCoins;
        internal int TestToyCoins => _toyCoins;

        /// <summary>TEST HOOK. Place NO random toys at the next site, so a case can build an
        /// exact board by hand (mirrors TestSuppressCrew). Default false = normal play.
        /// Does NOT suppress bones — see <see cref="TestSuppressBones"/>.</summary>
        internal static bool TestSuppressToys;

        // ---- Toy roller test hooks (DinoDigger-qhy) ----

        /// <summary>TEST HOOK. This site's FEATURED toy as a PrimaryToy ordinal (0 crystal
        /// cluster / 1 geode / 2 pot / 3 surprise pocket), or -1 when toys are suppressed.</summary>
        internal int TestPrimaryToy => _primaryToy;

        /// <summary>TEST HOOK. The feature the roller will refuse to repeat at the next site.</summary>
        internal static int TestLastPrimaryToy => _lastPrimary;

        /// <summary>TEST HOOK. Forget the last site's feature (and the saved copy) so a case
        /// starts its run of sites from a known "no history" state. Also called by the runner's
        /// between-case backstop, for the same reason every other static pin is: a feature
        /// remembered from an unrelated case would silently steer the next one's first roll.</summary>
        internal static void TestResetPrimaryToy()
        {
            _lastPrimary = -1;
            GameManager.Instance?.SetLastDigPrimaryToy(-1);
        }

        /// <summary>TEST HOOK. Alive tiles of one kind on the board right now, so a case can
        /// prove the featured toy is really ON the board and not merely recorded.</summary>
        internal int TestKindCount(DigTileKind kind)
        {
            int n = 0;
            for (int i = 0; i < _tiles.Count; i++)
            {
                DirtTile t = _tiles[i];
                if (t != null && !t.IsDestroyed && t.Kind == kind)
                {
                    n++;
                }
            }

            return n;
        }

        // ---- Fossil bone test hooks (DinoDigger-0z5) ----
        internal int TestBoneCount => _bones.Count;
        internal bool TestBoneAssigned => _boneAssigned;
        internal int TestBonesPopped => _bonesPopped;

        /// <summary>TEST HOOK. Which skeleton the <paramref name="bone"/>-th buried bone belongs
        /// to, so a case can assert the site is digging toward the species the BOARD wants.</summary>
        internal DinoType TestBoneSpecies(int bone) =>
            bone >= 0 && bone < _bones.Count ? _bones[bone].Species : default;

        /// <summary>TEST HOOK. Which <see cref="BoneType"/> the buried bone is.</summary>
        internal int TestBoneIndex(int bone) =>
            bone >= 0 && bone < _bones.Count ? _bones[bone].BoneIndex : -1;
        internal int TestBoneCellsUncovered => _boneCellsUncovered;

        /// <summary>TEST HOOK. Cells the <paramref name="bone"/>-th bone spans (0 = no such bone).</summary>
        internal int TestBoneCells(int bone) =>
            bone >= 0 && bone < _bones.Count ? _bones[bone].Rows.Length : 0;

        /// <summary>TEST HOOK. How many of that bone's cells have been uncovered so far. Never
        /// decreases — that is the no-regression rule, and a case asserts it directly.</summary>
        internal int TestBoneUncovered(int bone)
        {
            if (bone < 0 || bone >= _bones.Count)
            {
                return 0;
            }

            int n = 0;
            bool[] flags = _bones[bone].Uncovered;
            for (int i = 0; i < flags.Length; i++)
            {
                if (flags[i])
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>TEST HOOK. Whether one specific cell of a bone has been uncovered.</summary>
        internal bool TestBoneCellUncovered(int bone, int cell) =>
            bone >= 0 && bone < _bones.Count && cell >= 0 && cell < _bones[bone].Uncovered.Length &&
            _bones[bone].Uncovered[cell];

        /// <summary>TEST HOOK. True when r,c is a cell of some (unpopped) bone.</summary>
        internal bool TestIsBoneCell(int r, int c) => FindBoneAt(r, c) != null;

        /// <summary>TEST HOOK. Bury a KNOWN bone shape with its top-left cell at r,c, so a case
        /// can drive an exact multi-cell uncover instead of hunting for a rolled one. Refuses
        /// (returns false) on exactly the cells site generation refuses — off-grid, gone, hiding
        /// an item, a toy, the pocket, or already part of another bone — so a hand-placed bone is
        /// indistinguishable from a rolled one.</summary>
        internal bool TestPlaceBone(int r, int c, int template, DinoType species) =>
            PlaceBoneAt(r, c, template, species);

        /// <summary>TEST HOOK. Turn the cell at r,c into a crystal of <paramref name="color"/>.
        /// Refuses (returns false) on a cell that hides a buried item, is the surprise pocket, is
        /// already a toy, or is gone — exactly the cells site generation refuses too, so a case
        /// building a blob by hand can never accidentally create a board the game itself would
        /// never produce.</summary>
        internal bool TestSetCrystal(int r, int c, int color) =>
            TestSetToy(r, c, DigTileKind.Crystal, color);

        /// <summary>TEST HOOK. Turn the cell at r,c into a boom geode (same refusals).</summary>
        internal bool TestSetGeode(int r, int c) => TestSetToy(r, c, DigTileKind.Geode, 0);

        /// <summary>TEST HOOK. Turn the cell at r,c into a pinata pot (same refusals).</summary>
        internal bool TestSetPot(int r, int c) => TestSetToy(r, c, DigTileKind.Pot, 0);

        private bool TestSetToy(int r, int c, DigTileKind kind, int color)
        {
            DirtTile t = TileAt(r, c);
            if (t == null || t.IsDestroyed || t.HasItem || t.IsSurprise || t.CoversBone ||
                t.Kind != DigTileKind.Dirt)
            {
                return false;
            }

            t.SetKind(kind, color);
            return true;
        }

        internal DigTileKind TestKindAt(int r, int c)
        {
            DirtTile t = TileAt(r, c);
            return t != null ? t.Kind : DigTileKind.Dirt;
        }

        internal int TestCrystalColorAt(int r, int c)
        {
            DirtTile t = TileAt(r, c);
            return t != null ? t.CrystalColor : -1;
        }

        /// <summary>TEST HOOK. Size of the connected same-colour blob at r,c right now, without
        /// popping it — so a case can prove the flood fill sees exactly the blob it built.</summary>
        internal int TestBlobSizeAt(int r, int c)
        {
            CollectCrystalBlob(TileAt(r, c));
            return _blob.Count;
        }

        // ---- Gravity cascade test hooks ----
        internal int TestSettlePasses => _settlePasses;
        internal int TestSettleFalls => _settleFalls;
        internal int TestLandingCracks => _landingCracks;
        internal int TestSettleCap => MaxSettlePasses;

        /// <summary>TEST HOOK. Resolve the board to its stable state right now and return how
        /// many passes it took. The cascade is already synchronous on every clear path, so on
        /// a settled board this is a single no-move pass — it exists so a case can assert the
        /// engine's own idempotence and read the pass count without racing a tween.</summary>
        internal int TestSettleImmediately()
        {
            return SettleGrid("test settle hook");
        }

        /// <summary>TEST HOOK. World position of a grid cell, so a case can prove a fallen
        /// tile (and the peek riding it) really came to rest on its new cell.</summary>
        internal Vector3 TestCellPosition(int r, int c) => CellPosition(r, c);

        /// <summary>TEST HOOK. Alive tiles in a column (destroyed tiles are vacated from the
        /// grid, so this is the column's real height).</summary>
        internal int TestColumnCount(int c)
        {
            int n = 0;
            for (int r = 0; r < _rows; r++)
            {
                if (TileAt(r, c) != null)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>TEST HOOK. "" when the board is fully settled; otherwise the first floating
        /// tile found (an alive tile with an empty cell under it) — a named failure beats a bare
        /// false when a cascade wedges.</summary>
        internal string TestFloaterReport()
        {
            for (int c = 0; c < _cols; c++)
            {
                for (int r = 0; r < _rows - 1; r++)
                {
                    if (TileAt(r, c) != null && TileAt(r + 1, c) == null)
                    {
                        return $"tile at r{r}c{c} floats over an empty cell";
                    }
                }
            }

            return "";
        }

        /// <summary>TEST HOOK. Fully clear one cell through the crew-clear chokepoint (the same
        /// route the Trike column and the geode chain take), so a case can drive the cascade
        /// without depending on a hardness roll or a power cadence.</summary>
        internal void TestClearCell(int r, int c)
        {
            ClearTileFully(TileAt(r, c), "test cell clear");
        }

        /// <summary>TEST HOOK. Fire the geode chain on a cell without waiting for the surprise
        /// pool to roll one — the worst-case cascade driver (a radial clear on top of falls).</summary>
        internal void TestFireGeode(int r, int c)
        {
            DirtTile t = TileAt(r, c);
            if (t != null)
            {
                FireGeode(t);
            }
        }

        // ------------------------------------------------------------ DEMO HOOKS
        // PUBLIC on purpose (DinoDigger-73a): the DinoDigger/Demo/Dig menu lives in the editor
        // assembly, which cannot see the internal Test* hooks above. These are the same
        // operations the tests drive, exposed for a human driving a live build by eye — every
        // one is a no-op (returning false / 0) outside an open dig site, so a stray menu click
        // in the overworld does nothing at all.

        /// <summary>World position of the dig root — where the whole site is built. The scene-view
        /// capture frames on this (plus <see cref="DigCenter"/> for the camera framing).</summary>
        public Vector3 DigRootPosition => _root != null ? _root.position : transform.position;

        /// <summary>DEMO. Drop a 2x2 same-colour crystal cluster into the highest patch of plain
        /// dirt that can hold one. Returns the cells converted (0 = no room / not in a dig).</summary>
        public int DemoSpawnCrystalCluster(int color)
        {
            if (!_open || _grid == null)
            {
                return 0;
            }

            for (int r = 0; r + 1 < _rows; r++)
            {
                for (int c = 0; c + 1 < _cols; c++)
                {
                    if (!DemoCellFree(r, c) || !DemoCellFree(r, c + 1) ||
                        !DemoCellFree(r + 1, c) || !DemoCellFree(r + 1, c + 1))
                    {
                        continue;
                    }

                    TestSetToy(r, c, DigTileKind.Crystal, color);
                    TestSetToy(r, c + 1, DigTileKind.Crystal, color);
                    TestSetToy(r + 1, c, DigTileKind.Crystal, color);
                    TestSetToy(r + 1, c + 1, DigTileKind.Crystal, color);
                    return 4;
                }
            }

            return 0;
        }

        /// <summary>DEMO. Put a boom geode in the middle of the board (first free cell scanning
        /// out from the centre). Returns false when there is nowhere to put it.</summary>
        public bool DemoSpawnGeode() => DemoPlaceCentral(DigTileKind.Geode);

        /// <summary>DEMO. Put a pinata pot in the middle of the board.</summary>
        public bool DemoSpawnPot() => DemoPlaceCentral(DigTileKind.Pot);

        /// <summary>DEMO. Clear the bottom-middle cell, which drops that whole column and cracks
        /// whatever it lands on — the plainest way to watch one cascade run. Returns false when
        /// there is nothing to clear.</summary>
        public bool DemoCollapseColumn()
        {
            if (!_open || _grid == null)
            {
                return false;
            }

            int mid = _cols / 2;
            for (int step = 0; step < _cols; step++)
            {
                // Walk outward from the middle column so a chewed-up board still collapses.
                int c = step % 2 == 0 ? mid + step / 2 : mid - (step + 1) / 2;
                if (c < 0 || c >= _cols)
                {
                    continue;
                }

                for (int r = _rows - 1; r >= 0; r--)
                {
                    DirtTile t = TileAt(r, c);
                    if (t != null && !t.IsDestroyed)
                    {
                        ClearTileFully(t, "demo column collapse");
                        return true;
                    }
                }
            }

            return false;
        }

        private bool DemoPlaceCentral(DigTileKind kind)
        {
            if (!_open || _grid == null)
            {
                return false;
            }

            int midR = _rows / 2;
            int midC = _cols / 2;
            for (int radius = 0; radius < _rows + _cols; radius++)
            {
                for (int r = midR - radius; r <= midR + radius; r++)
                {
                    for (int c = midC - radius; c <= midC + radius; c++)
                    {
                        if (DemoCellFree(r, c) && TestSetToy(r, c, kind, 0))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>A cell a demo toy may take over: alive, plain dirt, no buried item, not the
        /// surprise pocket, not covering a bone — the same bar site generation holds itself to.</summary>
        private bool DemoCellFree(int r, int c)
        {
            DirtTile t = TileAt(r, c);
            return t != null && !t.IsDestroyed && !t.HasItem && !t.IsSurprise && !t.CoversBone &&
                   t.Kind == DigTileKind.Dirt;
        }

        // True when the excavator arm is parked and free to accept a fresh tap:
        // no bite in flight and an empty dig queue. The arm bites ONE tile at a
        // time and dedups a tile that is already the active/queued bite, so a
        // same-tile re-tap issued mid-bite is silently dropped. Tests pace their
        // taps to this so a re-tap can never be swallowed. Read-only; no player
        // behavior change (a legacy scene with no arm rig stays permanently ready).
        internal bool TestArmReady =>
            _arm == ArmState.Idle && _activeTile == null && _digQueue.Count == 0;

        internal DirtTile TestTileAt(int r, int c) => TileAt(r, c);

        private DirtTile TileAt(int r, int c)
        {
            if (_grid == null || r < 0 || r >= _rows || c < 0 || c >= _cols)
            {
                return null;
            }

            return _grid[r, c];
        }

        internal List<DirtTile> TestBuriedTiles()
        {
            return new List<DirtTile>(_buried.Keys);
        }

        internal ItemType TestBuriedType(DirtTile tile)
        {
            return (tile != null && _buried.TryGetValue(tile, out Buried b)) ? b.Type : ItemType.Fruit;
        }

        /// <summary>TEST HOOK. Bury an item on an EXACT cell, through the same two-step
        /// bookkeeping site generation uses (the <c>_buried</c> map plus the tile's own peek), so
        /// a case can CONSTRUCT the board configuration it needs instead of scanning a random
        /// site for a lucky one. Refuses any cell generation itself would refuse — off the board,
        /// dead, a toy, the surprise pocket, a bone cell, or already carrying an item — so a
        /// hand-buried item is exactly as legal as a rolled one, and a case cannot accidentally
        /// assert against a board state the real generator could never produce.</summary>
        internal bool TestBuryItemAt(int r, int c, ItemType type, int variant)
        {
            DirtTile t = TileAt(r, c);
            if (t == null || t.IsDestroyed || t.HasItem || t.IsSurprise || t.CoversBone ||
                t.Kind != DigTileKind.Dirt || _buried.ContainsKey(t))
            {
                return false;
            }

            var b = new Buried { Type = type, Dino = DinoType.TRex, Variant = variant };
            _buried[t] = b;
            Sprite peek = PeekSprite(b, out Color tint);
            t.SetPeek(peek, tint);
            return true;
        }

        internal int TestBuriedVariant(DirtTile tile)
        {
            return (tile != null && _buried.TryGetValue(tile, out Buried b)) ? b.Variant : 0;
        }

        /// <summary>TEST HOOK. Roll a single buried item using the real loot weights
        /// (including the owned-species egg-shard nerf) and hand it back as a
        /// DugItemInfo, so shard-drop-rate tests never have to grind slow dig loops.
        /// Uses whatever theme is currently active (null = flat default weights).</summary>
        internal DugItemInfo TestRollItemInfo()
        {
            Buried b = RollItem();
            return new DugItemInfo(b.Type, b.Dino, b.Variant, Vector3.zero);
        }

        /// <summary>TEST HOOK. Build a themed dig site off-screen (at the dig root) so the
        /// DigThemes case can inspect its tints + buried loot without driving the camera.
        /// Pair with <see cref="Close"/> (or GameManager.TestForceRoam) to tear it down.</summary>
        internal void TestBuildThemedSite(DigTheme theme)
        {
            _open = true;
            _finished = false;
            _found.Clear();
            _crewBuddies = null;
            _theme = theme;
            BuildGrid();
        }

        /// <summary>TEST HOOK. Current dig-backdrop tint (Color.white when no renderer).</summary>
        internal Color TestBackgroundColor => _background != null ? _background.color : Color.white;

        public void Configure(GameConfig config, PlaceholderLibrary lib)
        {
            _config = config;
            _lib = lib;
        }

        public Transform Root => _root;

        /// <summary>Build a fresh dig site and reveal it. Camera move is external.
        /// <paramref name="theme"/> is the mound's rolled dig postcard (null = the flat
        /// default look/weights); <paramref name="buddies"/> is the walk roster that came
        /// along (up to two), which staffs the Buddy Dig Crew and its superpowers. A null
        /// or empty list = no helpers shown (the old no-buddy behavior).</summary>
        public void Open(DigTheme theme, IReadOnlyList<DigBuddy> buddies)
        {
            _open = true;
            _finished = false;
            _found.Clear();
            _crewBuddies = buddies;
            _theme = theme;
            BuildGrid();
            GameEvents.RaiseDigModeEntered();
        }

        /// <summary>Back-compat overload (pre-crew callers/tests): a single Big T-Rex
        /// helper when <paramref name="bigDinoHelps"/> is true, otherwise no helpers.</summary>
        public void Open(bool bigDinoHelps, DigTheme theme = null)
        {
            var buddies = bigDinoHelps
                ? new List<DigBuddy> { new DigBuddy(DinoType.TRex, GrowthStage.Big) }
                : null;
            Open(theme, buddies);
        }

        public void Close()
        {
            _open = false;
            _theme = null;
            _digQueue.Clear();
            _activeTile = null;
            _arm = ArmState.Idle;
            _siteGeneration++; // retire this site's in-flight cascades (see _siteGeneration)
            ClearGrid();
            _crew.Clear();
            _crewBuddies = null;
            if (_helperDino != null)
            {
                _helperDino.enabled = false;
            }

            if (_helperDino2 != null)
            {
                _helperDino2.enabled = false;
            }

            GameEvents.RaiseDigModeExited();
        }

        private void BuildGrid()
        {
            ClearGrid();
            _siteGeneration++; // any cascade still in flight from the last site is now stale

            _rows = _config != null ? Mathf.Clamp(_config.DigRows, 4, 6) : 5;
            _cols = _config != null ? Mathf.Max(3, _config.DigColumns) : 7;

            _grid = new DirtTile[_rows, _cols];
            Vector3 origin = _root != null ? _root.position : transform.position;

            Color dirtTint = _theme != null ? _theme.DirtTint : Color.white;
            ApplyBackgroundTint(_theme != null ? _theme.BackgroundTint : Color.white);

            float halfW = (_cols - 1) * 0.5f;

            // Cell geometry the gravity cascade lands fallers on. Captured here (not in
            // PlaceBackhoe, which runs later) so a cell position is available the moment the
            // first tile exists, and per-site cascade bookkeeping starts clean.
            _origin = origin;
            _gridHalfW = halfW;
            _settling = false;
            _settlePasses = 0;
            _settleFalls = 0;
            _landingCracks = 0;
            _landings.Clear();

            // Per-site toy bookkeeping (DinoDigger-z4d).
            _crystalPairs.Clear();
            _crystalsPopped = 0;
            _crystalBlobs = 0;
            _lastBlobSize = 0;
            _autoPops = 0;
            _geodeBooms = 0;
            _potsBroken = 0;
            _lastPotCoins = 0;
            _toyCoins = 0;

            // Per-site bone bookkeeping (DinoDigger-0z5). Cleared BEFORE the tiles exist so
            // PlaceBones below starts from an empty layer even if the last site was torn down
            // mid-cascade.
            _bones.Clear();
            _boneAssigned = false;
            _bonesPopped = 0;
            _boneCellsUncovered = 0;

            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    var go = new GameObject($"Dirt_{r}_{c}");
                    go.transform.SetParent(_root != null ? _root : transform, false);
                    go.transform.position = CellPosition(r, c);

                    var box = go.AddComponent<BoxCollider2D>();
                    box.size = new Vector2(1.0f, 1.0f); // generous touch target

                    var tile = go.AddComponent<DirtTile>();
                    tile.Build(this, _lib, _config, r, c, RollTileHardness(), _crumbs);
                    tile.SetDirtTint(dirtTint); // theme multiply over the crack sprites

                    _tiles.Add(tile);
                    _grid[r, c] = tile;
                }
            }

            // GENERATION ORDER IS THE LAYER RULE. Each step only ever takes cells the steps
            // before it left alone, so no two layers can ever occupy one cell:
            //   toys   — turn plain dirt into crystals/geode/pot (and place this site's
            //            GUARANTEED feature first, DinoDigger-qhy)
            //   bones  — claim a contiguous run of still-plain cells for the buried bone layer
            //            (DinoDigger-0z5); each covering tile takes a bone peek
            //   items  — bury loot in what is left: never a toy cell, never a bone cell
            //   pocket — one wiggling mystery tile in what is left after that
            // A crystal can never also hide an egg, a bone cell can never also be loot, and a
            // pop can therefore never silently swallow something a peek had promised.
            PlaceDigToys();
            PlaceBones();
            PlaceItems();
            PlaceSurprisePocket();
            EnsurePrimaryToy(); // the pocket has landed (or not): make the guarantee true
            RefreshBonePeeks();
            PlaceBackhoe(origin, halfW);

            // Stegosaurus "treasure map": once at the start of the round, every buried
            // peek flashes bright and settles a little brighter than the default hint.
            Crew stego = FindCrew(DinoType.Stegosaurus);
            if (stego != null)
            {
                Cheer(stego);
                foreach (DirtTile t in _buried.Keys)
                {
                    t.FlashPeek(0.95f, 0.75f);
                }
            }

            // Frame body + grid: midpoint between the body's roof (with margin)
            // and the deepest tile row (with margin). Rows=5 => y = -1.5; paired
            // with GameConfig.DigOrthoSize 4.2 the frame spans y in [-5.7, +2.7].
            float frameTop = SurfaceY + DigBodyH + 0.2f;
            float frameBottom = -(_rows + 0.7f);
            DigCenter = origin + new Vector3(0f, (frameTop + frameBottom) * 0.5f, 0f);
        }

        /// <summary>Roll one dirt tile's break-tap hardness, LOW-biased so most tiles crumble
        /// at the soft end of the active theme's range and its max is rare — the fast crumble is
        /// the delight, a stubborn tile reads as a chore. Method: roll twice in the theme's
        /// [MinTaps,MaxTaps] (already clamped to [1,4]) and keep the SMALLER, which linearly skews
        /// toward MinTaps (e.g. a 2-3 theme lands ~75% at 2). Hardness is deliberately independent
        /// of buried contents — peek hints already telegraph loot; gating it behind taps would
        /// invert the reward curve. No theme active = the flat DirtHealth (legacy, unchanged).</summary>
        private int RollTileHardness()
        {
            if (_theme == null)
            {
                return _config != null ? _config.DirtHealth : 3;
            }

            _theme.GetTapRange(out int min, out int max);
            int a = Random.Range(min, max + 1);
            int b = Random.Range(min, max + 1);
            return Mathf.Min(a, b);
        }

        /// <summary>Tint the full-bleed dig backdrop for the active theme. Resolves the
        /// renderer by name off DigRoot the first time when SceneBuilder didn't wire it
        /// (a legacy baked scene), so the tint lands without a scene rebuild.</summary>
        private void ApplyBackgroundTint(Color tint)
        {
            if (_background == null && _root != null)
            {
                Transform bg = _root.Find("Background");
                if (bg != null)
                {
                    _background = bg.GetComponent<SpriteRenderer>();
                }
            }

            if (_background != null)
            {
                _background.color = tint;
            }
        }

        private void PlaceBackhoe(Vector3 origin, float halfW)
        {
            _origin = origin;
            Vector3 surface = origin + new Vector3(0f, SurfaceY, 0f);
            // Parked at the left end of the surface, wheels on the grass lip.
            _bodyBase = surface + new Vector3(BodyRestX, DigBodyH * 0.5f, 0f);
            _leanX = 0f;

            if (_backhoeBody != null)
            {
                _backhoeBody.enabled = true;
                if (_lib != null)
                {
                    // Prefer the armless dig body; fall back to the old side-view body.
                    Sprite body = _lib.DigBodySprite != null ? _lib.DigBodySprite : _lib.BackhoeBody;
                    if (body != null)
                    {
                        _backhoeBody.sprite = body;
                    }
                }

                // Close-up scale: 2.4 units tall regardless of the sprite's import
                // size, and MIRRORED so the rear arm-mount faces the grid.
                _backhoeBody.flipX = true;
                float srcH = _backhoeBody.sprite != null ? _backhoeBody.sprite.bounds.size.y : 0f;
                if (srcH > 0.0001f)
                {
                    float k = DigBodyH / srcH;
                    _backhoeBody.transform.localScale = new Vector3(k, k, 1f);
                }

                _backhoeBody.transform.position = _bodyBase;
            }

            // Build the rig. Generated anatomical art mounts pin-to-pin (1:1, no
            // stretching); the placeholder square falls back to a plain thin bar.
            Sprite fallback = _lib != null ? _lib.ScoopArm : null;
            if (_lib != null && _lib.BoomSprite != null)
            {
                AssignSegmentPins(_boom, _lib.BoomSprite, BoomLen, BoomBasePin, BoomTipPin);
            }
            else
            {
                AssignSegmentFallback(_boom, fallback, BoomLen, BoomThick);
            }

            if (_lib != null && _lib.StickSprite != null)
            {
                AssignSegmentPins(_stick, _lib.StickSprite, StickLen, StickBasePin, StickTipPin);
            }
            else
            {
                AssignSegmentFallback(_stick, fallback, StickLen, StickThick);
            }

            AssignBucket(_bucket, _lib != null && _lib.BucketSprite != null ? _lib.BucketSprite : fallback, BucketH);

            if (_elbow != null)
            {
                _elbow.localPosition = new Vector3(BoomLen, 0f, 0f);
            }

            if (_wrist != null)
            {
                _wrist.localPosition = new Vector3(StickLen, 0f, 0f);
            }

            // Anchor the shoulder to the body's rear mount. The ArmPivot lives
            // directly under DigRoot (NOT under the scaled body transform) so the
            // body's close-up scale never distorts the bone lengths; the
            // controller keeps it glued to the mount as the body traverses.
            if (_armPivot != null)
            {
                _armPivot.position = _bodyBase + new Vector3(MountX, MountY, 0f);
            }

            // Start parked: snap straight to the rest pose (infinite step — no
            // rate limiting while posing the freshly built rig).
            _arm = ArmState.Idle;
            _digQueue.Clear();
            _activeTile = null;
            _phaseT = 0f;
            _scoopDeg = RestScoop;
            _boomShownDeg = RestBoomDeg;
            _stickRelShownDeg = RestStickDeg - RestBoomDeg;
            _scoopShownDeg = RestScoop;
            if (_armPivot != null)
            {
                _effTarget = RestPoint();
                SolveIK(_effTarget, float.PositiveInfinity);
            }

            // DigArmV2 (DinoDigger-rrn): if the config selects the V2 art set, remount
            // the V2 sprites over the freshly assembled V1 rig (art only; see DigArmV2.cs).
            ApplyDigArmVersion();

            SetupCrew(surface);
        }

        // ---- Buddy Dig Crew ---------------------------------------------------

        /// <summary>Staff the pit-edge helper crew from the buddies that came along (up to
        /// two). Slot 0 reuses the scene-wired <see cref="_helperDino"/> renderer; slot 1
        /// uses a runtime renderer. Each helper shows its buddy's own species art and gets
        /// a Crew entry that its superpower fires off. No buddies = no helpers shown.</summary>
        private void SetupCrew(Vector3 surface)
        {
            _crew.Clear();
            _bites = 0;
            _bonusFruitDropped = 0;
            _headbuttCount = 0;
            _headbuttColumn = -1;
            _trexBigHelps = false;

            if (_helperDino != null)
            {
                _helperDino.enabled = false;
            }

            if (_helperDino2 != null)
            {
                _helperDino2.enabled = false;
            }

            if (_crewBuddies == null || TestSuppressCrew)
            {
                return;
            }

            int slot = 0;
            for (int i = 0; i < _crewBuddies.Count && slot < 2; i++)
            {
                DigBuddy b = _crewBuddies[i];
                SpriteRenderer sr = GetHelperRenderer(slot);
                if (sr == null)
                {
                    continue;
                }

                Sprite art = HelperSprite(b);
                if (art != null)
                {
                    sr.sprite = art;
                }

                // Right side of the frame, clear of the body's traverse range; the second
                // helper is stacked up-and-back so two never overlap.
                Vector3 pos = surface + (slot == 0
                    ? new Vector3(4.4f, 0f, 0f)
                    : new Vector3(5.2f, 1.1f, 0f));
                sr.transform.position = pos;
                sr.enabled = true;

                _crew.Add(new Crew { Type = b.Type, Stage = b.Stage, Sprite = sr, RestPos = pos });
                if (b.Type == DinoType.TRex && b.Stage == GrowthStage.Big)
                {
                    _trexBigHelps = true;
                }

                slot++;
            }
        }

        /// <summary>The renderer for a helper slot: slot 0 is the scene-wired helper; slot
        /// 1 is a runtime child created once (mirroring slot 0's parent + sorting + scale).</summary>
        private SpriteRenderer GetHelperRenderer(int slot)
        {
            if (slot == 0)
            {
                return _helperDino;
            }

            if (_helperDino2 == null)
            {
                Transform parent = _helperDino != null ? _helperDino.transform.parent
                    : (_root != null ? _root : transform);
                var go = new GameObject("HelperDino2");
                go.transform.SetParent(parent, false);
                _helperDino2 = go.AddComponent<SpriteRenderer>();
                if (_helperDino != null)
                {
                    _helperDino2.sortingLayerID = _helperDino.sortingLayerID;
                    _helperDino2.sortingOrder = _helperDino.sortingOrder;
                    _helperDino2.transform.localScale = _helperDino.transform.localScale;
                }
                else
                {
                    _helperDino2.sortingOrder = 15;
                }
            }

            return _helperDino2;
        }

        /// <summary>The buddy's own species art for the pit-edge helper: the W (grid-facing)
        /// walk sprite at the buddy's growth stage, falling back to the species idle.</summary>
        private Sprite HelperSprite(DigBuddy b)
        {
            DinoDefinition def = _config != null ? _config.GetDino(b.Type) : null;
            if (def == null)
            {
                return _helperDino != null ? _helperDino.sprite : null;
            }

            Sprite s = def.GetSprite(Dir8.W, b.Stage);
            return s != null ? s : def.GetIdle();
        }

        private Crew FindCrew(DinoType type)
        {
            for (int i = 0; i < _crew.Count; i++)
            {
                if (_crew[i] != null && _crew[i].Type == type)
                {
                    return _crew[i];
                }
            }

            return null;
        }

        /// <summary>Fire the automatic buddy superpowers for this player bite. Runs AFTER
        /// the tap has resolved normally, so every power is purely additive and never
        /// blocks or delays the child's own digging.</summary>
        private void FireCrewPowers(DirtTile lastTile)
        {
            for (int i = 0; i < _crew.Count && !_finished; i++)
            {
                Crew c = _crew[i];
                if (c == null)
                {
                    continue;
                }

                switch (c.Type)
                {
                    case DinoType.Triceratops:
                        int trikeEvery = c.Stage == GrowthStage.Big ? TrikeCadenceBig : TrikeCadence;
                        if (_bites % trikeEvery == 0)
                        {
                            HeadbuttColumn(lastTile, c);
                        }

                        break;

                    case DinoType.Brachiosaurus:
                        int brachioBite = c.Stage == GrowthStage.Big ? BrachioBonusBiteBig : BrachioBonusBite;
                        if (!c.BonusDropped && _bites >= brachioBite)
                        {
                            c.BonusDropped = true;
                            DropBonusFruit(c);
                        }

                        break;

                    // T-Rex (adjacent clear) fires inline in ResolveDig; Stegosaurus fires
                    // once at round start; Pteranodon fires on each uncover. Every other
                    // species has no dig power, so it just cheers the digger on.
                    case DinoType.TRex:
                    case DinoType.Stegosaurus:
                    case DinoType.Pteranodon:
                        break;

                    default:
                        if (_bites % CheerCadence == 0)
                        {
                            Cheer(c);
                        }

                        break;
                }
            }
        }

        /// <summary>Triceratops headbutt: clear the whole column of the last-tapped tile in
        /// a quick top-to-bottom cascade (rows staggered so it reads as a tumble).
        ///
        /// Top-DOWN is what makes this play nicely with gravity: each step clears a cell with
        /// nothing left above it, so no step ever drops tiles into the column it is emptying —
        /// the headbutt still ends with the column truly empty, not half-refilled by its own
        /// falls. (Each step still routes through ClearTileFully, so the rest of the board
        /// settles around the hole exactly as it would after a bite.)</summary>
        private void HeadbuttColumn(DirtTile tile, Crew c)
        {
            if (tile == null || _grid == null)
            {
                return;
            }

            int col = tile.Col;
            _headbuttCount++;
            _headbuttColumn = col;
            Cheer(c);

            int gen = _siteGeneration; // this cascade belongs to THIS site only
            for (int r = 0; r < _rows; r++)
            {
                int row = r;
                Tween.After(row * HeadbuttStagger, () =>
                {
                    if (!_open || _finished || _grid == null || gen != _siteGeneration ||
                        row >= _rows || col >= _cols)
                    {
                        return;
                    }

                    DirtTile t = _grid[row, col];
                    ClearTileFully(t, "Trike column");
                });
            }
        }

        /// <summary>Damage a tile until it crumbles, then collect anything it hid and let the
        /// board fall into the hole. Used by the Triceratops column cascade (these are helper
        /// hits, NOT player bites, so they never advance the power cadence) and by the geode
        /// chain, so every superpower clears through the SAME gravity chokepoint a bite does.
        /// <paramref name="cause"/> is the diagnostic breadcrumb recorded if this clear happens
        /// to crack the surprise pocket.</summary>
        private void ClearTileFully(DirtTile t, string cause)
        {
            ClearTileNoSettle(t, cause);
            SettleGrid(cause);
        }

        /// <summary>Clear one tile WITHOUT settling — the shared body of every "make this cell go
        /// away" path, split out so a caller that clears SEVERAL cells (the geode's 3x3, a
        /// crystal blob, an auto-pop pass) can settle the board exactly once at the end instead
        /// of once per cell.
        ///
        /// This is also where a toy's identity is honoured, so no caller has to know about toys:
        /// a crystal takes its whole blob (and its coins) with it, a geode lights its fuse rather
        /// than dying quietly, and everything else is hammered until it crumbles the way dirt
        /// always has.</summary>
        private void ClearTileNoSettle(DirtTile t, string cause)
        {
            if (t == null || t.IsDestroyed)
            {
                return;
            }

            switch (t.Kind)
            {
                case DigTileKind.Crystal:
                    // The blob, not just this cell — and deliberately the LOGICAL half, so a
                    // caller clearing several cells still settles exactly once at the end.
                    PopCrystalBlobLogical(t, cause);
                    return;

                case DigTileKind.Geode:
                    // Damage() lights the fuse and reports "still standing"; the boom itself
                    // clears this cell (and eight more) when the fuse burns down.
                    t.Damage();
                    return;
            }

            int guard = 0;
            while (!t.IsDestroyed && guard++ < 8)
            {
                t.Damage();
            }

            if (t.IsDestroyed)
            {
                ClearTile(t, cause);
            }
        }

        /// <summary>Brachiosaurus bonus fruit: drop one extra fruit into the round's spill
        /// batch (it rides the normal dug-item path, so FinishDig runs it through
        /// ResolveDugItem and the glut guard just like any dug fruit), plus a little
        /// falling-fruit flourish from the top of the frame.</summary>
        private void DropBonusFruit(Crew c)
        {
            Cheer(c);

            int variants = _config != null ? Mathf.Max(1, _config.FruitVariants) : 1;
            var info = new DugItemInfo(ItemType.Fruit, DinoType.TRex, Random.Range(0, variants),
                _origin);
            _found.Add(info);
            _bonusFruitDropped++;
            GameManager.Instance?.Audio?.ItemPop();

            SpawnFallingFruitVisual(info.Variant);
        }

        /// <summary>Purely decorative: a fruit sprite tumbles from the top of the frame down
        /// toward the spill side of the pit, then despawns (the real fruit is banked in
        /// <see cref="_found"/> and spills on FinishDig).</summary>
        private void SpawnFallingFruitVisual(int variant)
        {
            Sprite fruit = _lib != null ? _lib.Fruit(variant) : null;
            if (fruit == null)
            {
                return;
            }

            var go = new GameObject("BonusFruitFX");
            go.transform.SetParent(_root != null ? _root : transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = fruit;
            sr.sortingOrder = 20;

            Vector3 from = _origin + new Vector3(0f, SurfaceY + DigBodyH + 0.5f, 0f);
            Vector3 to = _bodyBase + new Vector3(0.6f, 0.2f, 0f);
            go.transform.position = from;
            Tween.MoveArc(go.transform, from, to, 1.4f, 0.6f, () =>
            {
                if (go != null)
                {
                    Destroy(go);
                }
            });
        }

        /// <summary>Pteranodon flourish: swoop the helper sprite in an arc out over the pit
        /// to the tile that was just uncovered and back to its perch. Pure spectacle.</summary>
        private void SwoopPteranodon(Crew c, Vector3 over)
        {
            if (c == null || c.Sprite == null)
            {
                return;
            }

            Vector3 rest = c.RestPos;
            Vector3 peak = over + new Vector3(0f, 0.6f, 0f);
            Tween.MoveArc(c.Sprite.transform, rest, peak, 1.2f, 0.35f, () =>
            {
                if (c.Sprite != null)
                {
                    Tween.MoveArc(c.Sprite.transform, peak, rest, 1.2f, 0.35f);
                }
            });
        }

        /// <summary>A helper's little "I helped!" beat: a punch-scale dance + a cheerful
        /// chime so the child reads the cause-and-effect of the power that just fired.</summary>
        private void Cheer(Crew c)
        {
            if (c == null || c.Sprite == null)
            {
                return;
            }

            Tween.PunchScale(c.Sprite.transform, 0.25f, 0.25f);
            GameManager.Instance?.Audio?.Chime();
        }

        // ---- Anatomical segment mounting (pin-to-pin, zero stretching) -------
        // Normalized (0..1, bottom-left origin) pin boss centroids MEASURED from
        // the generated art (dark pin-hole centroids; re-measure on regeneration —
        // keep in sync with GeneratedArtImporter's pin constants). The rig aligns
        // the drawn pin-to-pin line with the bone's +x axis via a uniform scale +
        // rotation, so the art renders 1:1: pins are perfect circles and the
        // gooseneck curve rides above/below the bone line exactly as drawn.
        private static readonly Vector2 BoomBasePin = new Vector2(0.1393f, 0.3525f);
        private static readonly Vector2 BoomTipPin = new Vector2(0.8970f, 0.5515f);
        private static readonly Vector2 StickBasePin = new Vector2(0.1162f, 0.5026f);
        private static readonly Vector2 StickTipPin = new Vector2(0.8929f, 0.5107f);

        /// <summary>Mount an anatomical segment sprite on its bone: UNIFORM scale
        /// chosen so the drawn base-pin -> tip-pin distance equals
        /// <paramref name="length"/>, rotated so that pin line lies along the
        /// bone's +x axis, positioned so the base pin sits exactly on the joint
        /// origin. No stretching of any kind.</summary>
        private static void AssignSegmentPins(SpriteRenderer sr, Sprite sprite, float length,
            Vector2 baseNorm, Vector2 tipNorm)
        {
            if (sr == null)
            {
                return;
            }

            sr.enabled = sprite != null;
            if (sprite == null)
            {
                return;
            }

            sr.sprite = sprite;
            sr.drawMode = SpriteDrawMode.Simple;

            float ppu = sprite.pixelsPerUnit;
            Rect r = sprite.rect;
            if (ppu <= 0.0001f || r.width <= 0f || r.height <= 0f)
            {
                return;
            }

            // Pin positions in sprite-local world units, relative to the pivot.
            Vector2 basePin = (new Vector2(baseNorm.x * r.width, baseNorm.y * r.height) - sprite.pivot) / ppu;
            Vector2 tipPin = (new Vector2(tipNorm.x * r.width, tipNorm.y * r.height) - sprite.pivot) / ppu;
            Vector2 v = tipPin - basePin;
            float pinDist = v.magnitude;
            if (pinDist <= 0.0001f)
            {
                return;
            }

            float scale = length / pinDist;
            float phiDeg = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, -phiDeg);
            sr.transform.localScale = new Vector3(scale, scale, 1f);

            // Base pin -> joint origin: p = -scale * R(-phi) * basePin.
            float c = Mathf.Cos(-phiDeg * Mathf.Deg2Rad);
            float s = Mathf.Sin(-phiDeg * Mathf.Deg2Rad);
            Vector2 rotated = new Vector2(c * basePin.x - s * basePin.y,
                                          s * basePin.x + c * basePin.y);
            sr.transform.localPosition = new Vector3(-rotated.x * scale, -rotated.y * scale, 0f);
        }

        /// <summary>Fallback segment mount for placeholder-only projects (no
        /// generated art, no measured pins): a plain thin bar via non-uniform
        /// scale — still IK-animated, never the old shoot-square.</summary>
        private static void AssignSegmentFallback(SpriteRenderer sr, Sprite sprite, float length,
            float thickness)
        {
            if (sr == null)
            {
                return;
            }

            sr.enabled = sprite != null;
            if (sprite == null)
            {
                return;
            }

            sr.sprite = sprite;
            sr.drawMode = SpriteDrawMode.Simple;
            sr.transform.localRotation = Quaternion.identity;
            float wUnits = sprite.bounds.size.x;
            float hUnits = sprite.bounds.size.y;
            if (wUnits <= 0.0001f || hUnits <= 0.0001f)
            {
                return;
            }

            sr.transform.localScale = new Vector3(length / wUnits, thickness / hUnits, 1f);
            float pivotNormX = sprite.rect.width > 0f ? sprite.pivot.x / sprite.rect.width : 0f;
            float pivotNormY = sprite.rect.height > 0f ? sprite.pivot.y / sprite.rect.height : 0.5f;
            sr.transform.localPosition = new Vector3(
                pivotNormX * length,
                (pivotNormY - 0.5f) * thickness,
                0f);
        }

        /// <summary>Assign the bucket sprite, keeping its aspect (uniform scale, no
        /// slicing — no distortion) and sizing it to <paramref name="height"/> world
        /// units tall. The importer gives the bucket a CUSTOM pivot at its drawn
        /// hinge bolt (top-left of digarm_bucket), so localPosition zero sockets the
        /// hinge rigidly onto the wrist joint at the stick's end and the curl rotates
        /// about that bolt. If the pivot didn't import (fallback square: centered),
        /// the bucket is still centered on the wrist rather than floating off it.</summary>
        private static void AssignBucket(SpriteRenderer sr, Sprite sprite, float height)
        {
            if (sr == null)
            {
                return;
            }

            sr.enabled = sprite != null;
            if (sprite == null)
            {
                return;
            }

            sr.sprite = sprite;
            sr.drawMode = SpriteDrawMode.Simple;
            float hUnits = sprite.bounds.size.y;
            if (hUnits <= 0.0001f)
            {
                return;
            }

            float scale = height / hUnits;
            sr.transform.localScale = new Vector3(scale, scale, 1f);
            sr.transform.localPosition = Vector3.zero; // pivot == hinge on the wrist
        }

        private void PlaceItems()
        {
            if (_config == null)
            {
                return;
            }

            // Buried-item count: the theme's range when themed, else the flat config range.
            int minItems = _theme != null ? _theme.MinItems : _config.MinItemsPerSite;
            int maxItems = _theme != null ? _theme.MaxItems : _config.MaxItemsPerSite;
            minItems = Mathf.Max(1, minItems);
            maxItems = Mathf.Max(minItems, maxItems);
            int count = Random.Range(minItems, maxItems + 1);
            count = Mathf.Min(count, _tiles.Count);

            // Bias buried items toward deeper rows so the child has to dig a bit.
            var candidates = new List<DirtTile>(_tiles);
            Shuffle(candidates);

            int placed = 0;
            for (int i = 0; i < candidates.Count && placed < count; i++)
            {
                DirtTile tile = candidates[i];
                if (tile.Row == 0)
                {
                    continue; // keep the top layer mostly clear so items feel buried
                }

                if (tile.Kind != DigTileKind.Dirt || tile.CoversBone)
                {
                    continue; // a toy or bone cell never also hides an item (see BuildGrid)
                }

                Buried b = RollSiteItem();
                _buried[tile] = b;

                Sprite peek = PeekSprite(b, out Color tint);
                tile.SetPeek(peek, tint);
                placed++;
            }

            // If everything was top-row / a toy (tiny grids), place on the first plain tile left
            // so a site always buries SOMETHING and the round can still be finished.
            if (placed == 0)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i] == null || candidates[i].Kind != DigTileKind.Dirt ||
                        candidates[i].CoversBone)
                    {
                        continue;
                    }

                    Buried b = RollSiteItem();
                    _buried[candidates[i]] = b;
                    Sprite peek = PeekSprite(b, out Color tint);
                    candidates[i].SetPeek(peek, tint);
                    break;
                }
            }
        }

        // ----- Dig toy site generation (DinoDigger-z4d) -----

        /// <summary>Roll this site's toys onto plain dirt cells.
        ///
        /// TWO STAGES (DinoDigger-qhy). First the GUARANTEE: one featured toy is drawn from the
        /// roster — crystal cluster / boom geode / pinata pot / surprise pocket — with the last
        /// site's feature excluded, and placed unconditionally. Then the old per-toy chances run
        /// as SECONDARY rolls on top, so a lucky site still stacks clusters and a geode and a pot
        /// exactly as it used to. What can no longer happen is a site with nothing on it.
        ///
        /// Runs before the bones, the buried items and the pocket, and every placement is on a
        /// cell that is still plain dirt, so no toy can ever land on another toy.</summary>
        private void PlaceDigToys()
        {
            _primaryToy = -1;
            if (_grid == null || _tiles.Count == 0 || TestSuppressToys)
            {
                return;
            }

            PlacePrimaryToy();

            float crystalChance = _config != null ? Mathf.Clamp01(_config.DigCrystalSiteChance) : 0.65f;
            if (Random.value < crystalChance)
            {
                int clusters = _config != null ? Mathf.Clamp(_config.DigCrystalClusterCount, 0, 4) : 2;
                int colors = _lib != null ? Mathf.Max(1, _lib.CrystalColorCount) : 3;
                int min = 3;
                int max = 6;
                _config?.GetCrystalClusterRange(out min, out max);

                for (int i = 0; i < clusters; i++)
                {
                    GrowCrystalCluster(Random.Range(min, max + 1), Random.Range(0, colors));
                }
            }

            float geodeChance = _config != null ? Mathf.Clamp01(_config.DigGeodeChance) : 0.3f;
            if (Random.value < geodeChance)
            {
                DirtTile t = RandomPlainTile();
                t?.SetKind(DigTileKind.Geode, 0);
            }

            float potChance = _config != null ? Mathf.Clamp01(_config.DigPotChance) : 0.35f;
            if (Random.value < potChance)
            {
                DirtTile t = RandomPlainTile();
                t?.SetKind(DigTileKind.Pot, 0);
            }
        }

        // ----- The toy roller: the anti-dull guarantee (DinoDigger-qhy) -----

        /// <summary>Pick this site's FEATURED toy and put it on the board, guaranteed.
        ///
        /// The draw excludes the last site's feature; if that pick has nowhere to go (a board
        /// already chewed up by a previous layer) the roster is walked from there, still skipping
        /// the last-seen kind, until something lands. Only if EVERY non-repeat option refuses is
        /// a repeat allowed — a repeated treat is a far smaller failure than a site with no treat
        /// at all, and the walk ends at the surprise pocket, which needs only one free cell.</summary>
        private void PlacePrimaryToy()
        {
            int pick = RollPrimaryToy();
            for (int i = 0; i < PrimaryToyCount; i++)
            {
                int k = (pick + i) % PrimaryToyCount;
                if (k == _lastPrimary)
                {
                    continue;
                }

                if (TryPlacePrimary(k))
                {
                    CommitPrimary(k);
                    return;
                }
            }

            for (int k = 0; k < PrimaryToyCount; k++)
            {
                if (TryPlacePrimary(k))
                {
                    CommitPrimary(k);
                    return;
                }
            }
        }

        /// <summary>Draw a featured toy by weight with the LAST-SEEN one excluded — the same
        /// no-repeat draw the surprise pool uses. A config that zeroes every remaining weight
        /// still returns a valid toy (the first non-repeat one) rather than nothing.</summary>
        private int RollPrimaryToy()
        {
            int total = 0;
            for (int k = 0; k < PrimaryToyCount; k++)
            {
                if (k != _lastPrimary)
                {
                    total += PrimaryWeight(k);
                }
            }

            if (total <= 0)
            {
                for (int k = 0; k < PrimaryToyCount; k++)
                {
                    if (k != _lastPrimary)
                    {
                        return k;
                    }
                }

                return 0;
            }

            int roll = Random.Range(0, total);
            int acc = 0;
            for (int k = 0; k < PrimaryToyCount; k++)
            {
                if (k == _lastPrimary)
                {
                    continue;
                }

                acc += PrimaryWeight(k);
                if (roll < acc)
                {
                    return k;
                }
            }

            return 0;
        }

        private int PrimaryWeight(int k) =>
            _config != null ? _config.DigPrimaryToyWeight(k) : FallbackPrimaryWeights[k];

        /// <summary>Put featured toy <paramref name="k"/> on the board. The POCKET places nothing
        /// here — PlaceSurprisePocket runs later and always marks a tile — so this only has to
        /// confirm there will still be a free cell for it; <see cref="EnsurePrimaryToy"/> covers
        /// the rare board where there is not.</summary>
        private bool TryPlacePrimary(int k)
        {
            switch ((PrimaryToy)k)
            {
                case PrimaryToy.CrystalCluster:
                {
                    int colors = _lib != null ? Mathf.Max(1, _lib.CrystalColorCount) : 3;
                    int min = 3;
                    int max = 6;
                    _config?.GetCrystalClusterRange(out min, out max);
                    return GrowCrystalCluster(Random.Range(min, max + 1), Random.Range(0, colors)) > 0;
                }

                case PrimaryToy.Geode:
                {
                    DirtTile t = RandomPlainTile();
                    if (t == null)
                    {
                        return false;
                    }

                    t.SetKind(DigTileKind.Geode, 0);
                    return true;
                }

                case PrimaryToy.Pot:
                {
                    DirtTile t = RandomPlainTile();
                    if (t == null)
                    {
                        return false;
                    }

                    t.SetKind(DigTileKind.Pot, 0);
                    return true;
                }

                default:
                    return RandomPlainTile() != null;
            }
        }

        private void CommitPrimary(int k)
        {
            _primaryToy = k;
            _lastPrimary = k;
            GameManager.Instance?.SetLastDigPrimaryToy(k);
        }

        /// <summary>Backstop run AFTER the pocket has been placed: if the pocket was this site's
        /// feature and no pocket actually landed (a tiny board where every free cell ended up
        /// buried), fall back to a real toy so the guarantee stays true. Practically never fires
        /// on a shipped 5x7 grid — it exists so the guarantee is a property of the code rather
        /// than of the grid size.</summary>
        private void EnsurePrimaryToy()
        {
            if (TestSuppressToys || _primaryToy != (int)PrimaryToy.Pocket || _surpriseTile != null)
            {
                return;
            }

            for (int k = 0; k < (int)PrimaryToy.Pocket; k++)
            {
                if (TryPlacePrimary(k))
                {
                    CommitPrimary(k);
                    return;
                }
            }

            _primaryToy = -1; // truly nowhere to put anything: report it rather than lie
        }

        /// <summary>Seed the no-repeat history from the save on load (see SaveData.LastPrimaryToy),
        /// so the first dig of a new SESSION still refuses to repeat the one the child last saw.
        /// Anything out of range restores as "no history".</summary>
        internal static void RestoreLastPrimaryToy(int index)
        {
            _lastPrimary = index >= 0 && index < PrimaryToyCount ? index : -1;
        }

        /// <summary>Grow one connected crystal cluster of up to <paramref name="size"/> cells in
        /// <paramref name="color"/>, by random 4-way walk from a plain seed cell. Stops early
        /// rather than forcing its way through toys or the pit walls, so a cramped board simply
        /// gets a smaller cluster. Returns the cells actually grown (0 = no room), which is what
        /// lets the roller tell a placed feature from a refused one.</summary>
        private int GrowCrystalCluster(int size, int color)
        {
            DirtTile seed = RandomPlainTile();
            if (seed == null)
            {
                return 0;
            }

            seed.SetKind(DigTileKind.Crystal, color);
            var grown = new List<DirtTile> { seed };

            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };
            int guard = 0;
            while (grown.Count < size && guard++ < size * 8)
            {
                DirtTile from = grown[Random.Range(0, grown.Count)];
                int d = Random.Range(0, 4);
                DirtTile next = TileAt(from.Row + dr[d], from.Col + dc[d]);
                if (next == null || next.IsDestroyed || next.HasItem || next.IsSurprise ||
                    next.CoversBone || next.Kind != DigTileKind.Dirt)
                {
                    continue; // same claimed-cell bar as RandomPlainTile: a cluster grows around them
                }

                next.SetKind(DigTileKind.Crystal, color);
                grown.Add(next);
            }

            return grown.Count;
        }

        /// <summary>A random cell no layer has claimed: alive, plain dirt, hiding no item, not the
        /// surprise pocket, not covering a bone.
        ///
        /// The item/pocket exclusions matter because the roller's LAST-RESORT placement
        /// (<see cref="EnsurePrimaryToy"/>) runs AFTER those layers exist. SetKind clears a
        /// tile's HasItem flag, so a toy dropped onto a buried tile would strand that item in the
        /// bookkeeping — a round that can never finish. Every caller before the items are placed
        /// is unaffected (nothing has claimed anything yet), so this is free insurance.</summary>
        private DirtTile RandomPlainTile()
        {
            var pool = new List<DirtTile>();
            for (int i = 0; i < _tiles.Count; i++)
            {
                DirtTile t = _tiles[i];
                if (t != null && !t.IsDestroyed && !t.HasItem && !t.IsSurprise && !t.CoversBone &&
                    t.Kind == DigTileKind.Dirt)
                {
                    pool.Add(t);
                }
            }

            return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : null;
        }

        private Sprite PeekSprite(Buried b, out Color tint)
        {
            tint = Color.white;
            if (_lib == null)
            {
                return null;
            }

            switch (b.Type)
            {
                case ItemType.Egg:
                    DinoDefinition def = _config != null ? _config.GetDino(b.Dino) : null;
                    if (def != null)
                    {
                        tint = def.EggColor;
                        return def.EggSprite;
                    }

                    return null;
                case ItemType.Fruit:
                    return _lib.Fruit(b.Variant);
                case ItemType.Shard:
                    return _lib.ShardSprite;
                default:
                    return _lib.Treasure(b.Variant);
            }
        }

        // EGG NERF: once every egg species is owned, a dug egg can no longer hatch anything
        // new, so its configured weight is cut to EggNerfFraction and the freed remainder rolls
        // TREASURE instead. (Any residual egg that still rolls resolves to treasure downstream
        // too, since no unique species remains — see GameManager.ResolveDugItem.) The late-game
        // COLLECTION is not in this table at all: it is the multi-cell fossil bone the site
        // buries in its own layer, behind the very same all-species-owned gate.
        private const float EggNerfFraction = 0.2f;

        /// <summary>Roll one buried item FOR THIS SITE — the loot table plus the site's own
        /// context. The site-specific layer that used to live here (trading a rolled egg shard
        /// for treasure at a site that buried a bone) retired with the shards themselves in
        /// save v5, so this is currently the plain loot table; it stays as the seam any future
        /// per-site loot rule hangs off, which is what kept the bone trade out of
        /// <see cref="RollItem"/> — the pure table the distribution tests measure.</summary>
        private Buried RollSiteItem()
        {
            return RollItem();
        }

        private Buried RollItem()
        {
            // Themed sites skew the loot; an unthemed site uses the flat config weights
            // (identical to Meadow Classic), so the existing roll tests are unchanged.
            float egg = _theme != null ? _theme.EggWeight : _config.EggWeight;
            float fruit = _theme != null ? _theme.FruitWeight : _config.FruitWeight;
            float treasure = _theme != null ? _theme.TreasureWeight : _config.TreasureWeight;

            // THE EGG NERF. Once every egg species is owned an egg has no dinosaur left to
            // contain, so most of its weight is freed. It used to become egg SHARDS for the
            // nest; the nest is retired (save v5) and the late-game collectible is now the
            // multi-cell FOSSIL BONE this site buries directly (see PlaceBones), which is not
            // part of the loot table at all. So the freed weight simply becomes treasure —
            // the reward the child can always use — and the collection rides on the bone.
            GameManager gm = GameManager.Instance;
            float nerfed = 0f;
            if (gm != null && gm.EggSpeciesAllOwned())
            {
                nerfed = egg * (1f - EggNerfFraction);
                egg *= EggNerfFraction;
                treasure += nerfed;
            }

            float total = Mathf.Max(0.0001f, egg + fruit + treasure);
            float roll = Random.value * total;

            var b = new Buried();
            if (roll < egg)
            {
                b.Type = ItemType.Egg;
                b.Dino = RandomDino();
            }
            else if (roll < egg + fruit)
            {
                b.Type = ItemType.Fruit;
                b.Variant = Random.Range(0, Mathf.Max(1, _config.FruitVariants));
            }
            else
            {
                b.Type = ItemType.Treasure;
                b.Variant = Random.Range(0, Mathf.Max(1, _config.TreasureVariants));
            }

            return b;
        }

        private DinoType RandomDino()
        {
            if (_config != null && _config.Dinos != null && _config.Dinos.Count > 0)
            {
                var d = _config.Dinos[Random.Range(0, _config.Dinos.Count)];
                if (d != null)
                {
                    return d.Type;
                }
            }

            return (DinoType)Random.Range(0, 4);
        }

        // ----- Surprise Pocket -----

        /// <summary>Mark exactly one NON-item tile (preferring rows below the top so it takes
        /// a couple of bites) as the wiggling surprise pocket, and roll which one-shot it will
        /// fire. Resets the per-site surprise bookkeeping. No-op when the feature is off or the
        /// (tiny) grid has no free tile.</summary>
        private void PlaceSurprisePocket()
        {
            _surpriseTile = null;
            _surpriseFired = false;
            _surpriseFireCount = 0;
            _surpriseFiredBy = "";
            _clearCause = "player bite";

            if (!SurprisePocketEnabled || _tiles.Count == 0)
            {
                return;
            }

            // Prefer a non-item tile below the top row; fall back to any non-item tile.
            var deep = new List<DirtTile>();
            var any = new List<DirtTile>();
            for (int i = 0; i < _tiles.Count; i++)
            {
                DirtTile t = _tiles[i];
                if (t == null || t.HasItem || t.CoversBone || t.Kind != DigTileKind.Dirt)
                {
                    // The pocket is a PLAIN dirt tile: a toy already has its own hook, and a
                    // bone cell already has its own peek (and its own reason to be dug out).
                    continue;
                }

                any.Add(t);
                if (t.Row > 0)
                {
                    deep.Add(t);
                }
            }

            List<DirtTile> pool = deep.Count > 0 ? deep : any;
            if (pool.Count == 0)
            {
                return; // every tile hides an item (tiny grid): skip the pocket this site
            }

            _surpriseTile = pool[Random.Range(0, pool.Count)];
            _surpriseTile.MarkSurprise();
            _surpriseKind = RollSurprise();
        }

        /// <summary>Draw a surprise kind by weight with the LAST-SEEN kind excluded (so two
        /// sites never surprise the same way in a row). A forced test kind overrides the roll
        /// but still updates the last-seen index.</summary>
        private SurpriseKind RollSurprise()
        {
            if (TestForceSurpriseKind >= 0 && TestForceSurpriseKind < SurpriseWeights.Length)
            {
                _lastSurprise = TestForceSurpriseKind;
                return (SurpriseKind)TestForceSurpriseKind;
            }

            int total = 0;
            for (int k = 0; k < SurpriseWeights.Length; k++)
            {
                if (k != _lastSurprise)
                {
                    total += SurpriseWeights[k];
                }
            }

            int roll = Random.Range(0, Mathf.Max(1, total));
            int picked = 0;
            int acc = 0;
            for (int k = 0; k < SurpriseWeights.Length; k++)
            {
                if (k == _lastSurprise)
                {
                    continue;
                }

                acc += SurpriseWeights[k];
                if (roll < acc)
                {
                    picked = k;
                    break;
                }
            }

            _lastSurprise = picked;
            return (SurpriseKind)picked;
        }

        /// <summary>Fire the rolled surprise EXACTLY ONCE. Called from CollectIfBuried — the one
        /// chokepoint every full-clear path funnels through (tap bite, T-Rex adjacent, Trike
        /// column, geode chain) — so any path that clears the pocket triggers it, and the
        /// _surpriseFired guard makes a re-clear a no-op.</summary>
        private void FireSurprise(DirtTile tile)
        {
            _surpriseFireCount++;
            Vector3 at = tile != null ? tile.transform.position : _origin;

            switch (_surpriseKind)
            {
                case SurpriseKind.Giggle:
                    FireGiggle(at);
                    break;
                case SurpriseKind.Duck:
                    FireDuck(at);
                    break;
                case SurpriseKind.Geode:
                    FireGeode(tile);
                    break;
                case SurpriseKind.BigBone:
                    FireBigBone(at);
                    break;
            }
        }

        /// <summary>Giggle Pocket: a confetti burst + a giggle-ish chime, then three coins arc
        /// out of the pit one after another and auto-bank through the guarded collect path.</summary>
        private void FireGiggle(Vector3 at)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null)
            {
                return;
            }

            SpawnPitBurst(at, new Color(1f, 0.85f, 0.3f), 26);
            gm.Audio?.Chime();

            for (int i = 0; i < GiggleCoins; i++)
            {
                Tween.After(i * GiggleCoinStagger, () =>
                {
                    GameManager g = GameManager.Instance;
                    g?.SpawnRewardPickup(ItemType.Treasure, DinoType.TRex, 0, g.RewardSpawnPoint);
                });
            }
        }

        /// <summary>Duck!: a duck pops out, quacks, and flies an arc off the top of the pit,
        /// dropping one coin (treasure variant 0) as it exits. Falls back to an invisible flyer
        /// if no duck art is reachable — the coin still drops.</summary>
        private void FireDuck(Vector3 at)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null)
            {
                return;
            }

            gm.Audio?.Honk(); // the duck's honk-quack (reuses the wired duck catch sound)

            var go = new GameObject("SurpriseDuckFX");
            go.transform.SetParent(_root != null ? _root : transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = gm.DuckSprite; // null-safe: an invisible flyer still drops the coin
            sr.sortingOrder = 30;
            go.transform.position = at;
            go.transform.localScale = Vector3.one * 3f;

            Vector3 to = at + new Vector3(2.2f, DigBodyH + 4.5f, 0f); // up and off the top
            Tween.MoveArc(go.transform, at, to, 2.2f, 0.9f, () =>
            {
                if (go != null)
                {
                    Destroy(go);
                }
            });

            // Drop one coin as the duck exits.
            Tween.After(0.7f, () =>
            {
                GameManager g = GameManager.Instance;
                g?.SpawnRewardPickup(ItemType.Treasure, DinoType.TRex, 0, g.RewardSpawnPoint);
            });
        }

        /// <summary>Rainbow Geode: the ring of neighbouring tiles chain-crumbles outward with
        /// sparkles (like a radial HeadbuttColumn), reusing ClearTileFully so any buried item a
        /// neighbour hid is collected too — which can help finish the round.</summary>
        private void FireGeode(DirtTile center)
        {
            if (center == null)
            {
                return;
            }

            GameManager.Instance?.Audio?.Chime();
            SpawnPitBurst(center.transform.position, new Color(0.6f, 0.9f, 1f), 22);

            // 8-neighbour ring, staggered so it reads as a tumble outward. The ring is
            // addressed by ROW/COL, so each delayed step must also prove the site it was
            // fired for is still open (see _siteGeneration): a step landing after a NEW site
            // was built would crumble that site's tile at the same coordinates.
            //
            // With gravity live, the board falls between ring steps, so a later step clears
            // whichever tile has dropped into that coordinate by then. That is deliberate: the
            // geode keeps blowing a hole in the same place rather than firing into thin air.
            int[] dr = { -1, 1, 0, 0, -1, -1, 1, 1 };
            int[] dc = { 0, 0, -1, 1, -1, 1, -1, 1 };
            int gen = _siteGeneration;
            for (int i = 0; i < 8; i++)
            {
                int r = center.Row + dr[i];
                int c = center.Col + dc[i];
                Tween.After(i * GeodeStagger, () =>
                {
                    if (!_open || _finished || _grid == null || gen != _siteGeneration)
                    {
                        return;
                    }

                    DirtTile t = TileAt(r, c);
                    if (t == null || t.IsDestroyed)
                    {
                        return;
                    }

                    SpawnPitBurst(t.transform.position, new Color(0.7f, 0.95f, 1f), 8);
                    ClearTileFully(t, "geode chain");
                });
            }
        }

        /// <summary>Big Bone (rare): a bone pops out scaled x2 with a big punch, then shrinks
        /// away — while the real payout banks 5 coins through the guarded collect path via a
        /// value override on a bone-variant reward (no fake variants).</summary>
        private void FireBigBone(Vector3 at)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null)
            {
                return;
            }

            gm.Audio?.ItemPop();

            Sprite bone = _lib != null ? _lib.Treasure(BigBoneVariant) : null;
            if (bone != null)
            {
                var go = new GameObject("SurpriseBoneFX");
                go.transform.SetParent(_root != null ? _root : transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = bone;
                sr.sortingOrder = 30;
                go.transform.position = at + new Vector3(0f, 0.4f, 0f);
                go.transform.localScale = Vector3.one * 2f;
                Tween.PunchScale(go.transform, 0.6f, 0.5f);
                Tween.After(0.9f, () =>
                {
                    Tween.ScaleTo(go.transform, Vector3.zero, 0.3f, () =>
                    {
                        if (go != null)
                        {
                            Destroy(go);
                        }
                    });
                });
            }

            ItemPickup p = gm.SpawnRewardPickup(ItemType.Treasure, DinoType.TRex, BigBoneVariant,
                gm.RewardSpawnPoint);
            p?.SetValueOverride(BigBoneCoins);
        }

        /// <summary>A little colourful star burst inside the pit (parented to the dig root),
        /// reusing GameManager's particle factory. Cleaned up shortly after.</summary>
        private void SpawnPitBurst(Vector3 at, Color color, int count)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || _lib == null)
            {
                return;
            }

            ParticleSystem ps = gm.TownCreateParticles(_root != null ? _root : transform,
                _lib.StarParticle, color, 0.35f);
            if (ps == null)
            {
                return;
            }

            ps.transform.position = at;
            ps.Emit(count);
            Tween.After(2f, () =>
            {
                if (ps != null)
                {
                    Destroy(ps.gameObject);
                }
            });
        }

        // ========================================================= FOSSIL BONES (0z5)
        // The reward layer under the tiles. See the Bone class comment above for the data model
        // and for the ONE rule that makes multi-cell bones survive gravity: bone cells are FIXED
        // to the grid and uncovering is MONOTONIC, so progress toward a bone never regresses.

        /// <summary>Bury this site's bone, if it gets one.
        ///
        /// GATED ON THE SHARD GATE. Bones appear once every egg species is owned — the same
        /// condition that switches the loot table over to egg shards — because that is the moment
        /// the game runs out of new dinosaurs to hatch and needs a new thing to collect. Before
        /// then a site never buries one, so nothing about the early game changes at all.</summary>
        private void PlaceBones()
        {
            if (_grid == null || _tiles.Count == 0 || TestSuppressBones || !BonesUnlocked())
            {
                return;
            }

            float chance = _config != null ? Mathf.Clamp01(_config.DigBoneSiteChance) : 1f;
            if (Random.value >= chance)
            {
                return;
            }

            _boneAssigned = TryPlaceRolledBone();
        }

        /// <summary>True once every egg species is owned — the gate egg shards already use.
        /// Mirrored rather than re-derived so the two can never drift apart.</summary>
        private bool BonesUnlocked()
        {
            GameManager gm = GameManager.Instance;
            return gm != null && gm.EggSpeciesAllOwned();
        }

        /// <summary>Pick the bone this site should bury and find somewhere on the board for it.
        ///
        /// THE SITE DIGS WHAT THE BOARD STILL NEEDS (DinoDigger-5ve). The skeleton board asks
        /// for the next species in its fill order with an incomplete skeleton, plus one of the
        /// bones that skeleton is still missing — so a dig always moves the collection forward
        /// and a child never banks a tenth skull. Templates for THAT bone are tried first (a
        /// femur has two shapes, the rest one); only if none of them fits the board does it fall
        /// back to any shape at all, because a bone that could not be placed is a dig with no
        /// treat in it, which is worse than an off-plan bone.
        ///
        /// Anchors are scanned from a shuffled start so the same bone never lands in the same
        /// spot, and row 0 is avoided while anything deeper fits — a bone lying along the
        /// surface would uncover itself on the first bite, and the beat is meant to take some
        /// digging.</summary>
        private bool TryPlaceRolledBone()
        {
            if (!TryBoneToBury(out DinoType species, out int wantedBone))
            {
                return false; // every skeleton is complete: nothing left worth burying
            }

            var order = new List<int>(BoneTemplates.Length);
            for (int i = 0; i < BoneTemplates.Length; i++)
            {
                order.Add(i);
            }

            Shuffle(order);

            // Templates that ARE the wanted bone come first; the rest stay as the fallback.
            order.Sort((a, b) =>
            {
                int wa = BoneTemplateType[a] == wantedBone ? 0 : 1;
                int wb = BoneTemplateType[b] == wantedBone ? 0 : 1;
                return wa.CompareTo(wb);
            });

            // Two passes: everything below the top row first, then the top row as a last resort.
            for (int pass = 0; pass < 2; pass++)
            {
                int minRow = pass == 0 ? 1 : 0;
                for (int i = 0; i < order.Count; i++)
                {
                    int template = order[i];
                    int start = Random.Range(0, Mathf.Max(1, _rows * _cols));
                    for (int step = 0; step < _rows * _cols; step++)
                    {
                        int cell = (start + step) % (_rows * _cols);
                        int r = cell / _cols;
                        int c = cell % _cols;
                        if (r < minRow)
                        {
                            continue;
                        }

                        if (PlaceBoneAt(r, c, template, species))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>Bury <paramref name="template"/> with its top-left cell at r,c, for
        /// <paramref name="species"/>'s skeleton. Returns false — changing nothing — unless EVERY
        /// cell of the shape is on the board and free, so a partial bone can never exist.</summary>
        private bool PlaceBoneAt(int r, int c, int template, DinoType species)
        {
            if (_grid == null || template < 0 || template >= BoneTemplates.Length)
            {
                return false;
            }

            int[] offsets = BoneTemplates[template];
            int cells = offsets.Length / 2;
            var rows = new int[cells];
            var cols = new int[cells];

            for (int i = 0; i < cells; i++)
            {
                rows[i] = r + offsets[i * 2];
                cols[i] = c + offsets[i * 2 + 1];
                if (!BoneCellFree(rows[i], cols[i]))
                {
                    return false;
                }
            }

            var bone = new Bone
            {
                Species = species,
                BoneIndex = BoneTemplateType[template],
                Rows = rows,
                Cols = cols,
                Uncovered = new bool[cells],
            };

            _bones.Add(bone);

            // Flag the covering tiles NOW (not in RefreshBonePeeks): the later generation steps
            // read CoversBone to keep loot, toys and the pocket off the bone layer.
            Sprite peek = BonePeekSprite(bone.BoneIndex);
            for (int i = 0; i < cells; i++)
            {
                TileAt(rows[i], cols[i])?.SetBonePeek(peek, BonePeekTint);
            }

            return true;
        }

        /// <summary>A cell the bone layer may claim: on the board, alive, plain dirt, hiding no
        /// item, not the pocket, and not already part of another bone.</summary>
        private bool BoneCellFree(int r, int c)
        {
            DirtTile t = TileAt(r, c);
            return t != null && !t.IsDestroyed && !t.HasItem && !t.IsSurprise && !t.CoversBone &&
                   t.Kind == DigTileKind.Dirt;
        }

        /// <summary>Which skeleton this bone belongs to. The four EGG species: they are the ones
        /// the child owns by the time bones unlock, so every bone dug is a bone for a dinosaur
        /// they know. (D2b owns the real board roster and may widen this.)</summary>
        /// <summary>Ask the skeleton board what to bury: the species it is currently filling in
        /// and one of the bones that skeleton still needs. False once every skeleton is complete
        /// (nothing left to collect) or with no GameManager (a bare test rig), and the site then
        /// buries no bone at all rather than a meaningless one.</summary>
        private bool TryBoneToBury(out DinoType species, out int boneIndex)
        {
            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                return gm.TryNextNeededBone(out species, out boneIndex);
            }

            species = default;
            boneIndex = -1;
            return false;
        }

        /// <summary>The unpopped bone owning cell r,c, or null.</summary>
        private Bone FindBoneAt(int r, int c)
        {
            for (int i = 0; i < _bones.Count; i++)
            {
                Bone b = _bones[i];
                if (b.Popped)
                {
                    continue;
                }

                for (int k = 0; k < b.Rows.Length; k++)
                {
                    if (b.Rows[k] == r && b.Cols[k] == c)
                    {
                        return b;
                    }
                }
            }

            return null;
        }

        /// <summary>Re-seat every bone peek after the board has moved.
        ///
        /// The peek says "a bone lives in this CELL", so it belongs to whichever tile currently
        /// stands on that cell — a tile that slid off one drops the hint, a tile that tumbled onto
        /// one picks it up. That is the visible half of the fixed-to-the-grid rule; the invisible
        /// half (the uncover flags) is <see cref="UpdateBones"/>, and it never goes backwards even
        /// when the visuals do.
        ///
        /// A tile hiding an item, a toy or the pocket keeps its own art: those all have their own
        /// promise to the child and it outranks the bone hint for that beat.</summary>
        private void RefreshBonePeeks()
        {
            if (_grid == null || _bones.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _tiles.Count; i++)
            {
                DirtTile t = _tiles[i];
                if (t != null && t.CoversBone && (t.IsDestroyed || FindBoneAt(t.Row, t.Col) == null))
                {
                    t.ClearBonePeek();
                }
            }

            for (int i = 0; i < _bones.Count; i++)
            {
                Bone b = _bones[i];
                if (b.Popped)
                {
                    continue;
                }

                Sprite peek = BonePeekSprite(b.BoneIndex);
                for (int k = 0; k < b.Rows.Length; k++)
                {
                    DirtTile t = TileAt(b.Rows[k], b.Cols[k]);
                    if (t != null && !t.IsDestroyed && !t.CoversBone)
                    {
                        t.SetBonePeek(peek, BonePeekTint); // no-ops on an item/toy/pocket tile
                    }
                }
            }
        }

        /// <summary>Book any bone cell that is currently EMPTY as uncovered, and pop any bone
        /// whose every cell has been. Called from the clear chokepoint and again when the board
        /// settles, so a cell uncovered mid-cascade counts the moment it happens.
        ///
        /// THE NO-REGRESSION RULE LIVES HERE, and it is one line: a flag is only ever set to
        /// true. Gravity may drop a fresh tile straight back onto a cell the child just cleared —
        /// visually the bone is buried again, and the peek follows the new tile — but the bone
        /// does not un-progress and the pop still fires on the last cell's FIRST uncovering. A
        /// toddler who has dug two thirds of a femur has dug two thirds of a femur.</summary>
        private void UpdateBones()
        {
            if (_grid == null || _bones.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _bones.Count; i++)
            {
                Bone b = _bones[i];
                if (b.Popped)
                {
                    continue;
                }

                bool all = true;
                for (int k = 0; k < b.Rows.Length; k++)
                {
                    if (!b.Uncovered[k] && TileAt(b.Rows[k], b.Cols[k]) == null)
                    {
                        b.Uncovered[k] = true;
                        _boneCellsUncovered++;
                    }

                    all &= b.Uncovered[k];
                }

                if (all)
                {
                    PopBone(b);
                }
            }
        }

        /// <summary>The whole bone comes out: it rises from the middle of its cells with a rattle
        /// and a sparkle, and banks to the collection. Guarded by the bone's own Popped flag, so
        /// however many paths uncover the last cell it happens exactly once.</summary>
        private void PopBone(Bone b)
        {
            b.Popped = true;
            _bonesPopped++;

            Vector3 at = BoneCenter(b);
            GameManager gm = GameManager.Instance;
            gm?.Audio?.ItemPop();

            int sparkles = _config != null ? Mathf.Clamp(_config.DigBoneSparkleCount, 0, 60) : 20;
            SpawnPitBurst(at, BonePeekTint, sparkles);
            SpawnBoneProp(b, at);

            // The bank, not the wallet: bones are the collection the skeleton board reads. Once
            // every skeleton has been revived there is nothing left to collect, and BankBone
            // pays the duplicate out as a fountain of coins instead — it says which it did, so
            // the pit's own flourish can follow suit.
            bool banked = gm == null || gm.BankBone(b.Species, b.BoneIndex, at);
            if (!banked)
            {
                SpawnPitBurst(at, new Color(1f, 0.85f, 0.4f), 16); // a coin-coloured second puff
            }

            // Whatever tiles are standing on its cells are no longer covering anything.
            for (int k = 0; k < b.Rows.Length; k++)
            {
                TileAt(b.Rows[k], b.Cols[k])?.ClearBonePeek();
            }
        }

        /// <summary>The assembled bone rising out of the pit. All of its motion is on GameConfig
        /// sliders (rise height/time, rattle, hold), and every sprite lookup is null-tolerant:
        /// the real fossil art, else the treasure bone, else a plain white silhouette sized to
        /// the bone's own footprint. A bone ALWAYS pops something the child can see.</summary>
        private void SpawnBoneProp(Bone b, Vector3 at)
        {
            Sprite art = BoneSpriteFor(b.BoneIndex);
            bool fallback = art == null;
            if (fallback)
            {
                art = WhiteBone();
            }

            if (art == null)
            {
                return; // no renderer to build (bare scene): the sparkle + the bank still ran
            }

            var go = new GameObject("BonePopFX");
            go.transform.SetParent(_root != null ? _root : transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = art;
            sr.color = Color.white;
            sr.sortingOrder = 30;
            go.transform.position = at;

            BoneFootprint(b, out float wide, out float tall);
            // The white silhouette is a 1x1 unit sprite, so it is scaled to the footprint
            // outright; real art keeps its own aspect and is just sized up a little.
            go.transform.localScale = fallback
                ? new Vector3(Mathf.Max(0.5f, wide * 0.8f), Mathf.Max(0.35f, tall * 0.45f), 1f)
                : Vector3.one * Mathf.Max(1f, Mathf.Max(wide, tall) * 0.7f);

            float rise = _config != null ? Mathf.Clamp(_config.DigBoneRiseSeconds, 0.05f, 3f) : 0.5f;
            float height = _config != null ? Mathf.Clamp(_config.DigBoneRiseHeight, 0f, 5f) : 1.1f;
            float rattle = _config != null ? Mathf.Clamp(_config.DigBoneRattleDegrees, 0f, 90f) : 22f;
            float rattleTime = _config != null ? Mathf.Clamp(_config.DigBoneRattleSeconds, 0.05f, 3f) : 0.55f;
            float hold = _config != null ? Mathf.Clamp(_config.DigBoneHoldSeconds, 0f, 4f) : 0.8f;

            Tween.ShakeRotation(go.transform, rattle, rattleTime);
            Tween.MoveTo(go.transform, at + new Vector3(0f, height, 0f), rise, () =>
            {
                Tween.After(hold, () =>
                {
                    Tween.ScaleTo(go.transform, Vector3.zero, 0.3f, () =>
                    {
                        if (go != null)
                        {
                            Destroy(go);
                        }
                    });
                });
            });
        }

        /// <summary>World centre of a bone's cells, where its prop rises from.</summary>
        private Vector3 BoneCenter(Bone b)
        {
            Vector3 sum = Vector3.zero;
            for (int k = 0; k < b.Rows.Length; k++)
            {
                sum += CellPosition(b.Rows[k], b.Cols[k]);
            }

            return sum / Mathf.Max(1, b.Rows.Length);
        }

        /// <summary>The bone's bounding box in CELLS (one unit per cell), which sizes its prop.</summary>
        private static void BoneFootprint(Bone b, out float wide, out float tall)
        {
            int minR = int.MaxValue, maxR = int.MinValue, minC = int.MaxValue, maxC = int.MinValue;
            for (int k = 0; k < b.Rows.Length; k++)
            {
                minR = Mathf.Min(minR, b.Rows[k]);
                maxR = Mathf.Max(maxR, b.Rows[k]);
                minC = Mathf.Min(minC, b.Cols[k]);
                maxC = Mathf.Max(maxC, b.Cols[k]);
            }

            wide = maxC - minC + 1;
            tall = maxR - minR + 1;
        }

        /// <summary>Art for one bone: the generated fossil sprite when the D2 art ticket has
        /// landed, else the existing treasure bone (the same one the Big Bone surprise pops), else
        /// null — which sends the caller to the white silhouette.</summary>
        private Sprite BoneSpriteFor(int boneIndex)
        {
            if (_lib == null)
            {
                return null;
            }

            // Explicit null checks, not ??: these are UnityEngine.Objects, whose "fake null" only
            // answers to the == operator.
            Sprite art = _lib.Bone(boneIndex);
            return art != null ? art : _lib.Treasure(BigBoneVariant);
        }

        /// <summary>What a covering tile draws as its bone hint. Never null: a peek renderer with
        /// no sprite draws nothing at all, and a bone cell with no hint is a bone the child has no
        /// reason to dig toward — so a stale library falls all the way through to the white
        /// silhouette rather than to an invisible promise.</summary>
        private Sprite BonePeekSprite(int boneIndex)
        {
            Sprite art = BoneSpriteFor(boneIndex);
            return art != null ? art : WhiteBone();
        }

        /// <summary>A 1x1 white sprite, built once: the last-resort bone silhouette, scaled to the
        /// bone's footprint by the caller. Deliberately unmistakable placeholder art — if this is
        /// what ships, the missing sprite is obvious rather than invisible.</summary>
        private static Sprite WhiteBone()
        {
            if (_whiteBoneFallback == null)
            {
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _whiteBoneFallback = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f),
                    new Vector2(0.5f, 0.5f), 1f);
            }

            return _whiteBoneFallback;
        }

        // ============================================================ DIG TOYS (z4d)
        // Three toys, one rule: a tap ALWAYS wins and every outcome is a bonus. Crystals pop
        // their whole colour blob, geodes blow a 3x3 after a moment of anticipation, pots break
        // into a fountain of coins. All three clear through ClearTile, so the gravity cascade
        // runs exactly as it does for dirt and every toy feeds every other one.

        // ----- Crystals -----

        /// <summary>Pop the whole 4-way connected same-colour blob containing
        /// <paramref name="start"/>, then let the board fall into it. The entry point for a tap;
        /// the auto-pop and the geode chain use the logical half directly so their several pops
        /// share ONE settle.</summary>
        private void PopCrystalBlob(DirtTile start, string cause)
        {
            if (PopCrystalBlobLogical(start, cause) > 0)
            {
                SettleGrid(cause);
            }
        }

        /// <summary>The blob pop itself: flood-fill from <paramref name="start"/>, clear every
        /// crystal in the blob, pay for them, and ripple the sparkles outward. Returns the blob
        /// size (0 when the tile is not a live crystal).
        ///
        /// LOGIC IS SYNCHRONOUS, THE LOOK IS STAGGERED — the same split the cascade engine uses
        /// for falling tiles, and for the same reason: the whole blob is cleared and the grid
        /// vacated on the tapped frame (so a test can assert it, and so nothing can wedge if the
        /// site closes a frame later), while each crystal HOLDS its pixels for ring * config
        /// seconds before sparkle-shrinking away. What the child sees is a pop rippling out from
        /// their finger; what the engine sees is one clean multi-cell clear.</summary>
        private int PopCrystalBlobLogical(DirtTile start, string cause)
        {
            if (start == null || start.IsDestroyed || start.Kind != DigTileKind.Crystal)
            {
                return 0;
            }

            CollectCrystalBlob(start);
            int count = _blob.Count;
            if (count == 0)
            {
                return 0;
            }

            // Copy out of the shared scratch before clearing anything: a clear runs the collect
            // chokepoint, and an auto-pop pass pops several blobs back to back — neither may be
            // iterating a list the next flood fill is allowed to overwrite.
            var cells = _blob.ToArray();
            var rings = _blobRing.ToArray();

            float ringTime = _config != null
                ? Mathf.Clamp(_config.DigCrystalPopRingSeconds, 0f, 0.5f)
                : 0.03f;
            int sparkles = _config != null
                ? Mathf.Clamp(_config.DigCrystalSparkleCount, 0, 40)
                : 12;
            int gen = _siteGeneration;

            for (int i = 0; i < count; i++)
            {
                DirtTile t = cells[i];
                if (t == null || t.IsDestroyed)
                {
                    continue;
                }

                float delay = rings[i] * ringTime;
                Color tint = DirtTile.CrystalTint(t.CrystalColor);
                Vector3 at = t.transform.position;

                t.ForceBreak(delay);   // cell vacated + collider off NOW, pixels linger
                ClearTile(t, cause);
                _crystalsPopped++;

                if (sparkles > 0)
                {
                    // The burst is the only DEFERRED part, and it addresses a captured world
                    // point rather than a grid cell, so a site that closes inside the ripple
                    // window simply drops it (guarded on the generation like every other
                    // delayed flourish here).
                    Tween.After(delay, () =>
                    {
                        if (!_open || gen != _siteGeneration)
                        {
                            return;
                        }

                        SpawnPitBurst(at, tint, sparkles);
                    });
                }
            }

            _crystalBlobs++;
            _lastBlobSize = count;
            GameManager.Instance?.Audio?.Chime();
            PayToyCoins(_config != null ? _config.DigCrystalCoins(count) : count);
            return count;
        }

        /// <summary>Flood-fill (4-way, same colour) from <paramref name="start"/> into
        /// <see cref="_blob"/>, with each cell's RING DEPTH from the start in
        /// <see cref="_blobRing"/> — a breadth-first walk, so ring depth is exactly the number of
        /// crystals between it and the tapped one, which is what the pop ripples along.</summary>
        private void CollectCrystalBlob(DirtTile start)
        {
            _blob.Clear();
            _blobRing.Clear();
            _blobSeen.Clear();

            if (start == null || start.IsDestroyed || start.Kind != DigTileKind.Crystal)
            {
                return;
            }

            int color = start.CrystalColor;
            _blob.Add(start);
            _blobRing.Add(0);
            _blobSeen.Add(start);

            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };
            for (int head = 0; head < _blob.Count; head++)
            {
                DirtTile cur = _blob[head];
                int ring = _blobRing[head];
                for (int i = 0; i < 4; i++)
                {
                    DirtTile n = TileAt(cur.Row + dr[i], cur.Col + dc[i]);
                    if (n == null || n.IsDestroyed || n.Kind != DigTileKind.Crystal ||
                        n.CrystalColor != color || _blobSeen.Contains(n))
                    {
                        continue;
                    }

                    _blobSeen.Add(n);
                    _blob.Add(n);
                    _blobRing.Add(ring + 1);
                }
            }
        }

        /// <summary>Record which same-colour crystal pairs are touching RIGHT NOW. Taken at the
        /// top of every settle so <see cref="AutoPopCrystals"/> can tell a contact gravity just
        /// made from one that was always there.</summary>
        private void SnapshotCrystalPairs()
        {
            _crystalPairs.Clear();
            if (_grid == null)
            {
                return;
            }

            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    DirtTile t = TileAt(r, c);
                    if (t == null || t.IsDestroyed || t.Kind != DigTileKind.Crystal)
                    {
                        continue;
                    }

                    AddPairIfMatching(t, TileAt(r + 1, c));
                    AddPairIfMatching(t, TileAt(r, c + 1));
                }
            }
        }

        private void AddPairIfMatching(DirtTile a, DirtTile b)
        {
            if (b != null && !b.IsDestroyed && b.Kind == DigTileKind.Crystal &&
                b.CrystalColor == a.CrystalColor)
            {
                _crystalPairs.Add(CrystalPairKey(a, b));
            }
        }

        /// <summary>Order-independent identity for a pair of tiles, by instance id. Keyed this
        /// way (rather than by row/col) because gravity renames every coordinate under the
        /// board — the PAIR is what has to stay recognisable, not where it sits.</summary>
        private static long CrystalPairKey(DirtTile a, DirtTile b)
        {
            int x = a.GetInstanceID();
            int y = b.GetInstanceID();
            if (x > y)
            {
                (x, y) = (y, x);
            }

            return ((long)(uint)x << 32) | (uint)y;
        }

        /// <summary>The settle loop's auto-pop pass: pop every blob that gravity has just
        /// created a NEW same-colour contact inside. Returns how many crystals went.
        ///
        /// Newness is the whole design. Popping "any blob of 2+" would evaporate the clusters a
        /// site is generated with the instant the child dug under one — they would never get to
        /// tap it. Popping only NEW contacts means a cluster riding a column down together is
        /// left alone, while a crystal that lands beside its own colour rewards the child with a
        /// free chain they did not have to plan.</summary>
        private int AutoPopCrystals(int gen)
        {
            if (_grid == null || gen != _siteGeneration || !_open || _finished)
            {
                return 0;
            }

            int popped = 0;
            for (int r = 0; r < _rows && !_finished; r++)
            {
                for (int c = 0; c < _cols && !_finished; c++)
                {
                    DirtTile t = TileAt(r, c);
                    if (t == null || t.IsDestroyed || t.Kind != DigTileKind.Crystal)
                    {
                        continue;
                    }

                    if (!HasNewCrystalContact(t))
                    {
                        continue;
                    }

                    popped += PopCrystalBlobLogical(t, "crystal auto-pop");
                }
            }

            if (popped > 0)
            {
                _autoPops++;
            }

            return popped;
        }

        /// <summary>True when this crystal now touches a same-colour crystal it was NOT touching
        /// when the current settle began.</summary>
        private bool HasNewCrystalContact(DirtTile t)
        {
            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                DirtTile n = TileAt(t.Row + dr[i], t.Col + dc[i]);
                if (n == null || n.IsDestroyed || n.Kind != DigTileKind.Crystal ||
                    n.CrystalColor != t.CrystalColor)
                {
                    continue;
                }

                if (!_crystalPairs.Contains(CrystalPairKey(t, n)))
                {
                    return true;
                }
            }

            return false;
        }

        // ----- Boom geode -----

        /// <summary>A geode has just been hit (tapped, cracked by a landing tile, or caught by a
        /// crew clear — <see cref="DirtTile.Damage"/> funnels all of them here). Light the fuse:
        /// sparkle, chime, and a short beat of anticipation before the whumph. The tile stays
        /// standing and fully solid while it burns, so the board around it keeps behaving
        /// normally; only the fuse callback has to prove its site is still current.</summary>
        internal void OnGeodeArmed(DirtTile geode)
        {
            if (geode == null || !_open || _finished)
            {
                return;
            }

            GameManager.Instance?.Audio?.Chime();
            SpawnPitBurst(geode.transform.position, new Color(0.75f, 0.95f, 1f), 10);
            Tween.PunchScale(geode.transform, 0.22f, 0.22f);

            float fuse = _config != null ? Mathf.Clamp(_config.DigGeodeFuseSeconds, 0f, 3f) : 0.4f;
            int gen = _siteGeneration;
            Tween.After(fuse, () =>
            {
                if (!_open || _finished || _grid == null || gen != _siteGeneration ||
                    geode == null || geode.IsDestroyed)
                {
                    return;
                }

                FireBoomGeode(geode);
            });
        }

        /// <summary>The whumph: a soft 3x3 clear centred on the geode, with a dust ring, a tiny
        /// camera nudge and a giggle hook for the audio pass.
        ///
        /// The centre is read from the geode's CURRENT row/col, not from where it was when the
        /// fuse was lit — a geode that fell during its own fuse blows up where it actually is.
        /// Cells are cleared TOP-DOWN so no step drops tiles into a cell a later step is about to
        /// clear (the same ordering trick the Trike headbutt uses), and the whole 3x3 shares ONE
        /// settle so the hole collapses as a single tumble rather than nine.</summary>
        private void FireBoomGeode(DirtTile geode)
        {
            int centerRow = geode.Row;
            int centerCol = geode.Col;
            Vector3 at = geode.transform.position;
            _geodeBooms++;

            GameManager gm = GameManager.Instance;
            gm?.Audio?.Roar();      // AUDIO HOOK: the dig pass swaps in a soft whumph + giggle
            gm?.DigShakeCamera(
                _config != null ? _config.DigGeodeShakeAmplitude : 0.09f,
                _config != null ? _config.DigGeodeShakeSeconds : 0.28f);

            SpawnPitBurst(at, new Color(0.85f, 0.95f, 1f), 24);
            SpawnDust(at, _config != null ? Mathf.Clamp(_config.DigGeodeDustCount, 0, 40) : 18);

            // The geode itself goes first (it is the centre of its own hole), then the ring.
            geode.ForceBreak(0f);
            ClearTile(geode, "boom geode");

            for (int r = centerRow - 1; r <= centerRow + 1; r++)
            {
                for (int c = centerCol - 1; c <= centerCol + 1; c++)
                {
                    if (r == centerRow && c == centerCol)
                    {
                        continue;
                    }

                    DirtTile t = TileAt(r, c);
                    if (t == null || t.IsDestroyed)
                    {
                        continue;
                    }

                    SpawnDust(t.transform.position, 3);
                    ClearTileNoSettle(t, "boom geode");   // a crystal here takes its blob with it
                }
            }

            SettleGrid("boom geode");
        }

        // ----- Pinata pot -----

        /// <summary>A pot just broke: spray 5-8 (config) coins that arc out over the pit, bounce,
        /// sit and shine, then auto-collect. Nothing to chase and nothing to miss — the child
        /// watches them get banked.
        ///
        /// Each coin is a throwaway sprite in the PIT plus, when it lands, one real reward
        /// pickup through the normal guarded bank path (the same split the Big Bone surprise
        /// uses): the spectacle lives where the child is looking, the money lives where the
        /// wallet can see it.</summary>
        private void SprayPotCoins(DirtTile pot)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null)
            {
                return;
            }

            int min = 5;
            int max = 8;
            _config?.GetPotCoinRange(out min, out max);
            int coins = Random.Range(min, max + 1);
            _potsBroken++;
            _lastPotCoins = coins;

            Vector3 at = pot != null ? pot.transform.position : _origin;
            gm.Audio?.ItemPop();
            SpawnPitBurst(at, new Color(1f, 0.85f, 0.4f), 20);

            float arc = _config != null ? Mathf.Clamp(_config.DigPotCoinArcSeconds, 0.1f, 2f) : 0.55f;
            float sit = _config != null ? Mathf.Clamp(_config.DigPotCoinCollectSeconds, 0.05f, 3f) : 1f;
            Sprite coinArt = _lib != null ? _lib.Treasure(0) : null;

            for (int i = 0; i < coins; i++)
            {
                // Fan the spray out both ways with a bit of scatter, so no two pots throw the
                // same shape and the fountain never reads as a queue.
                float spread = coins > 1 ? (i / (float)(coins - 1)) * 2f - 1f : 0f;
                Vector3 landing = at + new Vector3(
                    spread * Random.Range(1.1f, 2.0f),
                    Random.Range(-0.5f, 0.3f),
                    0f);
                SpawnPotCoinVisual(coinArt, at, landing, arc, sit);
            }

            _toyCoins += coins; // banked coin-by-coin above, so this is bookkeeping only
        }

        /// <summary>One sprayed coin: arc out of the pot, a little bounce as it lands, a moment
        /// of shine, then it shrinks away (its banked twin has already been paid).</summary>
        private void SpawnPotCoinVisual(Sprite art, Vector3 from, Vector3 to, float arc, float sit)
        {
            // The SPECTACLE is optional (a placeholder-only run with no coin art just shows
            // nothing); the BANK below is not. Keeping them separate is what guarantees "the pot
            // sprayed N coins" and "the wallet got N coins" can never disagree.
            if (art != null)
            {
                var go = new GameObject("PotCoinFX");
                go.transform.SetParent(_root != null ? _root : transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = art;
                sr.sortingOrder = 30;
                go.transform.position = from;

                Tween.MoveArc(go.transform, from, to, Random.Range(1.0f, 1.6f), arc, () =>
                {
                    if (go == null)
                    {
                        return;
                    }

                    // Bounce: a small hop in place, then it settles and shines.
                    Tween.MoveArc(go.transform, to, to, 0.28f, 0.22f, () =>
                    {
                        if (go != null)
                        {
                            Tween.PunchScale(go.transform, 0.25f, 0.2f);
                        }
                    });

                    Tween.After(sit, () =>
                    {
                        if (go == null)
                        {
                            return;
                        }

                        Tween.ScaleTo(go.transform, Vector3.zero, 0.2f, () =>
                        {
                            if (go != null)
                            {
                                Destroy(go);
                            }
                        });
                    });
                });
            }

            // The real money: one coin banked per sprayed coin, timed to its landing so the
            // counter ticks along with the fountain. Deliberately NOT generation-guarded — a
            // pot the child broke pays even if the round ends while the coins are still in the
            // air (same rule as the Giggle Pocket's coins). It touches nothing site-owned.
            Tween.After(arc + sit * 0.5f, () =>
            {
                GameManager g = GameManager.Instance;
                g?.SpawnRewardPickup(ItemType.Treasure, DinoType.TRex, 0, g.RewardSpawnPoint);
            });
        }

        /// <summary>Bank a crystal blob's coins through the normal guarded reward path — one
        /// pickup per coin, trickling out so the counter ticks up instead of jumping. The pot
        /// does NOT come through here: its coins are banked one at a time by the fountain, each
        /// timed to its own landing.</summary>
        private void PayToyCoins(int coins)
        {
            coins = Mathf.Max(0, coins);
            _toyCoins += coins;
            if (coins <= 0 || GameManager.Instance == null)
            {
                return;
            }

            for (int i = 0; i < coins; i++)
            {
                Tween.After(i * 0.08f, () =>
                {
                    GameManager g = GameManager.Instance;
                    g?.SpawnRewardPickup(ItemType.Treasure, DinoType.TRex, 0, g.RewardSpawnPoint);
                });
            }
        }

        // ----- Gravity cascade -----

        /// <summary>World position of a grid cell (row 0 is the top layer, one unit per row).
        /// The single source of truth for where a tile sits: BuildGrid places tiles with it and
        /// the cascade lands fallers on it, so the two can never drift apart.</summary>
        private Vector3 CellPosition(int r, int c)
        {
            return _origin + new Vector3(c - _gridHalfW, -(r + 1), 0f);
        }

        /// <summary>A tile has fully crumbled: vacate its cell so gravity sees a hole, then run
        /// the normal collect chokepoint (buried item, surprise pocket, round finish). Does NOT
        /// settle — the caller decides when the board is allowed to fall, so a single bite that
        /// clears several tiles cascades ONCE, and a clear that happens inside a running settle
        /// is simply picked up by that loop's next pass.</summary>
        private void ClearTile(DirtTile t, string cause)
        {
            if (t == null)
            {
                return;
            }

            if (_grid != null && t.Row >= 0 && t.Row < _rows && t.Col >= 0 && t.Col < _cols &&
                _grid[t.Row, t.Col] == t)
            {
                _grid[t.Row, t.Col] = null; // the corpse object lives on (it dies with the site)
            }

            // A PINATA POT pays here and nowhere else: this is the one place every "that tile is
            // gone" path meets, so the fountain fires whether the child tapped it twice, a crew
            // power smashed it, or a falling rock cracked it open by accident.
            if (t.Kind == DigTileKind.Pot)
            {
                SprayPotCoins(t);
            }

            _clearCause = cause;
            CollectIfBuried(t);

            // BONES (DinoDigger-0z5) are booked here, at the vacate chokepoint, so a cell
            // uncovered in the MIDDLE of a cascade counts the moment it happens rather than
            // waiting for the board to go quiet — and so a bone whose last cell is uncovered by
            // a landing crack pops on the same beat as one dug out by hand.
            UpdateBones();
        }

        /// <summary>The ONE chokepoint for "this tile just crumbled": vacate + collect, then let
        /// the board fall. Bites, superpowers, geode chains and landing cracks all end here, so
        /// every clearing path in the game cascades identically.</summary>
        private void TileCleared(DirtTile t, string cause)
        {
            ClearTile(t, cause);
            SettleGrid(cause);
        }

        /// <summary>Resolve the whole board to its stable state: repeat a compaction pass until
        /// one moves nothing, and return the passes taken.
        ///
        /// TERMINATION: a single pass compacts every column completely, so a later pass can only
        /// move something if a landing tick crumbled a tile — which is bounded by the tile count.
        /// The true bound is ~1 + rows*cols (36 here); <see cref="MaxSettlePasses"/> is a loud
        /// backstop that must never be reached in play, not a working limit.
        ///
        /// The loop re-checks the site every pass, so a close/rebuild/finish landing mid-cascade
        /// aborts it cleanly and silently. A re-entrant call (a clear raised from inside the
        /// loop) is a no-op: the running loop already sees that hole.
        ///
        /// AUTO-POP (DinoDigger-z4d) rides this loop as ONE extra pass, not a second engine:
        /// when the compaction has gone quiet, crystals that gravity has just pushed into a
        /// same-colour neighbour pop themselves, and — only if that popped something — the loop
        /// goes round again to settle into the new holes. The "one auto-pop pass per settle"
        /// rule is what bounds it: a chain started by an auto-pop settles fully but does not get
        /// to auto-pop again until the child's NEXT action, so a lucky board can cascade
        /// beautifully and still cannot loop forever. It shares the existing pass cap on top.</summary>
        private int SettleGrid(string cause)
        {
            if (_grid == null || _settling || !_open || _finished)
            {
                return 0;
            }

            _settling = true;
            int gen = _siteGeneration;
            int passes = 0;
            int falls = 0;
            bool stable = false;
            bool aborted = false;
            bool autoPopSpent = false;

            // Which same-colour crystal pairs were ALREADY touching before anything moved. Only
            // contacts that are new when the dust settles auto-pop, so a cluster the site was
            // generated with (or one a test placed by hand) is the child's to tap — it never
            // evaporates the first time the column under it is dug out.
            SnapshotCrystalPairs();

            try
            {
                while (passes < MaxSettlePasses)
                {
                    if (_grid == null || gen != _siteGeneration || !_open || _finished)
                    {
                        aborted = true;
                        break;
                    }

                    passes++;
                    int moved = SettlePass(gen);
                    falls += moved;
                    if (moved != 0)
                    {
                        continue;
                    }

                    if (!autoPopSpent)
                    {
                        autoPopSpent = true;
                        if (AutoPopCrystals(gen) > 0)
                        {
                            continue; // fall into the holes the auto-pop just opened
                        }
                    }

                    stable = true;
                    break;
                }
            }
            finally
            {
                _settling = false;
            }

            if (!stable && !aborted)
            {
                // Never silently: a cascade that needs this many passes is a bug in the
                // compaction, and the site is still playable, so it must be findable in the log.
                Debug.LogError($"[Dig] gravity cascade hit its {MaxSettlePasses}-pass cap after " +
                               $"'{cause}' ({falls} falls); board left as-is, site still playable");
            }

            // The board has stopped moving (or the site went away): re-seat the bone hints onto
            // whatever tiles now stand on the bone cells, and book any cell the falls left empty.
            // Peeks are visual and follow the tiles; uncover flags are progress and never go back
            // (see UpdateBones) — the two are updated together here so they can never disagree
            // about a cell for longer than one cascade.
            if (!aborted)
            {
                RefreshBonePeeks();
                UpdateBones();
            }

            _settlePasses = passes;
            _settleFalls = falls;
            return passes;
        }

        /// <summary>One compaction pass. Per column, bottom-up, every alive tile slides down to
        /// the lowest free cell and each tile that MOVED is recorded against whatever it came to
        /// rest on. Landing ticks are applied only once every column has compacted, so a pass
        /// never damages a tile it is still relocating. Returns the number of movers.</summary>
        private int SettlePass(int gen)
        {
            DirtTile[,] grid = _grid;
            if (grid == null)
            {
                return 0;
            }

            _landings.Clear();
            int moved = 0;

            for (int c = 0; c < _cols; c++)
            {
                int write = _rows - 1; // lowest cell still free in this column
                int order = 0;         // movers start bottom-first, so the column reads as a tumble
                for (int r = _rows - 1; r >= 0; r--)
                {
                    DirtTile t = grid[r, c];
                    if (t == null)
                    {
                        continue;
                    }

                    if (t.IsDestroyed)
                    {
                        // A path that crumbled a tile straight through DirtTile.Damage (a direct
                        // test probe, a future effect) leaves a corpse in the grid. Treat it as a
                        // hole so the engine stays correct even for a clear it was never told of.
                        grid[r, c] = null;
                        continue;
                    }

                    if (r != write)
                    {
                        grid[r, c] = null;
                        grid[write, c] = t;
                        t.SetCell(write, c); // logical move NOW; the tween below is pure travel

                        int drop = write - r;
                        float delay = FallStaggerFor(order); // config, re-read per mover
                        float time = FallSeconds(drop);
                        Vector3 landAt = CellPosition(write, c);
                        t.FallTo(landAt, delay, time, () => OnTileLanded(landAt, gen));

                        _landings.Add(new Landing
                        {
                            Faller = t,
                            Victim = write + 1 < _rows ? grid[write + 1, c] : null, // null = pit floor
                        });
                        moved++;
                        order++;
                    }

                    write--;
                }
            }

            ApplyLandingCracks(gen);
            return moved;
        }

        /// <summary>One hardness tick per landing, dealt to the tile that was landed ON (the pit
        /// floor takes none). A tick that crumbles its victim clears it through the normal
        /// chokepoint and the next pass turns that fresh hole into more falling — the cascade.
        ///
        /// The SURPRISE POCKET is exempt: it must be DISCOVERED, never squashed. A pocket that a
        /// falling tile could complete would fire its one-shot with the child never having
        /// cracked it — the wiggle would simply vanish mid-cascade, which is the opposite of the
        /// "find the mystery tile" beat. It still takes its thump and dust, just no damage.
        ///
        /// CRYSTALS are exempt too, for the mirror-image reason: a crystal is 1-hardness, so a
        /// single landing would shatter it — the child would watch a colour they were lining up
        /// get crushed by falling dirt, and it would leave the pit through a path that pays no
        /// coins. Crystal is hard: dirt lands ON it and stops. It only ever leaves by a tap, an
        /// auto-pop or a geode. (A PINATA POT is deliberately NOT exempt: getting cracked open by
        /// a falling rock is a lovely accident and it still sprays its coins.)</summary>
        private void ApplyLandingCracks(int gen)
        {
            for (int i = 0; i < _landings.Count; i++)
            {
                if (_grid == null || gen != _siteGeneration || !_open || _finished)
                {
                    break; // the site closed/finished mid-cascade: stop touching it
                }

                DirtTile victim = _landings[i].Victim;
                if (victim == null || victim.IsDestroyed || victim.IsSurprise ||
                    victim.Kind == DigTileKind.Crystal)
                {
                    continue;
                }

                _landingCracks++;
                if (victim.Damage())
                {
                    ClearTile(victim, "cascade landing");
                }
            }

            _landings.Clear();
        }

        /// <summary>Landing flourish for one fallen tile: a small dust puff at the impact line
        /// plus a soft thump. This fires from the tile's own travel tween, which can outlive its
        /// site, so it proves the generation before touching anything site-owned.</summary>
        private void OnTileLanded(Vector3 at, int gen)
        {
            if (!_open || gen != _siteGeneration)
            {
                return;
            }

            SpawnDust(at + new Vector3(0f, -0.45f, 0f), DustPerLanding);
            PlayThump();
        }

        /// <summary>Puff <paramref name="count"/> dust particles at a world point, on the
        /// generated dust art when it has been imported and on the crumb particle otherwise.
        /// The emitter is built once per site and reused, so a long cascade does not spawn a
        /// GameObject per landing.</summary>
        private void SpawnDust(Vector3 at, int count)
        {
            if (count <= 0)
            {
                return;
            }

            if (_dust == null && _lib != null && _lib.DustPuff != null)
            {
                _dust = GameManager.Instance?.TownCreateParticles(
                    _root != null ? _root : transform, _lib.DustPuff,
                    new Color(0.92f, 0.86f, 0.74f, 0.9f), 0.5f);
            }

            ParticleSystem ps = _dust != null ? _dust : _crumbs;
            if (ps == null)
            {
                return;
            }

            ps.transform.position = at;
            ps.Emit(count);
        }

        /// <summary>Soft landing thump. AUDIO HOOK: the dig audio pass gives falls their own low
        /// "whump"; until then a landing borrows the crumble sample, throttled to one per beat so
        /// a ten-tile cascade lands as one thud rather than a rattle.</summary>
        private void PlayThump()
        {
            if (Time.time - _lastThump < ThumpGap)
            {
                return;
            }

            _lastThump = Time.time;
            GameManager.Instance?.Audio?.Crumble();
        }

        // ----- Tap handling -----

        public void OnTileTapped(DirtTile tile)
        {
            // A tile that is still travelling to the cell the cascade moved it into is not a
            // valid bite target: the arm would chase a moving tile and the child would watch
            // the bucket miss. The tap is dropped, nothing else is blocked — input stays live
            // through the whole cascade and the tile is tappable again within ~0.3s.
            if (!_open || _finished || tile == null || tile.IsDestroyed || tile.IsFalling)
            {
                return;
            }

            GameManager.Instance?.Audio?.Dig();

            // No rig wired (legacy scene): resolve immediately rather than shooting a
            // placeholder square across the screen.
            if (_armPivot == null || _elbow == null || _wrist == null)
            {
                ResolveDig(tile);
                return;
            }

            // Queue the tap. A tap that arrives mid-dig is handled smoothly: the arm
            // finishes its current bite then reaches straight to the next tile without
            // returning to rest in between (see the Biting -> Reaching hand-off).
            if (tile != _activeTile && !_digQueue.Contains(tile))
            {
                _digQueue.Enqueue(tile);
            }
        }

        private void ResolveDig(DirtTile tile)
        {
            if (tile == null || _finished)
            {
                return;
            }

            _bites++;

            // A CRYSTAL tap is its own resolution: the bucket's bite pops the whole connected
            // same-colour blob (which clears through the same chokepoint and cascades once), and
            // the crew powers still fire on the bite afterwards. Nothing else about the bite
            // changes — the tap always wins, it just wins bigger.
            //
            // The one power this bite does NOT also run is the Big T-Rex adjacent clear below:
            // the blob pop has already cleared several cells and paid for them, so the bite is
            // strictly more generous than a normal one either way. Worth revisiting if the T-Rex
            // should visibly help on crystals too (it would want the logical pop + the adjacent
            // clear + a single shared settle, in that order).
            if (tile.Kind == DigTileKind.Crystal)
            {
                PopCrystalBlob(tile, "crystal tap");
                FireCrewPowers(tile);
                return;
            }

            bool destroyed = tile.Damage();
            GameManager.Instance?.Audio?.Crumble();

            // VACATE BEFORE ANY FALL. The bite's own clear is booked first (and without
            // settling): the T-Rex clear below picks its neighbour from the live grid, and a
            // board that fell while the tapped cell still read as occupied would drop a column
            // onto a tile that is not there any more.
            if (destroyed)
            {
                ClearTile(tile, "player bite");
            }

            // T-Rex superpower (Big-stage gate): the big fella's bite clears one adjacent
            // intact tile as well. Keyed off a Big T-Rex buddy being on the crew.
            if (!_finished && _trexBigHelps)
            {
                DirtTile adjacent = FindAdjacentIntact(tile);
                if (adjacent != null)
                {
                    Crew trex = FindCrew(DinoType.TRex);
                    if (trex != null && trex.Sprite != null)
                    {
                        Tween.PunchScale(trex.Sprite.transform, 0.25f, 0.25f);
                    }

                    if (adjacent.Kind == DigTileKind.Crystal)
                    {
                        // The big fella's helping bite pops a neighbouring blob properly (coins
                        // and all) rather than shattering one crystal for nothing.
                        PopCrystalBlobLogical(adjacent, "T-Rex adjacent clear");
                    }
                    else if (adjacent.Damage())
                    {
                        ClearTile(adjacent, "T-Rex adjacent clear");
                    }
                }
            }

            // ONE cascade for everything this bite cleared, so a bite plus its T-Rex bonus
            // reads as a single tumble instead of two staggered ones.
            SettleGrid("player bite");

            // Fire the rest of the crew's automatic powers on this bite (additive; the
            // tap has already fully resolved above).
            FireCrewPowers(tile);
        }

        /// <summary>
        /// A dirt tile just crumbled: if it hid an item, queue it up. Stay in the
        /// dig view until EVERY buried item is uncovered; only then hand the whole
        /// batch back to the overworld to spill out near the backhoe.
        /// </summary>
        private void CollectIfBuried(DirtTile tile)
        {
            if (_finished || tile == null)
            {
                return;
            }

            // Surprise Pocket: this is the one chokepoint every full-clear path funnels
            // through, so firing here (guarded to once) covers the tap bite, the T-Rex
            // adjacent clear, the Trike column, and the geode chain alike. The pocket tile
            // hides no item, so it falls through the buried lookup below with no double-handling.
            // The gravity cascade is deliberately NOT one of those paths: a landing tick skips
            // the pocket entirely (see ApplyLandingCracks) so it can only ever be uncovered by
            // digging, never squashed by a tile falling on it. The breadcrumb below still names
            // "cascade landing" if that ever regresses.
            if (tile == _surpriseTile && !_surpriseFired)
            {
                _surpriseFired = true;
                _surpriseFiredBy = $"{_clearCause} on r{tile.Row}c{tile.Col} " +
                                   $"(frame {Time.frameCount}, site gen {_siteGeneration})";
                FireSurprise(tile);
            }

            if (!_buried.TryGetValue(tile, out Buried b))
            {
                return;
            }

            _buried.Remove(tile);
            var info = new DugItemInfo(b.Type, b.Dino, b.Variant, tile.transform.position);
            _found.Add(info);
            GameManager.Instance?.Audio?.ItemPop();
            GameEvents.RaiseItemDug(info);

            // Pteranodon flourish: swoop over the pit as the item pops out (pure spectacle).
            Crew ptero = FindCrew(DinoType.Pteranodon);
            if (ptero != null)
            {
                Cheer(ptero);
                SwoopPteranodon(ptero, tile.transform.position);
            }

            if (_buried.Count == 0)
            {
                _finished = true;
                GameManager.Instance?.FinishDig(_found);
            }
        }

        private DirtTile FindAdjacentIntact(DirtTile tile)
        {
            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                int r = tile.Row + dr[i];
                int c = tile.Col + dc[i];
                if (r >= 0 && r < _rows && c >= 0 && c < _cols)
                {
                    DirtTile n = _grid[r, c];
                    if (n != null && !n.IsDestroyed)
                    {
                        return n;
                    }
                }
            }

            return null;
        }

        // ----- Excavator rig animation (two-bone IK reach + bucket bite) -----

        private void Update()
        {
            if (!_open || _finished || _armPivot == null || _elbow == null || _wrist == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            UpdateBodyLean(dt);

            switch (_arm)
            {
                case ArmState.Idle:
                    _effTarget = RestPoint();
                    _scoopDeg = RestScoop;
                    if (DequeueNext(out DirtTile next))
                    {
                        StartReach(next);
                    }

                    break;

                case ArmState.Reaching:
                    TickReaching(dt);
                    break;

                case ArmState.Biting:
                    TickBiting(dt);
                    break;

                case ArmState.Retracting:
                    TickRetracting(dt);
                    break;
            }

            SolveIK(_effTarget, ArmMaxDegPerSec * dt);
        }

        private void StartReach(DirtTile tile)
        {
            _activeTile = tile;
            _effFrom = _wrist != null ? _wrist.position : _effTarget;
            _phaseT = 0f;
            _biteFired = false;
            _arm = ArmState.Reaching;
        }

        // A tile that starts FALLING while the arm is reaching for it is simply followed down:
        // BiteAim reads the tile's live position every frame, so the bucket tracks it and the
        // bite still lands. (A tap AT a falling tile is dropped in OnTileTapped; this is the
        // already-accepted tap whose target the cascade moved out from under it.)
        private void TickReaching(float dt)
        {
            if (_activeTile == null || _activeTile.IsDestroyed)
            {
                _arm = ArmState.Retracting;
                _phaseT = 0f;
                _effFrom = _wrist != null ? _wrist.position : _effTarget;
                return;
            }

            _phaseT += dt / ReachTime;
            float e = Tween.EaseOutCubic(Mathf.Clamp01(_phaseT));
            _effTarget = Vector3.LerpUnclamped(_effFrom, BiteAim(_activeTile), e);
            _scoopDeg = Mathf.Lerp(RestScoop, ReachScoop, e);
            if (_phaseT >= 1f)
            {
                _phaseT = 0f;
                _arm = ArmState.Biting;
            }
        }

        private void TickBiting(float dt)
        {
            _phaseT += dt / BiteTime;
            float t = Mathf.Clamp01(_phaseT);
            if (_activeTile != null)
            {
                _effTarget = BiteAim(_activeTile);
            }

            _scoopDeg = Mathf.Lerp(ReachScoop, BiteScoop, Tween.EaseInOutCubic(t));

            // The bucket bites at the midpoint: damage the tile, burst crumbs, play the
            // crumble SFX — all synced to the scoop, not the tap.
            if (!_biteFired && t >= 0.5f)
            {
                _biteFired = true;
                ResolveDig(_activeTile);
            }

            if (_phaseT >= 1f)
            {
                _activeTile = null;
                if (DequeueNext(out DirtTile next))
                {
                    StartReach(next); // chain straight to the next tap, no rest in between
                }
                else
                {
                    _phaseT = 0f;
                    _effFrom = _wrist != null ? _wrist.position : _effTarget;
                    _arm = ArmState.Retracting;
                }
            }
        }

        private void TickRetracting(float dt)
        {
            _phaseT += dt / RetractTime;
            float e = Tween.EaseInOutCubic(Mathf.Clamp01(_phaseT));
            _effTarget = Vector3.LerpUnclamped(_effFrom, RestPoint(), e);
            _scoopDeg = Mathf.Lerp(BiteScoop, RestScoop, e);
            if (_phaseT >= 1f)
            {
                _arm = ArmState.Idle;
            }
        }

        // The excavator scoots along the surface toward the target column so the
        // shoulder tracks ~0.75x the tile's x offset — this is what buys reach at
        // the far columns. The ArmPivot is glued to the body's rear mount here,
        // every frame, BEFORE the IK solve.
        private void UpdateBodyLean(float dt)
        {
            if (_backhoeBody == null)
            {
                return;
            }

            float targetLean = 0f;
            if (_activeTile != null && !_activeTile.IsDestroyed)
            {
                float tileLocalX = _activeTile.transform.position.x - _origin.x;
                float restShoulderX = _bodyBase.x + MountX;
                // Shoulder parks slightly above-LEFT of the tile so the fixed
                // elbow-up bend side is always correct (see limits block).
                float desiredShoulderX = _origin.x + tileLocalX * ShoulderTrackGain + ShoulderTrackBias;
                targetLean = Mathf.Clamp(desiredShoulderX - restShoulderX, LeanMin, LeanMax);
            }

            _leanX = Mathf.Lerp(_leanX, targetLean, 1f - Mathf.Exp(-8f * dt));
            Vector3 bodyPos = new Vector3(_bodyBase.x + _leanX, _bodyBase.y, _bodyBase.z);
            _backhoeBody.transform.position = bodyPos;
            if (_armPivot != null)
            {
                _armPivot.position = bodyPos + new Vector3(MountX, MountY, 0f);
            }
        }

        private bool DequeueNext(out DirtTile tile)
        {
            while (_digQueue.Count > 0)
            {
                tile = _digQueue.Dequeue();
                if (tile != null && !tile.IsDestroyed)
                {
                    return true;
                }
            }

            tile = null;
            return false;
        }

        // Wrist position of the parked pose: forward kinematics of the explicit
        // rest joint angles, so the IK (with the elbow blended to the rear side)
        // reproduces exactly that compact fold over the cab.
        private Vector3 RestPoint()
        {
            if (_armPivot == null)
            {
                return _effTarget;
            }

            float b = RestBoomDeg * Mathf.Deg2Rad;
            float s = RestStickDeg * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(
                BoomLen * Mathf.Cos(b) + StickLen * Mathf.Cos(s),
                BoomLen * Mathf.Sin(b) + StickLen * Mathf.Sin(s),
                0f);
            return _armPivot.position + offset;
        }

        // Aim the wrist just above the tile so the bucket (hanging below the wrist)
        // digs into the tile face when it bites.
        private static Vector3 BiteAim(DirtTile tile)
        {
            return tile.transform.position + new Vector3(0f, 0.25f, 0f);
        }

        /// <summary>Two-segment (boom + stick) inverse kinematics with excavator
        /// joint limits. One FIXED elbow-up bend side (the traverse keeps every
        /// target below-right of the shoulder, so the side never needs to flip —
        /// this is what killed the pinwheel). After the analytic solve the boom
        /// is clamped to its arc, the stick is RE-AIMED at the target from the
        /// clamped elbow and clamped to its relative range, and all three joints
        /// move toward the result under per-frame angular velocity caps.
        /// <paramref name="maxDegStep"/> is the largest rotation allowed this
        /// frame for boom/stick (pass float.PositiveInfinity to snap, e.g. when
        /// posing the freshly built rig).</summary>
        private void SolveIK(Vector3 targetWorld, float maxDegStep)
        {
            if (_armPivot == null || _elbow == null || _wrist == null)
            {
                return;
            }

            Vector3 s = _armPivot.position;
            Vector2 d = new Vector2(targetWorld.x - s.x, targetWorld.y - s.y);
            float dist = d.magnitude;
            float maxR = BoomLen + StickLen - 0.02f;
            float minR = Mathf.Abs(BoomLen - StickLen) + 0.02f;
            float clamped = Mathf.Clamp(dist, minR, maxR);

            Vector2 dir = dist > 0.0001f ? d / dist : new Vector2(0f, -1f);
            Vector2 endPt = new Vector2(s.x, s.y) + dir * clamped;

            // Analytic elbow-up solve, then clamp the boom to its arc.
            float baseAng = Mathf.Atan2(dir.y, dir.x);
            float cosA = (clamped * clamped + BoomLen * BoomLen - StickLen * StickLen) / (2f * BoomLen * clamped);
            float a = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f));
            float boomDeg = Mathf.Clamp((baseAng + a) * Mathf.Rad2Deg, BoomMinDeg, BoomMaxDeg);

            // Re-aim the stick at the target from the CLAMPED elbow, then clamp
            // the elbow bend. (When both clamps engage the wrist falls short;
            // the hanging bucket and the body traverse cover the difference.)
            float boomRad = boomDeg * Mathf.Deg2Rad;
            Vector2 elbowPos = new Vector2(s.x, s.y)
                + new Vector2(Mathf.Cos(boomRad), Mathf.Sin(boomRad)) * BoomLen;
            float stickWorldDeg = Mathf.Atan2(endPt.y - elbowPos.y, endPt.x - elbowPos.x) * Mathf.Rad2Deg;
            float stickRelDeg = Mathf.Clamp(
                Mathf.DeltaAngle(boomDeg, stickWorldDeg), StickRelMinDeg, StickRelMaxDeg);

            // Rate-limited approach to the clamped solution.
            _boomShownDeg = Mathf.MoveTowardsAngle(_boomShownDeg, boomDeg, maxDegStep);
            _stickRelShownDeg = Mathf.MoveTowardsAngle(_stickRelShownDeg, stickRelDeg, maxDegStep);
            float scoopDeg = Mathf.Clamp(_scoopDeg, ScoopMinDeg, ScoopMaxDeg);
            float scoopStep = float.IsInfinity(maxDegStep)
                ? maxDegStep
                : maxDegStep * (BucketMaxDegPerSec / ArmMaxDegPerSec);
            _scoopShownDeg = Mathf.MoveTowardsAngle(_scoopShownDeg, scoopDeg, scoopStep);

            _armPivot.localRotation = Quaternion.Euler(0f, 0f, _boomShownDeg);
            _elbow.localRotation = Quaternion.Euler(0f, 0f, _stickRelShownDeg);
            _wrist.localRotation = Quaternion.Euler(0f, 0f, _scoopShownDeg);
        }

        private void ClearGrid()
        {
            for (int i = 0; i < _tiles.Count; i++)
            {
                if (_tiles[i] != null)
                {
                    Destroy(_tiles[i].gameObject);
                }
            }

            _tiles.Clear();
            _buried.Clear();
            _bones.Clear();    // the bone layer belongs to the site, not to the session
            _landings.Clear(); // a cascade in flight has nothing left to land on
            _crystalPairs.Clear();
            _blob.Clear();
            _blobRing.Clear();
            _blobSeen.Clear();
            _grid = null;
        }

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
