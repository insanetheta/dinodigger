using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// A fruit sprout in the Berry Patch garden. Close sibling of <see cref="DigMound"/>:
    /// a tappable prop with a sparkle child that cycles between two states.
    ///
    ///   BUDDING — a small sprite. A tap wiggles it and puffs a leaf rustle (never
    ///             ignored, no fruit). After <see cref="GameConfig.SproutRipenSeconds"/>
    ///             it ripens.
    ///   RIPE    — swells (scale-up) and sparkles like a mound. A tap harvests: one
    ///             random-variant fruit pops out in an arc to a nearby landing spot
    ///             through the standard pickup path (so it rides the whole feed chain),
    ///             then the sprout snaps back to a bud and regrows after
    ///             <see cref="GameConfig.SproutRegrowSeconds"/>.
    ///
    /// Zero new hand-made art: the base is the dig-mound sprite tinted green, with a
    /// scaled-down fruit sprite poking out of it. Never saved — every sprout starts
    /// budding each session (SceneBuilder staggers their initial timers so the three
    /// never ripen in sync). Toddler rules: no fail states, no text, every tap responds.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class BerrySprout : MonoBehaviour, ITappable
    {
        private enum SproutState { Budding, Ripe }

        [SerializeField] private SpriteRenderer _base;   // green mound base
        [SerializeField] private SpriteRenderer _fruit;  // fruit poking out of the base
        [SerializeField] private ParticleSystem _sparkle;

        // Fruit-renderer scale in each state: a shy bud that swells into a full berry.
        private const float BudFruitScale = 0.35f;
        private const float RipeFruitScale = 1.0f;

        private GameConfig _config;
        private PlaceholderLibrary _lib;

        private SproutState _state = SproutState.Budding;
        private float _timer;          // counts down to the next state change
        private int _variant;          // fruit sprite variant currently on show / to harvest
        private Vector3 _fruitBaseScale = Vector3.one;

        /// <summary>True while the sprout is grown and ready to harvest.</summary>
        public bool IsRipe => _state == SproutState.Ripe;

        private void Awake()
        {
            if (_fruit != null)
            {
                _fruitBaseScale = _fruit.transform.localScale;
            }
        }

        /// <summary>(Re)start this sprout budding. <paramref name="initialRipenSeconds"/>
        /// lets SceneBuilder stagger the three sprouts so they don't ripen together.</summary>
        public void Init(GameConfig config, PlaceholderLibrary lib, float initialRipenSeconds)
        {
            _config = config;
            _lib = lib;
            RollVariant();
            EnterBudding(initialRipenSeconds);
        }

        private void Update()
        {
            if (_timer <= 0f)
            {
                return; // nothing scheduled (unconfigured, or waiting on a tap while ripe)
            }

            _timer -= Time.deltaTime;
            if (_timer > 0f)
            {
                return;
            }

            _timer = 0f;
            if (_state == SproutState.Budding)
            {
                Ripen();
            }
        }

        public void OnTapped(Vector2 worldPoint)
        {
            if (_state == SproutState.Ripe)
            {
                Harvest();
            }
            else
            {
                Wiggle();
            }
        }

        // ------------------------------------------------------------ states

        private void EnterBudding(float ripenSeconds)
        {
            _state = SproutState.Budding;
            _timer = Mathf.Max(0f, ripenSeconds);

            if (_fruit != null)
            {
                _fruit.transform.localScale = _fruitBaseScale * BudFruitScale;
            }

            if (_sparkle != null)
            {
                _sparkle.Stop();
            }
        }

        private void Ripen()
        {
            _state = SproutState.Ripe;
            RollVariant();

            if (_fruit != null)
            {
                Tween.ScaleTo(_fruit.transform, _fruitBaseScale * RipeFruitScale, 0.45f);
            }

            if (_sparkle != null)
            {
                _sparkle.Play();
                _sparkle.Emit(10);
            }
        }

        /// <summary>Budding tap: a wiggle plus a green leaf rustle so the tap always
        /// does SOMETHING, but no fruit until the sprout ripens.</summary>
        private void Wiggle()
        {
            Tween.PunchScale(transform, 0.2f, 0.3f);
            GameManager.Instance?.SproutRustle(transform.position);
        }

        /// <summary>Ripe tap: pop one fruit out through the standard pickup path, then
        /// snap back to a bud and start the regrow timer.</summary>
        private void Harvest()
        {
            Tween.PunchScale(transform, 0.25f, 0.3f);
            GameManager.Instance?.SpawnSproutFruit(transform.position, _variant);

            float regrow = _config != null ? _config.SproutRegrowSeconds : 25f;
            EnterBudding(regrow);
        }

        private void RollVariant()
        {
            int variants = _config != null ? Mathf.Max(1, _config.FruitVariants) : 1;
            _variant = Random.Range(0, variants);
            if (_fruit != null && _lib != null)
            {
                Sprite s = _lib.Fruit(_variant);
                if (s != null)
                {
                    _fruit.sprite = s;
                }
            }
        }

        /// <summary>Idle-attract: a ripe sprout bounces + pulses its sparkle to invite a
        /// tap, exactly like a mound. A budding sprout has nothing to harvest, so it
        /// stays put (the pulse target picker skips it).</summary>
        public void AttractPulse()
        {
            if (_state != SproutState.Ripe)
            {
                return;
            }

            Tween.PunchScale(transform, 0.3f, 0.5f);
            if (_sparkle != null)
            {
                _sparkle.Emit(8);
            }
        }

        // ----------------------------------------------------------- TEST HOOKS

        /// <summary>TEST HOOK. Ripen right now instead of waiting out the timer.</summary>
        internal void TestForceRipen()
        {
            if (_state != SproutState.Ripe)
            {
                _timer = 0f;
                Ripen();
            }
        }

        /// <summary>TEST HOOK. The fruit variant currently on show (and next to harvest).</summary>
        internal int TestVariant => _variant;
    }
}
