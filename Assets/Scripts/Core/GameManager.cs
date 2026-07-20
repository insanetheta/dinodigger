using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Dig;
using DinoDigger.Input;
using DinoDigger.Managers;
using DinoDigger.Overworld;
using DinoDigger.UI;

namespace DinoDigger.Core
{
    /// <summary>
    /// The single MonoBehaviour that wires up every system. Owns the plain-C#
    /// managers (state, save, audio, spawn), routes taps, and coordinates the
    /// roam <-> dig flow. Everything else talks through <see cref="Instance"/>.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Config assets")]
        [SerializeField] private GameConfig _config;
        [SerializeField] private PlaceholderLibrary _library;
        [SerializeField] private AudioConfig _audioConfig;

        [Header("Scene refs")]
        [SerializeField] private Camera _mainCamera;
        [SerializeField] private InputService _input;
        [SerializeField] private BackhoeController _backhoe;
        [SerializeField] private OverworldMap _map;
        [SerializeField] private CameraFollow _cameraFollow;
        [SerializeField] private DigModeController _digMode;
        [SerializeField] private TreasureCounter _treasureCounter;
        [SerializeField] private MuteButton _muteButton;
        [SerializeField] private Transform _overworldRoot;
        [SerializeField] private MeadowArea _meadow;
        [SerializeField] private NestController _nest;
        [SerializeField] private TownController _town;
        [SerializeField] private List<DigMound> _mounds = new List<DigMound>();

        [Header("Audio sources")]
        [SerializeField] private int _sfxVoices = 6;

        // ---- Companion tuning ----
        private const int BuddyCap = 2;                    // max dinos following the backhoe
        private const float TreeShakeRange = 3f;           // Brachio must be this close to a tapped tree
        private const float TreeCooldownSeconds = 10f;     // per-tree fruit-drop cooldown
        private const float SnifferIntervalSeconds = 6f;   // Stego mound-sniff cadence
        private const float CourierScanSeconds = 0.8f;     // Trike fruit-scan cadence
        private const float CourierMinFruitDist = 2.5f;    // fruit farther than this gets fetched
        private const float CourierDropDist = 0.9f;        // set down about here from the backhoe
        private const float ParadeSeconds = 8f;
        private const float CeremonyLingerSeconds = 3f;    // nest ceremony auto-returns after this

        // ---- Fruit Stand (surplus-fruit -> coins) tuning ----
        private const float SellerCommuteSpeed = 1.1f;     // resident hauling fruit to the stand
        private const int FruitStandCoinVariant = 0;       // plain coin (TreasureValue 1)
        private const int FruitStandGemVariant = 1;        // jackpot gem (TreasureValue 3)
        private const int FruitStandGemEverySale = 5;      // every 5th sale pays a gem, not a coin

        // Managers
        public GameStateManager State { get; private set; }
        public SaveManager Save { get; private set; }
        public AudioManager Audio { get; private set; }
        public SpawnManager Spawn { get; private set; }

        private readonly List<DinoController> _dinos = new List<DinoController>();
        private DigMound _activeMound;
        private float _idleTimer;
        private Material _particleMat;

        // ---- Companion state ----
        private readonly List<DinoController> _buddies = new List<DinoController>(); // [0] = longest-serving
        private readonly List<ItemPickup> _pickups = new List<ItemPickup>();          // all live pickups (fruit scan)
        private readonly Dictionary<Vector3Int, float> _treeCooldownUntil = new Dictionary<Vector3Int, float>();
        private float _snifferTimer = SnifferIntervalSeconds;
        private int _snifferPulses;            // test-observable pulse counter
        private float _courierScanTimer;
        private DinoController _courier;       // Trike currently on a fruit run
        private ItemPickup _carriedFruit;
        private bool _paradeActive;

        // ---- Fruit Stand sell state ----
        // Residents currently hauling a sold fruit to the stand (may run concurrently with
        // taps). Kept out of the town's builder draft so a seller is never poached mid-haul.
        private readonly List<DinoController> _sellers = new List<DinoController>();
        private int _fruitSalesCount;          // transient (not saved) — drives the 5th-sale gem

        // ---- Shard-hatch ceremony state ----
        private bool _ceremonyActive;
        private DinoController _ceremonyDino;   // the freshly hatched baby waiting at the nest

        // ---- Egg-species uniqueness reservation ----
        // Species claimed by an egg that has been dug/finalized (after its unique
        // re-roll) but has NOT yet hatched — i.e. still buried-then-spilled and
        // sitting on the overworld, or resolved earlier in the same dig batch. The
        // egg roll excludes owned OR reserved species, so a duplicate can never
        // spill in one batch, across two quick digs, or from an un-hatched spill.
        // Ref-counted for defence in depth; the uniqueness invariant keeps each
        // count at 1 in practice. Released when the egg hatches (HatchEgg) or its
        // pickup is destroyed unhatched (ItemPickup.OnDestroy / TestReset).
        private readonly Dictionary<Config.DinoType, int> _reservedEggSpecies =
            new Dictionary<Config.DinoType, int>();

        // ---- Egg-shard nest ----
        /// <summary>How many shard-built eggs have already hatched. Derived, not stored:
        /// it equals the number of owned shard-exclusive species (DinoType 4-8), which the
        /// v3 save already captures via its Dinos list — so no new save field is needed.</summary>
        public int ShardEggsHatched => CountOwnedShardSpecies();

        /// <summary>Egg shards required for the NEXT shard hatch. Escalates with the number
        /// of shard eggs already hatched (5 / 8 / 15 / 20, then 20), driving the ceremony
        /// trigger, the remainder-carryover on hatch, and the nest assembly scaling.</summary>
        public int ShardsPerHatch =>
            _config != null ? _config.GetShardRequirement(ShardEggsHatched) : 20;
        public int ShardCount => Save != null ? Save.Data.ShardCount : 0;

        // ---------------------------------------------------------------- setup

        private void Awake()
        {
            Instance = this;

            State = new GameStateManager();
            Save = new SaveManager();
            Audio = new AudioManager();
            Spawn = new SpawnManager();

            SetupAudio();
            Spawn.Init(_config, _map, _mounds, _backhoe != null ? _backhoe.transform : null);
            Spawn.SetMeadow(_meadow);

            if (_cameraFollow != null)
            {
                _cameraFollow.Configure(_mainCamera, _backhoe != null ? _backhoe.transform : null, _config);
            }

            if (_backhoe != null)
            {
                _backhoe.Configure(_map, _config,
                    _library != null ? _library.BackhoeDir : null,
                    _library != null ? _library.BackhoeRollA : null,
                    _library != null ? _library.BackhoeRollB : null);
            }

            if (_digMode != null)
            {
                _digMode.Configure(_config, _library);
            }

            if (_muteButton != null)
            {
                _muteButton.Bind(Audio, _config);
            }
        }

        private void OnEnable()
        {
            if (_input != null)
            {
                _input.Tapped += OnTap;
            }

            GameEvents.DinoGrew += OnDinoGrew;
        }

        private void OnDisable()
        {
            if (_input != null)
            {
                _input.Tapped -= OnTap;
            }

            GameEvents.DinoGrew -= OnDinoGrew;
        }

        private void Start()
        {
            Save.Load();
            RestoreFromSave();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            SaveNow();
            GameEvents.ClearAll();
        }

        private void SetupAudio()
        {
            var sfx = new AudioSource[Mathf.Max(1, _sfxVoices)];
            for (int i = 0; i < sfx.Length; i++)
            {
                var go = new GameObject($"SFX_{i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                sfx[i] = src;
            }

            var musicGo = new GameObject("Music");
            musicGo.transform.SetParent(transform, false);
            var music = musicGo.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.spatialBlend = 0f;

            Audio.Init(_audioConfig, sfx, music);
        }

        private void RestoreFromSave()
        {
            if (_treasureCounter != null)
            {
                _treasureCounter.SetCount(Save.Data.TreasureCount);
            }

            // Show the egg's assembly state for the banked shard count right away.
            _nest?.RefreshAssembly(Save.Data.ShardCount);

            // Rebuild Dino Town: finished buildings return finished (no crew/confetti), a
            // partial site resumes accepting crew, and the queue continues from the saved
            // index. A v3 (or earlier) save has no town fields, so the town stays empty.
            _town?.RestoreFromSave(Save.Data);

            if (Save.Data.Dinos != null)
            {
                // Backward compatibility: saves from before the buddy system (v1)
                // have no IsBuddy field (JsonUtility default = false), so the first
                // two loaded dinos become the buddies. v2+ saves use the real flag.
                // (Keyed off BuddyFieldVersion, NOT CurrentVersion, so a v2 save is
                // still read with its real IsBuddy flags after the v3 bump.)
                bool legacy = Save.Data.Version < SaveData.BuddyFieldVersion;
                int index = 0;
                foreach (DinoSave d in Save.Data.Dinos)
                {
                    bool wantsBuddy = legacy ? index < BuddyCap : d.IsBuddy;
                    Vector3 pos = wantsBuddy || _meadow == null
                        ? DinoSpawnPos()
                        : _meadow.RandomInteriorPoint(); // residents wake up at home
                    SpawnDino(d.Type, d.Stage, d.FruitEaten, pos, persist: false,
                        wantsBuddy: wantsBuddy);
                    index++;
                }
            }
        }

        // --------------------------------------------------------------- update

        private void Update()
        {
            float dt = Time.deltaTime;
            Spawn.Tick(dt);
            TickIdleAttract(dt);
            TickSniffer(dt);
            TickCourier(dt);
            TickSellers();
            // Ambient town builder: auto-spends coins + drives resident construction.
            // Always ticks (you dig; they build) and never touches the player/backhoe.
            _town?.Tick(dt);
        }

        private void TickIdleAttract(float dt)
        {
            if (State == null || !State.Is(GameState.Roam) || _config == null)
            {
                return;
            }

            _idleTimer += dt;
            if (_idleTimer >= _config.IdleAttractSeconds)
            {
                _idleTimer = 0f;
                FireIdleAttract();
            }
        }

        private void FireIdleAttract()
        {
            _backhoe?.Honk();
            Audio?.Honk();
            NearestActiveMound(_backhoe != null ? _backhoe.transform.position : Vector3.zero)?.AttractPulse();
            GameEvents.RaiseIdleAttract();
        }

        // ------------------------------------------------------------ tap input

        private void OnTap(Vector2 screenPos)
        {
            _idleTimer = 0f;
            if (_mainCamera == null)
            {
                return;
            }

            Vector3 world = _mainCamera.ScreenToWorldPoint(
                new Vector3(screenPos.x, screenPos.y, Mathf.Abs(_mainCamera.transform.position.z)));
            world.z = 0f;

            ITappable tappable = FindTappable(world);
            if (tappable != null)
            {
                Audio?.Tap();
                tappable.OnTapped(world);
                return;
            }

            // No collider hit: a tapped TREE tile (Obstacles tilemap) routes to the
            // Brachiosaurus fruit-shake; anything else drives the backhoe.
            if (State.Is(GameState.Roam) && TryRouteTreeTap(world))
            {
                Audio?.Tap();
                return;
            }

            // Empty tap: only meaningful while roaming (drive the backhoe).
            if (State.Is(GameState.Roam) && _backhoe != null)
            {
                Audio?.Tap();
                _backhoe.MoveTo(world);
            }
        }

        /// <summary>If the tapped cell holds a tree tile (Obstacles tilemap), fire
        /// the tree-tap flow. Returns true when a tree consumed the tap. Only the
        /// tree's own (unwalkable) cell counts, so movement taps on the grass
        /// around it are never swallowed.</summary>
        private bool TryRouteTreeTap(Vector3 world)
        {
            if (_map == null || _library == null || _library.TreeTile == null)
            {
                return false;
            }

            Vector3Int cell = _map.WorldToCell(world);
            if (_map.ObstacleAt(cell) == _library.TreeTile)
            {
                OnTreeTapped(cell);
                return true;
            }

            return false;
        }

        private ITappable FindTappable(Vector3 world)
        {
            Collider2D[] hits = Physics2D.OverlapPointAll(world);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] == null)
                {
                    continue;
                }

                var t = hits[i].GetComponent<ITappable>() ?? hits[i].GetComponentInParent<ITappable>();
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        // ------------------------------------------------------------- dig flow

        /// <summary>Tapped a mound: drive there, then dig on arrival.</summary>
        public void RequestDig(DigMound mound)
        {
            if (State.Is(GameState.Roam))
            {
                _backhoe?.DriveToMound(mound);
            }
        }

        /// <summary>Backhoe reached the mound: build the dig site and zoom in.</summary>
        public void EnterDig(DigMound mound)
        {
            if (!State.Is(GameState.Roam) || _digMode == null)
            {
                return;
            }

            _activeMound = mound;
            State.Set(GameState.Transition);

            bool bigHelps = HasBigDino();
            _digMode.Open(bigHelps);

            if (_cameraFollow != null)
            {
                _cameraFollow.EnterDig(_digMode.DigCenter, () => State.Set(GameState.Dig));
            }
            else
            {
                State.Set(GameState.Dig);
            }
        }

        /// <summary>
        /// Every buried item at the site has been uncovered; return to the
        /// overworld and spill the whole batch out near the backhoe.
        /// </summary>
        public void FinishDig(List<DugItemInfo> items)
        {
            if (State.Is(GameState.Roam))
            {
                return;
            }

            State.Set(GameState.Transition);

            // Copy the batch: the dig controller clears its own list on Close().
            var batch = items != null ? new List<DugItemInfo>(items) : new List<DugItemInfo>();

            // EGG UNIQUENESS + SHARDS: this is the point where the dig site's item
            // roll becomes the overworld item, so resolve each item here. An egg is
            // reassigned an UNOWNED egg species (never a duplicate); once every egg
            // species is owned there is no unique species to give, so the egg becomes
            // an egg SHARD instead. The visible item (color + behavior) is fully
            // consistent from this point on; only the faint under-dirt peek tint used
            // the site's original roll.
            for (int i = 0; i < batch.Count; i++)
            {
                batch[i] = ResolveDugItem(batch[i]);
            }

            // Consume the mound and schedule its respawn elsewhere.
            if (_activeMound != null)
            {
                Spawn.ScheduleRespawn(_activeMound);
                _activeMound = null;
            }

            if (_cameraFollow != null)
            {
                _cameraFollow.ExitDig(() => AfterDigReturn(batch));
            }
            else
            {
                AfterDigReturn(batch);
            }
        }

        private void AfterDigReturn(List<DugItemInfo> items)
        {
            _digMode?.Close();
            State.Set(GameState.Roam);

            if (items == null)
            {
                return;
            }

            int count = items.Count;
            for (int i = 0; i < count; i++)
            {
                DugItemInfo info = items[i];
                int index = i;
                // Slight stagger so the items visibly spill out one after another.
                Tween.After(i * 0.09f, () => SpawnDugItem(info, index, count));
            }
        }

        /// <summary>Dig-helper gate (T-Rex superpower): a BIG T-REX that is
        /// currently a walk buddy. Name kept for hook compatibility.</summary>
        private bool HasBigDino()
        {
            for (int i = 0; i < _buddies.Count; i++)
            {
                DinoController b = _buddies[i];
                if (b != null && b.IsBig && b.Type == Config.DinoType.TRex)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------- item spawning

        private void SpawnDugItem(DugItemInfo info, int index, int total)
        {
            Vector3 backhoePos = _backhoe != null ? _backhoe.transform.position : Vector3.zero;
            Vector3 origin = backhoePos + new Vector3(0f, 0.2f, 0f);

            // Scatter multiple items in a flattened ring around the backhoe so they
            // land near it without overlapping (Y squashed for the iso ground plane).
            float angle = total > 1
                ? (index / (float)total) * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f)
                : Random.value * Mathf.PI * 2f;
            float radius = 1.1f + (index % 2) * 0.4f;
            Vector2 rnd = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.7f) * radius;
            Vector3 landing = backhoePos + new Vector3(rnd.x, rnd.y, 0f);
            if (_map != null)
            {
                landing = _map.NearestWalkable(landing, out _);
            }

            var infoAtBackhoe = new DugItemInfo(info.Type, info.DinoType, info.Variant, origin);
            CreatePickup(infoAtBackhoe, landing);
        }

        /// <summary>Build one <see cref="ItemPickup"/> that pops from its origin to a landing spot.</summary>
        private ItemPickup CreatePickup(DugItemInfo info, Vector3 landing)
        {
            var go = new GameObject($"Item_{info.Type}");
            go.transform.SetParent(_overworldRoot, false);
            go.transform.position = info.OriginWorld;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 20;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.6f; // generous touch target
            col.isTrigger = true;

            var item = go.AddComponent<ItemPickup>();
            ParticleSystem sparkle = CreateParticles(go.transform, _library != null ? _library.StarParticle : null,
                Color.white, 0.3f);
            item.AttachSparkle(sr, sparkle);
            item.Init(info, landing, _config, _library);

            // Registry for the Trike courier scan (no per-frame FindObjectsByType).
            // Sweep dead entries here too so the list stays tiny even when no
            // courier ever scans it.
            for (int i = _pickups.Count - 1; i >= 0; i--)
            {
                if (_pickups[i] == null)
                {
                    _pickups.RemoveAt(i);
                }
            }

            _pickups.Add(item);
            return item;
        }

        /// <summary>Public spawn hook (duck-catch reward + other ambient drops): pop a
        /// pickup that arcs from just above <paramref name="landing"/> down onto it.
        /// The landing is clamped to the nearest walkable cell so rewards never strand
        /// on water. Routes through the exact same path as dug items.</summary>
        public ItemPickup SpawnRewardPickup(ItemType type, Config.DinoType dinoType, int variant, Vector3 landing)
        {
            if (_map != null)
            {
                landing = _map.NearestWalkable(landing, out _);
            }

            Vector3 origin = landing + new Vector3(0f, 0.2f, 0f);
            var info = new DugItemInfo(type, dinoType, variant, origin);

            // An egg reward runs through the SAME uniqueness + reservation resolution
            // as a dug egg, so a reward can never hand out a duplicate/owned species.
            // (Real rewards today are only fruit/treasure, but this keeps every egg
            // DugItemInfo funneled through one gate.)
            if (info.Type == ItemType.Egg)
            {
                info = ResolveDugItem(info);
            }

            return CreatePickup(info, landing);
        }

        /// <summary>Resolve a freshly dug item into its final overworld identity.
        /// Eggs are reassigned an unowned egg species so a duplicate can never hatch;
        /// when every egg species is owned the egg becomes an egg SHARD instead. All
        /// other item types (fruit, treasure, and shards rolled directly by the loot
        /// nerf) pass through unchanged.</summary>
        private DugItemInfo ResolveDugItem(DugItemInfo info)
        {
            // Shard gate: shards only matter while a shard-exclusive species is still
            // unowned. Once the nest has produced all five, it is complete forever, so
            // any shard (rolled directly by the loot nerf) downgrades to treasure —
            // "shards stop dropping".
            if (info.Type == ItemType.Shard)
            {
                return AnyShardSpeciesUnowned()
                    ? info
                    : new DugItemInfo(ItemType.Treasure, info.DinoType, info.Variant, info.OriginWorld);
            }

            // FRUIT GLUT GUARD: fruit is 40% of drops but demand is finite (a Big dino is
            // never hungry). When there is NO fruit demand, most of it downgrades to a random
            // treasure so uneaten fruit can't pile up; the rest stays fruit so the world
            // still has some. "Fruit demand" widens as sinks come online: today it is a
            // hungry dino OR an open Fruit Stand (surplus fruit is now sellable gameplay, not
            // clutter, so it must NOT downgrade once the stand is finished). It still widens
            // further later to include builder snacks (planned follow-up).
            if (info.Type == ItemType.Fruit)
            {
                if (_config != null && !AnyDinoHungry() && !FruitStandFinished &&
                    Random.value < _config.FruitDowngradeFraction)
                {
                    int treasureVariants = Mathf.Max(1, _config.TreasureVariants);
                    return new DugItemInfo(ItemType.Treasure, info.DinoType,
                        Random.Range(0, treasureVariants), info.OriginWorld);
                }

                return info;
            }

            if (info.Type != ItemType.Egg)
            {
                return info;
            }

            if (TryRollUnownedEggSpecies(out Config.DinoType species))
            {
                // Claim this species for the egg we are about to spill so no sibling
                // egg (same batch / later dig / reward) can duplicate it before it
                // hatches. Released on hatch (HatchEgg) or unhatched destroy.
                ReserveEggSpecies(species);
                return new DugItemInfo(ItemType.Egg, species, info.Variant, info.OriginWorld);
            }

            // No UNIQUE egg species is available right now. Two distinct cases:
            //
            //  (a) Every egg species is genuinely OWNED — the egg-shard nerf is in
            //      effect. Feed the nest while it still wants shards; once every shard
            //      species is also owned there is nothing to build, so bank treasure.
            //
            //  (b) Egg species remain unowned in the WORLD but are all RESERVED by
            //      other un-hatched eggs (e.g. a second egg in this same dig batch).
            //      Shards must never drop before every egg species is owned, so we do
            //      NOT leak an early shard here — spill a FRUIT instead. The reserved
            //      species frees up again once its sibling egg hatches or is cleared.
            if (EggSpeciesAllOwned())
            {
                return AnyShardSpeciesUnowned()
                    ? new DugItemInfo(ItemType.Shard, info.DinoType, info.Variant, info.OriginWorld)
                    : new DugItemInfo(ItemType.Treasure, info.DinoType, info.Variant, info.OriginWorld);
            }

            int fruitVariants = _config != null ? Mathf.Max(1, _config.FruitVariants) : 1;
            return new DugItemInfo(ItemType.Fruit, info.DinoType,
                Random.Range(0, fruitVariants), info.OriginWorld);
        }

        /// <summary>Egg-species ownership, keyed strictly off the original four
        /// (DinoType index &lt; 4). Shard-exclusive species (index >= 4) are ignored
        /// here — owning one must never mask an egg species as "owned".</summary>
        private bool[] OwnedEggSpecies()
        {
            var owned = new bool[Config.DinoSpecies.EggHatchableCount];
            for (int i = 0; i < _dinos.Count; i++)
            {
                DinoController d = _dinos[i];
                if (d != null && Config.DinoSpecies.IsEggHatchable(d.Type))
                {
                    owned[(int)d.Type] = true;
                }
            }

            return owned;
        }

        /// <summary>Reserve an egg species so no other egg (this batch, a later dig,
        /// or a reward) can roll it until the reserving egg hatches or is destroyed.</summary>
        private void ReserveEggSpecies(Config.DinoType species)
        {
            _reservedEggSpecies.TryGetValue(species, out int n);
            _reservedEggSpecies[species] = n + 1;
        }

        /// <summary>Release a previously reserved egg species. Idempotent and guarded:
        /// a species that was never reserved (e.g. a direct <see cref="HatchEgg"/> with
        /// no pickup behind it) is a harmless no-op.</summary>
        internal void ReleaseEggSpecies(Config.DinoType species)
        {
            if (!_reservedEggSpecies.TryGetValue(species, out int n))
            {
                return;
            }

            if (n <= 1)
            {
                _reservedEggSpecies.Remove(species);
            }
            else
            {
                _reservedEggSpecies[species] = n - 1;
            }
        }

        private bool IsEggSpeciesReserved(Config.DinoType species) =>
            _reservedEggSpecies.ContainsKey(species);

        /// <summary>Pick a uniformly random egg species that is neither OWNED nor
        /// currently RESERVED by another un-hatched egg. Returns false when every egg
        /// species is spoken for (no unique egg can be given right now).</summary>
        private bool TryRollUnownedEggSpecies(out Config.DinoType species)
        {
            bool[] owned = OwnedEggSpecies();

            // Collect the available (unowned AND unreserved) egg-species indices,
            // then pick one uniformly.
            var unowned = new int[owned.Length];
            int n = 0;
            for (int t = 0; t < owned.Length; t++)
            {
                if (!owned[t] && !IsEggSpeciesReserved((Config.DinoType)t))
                {
                    unowned[n++] = t;
                }
            }

            if (n == 0)
            {
                species = default;
                return false;
            }

            species = (Config.DinoType)unowned[Random.Range(0, n)];
            return true;
        }

        /// <summary>True once every original egg species is owned. Drives the loot
        /// roll's egg-shard nerf (see DigModeController.RollItem).</summary>
        internal bool EggSpeciesAllOwned()
        {
            bool[] owned = OwnedEggSpecies();
            for (int t = 0; t < owned.Length; t++)
            {
                if (!owned[t])
                {
                    return false;
                }
            }

            return true;
        }

        public void HatchEgg(Config.DinoType type, Vector3 pos)
        {
            // Release this egg's reservation and take ownership in one synchronous
            // step: SpawnDino below adds the species to _dinos (now OWNED), so it stays
            // excluded from egg rolls with no gap for a duplicate to slip through. A
            // direct HatchEgg (test / no pickup) reserved nothing — release is a no-op.
            ReleaseEggSpecies(type);

            SpawnConfetti(pos);
            Audio?.Hatch();
            Audio?.Roar();
            GameEvents.RaiseEggHatched(type);
            // Buddy if a slot is free; otherwise a meadow resident that trots home
            // once the hatch celebration has had its moment (delayed home walk).
            SpawnDino(type, GrowthStage.Baby, 0, pos, persist: true,
                wantsBuddy: true, delayResidentWalk: true);
        }

        private DinoController SpawnDino(Config.DinoType type, GrowthStage stage, int fruitEaten,
            Vector3 pos, bool persist, bool wantsBuddy = true, bool delayResidentWalk = false)
        {
            DinoDefinition def = _config != null ? _config.GetDino(type) : null;

            var go = new GameObject($"Dino_{type}");
            go.transform.SetParent(_overworldRoot, false);
            go.transform.position = pos;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 15;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.6f;
            col.isTrigger = true;

            var dino = go.AddComponent<DinoController>();
            ParticleSystem hearts = CreateParticles(go.transform, _library != null ? _library.HeartParticle : null,
                new Color(1f, 0.4f, 0.6f), 0.35f);
            ParticleSystem poof = CreateParticles(go.transform, _library != null ? _library.StarParticle : null,
                Color.white, 0.4f);
            dino.AttachParticles(sr, hearts, poof);

            _dinos.Add(dino);
            dino.Init(def, _config, _backhoe != null ? _backhoe.transform : null,
                SlotOffset(0), stage, fruitEaten);
            dino.ConfigureWorld(_map, _meadow);

            // Role assignment: buddy while a slot is free, meadow resident otherwise.
            if (wantsBuddy && CountBuddies() < BuddyCap)
            {
                AddBuddy(dino);
            }
            else
            {
                dino.BecomeResident(delayResidentWalk);
            }

            Tween.PunchScale(dino.transform, 0.5f, 0.4f);

            if (persist)
            {
                SaveNow();
            }

            return dino;
        }

        private Vector2 SlotOffset(int index)
        {
            // Ring of offset slots behind/around the backhoe so dinos don't stack.
            float radius = 1.4f + (index / 8) * 0.9f;
            float angle = (index % 8) * (Mathf.PI * 2f / 8f) + Mathf.PI; // start behind
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.6f) * radius;
        }

        // ------------------------------------------------------- walk buddies

        private int CountBuddies()
        {
            PruneBuddies();
            return _buddies.Count;
        }

        private void PruneBuddies()
        {
            for (int i = _buddies.Count - 1; i >= 0; i--)
            {
                if (_buddies[i] == null)
                {
                    _buddies.RemoveAt(i);
                }
            }
        }

        private void AddBuddy(DinoController dino)
        {
            _buddies.Add(dino);
            RefreshBuddySlots();
            dino.BecomeBuddy(SlotOffset(_buddies.Count - 1));
        }

        private void RefreshBuddySlots()
        {
            PruneBuddies();
            for (int i = 0; i < _buddies.Count; i++)
            {
                _buddies[i].SetSlot(SlotOffset(i));
            }
        }

        private DinoController FindBuddy(Config.DinoType type)
        {
            for (int i = 0; i < _buddies.Count; i++)
            {
                DinoController b = _buddies[i];
                if (b != null && b.Type == type)
                {
                    return b;
                }
            }

            return null;
        }

        /// <summary>Tap-to-swap: any tapped dino dances (DinoController does that);
        /// if it is not a buddy it also joins the walk, bumping the LONGEST-SERVING
        /// buddy, who happily trots back to the meadow.</summary>
        public void NotifyDinoTapped(DinoController dino)
        {
            if (dino == null || _paradeActive)
            {
                return;
            }

            // Tap-to-join during the hatch ceremony: promoting the new baby also ends
            // the ceremony early (camera eases back to the backhoe).
            bool wasCeremonyDino = _ceremonyActive && dino == _ceremonyDino;

            PruneBuddies();
            if (_buddies.Contains(dino))
            {
                return; // already a buddy: the dance is the whole reaction
            }

            if (_buddies.Count >= BuddyCap)
            {
                DinoController oldest = _buddies[0];
                _buddies.RemoveAt(0);
                if (oldest != null)
                {
                    oldest.BecomeResident();
                }
            }

            _buddies.Add(dino);
            RefreshBuddySlots();
            dino.BecomeBuddy(SlotOffset(_buddies.Count - 1));
            SaveNow();

            if (wasCeremonyDino)
            {
                EndCeremony();
            }
        }

        private Vector3 DinoSpawnPos()
        {
            Vector3 b = _backhoe != null ? _backhoe.transform.position : Vector3.zero;
            Vector2 r = Random.insideUnitCircle * 1.5f;
            return b + new Vector3(r.x, r.y, 0f);
        }

        // ------------------------------------------------------------- feeding

        public void RequestFeed(ItemPickup fruit)
        {
            if (fruit == null || fruit.IsConsumed)
            {
                return;
            }

            DinoController dino = NearestHungryDino(fruit.transform.position);
            if (dino == null)
            {
                // Feed priority is absolute — a hungry dino always wins. Only once NOBODY is
                // hungry does a finished Fruit Stand buy the surplus fruit; otherwise the
                // fruit just bounced for feedback and waits to be eaten later.
                if (FruitStandFinished)
                {
                    TrySellFruit(fruit);
                }

                return;
            }

            Vector3 fruitPos = fruit.transform.position;
            dino.GoEat(fruitPos, () =>
            {
                if (fruit == null || fruit.IsConsumed || fruit.IsCarried)
                {
                    return; // gone, eaten, or riding on the courier's head by now
                }

                Audio?.Eat();
                fruit.ConsumeAsFood();
                GameEvents.RaiseFruitEaten();

                GrowthStage? grew = dino.Feed();
                if (grew.HasValue)
                {
                    Audio?.Grow();
                    GameEvents.RaiseDinoGrew(dino.Type, grew.Value);
                }

                SaveNow();
            });
        }

        /// <summary>True while at least one dino in the scene still wants fruit. Gates the
        /// fruit-&gt;treasure downgrade so drops only convert once fruit demand is exhausted.</summary>
        private bool AnyDinoHungry()
        {
            for (int i = 0; i < _dinos.Count; i++)
            {
                DinoController d = _dinos[i];
                if (d != null && d.IsHungry)
                {
                    return true;
                }
            }

            return false;
        }

        private DinoController NearestHungryDino(Vector3 pos)
        {
            DinoController best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < _dinos.Count; i++)
            {
                DinoController d = _dinos[i];
                if (d == null || !d.IsHungry || d.IsCarrying)
                {
                    continue; // a courier mid-run keeps its fruit on its head
                }

                float sq = (d.transform.position - pos).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = d;
                }
            }

            return best;
        }

        // ------------------------------------------------- species superpowers

        /// <summary>Brachiosaurus tree shake. The tree ALWAYS gives a little leaf
        /// rustle so the tap does something; fruit drops only when a buddy Brachio
        /// is close enough, walks over and neck-sways — and the tree is off its
        /// per-tree cooldown.</summary>
        private void OnTreeTapped(Vector3Int cell)
        {
            Vector3 treeWorld = _map != null ? _map.CellCenter(cell) : Vector3.zero;
            GameEvents.RaiseTreeTapped(cell);
            LeafRustle(treeWorld);

            DinoController brachio = FindBuddy(Config.DinoType.Brachiosaurus);
            if (brachio == null || brachio.IsBusy)
            {
                return;
            }

            if ((brachio.transform.position - treeWorld).sqrMagnitude > TreeShakeRange * TreeShakeRange)
            {
                return;
            }

            if (_treeCooldownUntil.TryGetValue(cell, out float until) && Time.time < until)
            {
                return; // tree is resting; the leaf rustle already played
            }

            _treeCooldownUntil[cell] = Time.time + TreeCooldownSeconds;

            Vector3 approach = treeWorld + new Vector3(0f, -0.7f, 0f);
            if (_map != null)
            {
                approach = _map.NearestWalkable(approach, out _);
            }

            brachio.WalkTo(approach, 1.2f, () =>
            {
                if (brachio == null)
                {
                    return;
                }

                brachio.Dance(); // Brachio's dance is the neck sway
                Tween.After(0.45f, () => DropTreeFruit(treeWorld));
            });
        }

        private void LeafRustle(Vector3 treeWorld)
        {
            ParticleSystem ps = CreateParticles(_overworldRoot,
                _library != null ? _library.StarParticle : null,
                new Color(0.45f, 0.75f, 0.35f), 0.22f);
            if (ps == null)
            {
                return;
            }

            ps.transform.position = treeWorld + new Vector3(0f, 0.55f, 0f);
            ps.Emit(7);
            Tween.After(1.5f, () =>
            {
                if (ps != null)
                {
                    Destroy(ps.gameObject);
                }
            });
        }

        private void DropTreeFruit(Vector3 treeWorld)
        {
            int count = Random.Range(1, 3); // 1-2 fruit in happy arcs
            Vector3 canopy = treeWorld + new Vector3(0f, 0.9f, 0f);
            int variants = _config != null ? Mathf.Max(1, _config.FruitVariants) : 1;

            for (int i = 0; i < count; i++)
            {
                float ang = Random.value * Mathf.PI * 2f;
                Vector3 landing = treeWorld + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang) * 0.7f, 0f) *
                                  Random.Range(0.9f, 1.4f);
                if (_map != null)
                {
                    landing = _map.NearestWalkable(landing, out _);
                }

                // Same spawn path as dug items: arc out of the canopy, land, bob.
                var info = new DugItemInfo(ItemType.Fruit, Config.DinoType.TRex,
                    Random.Range(0, variants), canopy);
                CreatePickup(info, landing);
            }

            Audio?.ItemPop();
        }

        /// <summary>Stegosaurus sniffer: while a buddy Stego roams, every few
        /// seconds it points a little star-sparkle trail toward the nearest active
        /// mound with a soft chime. Ambient — no UI.</summary>
        private void TickSniffer(float dt)
        {
            if (State == null || !State.Is(GameState.Roam) || _paradeActive)
            {
                return;
            }

            _snifferTimer -= dt;
            if (_snifferTimer > 0f)
            {
                return;
            }

            _snifferTimer = SnifferIntervalSeconds;

            DinoController stego = FindBuddy(Config.DinoType.Stegosaurus);
            if (stego == null || stego.IsBusy)
            {
                return;
            }

            DigMound mound = NearestActiveMound(stego.transform.position);
            if (mound == null)
            {
                return;
            }

            stego.EmitDirectedSparkles(mound.transform.position - stego.transform.position);
            Audio?.Chime();
            _snifferPulses++;
        }

        /// <summary>Triceratops fruit courier: a buddy Trike fetches any loose
        /// fruit sitting far from the backhoe and sets it down close by, one fruit
        /// at a time, then falls back in line.</summary>
        private void TickCourier(float dt)
        {
            if (State == null || !State.Is(GameState.Roam) || _paradeActive)
            {
                return;
            }

            if (_courier != null)
            {
                WatchActiveCarry();
                return;
            }

            _courierScanTimer -= dt;
            if (_courierScanTimer > 0f)
            {
                return;
            }

            _courierScanTimer = CourierScanSeconds;

            DinoController trike = FindBuddy(Config.DinoType.Triceratops);
            if (trike == null || trike.IsBusy)
            {
                return;
            }

            ItemPickup fruit = FindFarLooseFruit();
            if (fruit == null)
            {
                return;
            }

            BeginCarry(trike, fruit);
        }

        private ItemPickup FindFarLooseFruit()
        {
            Vector3 bp = _backhoe != null ? _backhoe.transform.position : Vector3.zero;
            float minSq = CourierMinFruitDist * CourierMinFruitDist;

            for (int i = _pickups.Count - 1; i >= 0; i--)
            {
                ItemPickup p = _pickups[i];
                if (p == null)
                {
                    _pickups.RemoveAt(i); // prune destroyed pickups as we scan
                    continue;
                }

                if (p.IsCarryableFruit && (p.transform.position - bp).sqrMagnitude > minSq)
                {
                    return p;
                }
            }

            return null;
        }

        private void BeginCarry(DinoController trike, ItemPickup fruit)
        {
            _courier = trike;
            _carriedFruit = fruit;

            trike.WalkTo(fruit.transform.position, 1.1f, () =>
            {
                if (trike == null || fruit == null || !fruit.IsCarryableFruit)
                {
                    EndCarryRun(); // fruit got eaten / tapped away while walking over
                    return;
                }

                fruit.BeginCarried();
                trike.AttachCarried(fruit.transform);
                Tween.PunchScale(trike.transform, 0.2f, 0.25f);

                Vector3 bp = _backhoe != null ? _backhoe.transform.position : trike.transform.position;
                Vector3 dir = trike.transform.position - bp;
                dir.z = 0f;
                dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.right;
                Vector3 drop = bp + dir * CourierDropDist;
                if (_map != null)
                {
                    drop = _map.NearestWalkable(drop, out _);
                }

                trike.WalkTo(drop, 1.1f, () => SetDownCarriedFruit());
            });
        }

        private void SetDownCarriedFruit()
        {
            if (_courier != null)
            {
                _courier.DetachCarried();
            }

            if (_carriedFruit != null)
            {
                // Compute the rest spot from the CURRENT backhoe position so the
                // fruit reliably ends ~CourierDropDist away (no double cell-snap
                // drift). Only fall back to a walkable search if the exact point
                // is blocked (rare right next to the backhoe).
                Vector3 rest;
                if (_backhoe != null)
                {
                    Vector3 bp = _backhoe.transform.position;
                    Vector3 dir = (_courier != null ? _courier.transform.position : _carriedFruit.transform.position) - bp;
                    dir.z = 0f;
                    dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.right;
                    rest = bp + dir * CourierDropDist;
                }
                else
                {
                    rest = _carriedFruit.transform.position;
                }

                rest.z = 0f;
                if (_map != null && !_map.IsWalkableWorld(rest))
                {
                    rest = _map.NearestWalkable(rest, out _);
                }

                _carriedFruit.EndCarried(rest);
                Audio?.Chime();
            }

            EndCarryRun();
        }

        /// <summary>Watchdog for an in-flight carry: if something interrupted the
        /// courier's scripted walk (a tap-dance, an eat call, the fruit dying) the
        /// chained callbacks never fire — recover instead of wedging the power.</summary>
        private void WatchActiveCarry()
        {
            if (_courier == null)
            {
                // Courier destroyed: free any orphaned fruit where it fell.
                if (_carriedFruit != null && _carriedFruit.IsCarried)
                {
                    _carriedFruit.transform.SetParent(null, true);
                    _carriedFruit.EndCarried(_carriedFruit.transform.position);
                }

                EndCarryRun();
                return;
            }

            if (_carriedFruit == null)
            {
                _courier.DetachCarried();
                EndCarryRun();
                return;
            }

            if (!_courier.IsTraveling)
            {
                // Walk was interrupted mid-run. If the fruit is on its head, set it
                // down right here; either way the run is over.
                if (_courier.IsCarrying)
                {
                    SetDownCarriedFruit();
                }
                else if (!_courier.IsBusy)
                {
                    EndCarryRun();
                }
            }
        }

        private void EndCarryRun()
        {
            _courier = null;
            _carriedFruit = null;
        }

        // ---------------------------------------------------------- fruit stand
        // Surplus-fruit sink: once the Fruit Stand (building index
        // GameConfig.FruitStandIndex) is finished, tapping a loose fruit that no dino wants
        // sells it. A free NON-buddy resident hauls it to the stand and it banks as a coin
        // (every 5th sale a gem); if no resident is free the fruit flies to the stand and
        // sells itself, so a toddler's tap ALWAYS produces something. Reuses the Trike
        // courier's carry primitives (ItemPickup.BeginCarried, DinoController.AttachCarried)
        // and the treasure arc/counter (SpawnRewardPickup -> CollectTreasure).

        /// <summary>True once the Fruit Stand has finished building (its plot is open for
        /// business). Gates both the sell flow and the glut-guard widening.</summary>
        private bool FruitStandFinished =>
            _town != null && _town.IsBuildingFinished(Config.GameConfig.FruitStandIndex);

        /// <summary>Sell one surplus fruit at the stand. A free resident carries it there;
        /// with no resident free the fruit arcs to the stand and sells itself (never a
        /// dead-end tap). Callers guarantee nobody is hungry and the stand is finished.</summary>
        private void TrySellFruit(ItemPickup fruit)
        {
            if (fruit == null || fruit.IsConsumed || fruit.IsCarried)
            {
                return;
            }

            Vector3 stand = _town.BuildingWorld(Config.GameConfig.FruitStandIndex);
            DinoController seller = AcquireFreeSeller(fruit.transform.position);
            if (seller == null)
            {
                SellFruitDirect(fruit, stand); // fallback: the fruit flies itself to the stand
                return;
            }

            BeginSellRun(seller, fruit, stand);
        }

        /// <summary>Nearest NON-buddy resident free to run a sale: not a buddy, not the
        /// ceremony baby, not already selling, and not busy — <see cref="DinoController.IsBusy"/>
        /// excludes eating, dancing, parading, AND any resident currently WORKING or COMMUTING
        /// to a build site, so a builder is never poached mid-site. Returns null when nobody
        /// is free (the caller then falls back to a self-selling fruit).</summary>
        private DinoController AcquireFreeSeller(Vector3 near)
        {
            DinoController best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < _dinos.Count; i++)
            {
                DinoController d = _dinos[i];
                if (d == null || d.IsBuddy || d.IsBusy || d == _ceremonyDino)
                {
                    continue;
                }

                if (_buddies.Contains(d) || _sellers.Contains(d))
                {
                    continue;
                }

                float sq = (d.transform.position - near).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = d;
                }
            }

            return best;
        }

        /// <summary>Send a resident to the fruit, hoist it onto its head, carry it to the
        /// stand, and bank the sale on arrival. Mirrors the Trike courier's carry chain; the
        /// per-frame <see cref="TickSellers"/> watchdog recovers a run whose walk got
        /// interrupted (e.g. the seller tapped into a buddy).</summary>
        private void BeginSellRun(DinoController seller, ItemPickup fruit, Vector3 stand)
        {
            _sellers.Add(seller);

            seller.WalkTo(fruit.transform.position, SellerCommuteSpeed, () =>
            {
                if (seller == null || fruit == null || !fruit.IsCarryableFruit)
                {
                    _sellers.Remove(seller); // fruit eaten / tapped away before pickup
                    return;
                }

                fruit.BeginCarried();
                seller.AttachCarried(fruit.transform);
                Tween.PunchScale(seller.transform, 0.2f, 0.25f);

                Vector3 drop = StandApproach(stand);
                seller.WalkTo(drop, SellerCommuteSpeed, () => CompleteSale(seller, fruit, stand));
            });
        }

        /// <summary>Seller reached the stand: set the fruit down off its head, then convert
        /// it to a coin/gem that arcs to the counter. The resident resumes its role on its
        /// own (it was never a buddy).</summary>
        private void CompleteSale(DinoController seller, ItemPickup fruit, Vector3 stand)
        {
            _sellers.Remove(seller);
            seller?.DetachCarried();
            BankFruitSale(fruit, stand);
        }

        /// <summary>Fallback with no resident free: the fruit itself arcs to the stand and
        /// sells on arrival, so the tap still pays out. Locked as "carried" during the flight
        /// so it stops bobbing/tapping and the Trike courier won't grab it mid-air.</summary>
        private void SellFruitDirect(ItemPickup fruit, Vector3 stand)
        {
            if (fruit == null)
            {
                return;
            }

            fruit.BeginCarried();
            Tween.MoveArc(fruit.transform, fruit.transform.position, stand, 1.2f, 0.55f,
                () => BankFruitSale(fruit, stand));
        }

        /// <summary>Consume the sold fruit and pay out at the stand: pop a coin — or, every
        /// <see cref="FruitStandGemEverySale"/>th sale, a jackpot gem — that flies to the
        /// treasure counter through the SAME reward/collect path as any treasure, so the
        /// denomination value and the counter pop just work. Shared by both sell paths.</summary>
        private void BankFruitSale(ItemPickup fruit, Vector3 stand)
        {
            if (fruit != null)
            {
                _pickups.Remove(fruit);
                Destroy(fruit.gameObject);
            }

            _fruitSalesCount++;
            bool jackpot = (_fruitSalesCount % FruitStandGemEverySale) == 0;
            int variant = jackpot ? FruitStandGemVariant : FruitStandCoinVariant;

            // A treasure reward pops at the stand and auto-collects to the corner counter
            // (ItemPickup.OnLanded -> CollectTreasure), banking TreasureValue(variant).
            SpawnRewardPickup(ItemType.Treasure, Config.DinoType.TRex, variant, stand);
            Audio?.Chime();
        }

        /// <summary>A walkable drop-off point just in front of the stand plot, so the seller
        /// stands beside the building rather than on top of it.</summary>
        private Vector3 StandApproach(Vector3 stand)
        {
            Vector3 front = stand + new Vector3(0f, -0.6f, 0f);
            if (_map != null)
            {
                front = _map.NearestWalkable(front, out _);
            }

            return front;
        }

        /// <summary>Watchdog for in-flight sell runs: a seller that got tap-promoted to a
        /// buddy (or destroyed) is released from the run, and any fruit still on its head is
        /// set back down where it stands so it never rides off stranded.</summary>
        private void TickSellers()
        {
            for (int i = _sellers.Count - 1; i >= 0; i--)
            {
                DinoController s = _sellers[i];
                if (s == null)
                {
                    _sellers.RemoveAt(i);
                    continue;
                }

                if (s.IsBuddy)
                {
                    Transform t = s.DetachCarried();
                    if (t != null)
                    {
                        var pk = t.GetComponent<ItemPickup>();
                        pk?.EndCarried(t.position);
                    }

                    _sellers.RemoveAt(i);
                }
            }
        }

        // ------------------------------------------------------ milestone parade

        private void OnDinoGrew(Config.DinoType type, GrowthStage stage)
        {
            TryStartParade();
        }

        /// <summary>Once-ever celebration: the first time every one of the four
        /// species is owned AND grown Big, confetti bursts and the whole family
        /// (buddies + residents) parades a loop around the backhoe, then everyone
        /// returns to their normal spots. Persisted via SaveData.ParadeDone.</summary>
        private void TryStartParade()
        {
            if (_paradeActive || Save == null || Save.Data.ParadeDone)
            {
                return;
            }

            if (!AllFourSpeciesBig())
            {
                return;
            }

            _paradeActive = true;
            Save.Data.ParadeDone = true;
            SaveNow(); // flag lands on disk immediately — the parade can never repeat

            Vector3 center = _backhoe != null ? _backhoe.transform.position : Vector3.zero;
            SpawnConfetti(center + new Vector3(0f, 0.6f, 0f));
            Audio?.Grow();
            Audio?.Hatch();
            GameEvents.RaiseParadeStarted();

            int marching = 0;
            for (int i = 0; i < _dinos.Count; i++)
            {
                if (_dinos[i] != null)
                {
                    marching++;
                }
            }

            int k = 0;
            for (int i = 0; i < _dinos.Count; i++)
            {
                DinoController d = _dinos[i];
                if (d == null)
                {
                    continue;
                }

                float phase = marching > 0 ? (k / (float)marching) * Mathf.PI * 2f : 0f;
                d.StartParade(center, phase, ParadeSeconds);
                k++;
            }

            Tween.After(ParadeSeconds + 0.6f, () =>
            {
                _paradeActive = false; // dinos resume their roles on their own
            });
        }

        private bool AllFourSpeciesBig()
        {
            // "All four egg species exist and all are Big": every ORIGINAL species
            // has a BIG specimen. Shard-exclusive species (index >= 4) don't gate the
            // parade, so they're ignored here rather than folded in via a modulo.
            var bigByType = new bool[Config.DinoSpecies.EggHatchableCount];
            for (int i = 0; i < _dinos.Count; i++)
            {
                DinoController d = _dinos[i];
                if (d != null && d.IsBig && Config.DinoSpecies.IsEggHatchable(d.Type))
                {
                    bigByType[(int)d.Type] = true;
                }
            }

            for (int t = 0; t < bigByType.Length; t++)
            {
                if (!bigByType[t])
                {
                    return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------ treasure

        public void CollectTreasure(ItemPickup treasure)
        {
            if (treasure == null)
            {
                return;
            }

            Vector3 target = _treasureCounter != null
                ? _treasureCounter.GetWorldTarget(_mainCamera)
                : treasure.transform.position + Vector3.up * 3f;

            // Denominations: each treasure variant banks its configured value (coin=1,
            // gem=3, boot=1, bone=2), clamped so an odd variant safely banks 1.
            int value = _config != null ? _config.TreasureValue(treasure.Variant) : 1;

            Tween.MoveArc(treasure.transform, treasure.transform.position, target, 1.2f, 0.6f, () =>
            {
                Save.Data.TreasureCount += value;
                Audio?.Treasure();
                GameEvents.RaiseTreasureCollected(Save.Data.TreasureCount);
                SaveNow();
                if (treasure != null)
                {
                    Destroy(treasure.gameObject);
                }
            });
        }

        // ------------------------------------------------------------ dino town
        // Hooks used by TownController. Money and the builder POOL both flow through
        // here so there is a single source of truth — and so the town can only ever
        // reach NON-buddy residents. There is deliberately NO hook that hands out the
        // backhoe or a buddy: the player character can never be drafted to build.

        /// <summary>The town wallet: the banked treasure count.</summary>
        internal int TownWallet => Save != null ? Save.Data.TreasureCount : 0;

        /// <summary>Spend a building's price from the wallet if affordable. On success the
        /// save is written and the corner counter refreshed; returns false when broke.
        /// The ONLY path by which the town consumes coins.</summary>
        internal bool TownTrySpend(int amount)
        {
            if (Save == null || amount < 0 || Save.Data.TreasureCount < amount)
            {
                return false;
            }

            Save.Data.TreasureCount -= amount;
            _treasureCounter?.SetCount(Save.Data.TreasureCount);
            SaveNow();
            return true;
        }

        /// <summary>Up to <paramref name="max"/> dinos eligible to build: NON-buddy meadow
        /// residents that are not already working and not the ceremony baby. Buddies, the
        /// ceremony dino, and (by construction — it is not a DinoController) the player
        /// backhoe are all excluded. This is the structural guarantee behind the hard rule
        /// that town construction is 100% NPC and never commandeers the player or a buddy.</summary>
        internal List<DinoController> TownAcquireBuilders(int max)
        {
            var result = new List<DinoController>();
            if (max <= 0)
            {
                return result;
            }

            for (int i = 0; i < _dinos.Count; i++)
            {
                DinoController d = _dinos[i];
                if (d == null || d.IsBuddy || d.IsWorking || d == _ceremonyDino)
                {
                    continue;
                }

                if (_buddies.Contains(d) || _sellers.Contains(d))
                {
                    continue; // a resident mid-sale is committed — never draft it to build
                }

                result.Add(d);
                if (result.Count >= max)
                {
                    break;
                }
            }

            return result;
        }

        /// <summary>The town's build state changed (a site broke ground, advanced a state,
        /// or finished): write it to disk. Routes through <see cref="SaveNow"/> so the town's
        /// per-building progress is persisted alongside the rest of the save.</summary>
        internal void TownPersist() => SaveNow();

        /// <summary>Reuse the shared confetti burst for a building completion.</summary>
        internal void TownSpawnConfetti(Vector3 pos) => SpawnConfetti(pos);

        /// <summary>Reuse the shared particle-system factory for a build site's dust/crumbs.</summary>
        internal ParticleSystem TownCreateParticles(Transform parent, Sprite sprite, Color color, float size) =>
            CreateParticles(parent, sprite, color, size);

        // -------------------------------------------------------------- shards

        /// <summary>An egg shard was dug up: fly it to the nest (or a graceful
        /// fallback while no nest exists), bank it in <see cref="SaveData.ShardCount"/>,
        /// and announce it via <see cref="GameEvents.ShardCollected"/>. The nest visual
        /// itself lands in a later pass (bl6.4); this only emits the event + routes the
        /// flight, so it degrades cleanly on a scene with no nest yet.</summary>
        public void CollectShard(ItemPickup shard)
        {
            if (shard == null)
            {
                return;
            }

            Vector3 target = ResolveShardTarget(shard.transform.position);

            Tween.MoveArc(shard.transform, shard.transform.position, target, 1.2f, 0.6f, () =>
            {
                Save.Data.ShardCount++;
                Audio?.Chime();
                GameEvents.RaiseShardCollected(Save.Data.ShardCount); // nest advances its assembly sprite
                SaveNow();
                if (shard != null)
                {
                    Destroy(shard.gameObject);
                }

                // A full nest hatches a new shard-exclusive species (if any remain).
                TryBeginCeremony();
            });
        }

        /// <summary>Where a dug shard flies: the nest provider if one is registered
        /// (bl6.4), else the meadow center (the nest's future home), else the treasure
        /// counter corner, else straight up. Never throws on a partially-wired scene.</summary>
        private Vector3 ResolveShardTarget(Vector3 from)
        {
            if (GameEvents.NestTargetProvider != null)
            {
                Vector3? nest = GameEvents.NestTargetProvider();
                if (nest.HasValue)
                {
                    return nest.Value;
                }
            }

            if (_meadow != null)
            {
                return _meadow.Center;
            }

            if (_treasureCounter != null)
            {
                return _treasureCounter.GetWorldTarget(_mainCamera);
            }

            return from + Vector3.up * 3f;
        }

        // --------------------------------------------------- shard-hatch ceremony

        /// <summary>The nest reached <see cref="ShardsPerHatch"/>: if a shard-exclusive
        /// species is still unowned, run the hatch ceremony (camera to the nest, egg
        /// wobble + crack + confetti, a new baby that waits to be tapped). Guarded so
        /// it can never re-enter or fire when the roster is complete.</summary>
        private void TryBeginCeremony()
        {
            if (_ceremonyActive || Save == null || State == null)
            {
                return;
            }

            if (Save.Data.ShardCount < ShardsPerHatch)
            {
                return;
            }

            if (!TryRollUnownedShardSpecies(out Config.DinoType species))
            {
                return; // roster complete — nothing left to hatch (shards already stopped dropping)
            }

            _ceremonyActive = true;
            State.Set(GameState.Ceremony); // blocks dig entry + backhoe move during the zoom

            Vector3 nestPos = NestFocusPoint();
            if (_cameraFollow != null)
            {
                _cameraFollow.EnterFocus(nestPos, () => PlayCeremonyHatch(species, nestPos));
            }
            else
            {
                PlayCeremonyHatch(species, nestPos);
            }
        }

        private void PlayCeremonyHatch(Config.DinoType species, Vector3 nestPos)
        {
            Audio?.Hatch();
            SpawnConfetti(nestPos + new Vector3(0f, 0.4f, 0f));

            if (_nest != null)
            {
                _nest.PlayHatch(() => FinishCeremonyHatch(species, nestPos));
            }
            else
            {
                FinishCeremonyHatch(species, nestPos);
            }
        }

        private void FinishCeremonyHatch(Config.DinoType species, Vector3 nestPos)
        {
            Audio?.Roar();

            // Consume one nest's worth of shards, keeping any remainder toward the next.
            // Capture the requirement BEFORE spawning the new dino below: the species is
            // not yet owned here, so ShardsPerHatch still reflects the egg hatching NOW.
            if (Save != null)
            {
                int req = ShardsPerHatch;
                Save.Data.ShardCount = Mathf.Max(0, Save.Data.ShardCount - req);
            }

            // Spawn AT the nest, Baby stage, forced RESIDENT: it waits in the meadow
            // until tapped (the tap promotes it to buddy via NotifyDinoTapped). Use the
            // meadow's nest cell center so the baby lands squarely inside the interior.
            Vector3 spawnPos = _meadow != null ? _meadow.NestWorld : nestPos;
            _ceremonyDino = SpawnDino(species, GrowthStage.Baby, 0, spawnPos, persist: true, wantsBuddy: false);
            _ceremonyDino?.Dance();

            GameEvents.RaiseEggHatched(species);
            _nest?.RefreshAssembly(Save != null ? Save.Data.ShardCount : 0);

            // Ease back to the backhoe after a few beats (a tap on the baby ends it early).
            Tween.After(CeremonyLingerSeconds, EndCeremony);
        }

        /// <summary>Close out the ceremony: ease the camera back to the backhoe and
        /// return to Roam. Idempotent — the timer and a tap-to-join can both call it.</summary>
        private void EndCeremony()
        {
            if (!_ceremonyActive)
            {
                return;
            }

            _ceremonyActive = false;
            _ceremonyDino = null;

            if (_cameraFollow != null)
            {
                _cameraFollow.ExitFocus(() =>
                {
                    if (State != null && State.Is(GameState.Ceremony))
                    {
                        State.Set(GameState.Roam);
                    }
                });
            }
            else if (State != null)
            {
                State.Set(GameState.Roam);
            }
        }

        /// <summary>Nest focus point for the camera + hatch spawn: the registered nest
        /// target, else the meadow's nest corner, else the backhoe.</summary>
        private Vector3 NestFocusPoint()
        {
            if (GameEvents.NestTargetProvider != null)
            {
                Vector3? n = GameEvents.NestTargetProvider();
                if (n.HasValue)
                {
                    return n.Value;
                }
            }

            if (_meadow != null)
            {
                return _meadow.NestWorld;
            }

            return _backhoe != null ? _backhoe.transform.position : Vector3.zero;
        }

        /// <summary>Ownership of the five shard-exclusive species (DinoType 4-8),
        /// indexed 0-based from the first shard species.</summary>
        private bool[] OwnedShardSpecies()
        {
            int count = Config.DinoSpecies.TotalCount - Config.DinoSpecies.EggHatchableCount;
            var owned = new bool[count];
            for (int i = 0; i < _dinos.Count; i++)
            {
                DinoController d = _dinos[i];
                if (d != null && !Config.DinoSpecies.IsEggHatchable(d.Type))
                {
                    owned[(int)d.Type - Config.DinoSpecies.EggHatchableCount] = true;
                }
            }

            return owned;
        }

        /// <summary>How many shard-exclusive species are owned == shard eggs hatched.</summary>
        private int CountOwnedShardSpecies()
        {
            bool[] owned = OwnedShardSpecies();
            int n = 0;
            for (int i = 0; i < owned.Length; i++)
            {
                if (owned[i])
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>Pick a uniformly random shard-exclusive species that is NOT yet
        /// owned. Returns false once all five are owned.</summary>
        private bool TryRollUnownedShardSpecies(out Config.DinoType species)
        {
            bool[] owned = OwnedShardSpecies();
            var unowned = new int[owned.Length];
            int n = 0;
            for (int i = 0; i < owned.Length; i++)
            {
                if (!owned[i])
                {
                    unowned[n++] = i;
                }
            }

            if (n == 0)
            {
                species = default;
                return false;
            }

            int pick = unowned[Random.Range(0, n)];
            species = (Config.DinoType)(Config.DinoSpecies.EggHatchableCount + pick);
            return true;
        }

        /// <summary>True while any shard-exclusive species remains unowned. Gates the
        /// shard drop (see ResolveDugItem) and whether the nest can still hatch.</summary>
        internal bool AnyShardSpeciesUnowned()
        {
            bool[] owned = OwnedShardSpecies();
            for (int i = 0; i < owned.Length; i++)
            {
                if (!owned[i])
                {
                    return true;
                }
            }

            return false;
        }

        // ----------------------------------------------------------- utilities

        private DigMound NearestActiveMound(Vector3 pos)
        {
            DigMound best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < _mounds.Count; i++)
            {
                DigMound m = _mounds[i];
                if (m == null || !m.IsActive)
                {
                    continue;
                }

                float sq = (m.transform.position - pos).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = m;
                }
            }

            return best;
        }

        private void SpawnConfetti(Vector3 pos)
        {
            ParticleSystem ps = CreateParticles(_overworldRoot, _library != null ? _library.StarParticle : null,
                Color.white, 0.4f);
            if (ps != null)
            {
                ps.transform.position = pos;
                var main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(1f, 0.3f, 0.3f), new Color(0.3f, 0.6f, 1f));
                ps.Emit(30);
                Tween.After(2f, () =>
                {
                    if (ps != null)
                    {
                        Destroy(ps.gameObject);
                    }
                });
            }
        }

        private ParticleSystem CreateParticles(Transform parent, Sprite sprite, Color color, float size)
        {
            var go = new GameObject("FX");
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = 0.7f;
            main.startSpeed = 2.5f;
            main.startSize = size;
            main.gravityModifier = 0.6f;
            main.startColor = color;
            main.maxParticles = 128;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.25f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.material = GetParticleMaterial(sprite);
                renderer.sortingOrder = 60;
            }

            return ps;
        }

        private Material GetParticleMaterial(Sprite sprite)
        {
            if (_particleMat == null)
            {
                Shader sh = Shader.Find("Sprites/Default");
                if (sh == null)
                {
                    sh = Shader.Find("Universal Render Pipeline/Unlit");
                }

                _particleMat = sh != null ? new Material(sh) : null;
            }

            // Use a per-call material only if a sprite texture is provided; otherwise
            // the shared white material tints via particle color.
            if (sprite != null && sprite.texture != null && _particleMat != null)
            {
                var mat = new Material(_particleMat);
                mat.mainTexture = sprite.texture;
                return mat;
            }

            return _particleMat;
        }

        private void SaveNow()
        {
            if (Save == null)
            {
                return;
            }

            Save.Data.Version = SaveData.CurrentVersion;
            Save.Data.Dinos.Clear();
            for (int i = 0; i < _dinos.Count; i++)
            {
                DinoController d = _dinos[i];
                if (d == null)
                {
                    continue;
                }

                Save.Data.Dinos.Add(new DinoSave
                {
                    Type = d.Type,
                    Stage = d.Stage,
                    FruitEaten = d.FruitEaten,
                    IsBuddy = d.IsBuddy
                });
            }

            // Capture live Dino Town state (queue index + per-building progress) so every
            // save — treasure, feed, hatch, or a town build event — persists the town too.
            _town?.WriteSave(Save.Data);

            Save.Save();
        }

        // ------------------------------------------------------------ TEST HOOKS
        // Marked internal for the integration test runner (Assets/Scripts/Testing).
        // None of these change behavior for real players; they only expose already
        // existing state / drive already existing flows so tests avoid reflection.

        internal BackhoeController TestBackhoe => _backhoe;
        internal OverworldMap TestMap => _map;
        internal Camera TestCamera => _mainCamera;
        internal GameConfig TestConfig => _config;
        internal DigModeController TestDigMode => _digMode;
        internal InputService TestInput => _input;
        internal TreasureCounter TestTreasureCounter => _treasureCounter;
        internal MuteButton TestMuteButton => _muteButton;
        internal Transform TestOverworldRoot => _overworldRoot;
        internal IReadOnlyList<DigMound> TestMounds => _mounds;
        internal IReadOnlyList<DinoController> TestDinos => _dinos;
        internal IReadOnlyList<DinoController> TestBuddies => _buddies;
        internal MeadowArea TestMeadow => _meadow;
        internal NestController TestNest => _nest;
        internal TownController TestTown => _town;
        internal int TestSnifferPulses => _snifferPulses;
        internal bool TestParadeActive => _paradeActive;
        internal bool TestCeremonyActive => _ceremonyActive;
        internal DinoController TestCeremonyDino => _ceremonyDino;
        internal PlaceholderLibrary TestLibrary => _library;
        internal int TestShardCount => Save != null ? Save.Data.ShardCount : 0;
        internal bool TestEggSpeciesAllOwned => EggSpeciesAllOwned();
        internal bool TestAnyShardSpeciesUnowned => AnyShardSpeciesUnowned();
        internal int TestReservedEggSpeciesCount => _reservedEggSpecies.Count;
        internal bool TestFruitStandFinished => FruitStandFinished;
        internal int TestFruitSalesCount => _fruitSalesCount;
        internal int TestSellerCount => _sellers.Count;

        /// <summary>TEST HOOK. Run the REAL glut-guard/uniqueness resolution on a hand-built
        /// item (no dig site, no pickup). Lets the Fruit Stand case assert that a dug fruit
        /// stops downgrading to treasure once the stand is open. Drops any egg reservation the
        /// resolution just made so repeated sampling stays stationary (mirrors TestRollDugItem).</summary>
        internal DugItemInfo TestResolveItem(DugItemInfo info)
        {
            DugItemInfo resolved = ResolveDugItem(info);
            if (resolved.Type == ItemType.Egg)
            {
                ReleaseEggSpecies(resolved.DinoType);
            }

            return resolved;
        }

        /// <summary>TEST HOOK. Roll one dug item through the REAL pipeline: the dig
        /// site's loot roll (with the owned-species egg-shard nerf) then the uniqueness
        /// + shard resolution FinishDig applies. Lets shard/uniqueness tests sample the
        /// distribution directly instead of grinding whole dig sites.</summary>
        internal DugItemInfo TestRollDugItem()
        {
            DugItemInfo raw = _digMode != null
                ? _digMode.TestRollItemInfo()
                : new DugItemInfo(ItemType.Fruit, Config.DinoType.TRex, 0, Vector3.zero);
            DugItemInfo resolved = ResolveDugItem(raw);

            // Sampling hook: no pickup carries this result, so immediately drop any
            // reservation ResolveDugItem just made. That keeps the roll distribution
            // stationary across repeated sampling calls (no cumulative reservation
            // leak), while real dig batches keep their reservations via live pickups.
            if (resolved.Type == ItemType.Egg)
            {
                ReleaseEggSpecies(resolved.DinoType);
            }

            return resolved;
        }

        /// <summary>TEST HOOK. Resolve a batch of <paramref name="count"/> freshly dug
        /// eggs exactly the way <see cref="FinishDig"/> does — each egg re-rolls to a
        /// unique species and RESERVES it, so later eggs in the batch avoid it. No
        /// pickups are created, so the reservations persist (mirroring a batch of eggs
        /// spilled but not yet hatched) until <see cref="TestReset"/> clears them.</summary>
        internal List<DugItemInfo> TestResolveDugBatch(int count)
        {
            var batch = new List<DugItemInfo>(Mathf.Max(0, count));
            for (int i = 0; i < count; i++)
            {
                var raw = new DugItemInfo(ItemType.Egg, Config.DinoType.TRex, 0, Vector3.zero);
                batch.Add(ResolveDugItem(raw));
            }

            return batch;
        }

        /// <summary>TEST HOOK. Spawn an item pickup that lands at the given world spot.</summary>
        internal ItemPickup TestSpawnItem(ItemType type, Config.DinoType dinoType, int variant, Vector3 landing)
        {
            Vector3 origin = landing + new Vector3(0f, 0.2f, 0f);
            var info = new DugItemInfo(type, dinoType, variant, origin);

            // Tests hand-pick the egg species (no re-roll), but the resulting pickup
            // must still hold the reservation for its lifetime — exactly like a dug
            // egg — so a concurrent dig avoids it. Released on hatch / OnDestroy / reset.
            if (info.Type == ItemType.Egg)
            {
                ReserveEggSpecies(info.DinoType);
            }

            return CreatePickup(info, landing);
        }

        /// <summary>TEST HOOK. Spawn a dino at a given growth stage near the backhoe.
        /// Takes the normal role path: buddy while a slot is free, else resident.</summary>
        internal DinoController TestSpawnDino(Config.DinoType type, GrowthStage stage)
        {
            int fruit = _config != null ? _config.FruitThreshold(stage) : 0;
            return SpawnDino(type, stage, fruit, DinoSpawnPos(), persist: false);
        }

        /// <summary>TEST HOOK. Demote a dino to meadow resident, optionally already
        /// standing inside the meadow (skips the long walk in short test windows).</summary>
        internal void TestMakeResident(DinoController dino, bool teleportIntoMeadow)
        {
            if (dino == null)
            {
                return;
            }

            _buddies.Remove(dino);
            RefreshBuddySlots();
            if (teleportIntoMeadow && _meadow != null)
            {
                dino.transform.position = _meadow.RandomInteriorPoint();
            }

            dino.BecomeResident();
        }

        /// <summary>TEST HOOK. Run the same parade check the DinoGrew event runs.</summary>
        internal void TestTryStartParade()
        {
            TryStartParade();
        }

        /// <summary>TEST HOOK. Inject a town controller when the scene has none wired yet
        /// (the town district is placed by a concurrent ticket). No-op if one already
        /// exists, so a fully-built scene keeps its real town.</summary>
        internal void TestInstallTown(TownController town)
        {
            if (_town == null)
            {
                _town = town;
            }
        }

        /// <summary>TEST HOOK. Route a world-space tap exactly like OnTap does
        /// (collider first, then tree tile, then backhoe move) without needing the
        /// point to be on screen.</summary>
        internal void TestTapWorldRouted(Vector3 world)
        {
            world.z = 0f;
            ITappable tappable = FindTappable(world);
            if (tappable != null)
            {
                tappable.OnTapped(world);
                return;
            }

            if (State.Is(GameState.Roam) && TryRouteTreeTap(world))
            {
                return;
            }

            if (State.Is(GameState.Roam) && _backhoe != null)
            {
                _backhoe.MoveTo(world);
            }
        }

        /// <summary>TEST HOOK. Trigger the idle-attract behavior immediately (same path as the timer).</summary>
        internal void ForceIdleAttract()
        {
            _idleTimer = 0f;
            FireIdleAttract();
        }

        /// <summary>TEST HOOK. Snap back to the roam view (closes any open dig site).</summary>
        internal void TestForceRoam()
        {
            if (_digMode != null && _digMode.IsOpen)
            {
                _digMode.Close();
            }

            _cameraFollow?.TestForceRoam();
            _activeMound = null;
            _ceremonyActive = false;
            _ceremonyDino = null;
            State?.Set(GameState.Roam);
        }

        /// <summary>TEST HOOK. Reset transient world state between cases (dinos,
        /// buddies, pickups, companion timers).</summary>
        internal void TestReset()
        {
            TestForceRoam();

            for (int i = _dinos.Count - 1; i >= 0; i--)
            {
                if (_dinos[i] != null)
                {
                    Destroy(_dinos[i].gameObject);
                }
            }

            _dinos.Clear();
            _buddies.Clear();

            if (_overworldRoot != null)
            {
                ItemPickup[] pickups = _overworldRoot.GetComponentsInChildren<ItemPickup>(true);
                for (int i = 0; i < pickups.Length; i++)
                {
                    if (pickups[i] != null)
                    {
                        Destroy(pickups[i].gameObject);
                    }
                }
            }

            _pickups.Clear();
            // Every reserving egg pickup was just destroyed above; drop any stragglers
            // (batch resolutions with no pickup, sampling leftovers) so a fresh case
            // starts with all egg species available again.
            _reservedEggSpecies.Clear();
            _treeCooldownUntil.Clear();
            _courier = null;
            _carriedFruit = null;
            _sellers.Clear();
            _fruitSalesCount = 0;
            _paradeActive = false;
            _snifferTimer = SnifferIntervalSeconds;
            _snifferPulses = 0;
            _courierScanTimer = 0f;
            _idleTimer = 0f;

            // Town builder: clear any in-progress/finished sites and rewind the queue. The
            // caller owns Save.Data.Town* (a save-state test sets/clears those explicitly);
            // this only wipes the live scene town between cases.
            _town?.TestResetTown();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                SaveNow();
            }
        }

        private void OnApplicationQuit()
        {
            SaveNow();
        }
    }
}
