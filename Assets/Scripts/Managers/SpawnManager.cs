using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Overworld;

namespace DinoDigger.Managers
{
    /// <summary>
    /// Owns the dig-mound pool. When a mound is consumed it schedules a respawn
    /// at a fresh random walkable cell after <see cref="GameConfig.MoundRespawnSeconds"/>.
    /// Driven each frame by GameManager.Tick so it needs no MonoBehaviour of its own.
    /// </summary>
    public class SpawnManager
    {
        private struct Pending
        {
            public DigMound Mound;
            public float TimeLeft;
        }

        // Keep respawns at least this far (world units) from the backhoe so a
        // mound never pops up on top of the player. 4 units on the 48x48 island.
        private const float BackhoeClearSqr = 16f;

        // Mound-to-mound spacing on respawn (world units squared): matches the
        // doubled build-time separation so holes stay ~2x apart.
        private const float MoundClearSqr = 9f;

        private GameConfig _config;
        private OverworldMap _map;
        private List<DigMound> _mounds;
        private Transform _backhoe;
        private MeadowArea _meadow;
        private readonly List<Pending> _pending = new List<Pending>();

        public void Init(GameConfig config, OverworldMap map, List<DigMound> mounds, Transform backhoe)
        {
            _config = config;
            _map = map;
            _mounds = mounds ?? new List<DigMound>();
            _backhoe = backhoe;
        }

        /// <summary>Optional: mounds never respawn inside the dino meadow.</summary>
        public void SetMeadow(MeadowArea meadow)
        {
            _meadow = meadow;
        }

        public void ScheduleRespawn(DigMound mound)
        {
            if (mound == null)
            {
                return;
            }

            mound.Consume();
            float delay = _config != null ? _config.MoundRespawnSeconds : 20f;
            _pending.Add(new Pending { Mound = mound, TimeLeft = delay });
        }

        public void Tick(float dt)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending p = _pending[i];
                p.TimeLeft -= dt;
                if (p.TimeLeft <= 0f)
                {
                    RespawnNow(p.Mound);
                    _pending.RemoveAt(i);
                }
                else
                {
                    _pending[i] = p;
                }
            }
        }

        private void RespawnNow(DigMound mound)
        {
            if (mound == null || _map == null)
            {
                return;
            }

            // Try a handful of times to land on a cell not occupied by another mound.
            for (int attempt = 0; attempt < 24; attempt++)
            {
                if (!_map.TryRandomWalkableCell(out Vector3Int cell))
                {
                    break;
                }

                Vector3 world = _map.CellCenter(cell);
                if (!IsOccupied(world, mound) && !TooCloseToBackhoe(world) && !InMeadow(world))
                {
                    mound.Respawn(world);
                    return;
                }
            }

            // Fallback: just respawn in place.
            mound.Respawn(mound.transform.position);
        }

        private bool InMeadow(Vector3 world)
        {
            return _meadow != null && _meadow.ContainsOuter(world);
        }

        private bool TooCloseToBackhoe(Vector3 world)
        {
            if (_backhoe == null)
            {
                return false;
            }

            return (_backhoe.position - world).sqrMagnitude < BackhoeClearSqr;
        }

        private bool IsOccupied(Vector3 world, DigMound self)
        {
            if (_mounds == null)
            {
                return false;
            }

            for (int i = 0; i < _mounds.Count; i++)
            {
                DigMound m = _mounds[i];
                if (m == null || m == self || !m.IsActive)
                {
                    continue;
                }

                if ((m.transform.position - world).sqrMagnitude < MoundClearSqr)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
