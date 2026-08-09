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
        [SerializeField] private GardenArea _garden;
        [SerializeField] private List<BerrySprout> _sprouts = new List<BerrySprout>();
        [SerializeField] private MachineFriendController _machines;

        // The fossil finale (DinoDigger-5ve / -3rz). Both are OPTIONAL wires: nothing is
        // pre-placed and neither exists in a scene saved before the finale shipped, so Awake
        // ENSURES them at boot the same way TownController ensures its life service. That
        // keeps one construction path (a test drives what a child sees) and means the shipped
        // scene needs no rebuild.
        [SerializeField] private SkeletonBoard _skeletonBoard;
        [SerializeField] private DinoMaticController _dinoMatic;

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
        private const float CeremonyLingerSeconds = 3f;    // revival ceremony auto-returns after this
        private const float TownTourLingerSeconds = 2.5f;  // idle-attract holds on the town this long

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
        private readonly Dictionary<Vector3Int, float> _rockCooldownUntil = new Dictionary<Vector3Int, float>();
        private int _rockSmashPayouts;         // test-observable smash-payout counter
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

        // ---- Ceremony state (the Dino-Matic revival; formerly the shard hatch) ----
        private bool _ceremonyActive;
        private DinoController _ceremonyDino;   // the freshly revived baby waiting to be tapped

        // ---- Idle-attract town tour (DinoDigger-sbc) ----
        // Once the town has something to show, some idle beats glide the camera over the
        // district, hold a moment on the townsfolk/crew, and glide back. It borrows the nest
        // ceremony's camera machinery (CameraFollow.EnterFocus/ExitFocus) but NOT its game
        // state: the tour never leaves GameState.Roam, so a tap during it drives the backhoe
        // exactly as it always would — the toddler rule, input always wins.
        private bool _townTourActive;
        private Coroutine _townTourLinger;
        private bool _townTourNext = true;   // alternates: not every idle beat is a tour
        private int _townTours;              // test-observable

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

        // ---- Fossil bones + the skeleton board (DinoDigger-0z5 / -5ve) ----
        // Multi-cell bones dug out of the pit bank HERE, not into the treasure wallet: they are
        // the late-game COLLECTION that took over from egg shards once every egg species is
        // owned. The bank is a flat count per (species, bone); EVERYTHING else — which slots
        // the board draws filled, whether a skeleton is complete, what the dig should bury
        // next, what a leftover v4 shard converts into — is derived from these counts through
        // Config.SkeletonPlan, so the picture and the truth cannot drift apart.
        //
        // PERSISTED SINCE SAVE v5 as SaveData.Bones (see RestoreFromSave / SaveNow).
        private readonly Dictionary<int, int> _boneBank = new Dictionary<int, int>();

        // Fossil species already carried through the Dino-Matic. Persisted as
        // SaveData.RevivedSpecies AND re-derived from the live dinos on load, so a species the
        // child can see walking around is always revived whatever the file says.
        private readonly HashSet<Config.DinoType> _revived = new HashSet<Config.DinoType>();

        // How many coins one DUPLICATE bone pays out once every skeleton has been revived.
        // Generous on purpose: at that point a bone is the only thing a dig site still buries,
        // and a reward beat that pays nothing would read as the game breaking.
        private const int DuplicateBoneCoins = 5;

        /// <summary>Total bones banked, across every species and bone. Save-backed, so a
        /// returning player's collection is not "empty" until they dig again.</summary>
        public int BonesBanked { get; private set; }

        /// <summary>True once ANY bone has ever been banked — the state the HUD bone button's
        /// existence is derived from, and the gate that first summons the Dino-Matic.</summary>
        public bool AnyBoneBanked => BonesBanked > 0;

        /// <summary>The last bone banked, as species*<see cref="BoneSpecies.BonesPerSkeleton"/> +
        /// bone index, or -1 before the first one. Cheap breadcrumb for the dig site's own
        /// flourish and for tests; the board reads the counts, not this.</summary>
        public int LastBoneBanked { get; private set; } = -1;

        /// <summary>Bank one uncovered fossil bone against <paramref name="species"/>'s skeleton.
        /// <paramref name="boneIndex"/> is a <see cref="BoneType"/> ordinal — a stable contract,
        /// so a bone banked before the board shipped still names the same slot today.
        ///
        /// DUPLICATES PAY OUT. Once every skeleton has been revived there is nothing left to
        /// collect, so a bone dug after that converts at bank time into a fountain of coins
        /// instead (<paramref name="worldPoint"/> is only used to decide where the fountain
        /// reads from; the coins themselves bank through the normal guarded reward path).
        /// Returns TRUE when the bone went into the collection and false when it paid out —
        /// which is exactly what the dig site needs to know to pick its flourish.</summary>
        public bool BankBone(Config.DinoType species, int boneIndex, Vector3? worldPoint = null)
        {
            if (boneIndex < 0 || boneIndex >= BoneSpecies.BonesPerSkeleton)
            {
                return false; // an unknown bone is dropped rather than corrupting the collection
            }

            if (AllSkeletonsRevived())
            {
                PayDuplicateBone(worldPoint);
                return false;
            }

            int key = BoneKey(species, boneIndex);
            _boneBank.TryGetValue(key, out int had);
            _boneBank[key] = had + 1;
            BonesBanked++;
            LastBoneBanked = key;

            // The first bone ever banked is the Dino-Matic's discovery gate.
            _dinoMatic?.NotifyBoneBanked();

            SaveNow();
            GameEvents.RaiseBoneBanked(species, boneIndex);
            return true;
        }

        /// <summary>A duplicate bone, dug once the whole board is revived: a small coin
        /// fountain, banked one coin at a time through the SAME reward path the pinata pot
        /// uses, so the counter ticks up instead of jumping and the money and the spectacle
        /// can never disagree.</summary>
        private void PayDuplicateBone(Vector3? worldPoint)
        {
            Audio?.ItemPop();
            Vector3 at = worldPoint ?? RewardSpawnPoint;
            SpawnConfetti(at + new Vector3(0f, 0.4f, 0f));

            for (int i = 0; i < DuplicateBoneCoins; i++)
            {
                Tween.After(i * 0.08f, () =>
                {
                    GameManager g = Instance;
                    if (g != null)
                    {
                        g.SpawnRewardPickup(ItemType.Treasure, Config.DinoType.TRex, 0, g.RewardSpawnPoint);
                    }
                });
            }
        }

        /// <summary>How many of <paramref name="species"/>'s <paramref name="boneIndex"/> bone
        /// have been banked.</summary>
        public int BoneCount(Config.DinoType species, int boneIndex)
        {
            return _boneBank.TryGetValue(BoneKey(species, boneIndex), out int n) ? n : 0;
        }

        /// <summary>Bones banked toward one species' skeleton (all bone slots summed).</summary>
        public int BonesForSpecies(Config.DinoType species)
        {
            int n = 0;
            for (int i = 0; i < BoneSpecies.BonesPerSkeleton; i++)
            {
                n += BoneCount(species, i);
            }

            return n;
        }

        /// <summary>True when every slot of <paramref name="species"/>' skeleton is filled —
        /// i.e. the bank holds at least as many of each bone as the skeleton needs. This is the
        /// board's "complete" and the Dino-Matic's "revivable", derived from one place so they
        /// can never mean different things. Always false for a non-fossil species (the
        /// egg-hatchable four have no skeleton).</summary>
        public bool SkeletonComplete(Config.DinoType species)
        {
            if (!SkeletonPlan.IsFossilSpecies(species))
            {
                return false;
            }

            for (int bone = 0; bone < BoneSpecies.BonesPerSkeleton; bone++)
            {
                if (BoneCount(species, bone) < SkeletonPlan.NeedOf(species, bone))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Has this species been brought back by the Dino-Matic (or, for a save
        /// migrated from the nest era, hatched before the machine existed)?</summary>
        public bool IsSpeciesRevived(Config.DinoType species) => _revived.Contains(species);

        /// <summary>True when a completed skeleton is waiting for the machine. The machine's
        /// glow, its harder glint while still buried, and whether a tap runs the ceremony are
        /// all this one predicate.</summary>
        public bool RevivalPending => TryNextRevivable(out _);

        /// <summary>The next species to bring back: the first in the board's fill order whose
        /// skeleton is complete and which has not been revived.</summary>
        private bool TryNextRevivable(out Config.DinoType species)
        {
            for (int i = 0; i < SkeletonPlan.FocusOrder.Length; i++)
            {
                Config.DinoType s = SkeletonPlan.FocusOrder[i];
                if (!_revived.Contains(s) && SkeletonComplete(s))
                {
                    species = s;
                    return true;
                }
            }

            species = default;
            return false;
        }

        /// <summary>The skeleton the dig should be burying bones toward: the first species in
        /// the board's fill order that is still being collected, plus which of its bones is
        /// missing. False once there is nothing left worth burying, and the site then buries no
        /// bone at all — the post-completion answer is NO MORE BONE BURIALS, mirroring the egg
        /// cutover, not an endless stream of things that can only ever be duplicates.
        ///
        /// A species is out of the running once it is complete OR REVIVED, and the revived half
        /// of that test is load-bearing rather than belt-and-braces. A skeleton can be revived
        /// while its bone counts say otherwise — a save migrated from the v4 nest revives every
        /// species the child had already hatched WITHOUT giving it bones it never dug — and
        /// keying only off the counts would aim every future dig at a dinosaur that is already
        /// walking around the meadow. (The bank-time duplicate payout in <see cref="BankBone"/>
        /// stays exactly as it was: it covers extras dug DURING collection, not this.)</summary>
        public bool TryNextNeededBone(out Config.DinoType species, out int boneIndex)
        {
            for (int i = 0; i < SkeletonPlan.FocusOrder.Length; i++)
            {
                Config.DinoType s = SkeletonPlan.FocusOrder[i];
                if (_revived.Contains(s) || SkeletonComplete(s))
                {
                    continue;
                }

                // Among the bones this skeleton still wants, pick one at random so a run of
                // digs does not deal them out in a fixed, predictable order.
                var wanted = new List<int>(BoneSpecies.BonesPerSkeleton);
                for (int bone = 0; bone < BoneSpecies.BonesPerSkeleton; bone++)
                {
                    if (BoneCount(s, bone) < SkeletonPlan.NeedOf(s, bone))
                    {
                        wanted.Add(bone);
                    }
                }

                if (wanted.Count == 0)
                {
                    continue; // can't happen (that IS complete), but never hand back a bad bone
                }

                species = s;
                boneIndex = wanted[Random.Range(0, wanted.Count)];
                return true;
            }

            species = default;
            boneIndex = -1;
            return false;
        }

        /// <summary>True once all five skeletons have been revived: the collection is finished
        /// and further bones are duplicates.</summary>
        public bool AllSkeletonsRevived()
        {
            for (int i = 0; i < SkeletonPlan.Species.Length; i++)
            {
                if (!_revived.Contains(SkeletonPlan.Species[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The bone bank as the serializable rows the save persists. Built on demand
        /// (the bank itself stays a dictionary).</summary>
        public List<BoneSave> BoneBankSnapshot()
        {
            var rows = new List<BoneSave>(_boneBank.Count);
            foreach (KeyValuePair<int, int> kv in _boneBank)
            {
                rows.Add(new BoneSave
                {
                    Species = (Config.DinoType)(kv.Key / BoneSpecies.BonesPerSkeleton),
                    BoneIndex = kv.Key % BoneSpecies.BonesPerSkeleton,
                    Count = kv.Value,
                });
            }

            return rows;
        }

        private static int BoneKey(Config.DinoType species, int boneIndex) =>
            (int)species * BoneSpecies.BonesPerSkeleton + boneIndex;

        /// <summary>Rebuild the live bone bank + revival set from a loaded save (v5). Revival
        /// is the UNION of what the file says and what actually exists in the meadow: a fossil
        /// species walking around must read as revived whatever the file claims, which is what
        /// makes the v4 nest migration lossless without the migration having to be perfect.</summary>
        private void RestoreBoneCollection(SaveData data)
        {
            _boneBank.Clear();
            _revived.Clear();
            BonesBanked = 0;
            LastBoneBanked = -1;

            if (data == null)
            {
                return;
            }

            if (data.Bones != null)
            {
                for (int i = 0; i < data.Bones.Count; i++)
                {
                    BoneSave row = data.Bones[i];
                    if (row == null || row.Count <= 0 ||
                        row.BoneIndex < 0 || row.BoneIndex >= BoneSpecies.BonesPerSkeleton)
                    {
                        continue; // a corrupt row is dropped, never allowed to poison the board
                    }

                    int key = BoneKey(row.Species, row.BoneIndex);
                    _boneBank.TryGetValue(key, out int had);
                    _boneBank[key] = had + row.Count;
                    BonesBanked += row.Count;
                    LastBoneBanked = key;
                }
            }

            if (data.RevivedSpecies != null)
            {
                for (int i = 0; i < data.RevivedSpecies.Count; i++)
                {
                    Config.DinoType s = data.RevivedSpecies[i];
                    if (SkeletonPlan.IsFossilSpecies(s))
                    {
                        _revived.Add(s);
                    }
                }
            }

            if (data.Dinos != null)
            {
                for (int i = 0; i < data.Dinos.Count; i++)
                {
                    DinoSave d = data.Dinos[i];
                    if (d != null && SkeletonPlan.IsFossilSpecies(d.Type))
                    {
                        _revived.Add(d.Type);
                    }
                }
            }
        }

        /// <summary>Write the collection into the save payload (v5).</summary>
        private void WriteBoneCollection(SaveData data)
        {
            if (data == null)
            {
                return;
            }

            data.Bones = BoneBankSnapshot();

            if (data.RevivedSpecies == null)
            {
                data.RevivedSpecies = new List<Config.DinoType>();
            }

            // Belt and braces, matching the restore: a fossil species that is genuinely IN THE
            // WORLD is revived by definition, whatever the set says. A revived dino can then
            // never be lost by a bookkeeping slip in either direction.
            for (int i = 0; i < _dinos.Count; i++)
            {
                DinoController d = _dinos[i];
                if (d != null && SkeletonPlan.IsFossilSpecies(d.Type))
                {
                    _revived.Add(d.Type);
                }
            }

            data.RevivedSpecies.Clear();
            for (int i = 0; i < SkeletonPlan.Species.Length; i++)
            {
                if (_revived.Contains(SkeletonPlan.Species[i]))
                {
                    data.RevivedSpecies.Add(SkeletonPlan.Species[i]);
                }
            }
        }

        /// <summary>Remember which FEATURED toy the dig site just led with (DinoDigger-qhy), so
        /// the next site can refuse to repeat it even across an app restart. Stored index+1 so
        /// the absent-field default on an older save reads as "no history" (see SaveData).</summary>
        internal void SetLastDigPrimaryToy(int index)
        {
            if (Save != null && Save.Data != null)
            {
                Save.Data.LastPrimaryToy = index >= 0 ? index + 1 : 0;
            }
        }

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
            Spawn.SetGarden(_garden);
            Spawn.SetTown(_town);

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

            EnsureSkeletonBoard();
            EnsureDinoMatic();
        }

        /// <summary>Self-heal the skeleton board (DinoDigger-5ve). A scene serialized before the
        /// board existed has no wire for it and nothing else builds one, so it is constructed
        /// here under the existing HUD canvas — the same "nothing is pre-placed, the service
        /// builds it" choice the machine friends made, and the reason this feature needs no
        /// scene rebuild. Idempotent: a wired board (or one already in the scene) wins.</summary>
        private void EnsureSkeletonBoard()
        {
            if (_skeletonBoard != null)
            {
                return;
            }

            _skeletonBoard = FindAnyObjectByType<SkeletonBoard>();
            if (_skeletonBoard != null)
            {
                return;
            }

            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas != null)
            {
                _skeletonBoard = SkeletonBoard.Build(canvas, _library, _config);
            }
        }

        /// <summary>Self-heal the Dino-Matic service (DinoDigger-3rz), for the same reason and
        /// on the same terms as the board above. The SERVICE only — the machine itself is not
        /// placed until the child banks their first bone.</summary>
        private void EnsureDinoMatic()
        {
            if (_dinoMatic == null)
            {
                _dinoMatic = FindAnyObjectByType<DinoMaticController>();
            }

            if (_dinoMatic == null)
            {
                var go = new GameObject("DinoMaticService");
                go.transform.SetParent(_overworldRoot != null ? _overworldRoot : transform, false);
                _dinoMatic = go.AddComponent<DinoMaticController>();
            }

            TownArea townArea = _town != null ? _town.GetComponent<TownArea>() : null;
            _dinoMatic.Configure(_map, _library, _config, _town, townArea, _machines,
                _meadow, _garden, _mounds, _overworldRoot);
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
            RollMoundThemes();
            InitSprouts();
        }

        /// <summary>Start the Berry Patch sprouts budding with STAGGERED initial timers so
        /// the three never ripen in sync. Not saved — every sprout begins budding each
        /// session. Runs in Start so BerrySprout.Awake has cached its renderers first.</summary>
        private void InitSprouts()
        {
            if (_sprouts == null)
            {
                return;
            }

            float ripen = _config != null ? _config.SproutRipenSeconds : 25f;
            int total = Mathf.Max(1, _sprouts.Count);
            for (int i = 0; i < _sprouts.Count; i++)
            {
                // Spread the first ripen across the cycle (e.g. ~1/3, ~2/3, full of 25s).
                float stagger = ripen * (i + 1) / total;
                _sprouts[i]?.Init(_config, _library, stagger);
            }
        }

        /// <summary>Give every scene-baked mound a rolled dig postcard theme so it tints
        /// itself from the start (respawned mounds re-roll on their own via DigMound.Respawn).
        /// Runs in Start so DigMound.Awake has cached its renderer/sparkle first.</summary>
        private void RollMoundThemes()
        {
            if (_mounds == null)
            {
                return;
            }

            for (int i = 0; i < _mounds.Count; i++)
            {
                _mounds[i]?.RollTheme(_config);
            }
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

            // The nest is retired scenery now (save v5): it shows its finished egg and echoes
            // banked bones. Nothing about it is progress any more.
            _nest?.ShowFinishedEgg();

            // The fossil collection (save v5): every banked bone and every revived skeleton.
            // Restored BEFORE the dinos are spawned below, because the revival set is UNION'd
            // with the SAVE's dino list here and re-checked from the live list on every write.
            RestoreBoneCollection(Save.Data);

            // The Dino-Matic's discovery + excavation state. The machine itself is not built
            // here — the service's tick builds it, so "the Dino-Matic appears" has exactly one
            // code path whether it is arriving or coming back from a save.
            _dinoMatic?.RestoreFromSave(Save.Data);

            // Draw the board for the restored collection right away, so a returning player's
            // HUD button and filled slots are correct on frame one without a bank event.
            _skeletonBoard?.Refresh();

            // Hand the dig site back the FEATURED toy the last session ended on, so the very
            // first dig after a restart still refuses to repeat it (DinoDigger-qhy). Stored
            // index+1; 0 (a fresh or pre-roller save) restores as "no history".
            DigModeController.RestoreLastPrimaryToy(Save.Data.LastPrimaryToy - 1);

            // Rebuild Dino Town: finished buildings return finished (no crew/confetti), a
            // partial site resumes accepting crew, and the queue continues from the saved
            // index. A v3 (or earlier) save has no town fields, so the town stays empty.
            _town?.RestoreFromSave(Save.Data);

            // Machine Friends (DinoDigger-b48): which discovery gates the child has already
            // earned, and which machines they have already woken. The machines themselves are
            // NOT rebuilt here — the service's arrival queue builds them on its next tick, so
            // "a machine appears" has exactly ONE code path whether it is arriving for the
            // first time or coming back from a save. Absent save fields = nothing earned yet,
            // which is also what a brand-new player gets.
            _machines?.RestoreFromSave(Save.Data);

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
            // Machine Friends: polls the town discovery gate and releases queued arrivals.
            // Owns no dino, blocks nothing, and does nothing at all until a gate trips.
            _machines?.Tick(dt);
            // The Dino-Matic: same shape, same pacing queue — nothing at all until the first
            // bone is banked, then an arrival and a hand-off to the town crew.
            _dinoMatic?.Tick(dt);
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
            Vector3 from = _backhoe != null ? _backhoe.transform.position : Vector3.zero;
            NearestActiveMound(from)?.AttractPulse();
            // A ripe berry is a harvest invite too — pulse the nearest one alongside the mound.
            NearestRipeSprout(from)?.AttractPulse();
            // ...and so is a machine that arrived while the child was busy elsewhere and is
            // still sitting there glinting, undiscovered. Once woken it drops out of the
            // rotation on its own (MachineFriend.AttractPulse no-ops on an awake machine), so
            // a found friend is never nagged about again.
            _machines?.NearestUndiscovered(from)?.AttractPulse();
            // ...and the Dino-Matic, while it is still buried or has a finished skeleton
            // waiting to be collected. It refuses the pulse itself once it has nothing to
            // offer, so a dug-out machine with an empty board is never nagged about.
            _dinoMatic?.Site?.AttractPulse();
            GameEvents.RaiseIdleAttract();
            TryTownAttractTour();
        }

        /// <summary>Idle-attract's second act (DinoDigger-sbc): once Dino Town has something to
        /// show — a finished building, or a site with a crew on it — some idle beats glide the
        /// camera over to the district, linger long enough to watch a bit of townsfolk life or
        /// construction, and glide back to the backhoe.
        ///
        /// Deliberately ALTERNATING rather than every beat: a tour that fired on every idle
        /// would stop reading as a treat. With nothing built the whole thing is skipped and idle
        /// attract stays exactly what it always was (honk + nearest-mound/berry pulse).
        ///
        /// The tour takes NOTHING away from the player: the game stays in Roam (so taps route
        /// normally), the backhoe is untouched, and the first tap cancels it — see
        /// <see cref="CancelTownAttractTour"/>.</summary>
        private void TryTownAttractTour()
        {
            if (_townTourActive || _cameraFollow == null || _town == null || !_town.HasVisibleTown)
            {
                return;
            }

            if (State == null || !State.Is(GameState.Roam) || _ceremonyActive ||
                (_digMode != null && _digMode.IsOpen))
            {
                return; // never over a dig, a ceremony, or a transition
            }

            if (_paradeActive || (_backhoe != null && _backhoe.IsMoving))
            {
                // Never yank the camera off a MOVING backhoe (the toddler is watching their
                // digger go somewhere, and losing sight of it is the opposite of an attract),
                // and never upstage the milestone parade — it is already the show.
                return;
            }

            bool tourNow = _townTourNext;
            _townTourNext = !_townTourNext;
            if (!tourNow)
            {
                return; // this beat is a plain honk; the next qualifying one tours
            }

            _townTourActive = true;
            _townTours++;
            _cameraFollow.EnterFocus(_town.AttractFocusPoint, () =>
            {
                if (!_townTourActive)
                {
                    return; // cancelled while gliding in — the return trip is already running
                }

                _townTourLinger = Tween.After(TownTourLingerSeconds, CancelTownAttractTour);
            });
        }

        /// <summary>End the attract tour and hand the camera back to the backhoe. Used for BOTH
        /// exits — the linger timer running out and a player tap cutting it short — because they
        /// want the same thing: stop waiting, glide home. Idempotent, so the timer firing after a
        /// tap already cancelled is harmless.</summary>
        private void CancelTownAttractTour()
        {
            if (!_townTourActive)
            {
                return;
            }

            _townTourActive = false;
            Tween.Stop(_townTourLinger);
            _townTourLinger = null;

            // ExitFocus stops the glide-in mid-flight and reverses from wherever the camera is,
            // so a tap two frames into the tour turns around immediately.
            _cameraFollow?.ExitFocus(null);
        }

        // ------------------------------------------------------------ tap input

        private void OnTap(Vector2 screenPos)
        {
            _idleTimer = 0f;

            // INPUT ALWAYS WINS: an attract tour is cancelled before the tap is even resolved,
            // so the camera is already on its way back while the tap does its normal job. The
            // world point below is read with the camera where it is THIS frame, which is what
            // the player was looking at when they touched the screen.
            CancelTownAttractTour();

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
            // Brachiosaurus fruit-shake, a tapped ROCK to the Ankylosaurus smash;
            // anything else drives the backhoe.
            if (State.Is(GameState.Roam) && TryRouteTreeTap(world))
            {
                Audio?.Tap();
                return;
            }

            if (State.Is(GameState.Roam) && TryRouteRockTap(world))
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

        /// <summary>If the tapped cell holds a rock tile (Obstacles tilemap), fire
        /// the rock-tap flow. Returns true when a rock consumed the tap. Only the
        /// rock's own (unwalkable) cell counts, so movement taps on the grass around
        /// it are never swallowed.</summary>
        private bool TryRouteRockTap(Vector3 world)
        {
            if (_map == null || _library == null || _library.RockTile == null)
            {
                return false;
            }

            Vector3Int cell = _map.WorldToCell(world);
            if (_map.ObstacleAt(cell) == _library.RockTile)
            {
                OnRockTapped(cell);
                return true;
            }

            return false;
        }

        /// <summary>
        /// The tappable under a world point, resolved DETERMINISTICALLY (DinoDigger-lie).
        /// Physics2D.OverlapPointAll returns overlapping colliders in no defined order, so
        /// two tappables sharing a point (a respawned mound clipping a finished building's
        /// footprint) used to answer a tap differently from one frame to the next. Now the
        /// hit with the lowest <see cref="TappableRank"/> wins, and within one rank the
        /// collider whose center is NEAREST the tap point does — so the same tap always
        /// does the same thing.
        /// </summary>
        private ITappable FindTappable(Vector3 world)
        {
            Collider2D[] hits = Physics2D.OverlapPointAll(world);
            ITappable best = null;
            int bestRank = int.MaxValue;
            float bestDistSq = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] == null)
                {
                    continue;
                }

                var t = hits[i].GetComponent<ITappable>() ?? hits[i].GetComponentInParent<ITappable>();
                if (t == null)
                {
                    continue;
                }

                int rank = TappableRank(t);
                Vector3 center = hits[i].bounds.center;
                center.z = world.z;
                float distSq = (center - world).sqrMagnitude;
                if (rank < bestRank || (rank == bestRank && distSq < bestDistSq))
                {
                    best = t;
                    bestRank = rank;
                    bestDistSq = distSq;
                }
            }

            return best;
        }

        /// <summary>
        /// Tap priority, lowest wins. The order is "the most specific, most alive thing the
        /// toddler could have meant", never "whatever physics listed first":
        ///
        ///   0 dirt tile   — the dig pit is modal; while a site is open its tiles own the taps.
        ///   1 duck        — a fleeting critter that wanders over anything; catching it wins.
        ///   2 dino        — a living buddy/resident.
        ///   3 machine     — a helper machine: a CHARACTER, not scenery (DinoDigger-b48).
        ///   4 pickup      — fruit/egg lying around, the feed-and-hatch loop.
        ///   5 berry sprout— an interactive plant (harvest).
        ///   6 dig mound   — a ground prop, and the TRANSIENT one: it vanishes once dug.
        ///   7 build site  — a building still going up: a tap cheers the crew on (DinoDigger-5y9).
        ///   8 building    — the permanent town prop underneath everything else.
        ///
        /// Mound above building is deliberate: the mound is the smaller, temporary object a
        /// toddler is aiming AT when the two overlap. That overlap should not happen at all
        /// any more — SpawnManager keeps respawns off built plots — so this ordering only
        /// decides the leftover degenerate case, and decides it the same way every time.
        /// The under-construction SITE outranks a finished building for the same reason it sits
        /// below the mound: it is the state that changes, the one with a crew hammering on it,
        /// so if a site somehow overlapped a finished neighbour the live one should answer.
        ///
        /// MACHINES slot between the dinos and the pickups because that is what they are: alive
        /// enough to answer before a prop, but never before an animal. The overlap that really
        /// happens is Tuggy's: he chugs a one-cell stream that ducks (including the ducklings he
        /// himself tows out) drift straight across. Duck-over-machine means the fleeting,
        /// catchable, rewarding thing always wins that tap and the big steady boat underneath
        /// never steals it — which is precisely the ambiguity the roster eval warned a tugboat
        /// would create, closed here by construction rather than by hoping it never happens.
        /// Unknown implementors fall to the bottom, which is the old first-hit behaviour.
        /// </summary>
        private static int TappableRank(ITappable t)
        {
            switch (t)
            {
                case DirtTile _: return 0;
                case Duck _: return 1;
                case DinoController _: return 2;
                case MachineFriend _: return 3;
                // The Dino-Matic is a BuildingController by construction (it reuses the town's
                // excavation state machine) but it is a MACHINE to the child, so it ranks with
                // its cousins rather than with the scenery underneath them. Without this it
                // would fall to 7/8 and a dig mound sitting near it — the belt it is
                // deliberately placed among — could swallow the tap that starts a revival.
                case DinoMatic _: return 3;
                case ItemPickup _: return 4;
                case BerrySprout _: return 5;
                case DigMound _: return 6;
                case BuildingController b: return b.IsFinished ? 8 : 7;
                default: return 9;
            }
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

            // Belt-and-braces: every route into a dig starts with a tap (which already cancels),
            // but the dig camera and an attract glide must never both own the camera.
            CancelTownAttractTour();

            _activeMound = mound;
            State.Set(GameState.Transition);

            // The mound carries its rolled dig postcard: the site reads it for tints,
            // loot skew and buried-item count. Null-safe -> flat default look.
            Config.DigTheme theme = (_config != null && mound != null)
                ? _config.GetTheme(mound.ThemeIndex)
                : null;
            // The whole walk roster (up to two) comes along and staffs the Buddy Dig Crew;
            // each species runs its own automatic dig superpower inside the site.
            _digMode.Open(theme, BuildDigCrew());

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

        /// <summary>Snapshot the live walk buddies (species + growth stage) for the dig
        /// site's Buddy Dig Crew. Up to two, in join order; nulls are pruned first.</summary>
        private List<DigModeController.DigBuddy> BuildDigCrew()
        {
            PruneBuddies();
            var crew = new List<DigModeController.DigBuddy>(_buddies.Count);
            for (int i = 0; i < _buddies.Count; i++)
            {
                DinoController b = _buddies[i];
                if (b != null)
                {
                    crew.Add(new DigModeController.DigBuddy(b.Type, b.Stage));
                }
            }

            return crew;
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

        /// <summary>Tiny camera nudge for a dig-site whumph (the boom geode). Routed through the
        /// camera rig, which owns the dig framing and shakes around it — the dig controller has
        /// no business touching the camera transform itself. Silently does nothing when the
        /// camera is mid-transition or not parked in the dig view.</summary>
        internal void DigShakeCamera(float amplitude, float seconds)
        {
            _cameraFollow?.ShakeDig(amplitude, seconds);
        }

        /// <summary>World spot where a dig-surprise reward pops out: the overworld backhoe's
        /// position (where dug loot spills). SpawnRewardPickup clamps it to walkable ground and
        /// then the coin flies to the corner counter — so surprises fired inside the dig site
        /// still bank cleanly through the existing path.</summary>
        public Vector3 RewardSpawnPoint =>
            _backhoe != null ? _backhoe.transform.position : Vector3.zero;

        // Cached scene duck spawner, resolved lazily so the Duck! surprise can borrow the
        // duck's own art without a new serialized wire or a scene rebuild.
        private DuckController _duckSpawner;

        /// <summary>The ambient duck's side sprite, for the Duck! dig surprise. Null when no
        /// duck art / spawner is present (the surprise then flies an invisible duck).</summary>
        public Sprite DuckSprite
        {
            get
            {
                if (_duckSpawner == null)
                {
                    _duckSpawner = FindAnyObjectByType<DuckController>();
                }

                return _duckSpawner != null ? _duckSpawner.SurpriseSprite : null;
            }
        }

        /// <summary>Harvest a ripe Berry Sprout: pop one fruit of <paramref name="variant"/>
        /// out of the sprout in an arc to a nearby walkable landing spot, through the SAME
        /// pickup path as dug fruit — so it bobs, its tap routes into the feed chain, and the
        /// Trike courier can fetch it. Public so <see cref="BerrySprout"/> can call it.</summary>
        public ItemPickup SpawnSproutFruit(Vector3 sproutWorld, int variant)
        {
            // MACHINE DISCOVERY GATE (DinoDigger-25j): the garden's only PLAYER verb is this
            // harvest tap — a sprout ripens on a timer with nobody involved — so a harvest is
            // the honest signal that the child has actually engaged the garden. That is what
            // summons Sprinkles; before it, the berry patch is just a berry patch.
            _machines?.NotifyBerryHarvested();

            int variants = _config != null ? Mathf.Max(1, _config.FruitVariants) : 1;
            variant = Mathf.Clamp(variant, 0, variants - 1);

            // Pop out of the sprout, arc to a nearby (flattened for the iso plane) spot.
            float angle = Random.value * Mathf.PI * 2f;
            Vector3 landing = sproutWorld +
                new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * 0.7f, 0f) * Random.Range(0.8f, 1.2f);
            if (_map != null)
            {
                landing = _map.NearestWalkable(landing, out _);
            }

            Vector3 origin = sproutWorld + new Vector3(0f, 0.3f, 0f);
            var info = new DugItemInfo(ItemType.Fruit, Config.DinoType.TRex, variant, origin);
            return CreatePickup(info, landing);
        }

        /// <summary>Green leaf-rustle feedback for a budding Berry Sprout tap (same puff a
        /// tapped tree gives), so a not-yet-ripe sprout's tap always does something. Public
        /// so <see cref="BerrySprout"/> can call it.</summary>
        public void SproutRustle(Vector3 world) => LeafRustle(world);

        /// <summary>Resolve a freshly dug item into its final overworld identity.
        /// Eggs are reassigned an unowned egg species so a duplicate can never hatch;
        /// when every egg species is owned there is no unique egg left to give, so the
        /// egg banks as treasure instead (the FOSSIL BONES buried in the site itself are
        /// what the late game collects — see DigModeController.PlaceBones). All other
        /// item types pass through unchanged.</summary>
        private DugItemInfo ResolveDugItem(DugItemInfo info)
        {
            // SHARDS ARE RETIRED (save v5). Nothing produces one any more, but a stray from an
            // older code path — or a test spawning one by hand — must still be worth something
            // rather than banking into a counter nobody reads, so it downgrades to treasure.
            if (info.Type == ItemType.Shard)
            {
                return new DugItemInfo(ItemType.Treasure, info.DinoType, info.Variant, info.OriginWorld);
            }

            // FRUIT GLUT GUARD: fruit is 40% of drops but demand is finite (a Big dino is
            // never hungry). When there is NO fruit demand, most of it downgrades to a random
            // treasure so uneaten fruit can't pile up; the rest stays fruit so the world
            // still has some. "Fruit demand" is now FULLY widened to every sink: a hungry dino
            // OR an open Fruit Stand (surplus fruit sells there) OR an active construction site
            // with a builder on it (the fruit becomes a builder snack that banks build work).
            // Only when NONE of those want the fruit does most of it downgrade. This is the
            // final planned widening.
            if (info.Type == ItemType.Fruit)
            {
                if (_config != null && !AnyDinoHungry() && !FruitStandFinished && !HasCrewedBuildSite &&
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
            //  (a) Every egg species is genuinely OWNED — the egg nerf is in effect and there
            //      is no dinosaur left for an egg to contain. It banks as TREASURE. (Before
            //      save v5 it became an egg shard for the nest; the nest is retired and the
            //      fossil species now come out of the ground as BONES, which the dig site
            //      buries directly rather than routing through the loot table.)
            //
            //  (b) Egg species remain unowned in the WORLD but are all RESERVED by
            //      other un-hatched eggs (e.g. a second egg in this same dig batch).
            //      Spill a FRUIT instead — the reserved species frees up again once its
            //      sibling egg hatches or is cleared, and a treasure here would quietly
            //      cheat the child out of a dinosaur they are one dig away from.
            if (EggSpeciesAllOwned())
            {
                return new DugItemInfo(ItemType.Treasure, info.DinoType, info.Variant, info.OriginWorld);
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

            // OWNING A FOSSIL DINO IS BEING REVIVED. Whether it came out of the Dino-Matic, out
            // of a v4 nest save, or out of a test hook, a fossil species standing in the world
            // means its skeleton is done — so the board colours it in and the machine never
            // offers it again. Keeping the invariant HERE (one place every dino is born) is
            // what makes the save's revived list a cache rather than a second source of truth.
            if (SkeletonPlan.IsFossilSpecies(type))
            {
                _revived.Add(type);
            }

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
                // Feed priority is absolute — a hungry dino always wins (handled below). Once
                // NOBODY is hungry the surplus fruit next feeds a BUILDER on an active site (a
                // snack that banks build work), then finally sells at a finished Fruit Stand;
                // the stand path keeps its own self-serve fallback so a toddler's tap always does
                // SOMETHING. If neither sink wants it, the fruit just bounced for feedback and
                // waits to be eaten later.
                if (TrySnackBuilder(fruit))
                {
                    return;
                }

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

        // -------------------------------------------------------- builder snack
        // Middle link of the feed-priority chain (hungry dino -> BUILDER SNACK -> Fruit Stand sale):
        // once nobody is hungry, a fruit fed while a construction site has a builder ON it becomes a
        // SNACK — it arcs to the worker, who munches it, and the site banks one construction state's
        // worth of bonus work so the building visibly jumps ahead. Reuses the courier/seller carry
        // lock + arc (ItemPickup.BeginCarried, Tween.MoveArc) and the growth-feed eat feedback (a
        // punch-chomp + eat sting) WITHOUT reaching into DinoController's build state.

        /// <summary>If an active construction site has a builder physically on site working, snack the
        /// tapped fruit to that builder and bank <see cref="Config.GameConfig.SnackWorkSeconds"/> of
        /// build work on arrival; returns true when the snack was taken. Returns false — so the caller
        /// falls through to the Fruit Stand sale — when there is no active site or no builder is working
        /// yet (a builder merely commuting does NOT count). Banking is idempotent per fruit, so a second
        /// fruit tapped mid-flight simply flies and banks on its own (no queue).</summary>
        private bool TrySnackBuilder(ItemPickup fruit)
        {
            if (fruit == null || fruit.IsConsumed || fruit.IsCarried || _town == null)
            {
                return false;
            }

            DinoController builder = _town.FirstWorkingBuilder();
            if (builder == null)
            {
                return false; // no crewed active site: fall through to the stand sale
            }

            // Lock the fruit for flight (stops it bobbing/tapping and keeps the Trike courier off it),
            // then arc it to the builder — the same primitives the stand's self-serve sale uses.
            fruit.BeginCarried();
            Vector3 from = fruit.transform.position;
            Vector3 to = builder.transform.position;
            Tween.MoveArc(fruit.transform, from, to, 1.2f, 0.55f, () =>
            {
                // Munch feedback: a chomp punch on the builder + the eat sting, reusing the
                // growth-feed feedback without touching DinoController's build state.
                if (builder != null)
                {
                    Tween.PunchScale(builder.transform, 0.25f, 0.3f);
                }

                Audio?.Eat();

                // Consume the fruit (same shrink-pop eat animation as a growth feed), then bank the
                // snack: the site advances a state almost immediately if it was mid-state.
                if (fruit != null && !fruit.IsConsumed)
                {
                    _pickups.Remove(fruit);
                    fruit.ConsumeAsFood();
                }

                _town?.BankBuilderSnack();
            });

            return true;
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

        /// <summary>Ankylosaurus rock smash. A rock ALWAYS gives a little pebble
        /// wiggle so the tap does something; treasure (or, while the nest still wants
        /// them, an egg shard) only pops out when a buddy Anky is close enough, walks
        /// over and tail-clubs it — and the rock is off its per-rock cooldown. A tap on
        /// a cooling rock still wiggles, it just doesn't pay out again.</summary>
        private void OnRockTapped(Vector3Int cell)
        {
            Vector3 rockWorld = _map != null ? _map.CellCenter(cell) : Vector3.zero;
            RockWiggle(rockWorld);

            DinoController anky = FindBuddy(Config.DinoType.Ankylosaurus);
            if (anky == null || anky.IsBusy)
            {
                return;
            }

            float range = _config != null ? _config.RockSmashRange : 3f;
            if ((anky.transform.position - rockWorld).sqrMagnitude > range * range)
            {
                return;
            }

            if (_rockCooldownUntil.TryGetValue(cell, out float until) && Time.time < until)
            {
                return; // rock is resting; the pebble wiggle already played
            }

            float cooldown = _config != null ? _config.RockCooldownSeconds : 15f;
            _rockCooldownUntil[cell] = Time.time + cooldown;

            Vector3 approach = rockWorld + new Vector3(0f, -0.7f, 0f);
            if (_map != null)
            {
                approach = _map.NearestWalkable(approach, out _);
            }

            anky.WalkTo(approach, 1.2f, () =>
            {
                if (anky == null)
                {
                    return;
                }

                anky.Dance(); // Anky's dance is the tail-club swing
                Tween.After(0.45f, () => SmashRockLoot(rockWorld));
            });
        }

        /// <summary>Small pebble puff so EVERY rock tap does something (toddler rule:
        /// no tap is ever ignored), plus a soft crumble thud.</summary>
        private void RockWiggle(Vector3 rockWorld)
        {
            Audio?.Crumble();

            ParticleSystem ps = CreateParticles(_overworldRoot,
                _library != null ? _library.CrumbParticle : null,
                new Color(0.62f, 0.57f, 0.5f), 0.18f);
            if (ps == null)
            {
                return;
            }

            ps.transform.position = rockWorld;
            ps.Emit(6);
            Tween.After(1.5f, () =>
            {
                if (ps != null)
                {
                    Destroy(ps.gameObject);
                }
            });
        }

        /// <summary>The Anky tail-clubbed the rock: a big crumb burst, a pop, and a
        /// treasure spilling out in a happy arc.</summary>
        private void SmashRockLoot(Vector3 rockWorld)
        {
            RockBurst(rockWorld);
            Audio?.ItemPop();

            DugItemInfo payout = RollRockPayout(rockWorld);
            // Same spawn path as dug loot: SpawnRewardPickup clamps the landing to a
            // walkable cell (the rock's own cell is unwalkable) and the treasure then
            // flies to the corner counter.
            SpawnRewardPickup(payout.Type, payout.DinoType, payout.Variant, rockWorld);
            _rockSmashPayouts++;
        }

        private void RockBurst(Vector3 rockWorld)
        {
            ParticleSystem ps = CreateParticles(_overworldRoot,
                _library != null ? _library.CrumbParticle : null,
                new Color(0.62f, 0.57f, 0.5f), 0.32f);
            if (ps == null)
            {
                return;
            }

            ps.transform.position = rockWorld;
            ps.Emit(22);
            Tween.After(2f, () =>
            {
                if (ps != null)
                {
                    Destroy(ps.gameObject);
                }
            });
        }

        /// <summary>Decide what a smashed rock coughs up: ALWAYS a random-denomination
        /// treasure. It used to roll an egg shard some of the time to keep the nest ticking
        /// over; the nest is retired (save v5) and the fossil species come out of dig sites as
        /// bones now, so a rock is pure coins — which is also what a rock has always looked
        /// like it should give.</summary>
        private DugItemInfo RollRockPayout(Vector3 world)
        {
            int variants = _config != null ? Mathf.Max(1, _config.TreasureVariants) : 1;
            return new DugItemInfo(ItemType.Treasure, Config.DinoType.TRex,
                Random.Range(0, variants), world);
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

        /// <summary>True while a construction site is active with at least one builder physically on
        /// site working: the third fruit-demand sink (builder snacks), alongside a hungry dino and the
        /// open Fruit Stand. Gates the glut guard so fruit stops downgrading while a crew can snack it.</summary>
        private bool HasCrewedBuildSite => _town != null && _town.HasWorkingBuilderOnSite();

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
            // gem=3, boot=1, bone=2), clamped so an odd variant safely banks 1. A reward may
            // carry a value override (e.g. the Big Bone surprise: a bone sprite that banks 5).
            int value = treasure.ValueOverride >= 0
                ? treasure.ValueOverride
                : (_config != null ? _config.TreasureValue(treasure.Variant) : 1);

            Tween.MoveArc(treasure.transform, treasure.transform.position, target, 1.2f, 0.6f, () =>
            {
                // A treasure destroyed mid-flight (only ever a TestReset / scene teardown —
                // never real play) must NOT phantom-bank: the tween's onComplete always
                // fires, so guard the bank here. Otherwise a stray +value could land a
                // frame (or a whole test case) later and corrupt a count-exact assertion.
                if (treasure == null)
                {
                    return;
                }

                Save.Data.TreasureCount += value;
                Audio?.Treasure();
                GameEvents.RaiseTreasureCollected(Save.Data.TreasureCount);
                SaveNow();
                Destroy(treasure.gameObject);
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

        /// <summary>Up to <paramref name="max"/> residents free to join a recess party: the SAME
        /// eligibility as the builder draft/seller pick — NON-buddy, not the ceremony baby, not a
        /// seller, and not BUSY (eating, dancing, traveling, parading, OR working/commuting to a
        /// build site, since <see cref="DinoController.IsBusy"/> covers all of those). Excluding
        /// busy dinos means a builder on an active site is never poached, and a dino already
        /// commuting to / orbiting another recess is never double-booked (so different buildings
        /// can party at once). Buddies and the player backhoe can never appear here.</summary>
        internal List<DinoController> TownAcquireRecessGoers(int max)
        {
            var result = new List<DinoController>();
            if (max <= 0)
            {
                return result;
            }

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

                result.Add(d);
                if (result.Count >= max)
                {
                    break;
                }
            }

            return result;
        }

        /// <summary>Every dino within <paramref name="radius"/> of a world point — who the town's
        /// completion celebration invites to cheer (DinoDigger-0gd). Deliberately UNFILTERED by
        /// role: a buddy that happened to be standing in the plaza when the roof went on should
        /// cheer too, and cheering claims nothing (<see cref="DinoController.CheerHop"/> changes
        /// no mode, cancels no walk), so handing the town this list can never take a dino away
        /// from the player. An ambient VISITOR refuses the hop itself, inside CheerHop, because
        /// town life owns its pose.</summary>
        internal List<DinoController> TownDinosNear(Vector3 world, float radius)
        {
            var result = new List<DinoController>();
            float rSq = radius * radius;
            for (int i = 0; i < _dinos.Count; i++)
            {
                DinoController d = _dinos[i];
                if (d == null)
                {
                    continue;
                }

                Vector3 p = d.transform.position;
                p.z = world.z;
                if ((p - world).sqrMagnitude <= rSq)
                {
                    result.Add(d);
                }
            }

            return result;
        }

        /// <summary>The town's build state changed (a site broke ground, advanced a state,
        /// or finished): write it to disk. Routes through <see cref="SaveNow"/> so the town's
        /// per-building progress is persisted alongside the rest of the save.</summary>
        internal void TownPersist() => SaveNow();

        // ------------------------------------------------- Machine Friends (b48)
        // Small, explicit hooks the machine service borrows. Every one of them either reuses
        // an existing shared facility (particles, FX bursts, the save) or applies an EXISTING
        // eligibility rule — none of them adds a new way to claim a dino.

        /// <summary>A machine was woken, or a discovery gate tripped: persist it, so a friend
        /// the child found is never re-buried and progress toward the next one is never lost.</summary>
        internal void MachinePersist() => SaveNow();

        /// <summary>The Dino-Matic was found, or its excavation moved on: persist it, for
        /// exactly the same reason.</summary>
        internal void DinoMaticPersist() => SaveNow();

        /// <summary>A duck was caught (<see cref="Duck.OnTapped"/>): trip Tuggy's discovery
        /// gate. Idempotent — only the FIRST catch means anything.</summary>
        public void NotifyDuckCaught() => _machines?.NotifyDuckCaught();

        /// <summary>Reuse the shared particle-system factory for a machine's sparkle.</summary>
        internal ParticleSystem MachineCreateParticles(Transform parent, Sprite sprite,
            Color color, float size) => CreateParticles(parent, sprite, color, size);

        /// <summary>Reuse the shared one-shot FX burst (music notes, spray droplets, a toot
        /// puff). Same throwaway-system + auto-destroy shape as the town's ambient FX.</summary>
        internal void MachineSpawnFx(Vector3 pos, Sprite sprite, Color color, float size, int count) =>
            TownSpawnFx(pos, sprite, color, size, count);

        /// <summary>Residents free to join DOODLE'S DANCE PARTY: the town visit system's
        /// eligibility rule VERBATIM (<see cref="TownAcquireRecessGoers"/> — non-buddy, not
        /// busy, not a seller, not the ceremony baby) plus a proximity test, because a party
        /// should pull in the dinos who can hear the music rather than the whole island.
        ///
        /// Reusing that filter is what makes "construction always wins" structural rather than
        /// policed: <see cref="DinoController.IsBusy"/> already covers working AND commuting to
        /// a build site, so a builder is never even offered to Doodle. And a dancer that gets
        /// drafted mid-song is simply taken — <see cref="DinoController.GoWork"/> refuses
        /// nothing — with the party dropping it on its next beat.</summary>
        internal List<DinoController> MachineAcquireDancers(Vector3 world, float radius, int max)
        {
            var result = new List<DinoController>();
            if (max <= 0)
            {
                return result;
            }

            float radiusSq = radius * radius;
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

                Vector3 delta = d.transform.position - world;
                delta.z = 0f;
                if (delta.sqrMagnitude > radiusSq)
                {
                    continue;
                }

                result.Add(d);
                if (result.Count >= max)
                {
                    break;
                }
            }

            return result;
        }

        /// <summary>Reuse the shared confetti burst for a building completion.</summary>
        internal void TownSpawnConfetti(Vector3 pos) => SpawnConfetti(pos);

        /// <summary>Reuse the shared particle-system factory for a build site's dust/crumbs.</summary>
        internal ParticleSystem TownCreateParticles(Transform parent, Sprite sprite, Color color, float size) =>
            CreateParticles(parent, sprite, color, size);

        /// <summary>One-shot ambient FX burst for town life (DinoDigger-3pz): a small puff of
        /// <paramref name="count"/> particles of an EXISTING library sprite at a world point —
        /// hearts over a coffee sip, tinted crumbs as spa bubbles, stars as fountain splash.
        /// Same throwaway-system + auto-destroy shape as <see cref="SpawnConfetti"/>, so no new
        /// art and no pooling; null sprite just tints the default particle.</summary>
        internal void TownSpawnFx(Vector3 pos, Sprite sprite, Color color, float size, int count)
        {
            ParticleSystem ps = CreateParticles(_overworldRoot, sprite, color, size);
            if (ps == null)
            {
                return;
            }

            ps.transform.position = pos;
            ps.Emit(Mathf.Clamp(count, 1, 24));
            Tween.After(2f, () =>
            {
                if (ps != null)
                {
                    Destroy(ps.gameObject);
                }
            });
        }

        // ------------------------------------------------- the revival ceremony
        // Save v5 retired the egg-shard nest and its hatch ceremony. What replaces it is the
        // SAME ceremony, re-pointed: same _ceremonyActive guard, same GameState.Ceremony, same
        // CameraFollow.EnterFocus/ExitFocus, same "a baby waits to be tapped, and the tap both
        // joins it and ends the ceremony" (NotifyDinoTapped, untouched). Only the trigger and
        // the middle beat changed — a completed skeleton and the Dino-Matic, instead of a full
        // nest and a cracking egg — which is exactly the amount of this that WAS about shards.

        /// <summary>The Dino-Matic was tapped while dug out. If a skeleton is finished, run the
        /// revival; if not, the machine gives its wordless "not yet" wobble — a tap always does
        /// something. Guarded so it can never re-enter.</summary>
        internal void RequestRevival(DinoMatic machine)
        {
            if (machine == null || !machine.IsExcavated)
            {
                return;
            }

            if (_ceremonyActive || State == null)
            {
                return;
            }

            if (!TryNextRevivable(out Config.DinoType species))
            {
                machine.NotReadyWobble();
                return;
            }

            _ceremonyActive = true;
            State.Set(GameState.Ceremony); // blocks dig entry + backhoe move during the zoom

            Vector3 at = machine.PadWorld;
            if (_cameraFollow != null)
            {
                _cameraFollow.EnterFocus(at, () => PlayRevival(machine, species, at));
            }
            else
            {
                PlayRevival(machine, species, at);
            }
        }

        private void PlayRevival(DinoMatic machine, Config.DinoType species, Vector3 at)
        {
            SpawnConfetti(at + new Vector3(0f, 0.4f, 0f));

            // The skeleton the child assembled floats in as the board's own silhouette, so the
            // thing going into the machine is visibly the thing they filled in.
            Sprite skeleton = _library != null
                ? _library.SkeletonBoard(SkeletonPlan.BoardIndex(species))
                : null;

            machine.PlayRevival(skeleton, () => FinishRevival(machine, species, at));
        }

        private void FinishRevival(DinoMatic machine, Config.DinoType species, Vector3 at)
        {
            // Mark it revived BEFORE spawning: SpawnDino persists, and a save written between
            // the two would show a dino whose skeleton was still "waiting for the machine".
            _revived.Add(species);

            // The baby lands on the pad IN FRONT of the machine (screen-south, where the
            // ceremony camera is already looking), Baby stage, forced RESIDENT — it waits to be
            // tapped, and that tap promotes it to buddy AND ends the ceremony, exactly as the
            // hatch ceremony always worked (see NotifyDinoTapped). The forward offset keeps it
            // off the machine's own silhouette so the child can see what they just made.
            Vector3 spawnPos = at + new Vector3(0f, -0.6f, 0f);
            if (_map != null)
            {
                spawnPos = _map.NearestWalkable(spawnPos, out _);
            }

            _ceremonyDino = SpawnDino(species, GrowthStage.Baby, 0, spawnPos, persist: true, wantsBuddy: false);
            _ceremonyDino?.Dance();

            // The same "a new dinosaur joined the island" beat an egg hatch raises, so anything
            // listening for a new dino (the parade check, future systems) sees one code path.
            GameEvents.RaiseEggHatched(species);

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

        private BerrySprout NearestRipeSprout(Vector3 pos)
        {
            if (_sprouts == null)
            {
                return null;
            }

            BerrySprout best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < _sprouts.Count; i++)
            {
                BerrySprout s = _sprouts[i];
                if (s == null || !s.IsRipe)
                {
                    continue;
                }

                float sq = (s.transform.position - pos).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = s;
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

            // Machine Friends: which discovery gates have been earned and which machines have
            // been woken. Purely additive fields on the v4 schema (see SaveData) — nothing
            // else in this payload changes shape, so an older build reading this save simply
            // ignores them and a newer build reading an older save finds none earned.
            _machines?.WriteSave(Save.Data);

            // The fossil finale (save v5): the bone bank, which skeletons have been revived,
            // and how far the crew has got with the Dino-Matic.
            WriteBoneCollection(Save.Data);
            _dinoMatic?.WriteSave(Save.Data);

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
        internal IReadOnlyList<BerrySprout> TestSprouts => _sprouts;
        internal GardenArea TestGarden => _garden;
        internal IReadOnlyList<DinoController> TestDinos => _dinos;
        internal IReadOnlyList<DinoController> TestBuddies => _buddies;
        internal MeadowArea TestMeadow => _meadow;
        internal NestController TestNest => _nest;
        internal TownController TestTown => _town;
        internal MachineFriendController TestMachines => _machines;
        internal int TestSnifferPulses => _snifferPulses;
        internal int TestRockSmashPayouts => _rockSmashPayouts;
        internal bool TestParadeActive => _paradeActive;
        internal bool TestCeremonyActive => _ceremonyActive;
        internal DinoController TestCeremonyDino => _ceremonyDino;
        internal CameraFollow TestCameraFollow => _cameraFollow;

        /// <summary>Idle-attract town tour (DinoDigger-sbc): whether one is running right now,
        /// and how many have run since the last reset.</summary>
        internal bool TestTownTourActive => _townTourActive;
        internal int TestTownTours => _townTours;
        internal PlaceholderLibrary TestLibrary => _library;
        internal bool TestEggSpeciesAllOwned => EggSpeciesAllOwned();
        internal int TestBonesBanked => BonesBanked;
        internal int TestBoneCount(Config.DinoType species, int boneIndex) => BoneCount(species, boneIndex);
        internal int TestBoneRowCount => BoneBankSnapshot().Count;

        // ---- Fossil finale (DinoDigger-5ve / -3rz) ----
        internal SkeletonBoard TestSkeletonBoard => _skeletonBoard;
        internal DinoMaticController TestDinoMatic => _dinoMatic;
        internal bool TestRevivalPending => RevivalPending;
        internal bool TestAllSkeletonsRevived => AllSkeletonsRevived();
        internal int TestDuplicateBoneCoins => DuplicateBoneCoins;
        internal bool TestSkeletonComplete(Config.DinoType s) => SkeletonComplete(s);
        internal bool TestSpeciesRevived(Config.DinoType s) => IsSpeciesRevived(s);

        /// <summary>TEST HOOK. Bank exactly the bones a species' skeleton still needs, through
        /// the REAL <see cref="BankBone"/> path (so the save, the board, the nest echo and the
        /// Dino-Matic gate all fire as they would in play). Returns how many were banked.</summary>
        internal int TestCompleteSkeleton(Config.DinoType species)
        {
            int banked = 0;
            for (int bone = 0; bone < BoneSpecies.BonesPerSkeleton; bone++)
            {
                int need = SkeletonPlan.NeedOf(species, bone);
                while (BoneCount(species, bone) < need)
                {
                    if (!BankBone(species, bone))
                    {
                        return banked; // the board is fully revived: further bones pay out
                    }

                    banked++;
                }
            }

            return banked;
        }
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

        /// <summary>TEST HOOK. Build a themed dig site off-screen (at the dig root) so the
        /// DigThemes case can inspect its tints + buried loot. Tear down with TestForceRoam.</summary>
        internal void TestBuildThemedDigSite(int themeIndex)
        {
            if (_digMode != null && _config != null)
            {
                _digMode.TestBuildThemedSite(_config.GetTheme(themeIndex));
            }
        }

        /// <summary>TEST HOOK. Roll one dug item through the dig site's RAW loot roll
        /// (theme weights + egg-shard nerf) WITHOUT the FinishDig uniqueness/glut resolution.
        /// Lets the theme-distribution check see the pure loot skew (e.g. Berry Bog -> fruit),
        /// unclouded by the fruit-glut downgrade. Requires a site built (TestBuildThemedDigSite).</summary>
        internal DugItemInfo TestRollDugItemRaw()
        {
            return _digMode != null
                ? _digMode.TestRollItemInfo()
                : new DugItemInfo(ItemType.Fruit, Config.DinoType.TRex, 0, Vector3.zero);
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

            // Keep mound respawns off this town's built plots too (DinoDigger-lie): an
            // injected town is the only one a fresh scene has.
            Spawn?.SetTown(_town);
        }

        /// <summary>TEST HOOK. Install a machine-friends service when the scene has none (an
        /// older scene asset, or a hand-built rig). Never replaces a wired one — the built
        /// scene's service is what the cases should be exercising.</summary>
        internal void TestInstallMachines(MachineFriendController machines)
        {
            if (_machines == null)
            {
                _machines = machines;
            }
        }

        /// <summary>TEST HOOK. The tappable a tap at this world point resolves to, as a
        /// Component (null = nothing tappable there). Lets a case assert the RESOLUTION of an
        /// overlap directly (DinoDigger-lie) instead of inferring it from a side effect.</summary>
        internal Component TestFindTappable(Vector3 world)
        {
            return FindTappable(world) as Component;
        }

        /// <summary>TEST HOOK. Route a world-space tap exactly like OnTap does
        /// (collider first, then tree tile, then backhoe move) without needing the
        /// point to be on screen.</summary>
        internal void TestTapWorldRouted(Vector3 world)
        {
            world.z = 0f;

            // Make colliders reflect any transform written THIS frame before the overlap query,
            // exactly as TestContext.TapWorld already does for the screen-space path. Without
            // it this hook silently mis-resolves taps on anything that moves under its own
            // power (a trundling machine, a drifting duck): Physics2D.autoSyncTransforms is
            // false, so a collider stays at its last FixedUpdate position while the transform
            // has already advanced, and the tap lands on stale geometry — or on nothing.
            Physics2D.SyncTransforms();

            CancelTownAttractTour(); // same input-wins cancel as the real OnTap
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

            if (State.Is(GameState.Roam) && TryRouteRockTap(world))
            {
                return;
            }

            if (State.Is(GameState.Roam) && _backhoe != null)
            {
                _backhoe.MoveTo(world);
            }
        }

        /// <summary>TEST HOOK. Roll one rock-smash payout through the REAL gate
        /// (treasure vs shard-while-unowned). Lets the shard-gating case sample the
        /// distribution directly instead of grinding whole smashes.</summary>
        internal DugItemInfo TestRollRockPayout() => RollRockPayout(Vector3.zero);

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

            // Drop any attract tour BEFORE snapping the camera, so its glide can't write over
            // the snap a frame later.
            CancelTownAttractTour();

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
            _rockCooldownUntil.Clear();
            _rockSmashPayouts = 0;
            _courier = null;
            _carriedFruit = null;
            _sellers.Clear();
            _fruitSalesCount = 0;
            _paradeActive = false;
            _snifferTimer = SnifferIntervalSeconds;
            _snifferPulses = 0;
            _courierScanTimer = 0f;
            _idleTimer = 0f;

            // Attract tours are transient: TestForceRoam (above) already cancelled any running
            // one; rewind the tally + the alternation so every case sees the SAME first beat.
            _townTours = 0;
            _townTourNext = true;

            // Town builder: clear any in-progress/finished sites and rewind the queue. The
            // caller owns Save.Data.Town* (a save-state test sets/clears those explicitly);
            // this only wipes the live scene town between cases.
            _town?.TestResetTown();

            // Machine Friends: tear every machine back out of the world and forget every
            // discovery gate, so each case starts from day zero and trips exactly the gate it
            // cares about. Like the town, the caller owns Save.Data.Machine* — this only
            // wipes the live scene.
            _machines?.TestResetMachines();

            // The Dino-Matic: destroy the machine and forget the gate, so each case starts from
            // day zero and earns the arrival it cares about. Like the town and the machines,
            // the caller owns Save.Data — this only wipes the live scene.
            _dinoMatic?.TestResetDinoMatic();

            // The fossil collection is wiped between cases — otherwise a bank delta asserted
            // late in the suite would be measured against whatever an earlier case dug up, and
            // a case that revived a species would leave the board finished for everything
            // after it. Same convention as the town/machines: the SAVE is the caller's.
            _boneBank.Clear();
            _revived.Clear();
            BonesBanked = 0;
            LastBoneBanked = -1;
            _skeletonBoard?.Close();
            _skeletonBoard?.Refresh();

            // Re-bud every Berry Sprout (staggered) so a case that force-ripened one starts
            // the next case from a clean, all-budding garden.
            InitSprouts();
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
