using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// A dug-up item on the overworld. Pops out of the ground in an arc, bounces,
    /// then behaves per type: eggs wobble + hatch, fruit waits to be tapped/eaten,
    /// treasure flies to the corner counter.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class ItemPickup : MonoBehaviour, ITappable
    {
        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private ParticleSystem _sparkle;

        private ItemType _type;
        private DinoType _dinoType;
        private GameConfig _config;
        private PlaceholderLibrary _lib;

        private bool _landed;
        private bool _consumed;
        private bool _edible;   // fruit is tappable once landed
        private bool _carried;  // riding on a Triceratops courier's head
        private bool _hatched;  // egg actually hatched (vs destroyed still un-hatched)
        private Vector3 _restPos;
        private float _bobPhase;

        public ItemType Type => _type;
        public DinoType DinoType => _dinoType;
        public bool IsConsumed => _consumed;
        public bool IsCarried => _carried;

        /// <summary>Landed, edible, untouched fruit — a candidate for the Trike courier.</summary>
        public bool IsCarryableFruit => _type == ItemType.Fruit && _landed && _edible &&
                                        !_consumed && !_carried;

        private void Awake()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<SpriteRenderer>();
            }

            _bobPhase = Random.value * Mathf.PI * 2f;
        }

        /// <summary>Wire renderer + sparkle when this pickup is built at runtime.</summary>
        public void AttachSparkle(SpriteRenderer renderer, ParticleSystem sparkle)
        {
            if (renderer != null)
            {
                _renderer = renderer;
            }

            _sparkle = sparkle;
        }

        public void Init(DugItemInfo info, Vector3 landing, GameConfig config, PlaceholderLibrary lib)
        {
            _type = info.Type;
            _dinoType = info.DinoType;
            _config = config;
            _lib = lib;

            ApplySprite(info.Variant);
            SetColliderEnabled(false);

            transform.position = info.OriginWorld;
            transform.localScale = Vector3.one;

            _restPos = landing;
            Tween.MoveArc(transform, info.OriginWorld, landing, 1.6f, 0.55f, OnLanded);
        }

        private void ApplySprite(int variant)
        {
            if (_renderer == null || _lib == null)
            {
                return;
            }

            switch (_type)
            {
                case ItemType.Egg:
                    DinoDefinition def = _config != null ? _config.GetDino(_dinoType) : null;
                    _renderer.sprite = def != null ? def.EggSprite : null;
                    if (def != null && _renderer.sprite == null)
                    {
                        _renderer.color = def.EggColor;
                    }
                    break;
                case ItemType.Fruit:
                    _renderer.sprite = _lib.Fruit(variant);
                    break;
                case ItemType.Treasure:
                    _renderer.sprite = _lib.Treasure(variant);
                    break;
                case ItemType.Shard:
                    _renderer.sprite = _lib.ShardSprite;
                    break;
            }
        }

        private void OnLanded()
        {
            if (this == null)
            {
                return; // destroyed mid-arc (dig re-entry, reset) — the tween's completion still fires
            }

            _landed = true;
            _restPos = transform.position;
            Tween.PunchScale(transform, 0.4f, 0.35f);

            if (_sparkle != null)
            {
                _sparkle.Emit(14);
            }

            GameManager.Instance?.Audio?.Chime();

            switch (_type)
            {
                case ItemType.Egg:
                    BeginHatch();
                    break;
                case ItemType.Fruit:
                    _edible = true;
                    SetColliderEnabled(true);
                    break;
                case ItemType.Treasure:
                    SetColliderEnabled(false);
                    GameManager.Instance?.CollectTreasure(this);
                    break;
                case ItemType.Shard:
                    // Sparkly shell piece: auto-collects like treasure, but flies to
                    // the nest (or a graceful fallback) instead of the corner counter.
                    SetColliderEnabled(false);
                    GameManager.Instance?.CollectShard(this);
                    break;
            }
        }

        private void Update()
        {
            if (!_landed || _consumed || _carried || _type != ItemType.Fruit)
            {
                return;
            }

            // Gentle idle bob for fruit so it reads as "pick me".
            _bobPhase += Time.deltaTime * 3f;
            float y = Mathf.Sin(_bobPhase) * 0.06f;
            transform.position = _restPos + new Vector3(0f, y, 0f);
        }

        // ----- Carrying (Triceratops fruit courier) -----

        /// <summary>Picked up by a courier dino: stop bobbing/tapping while it rides
        /// (the courier parents this transform over its head).</summary>
        public void BeginCarried()
        {
            _carried = true;
            SetColliderEnabled(false);
        }

        /// <summary>Set down at a new rest spot: normal bob + tap-to-eat resume.</summary>
        public void EndCarried(Vector3 restPos)
        {
            _carried = false;
            transform.localScale = Vector3.one;
            transform.position = restPos;
            _restPos = restPos;

            if (!_consumed && _type == ItemType.Fruit)
            {
                _edible = true;
                SetColliderEnabled(true);
                if (_sparkle != null)
                {
                    _sparkle.Emit(6);
                }
            }
        }

        // ----- Egg -----

        private void BeginHatch()
        {
            SetColliderEnabled(false);
            // Wobble 3 times, then hatch.
            Tween.ShakeRotation(transform, 16f, 1.2f, 3, () =>
            {
                if (this == null)
                {
                    return;
                }

                // The egg becomes a real (owned) dino now: mark it hatched so the
                // OnDestroy release below is skipped — HatchEgg releases the species'
                // reservation and takes ownership atomically.
                _hatched = true;
                GameManager.Instance?.HatchEgg(_dinoType, transform.position);
                Destroy(gameObject);
            });
        }

        // ----- Fruit -----

        public void OnTapped(Vector2 worldPoint)
        {
            if (_consumed || !_edible || _type != ItemType.Fruit)
            {
                return;
            }

            Tween.PunchScale(transform, 0.25f, 0.25f);
            GameManager.Instance?.RequestFeed(this);
        }

        /// <summary>Called when a dino reaches and eats this fruit.</summary>
        public void ConsumeAsFood()
        {
            if (_consumed)
            {
                return;
            }

            _consumed = true;
            _edible = false;
            SetColliderEnabled(false);

            if (_sparkle != null)
            {
                _sparkle.Emit(10);
            }

            Tween.ScaleTo(transform, Vector3.zero, 0.25f, () =>
            {
                if (this != null)
                {
                    Destroy(gameObject);
                }
            });
        }

        private void SetColliderEnabled(bool enabled)
        {
            var col = GetComponent<Collider2D>();
            if (col != null)
            {
                col.enabled = enabled;
            }
        }

        /// <summary>An egg that is destroyed WITHOUT hatching (dig re-entry, TestReset,
        /// a scene teardown) must release the egg species it reserved so a future dig
        /// can roll it again. A hatched egg's reservation was already released by
        /// HatchEgg. Guarded against play-mode exit / domain reload, where the
        /// GameManager (and its reservation set) is already gone — a missed release is
        /// harmless there because the whole set dies with it.</summary>
        private void OnDestroy()
        {
            if (_type != ItemType.Egg || _hatched)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return;
            }
#endif

            GameManager.Instance?.ReleaseEggSpecies(_dinoType);
        }
    }
}
