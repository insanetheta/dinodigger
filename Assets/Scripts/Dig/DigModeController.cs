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
    public class DigModeController : MonoBehaviour
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

        // TEST HOOK. Force the next site's surprise kind (>= 0 selects a SurpriseKind and
        // updates the last-seen index; -1 = roll normally). Reset by the test after use.
        internal static int TestForceSurpriseKind = -1;

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

        /// <summary>TEST HOOK. Fully clear the surprise tile through the SAME crew-clear
        /// chokepoint the Trike headbutt / geode chain use (ClearTileFully -> CollectIfBuried),
        /// so a test can prove the pocket fires on a non-tap path and never fires twice.</summary>
        internal void TestClearSurpriseTile()
        {
            if (_surpriseTile != null)
            {
                ClearTileFully(_surpriseTile);
            }
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

            _rows = _config != null ? Mathf.Clamp(_config.DigRows, 4, 6) : 5;
            _cols = _config != null ? Mathf.Max(3, _config.DigColumns) : 7;
            int health = _config != null ? _config.DirtHealth : 3;

            _grid = new DirtTile[_rows, _cols];
            Vector3 origin = _root != null ? _root.position : transform.position;

            Color dirtTint = _theme != null ? _theme.DirtTint : Color.white;
            ApplyBackgroundTint(_theme != null ? _theme.BackgroundTint : Color.white);

            float halfW = (_cols - 1) * 0.5f;

            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    var go = new GameObject($"Dirt_{r}_{c}");
                    go.transform.SetParent(_root != null ? _root : transform, false);
                    go.transform.position = origin + new Vector3(c - halfW, -(r + 1), 0f);

                    var box = go.AddComponent<BoxCollider2D>();
                    box.size = new Vector2(1.0f, 1.0f); // generous touch target

                    var tile = go.AddComponent<DirtTile>();
                    tile.Build(this, _lib, r, c, health, _crumbs);
                    tile.SetDirtTint(dirtTint); // theme multiply over the crack sprites

                    _tiles.Add(tile);
                    _grid[r, c] = tile;
                }
            }

            PlaceItems();
            PlaceSurprisePocket();
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

            if (_crewBuddies == null)
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
        /// a quick top-to-bottom cascade (rows staggered so it reads as a tumble).</summary>
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

            for (int r = 0; r < _rows; r++)
            {
                int row = r;
                Tween.After(row * HeadbuttStagger, () =>
                {
                    if (!_open || _finished || _grid == null || row >= _rows || col >= _cols)
                    {
                        return;
                    }

                    DirtTile t = _grid[row, col];
                    ClearTileFully(t);
                });
            }
        }

        /// <summary>Damage a tile until it crumbles, then collect anything it hid. Used by
        /// the Triceratops column cascade (these are helper hits, NOT player bites, so they
        /// never advance the power cadence).</summary>
        private void ClearTileFully(DirtTile t)
        {
            if (t == null || t.IsDestroyed)
            {
                return;
            }

            int guard = 0;
            while (!t.IsDestroyed && guard++ < 8)
            {
                t.Damage();
            }

            if (t.IsDestroyed)
            {
                CollectIfBuried(t);
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

                Buried b = RollItem();
                _buried[tile] = b;

                Sprite peek = PeekSprite(b, out Color tint);
                tile.SetPeek(peek, tint);
                placed++;
            }

            // If everything was top-row (tiny grids), just place on whatever is left.
            if (placed == 0 && candidates.Count > 0)
            {
                Buried b = RollItem();
                _buried[candidates[0]] = b;
                Sprite peek = PeekSprite(b, out Color tint);
                candidates[0].SetPeek(peek, tint);
            }
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

        // EGG-SHARD NERF: once every egg species is owned, a dug egg can no longer
        // hatch anything new, so its configured weight is cut to EggNerfFraction and
        // the freed remainder rolls EGG SHARDS instead. (Any residual egg that still
        // rolls resolves to a shard downstream too, since no unique species remains —
        // see GameManager.ResolveDugItem — so the nest, not duplicates, gets fed.)
        private const float EggNerfFraction = 0.2f;

        private Buried RollItem()
        {
            // Themed sites skew the loot; an unthemed site uses the flat config weights
            // (identical to Meadow Classic), so the existing roll tests are unchanged.
            float egg = _theme != null ? _theme.EggWeight : _config.EggWeight;
            float fruit = _theme != null ? _theme.FruitWeight : _config.FruitWeight;
            float treasure = _theme != null ? _theme.TreasureWeight : _config.TreasureWeight;
            float shard = 0f;

            GameManager gm = GameManager.Instance;
            if (gm != null && gm.EggSpeciesAllOwned())
            {
                shard = egg * (1f - EggNerfFraction);
                egg *= EggNerfFraction;
            }

            float total = Mathf.Max(0.0001f, egg + shard + fruit + treasure);
            float roll = Random.value * total;

            var b = new Buried();
            if (roll < egg)
            {
                b.Type = ItemType.Egg;
                b.Dino = RandomDino();
            }
            else if (roll < egg + shard)
            {
                b.Type = ItemType.Shard;
            }
            else if (roll < egg + shard + fruit)
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
                if (t == null || t.HasItem)
                {
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

            // 8-neighbour ring, staggered so it reads as a tumble outward.
            int[] dr = { -1, 1, 0, 0, -1, -1, 1, 1 };
            int[] dc = { 0, 0, -1, 1, -1, 1, -1, 1 };
            for (int i = 0; i < 8; i++)
            {
                int r = center.Row + dr[i];
                int c = center.Col + dc[i];
                Tween.After(i * GeodeStagger, () =>
                {
                    if (!_open || _finished || _grid == null)
                    {
                        return;
                    }

                    DirtTile t = TileAt(r, c);
                    if (t == null || t.IsDestroyed)
                    {
                        return;
                    }

                    SpawnPitBurst(t.transform.position, new Color(0.7f, 0.95f, 1f), 8);
                    ClearTileFully(t);
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

        // ----- Tap handling -----

        public void OnTileTapped(DirtTile tile)
        {
            if (!_open || _finished || tile == null || tile.IsDestroyed)
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
            bool destroyed = tile.Damage();
            GameManager.Instance?.Audio?.Crumble();

            // T-Rex superpower (Big-stage gate): the big fella's bite clears one adjacent
            // intact tile as well. Keyed off a Big T-Rex buddy being on the crew.
            if (_trexBigHelps)
            {
                DirtTile adjacent = FindAdjacentIntact(tile);
                if (adjacent != null)
                {
                    Crew trex = FindCrew(DinoType.TRex);
                    if (trex != null && trex.Sprite != null)
                    {
                        Tween.PunchScale(trex.Sprite.transform, 0.25f, 0.25f);
                    }

                    bool adjDestroyed = adjacent.Damage();
                    if (adjDestroyed)
                    {
                        CollectIfBuried(adjacent);
                    }
                }
            }

            if (destroyed)
            {
                CollectIfBuried(tile);
            }

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
            if (tile == _surpriseTile && !_surpriseFired)
            {
                _surpriseFired = true;
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
