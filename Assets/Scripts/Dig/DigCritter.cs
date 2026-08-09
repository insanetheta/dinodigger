using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;

namespace DinoDigger.Dig
{
    /// <summary>
    /// THE DIG CRITTER (DinoDigger-u47) — a glowbug that pops out when a tile clears, scurries
    /// from cell to cell, and giggles out a coin if the child manages to land a tap on it.
    ///
    /// IT BLOCKS NOTHING. That is not a nice-to-have, it is the entire design constraint on a
    /// moving object inside a modal pit, and it is enforced structurally rather than by care:
    ///
    ///   * it is not in <c>_grid</c> and not in <c>_tiles</c>, so gravity, the settle loop, the
    ///     cascade's landing cracks and every clear path are all blind to it;
    ///   * it hides nothing and gates nothing — the round ends exactly when the last buried item
    ///     is uncovered, whether or not a critter is loose;
    ///   * its collider is a SMALL circle (about a third of a cell) so a tap aimed at the dirt
    ///     under it still digs; and
    ///   * it burrows away by itself after <c>GameConfig.DigCritterLifeSeconds</c>, so the worst
    ///     case for a child who ignores it completely is that a light blinks out.
    ///
    /// Missing one is never a loss. There is always another tile to clear, and another bug
    /// under it.
    /// </summary>
    public class DigCritter : MonoBehaviour, ITappable
    {
        private DigModeController _owner;
        private GameConfig _config;

        private float _hopTimer;
        private float _life;
        private int _hops;
        private Vector3 _restScale = Vector3.one;
        private float _bobPhase;

        // Scurry travel time. Short and fixed: the WAIT between scurries is the tunable beat
        // (config), the dash itself just has to look like a dash.
        private const float HopSeconds = 0.28f;
        private const float BobRate = 7f;
        private const float BobHeight = 0.06f;

        /// <summary>TEST HOOK. Scurries made since it appeared.</summary>
        internal int TestHops => _hops;

        /// <summary>TEST HOOK. Seconds of life left before it burrows away.</summary>
        internal float TestLifeLeft => _life;

        public void Build(DigModeController owner, GameConfig config)
        {
            _owner = owner;
            _config = config;
            _restScale = transform.localScale;   // the ONE authoritative scale (see OnTapped)
            _bobPhase = Random.value * Mathf.PI * 2f;

            _life = _config != null ? Mathf.Max(1f, _config.DigCritterLifeSeconds) : 10f;
            _hopTimer = HopInterval;

            // Pops out of the hole it was hiding in.
            transform.localScale = _restScale * 0.1f;
            Tween.ScaleTo(transform, _restScale, 0.25f);
        }

        private float HopInterval =>
            _config != null ? Mathf.Clamp(_config.DigCritterHopSeconds, 0.2f, 6f) : 1.5f;

        private void Update()
        {
            float dt = Time.deltaTime;

            // A little idle bob so a critter standing still still reads as alive. Position only —
            // it never writes scale, which is what keeps it clear of the punch on a tap.
            _bobPhase += dt * BobRate;
            Vector3 p = transform.position;
            p.y += Mathf.Cos(_bobPhase) * BobHeight * dt * BobRate;
            transform.position = p;

            _life -= dt;
            if (_life <= 0f)
            {
                Burrow();
                return;
            }

            _hopTimer -= dt;
            if (_hopTimer <= 0f)
            {
                _hopTimer = HopInterval;
                Scurry();
            }
        }

        /// <summary>One scurry: a low hop to another cell of the pit. The owner picks the target
        /// (it is the only thing that knows where the board is), so a critter can never wander
        /// out of the frame or off into the overworld.</summary>
        private void Scurry()
        {
            if (_owner == null)
            {
                return;
            }

            Vector3 from = transform.position;
            Vector3 to = _owner.CritterHopTarget(from);
            if ((to - from).sqrMagnitude < 0.0001f)
            {
                return;
            }

            _hops++;
            _owner.NoteCritterHop();
            Tween.MoveArc(transform, from, to, 0.45f, HopSeconds);
        }

        /// <summary>Uncaught: a little puff and it is gone. Nothing is lost and nothing is
        /// scored — an uncaught critter is a thing that happened, not a thing that failed.</summary>
        private void Burrow()
        {
            DigModeController owner = _owner;
            _owner = null; // one exit, whichever way it is reached
            owner?.DespawnCritter(this);
        }

        /// <summary>Caught. RESTING-SCALE SAFE: the delighted punch hands the transform over from
        /// any in-flight punch and re-bases first, so even a frantic toddler double-tap in the
        /// same frame cannot inflate it before it despawns.</summary>
        public void OnTapped(Vector2 worldPoint)
        {
            DigModeController owner = _owner;
            if (owner == null)
            {
                return; // already caught / already burrowing: a second tap is simply nothing
            }

            _owner = null;
            Tween.CancelPunch(transform);
            transform.localScale = _restScale;
            Tween.PunchScale(transform, 0.4f, 0.25f);

            owner.OnCritterCaught(this);
        }
    }
}
