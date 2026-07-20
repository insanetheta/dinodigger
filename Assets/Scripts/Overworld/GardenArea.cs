using System.Collections.Generic;
using UnityEngine;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// Data-only description of the Berry Patch garden: its cell rectangle, world
    /// center, and the three sprout world-positions planted inside it. Holds NO
    /// growing logic — each <see cref="BerrySprout"/> ticks itself. Built and wired by
    /// SceneBuilder (sibling of <see cref="TownArea"/> / <see cref="MeadowArea"/>);
    /// consumed by SpawnManager (mound exclusion) and the integration tests. Every
    /// accessor is null-tolerant so a partially-wired scene never throws.
    ///
    /// The patch sits on plain walkable grass — dinos and the backhoe stroll through
    /// it and harvested fruit lands on it — so, like the town district, it is NOT
    /// excluded by walkability; the rect (plus a small mound-clearance margin) is what
    /// keeps dig mounds from ever crowding the sprouts.
    /// </summary>
    public class GardenArea : MonoBehaviour
    {
        [SerializeField] private OverworldMap _map;

        // Cell rect of the reserved garden patch (sprouts live inside it).
        [SerializeField] private int _minX;
        [SerializeField] private int _minY;
        [SerializeField] private int _maxX;
        [SerializeField] private int _maxY;

        // World-space centers of the planted sprouts (build order).
        [SerializeField] private List<Vector3> _sproutWorlds = new List<Vector3>();

        /// <summary>Number of sprout spots in the patch.</summary>
        public int SproutCount => _sproutWorlds != null ? _sproutWorlds.Count : 0;

        /// <summary>World-space center of the garden patch.</summary>
        public Vector3 Center
        {
            get
            {
                if (_map == null)
                {
                    return transform.position;
                }

                var c = new Vector3Int((_minX + _maxX) / 2, (_minY + _maxY) / 2, 0);
                return _map.CellCenter(c);
            }
        }

        /// <summary>World position of sprout <paramref name="index"/> (clamped).</summary>
        public Vector3 SproutWorld(int index)
        {
            if (_sproutWorlds == null || _sproutWorlds.Count == 0)
            {
                return Center;
            }

            index = Mathf.Clamp(index, 0, _sproutWorlds.Count - 1);
            return _sproutWorlds[index];
        }

        /// <summary>Wire the patch: its cell rect, sprout world-positions, and the map.
        /// Copies the sprout list so the caller may reuse its own.</summary>
        public void Configure(OverworldMap map, int minX, int minY, int maxX, int maxY,
            IList<Vector3> sproutWorlds)
        {
            _map = map;
            _minX = minX;
            _minY = minY;
            _maxX = maxX;
            _maxY = maxY;
            _sproutWorlds = new List<Vector3>();
            if (sproutWorlds != null)
            {
                for (int i = 0; i < sproutWorlds.Count; i++)
                {
                    _sproutWorlds.Add(sproutWorlds[i]);
                }
            }
        }

        /// <summary>True when the world point lies inside the reserved garden rect.</summary>
        public bool ContainsWorld(Vector3 world)
        {
            return ContainsWorldExpanded(world, 0);
        }

        /// <summary>True when the world point lies inside the garden rect grown by
        /// <paramref name="cellMargin"/> cells on every side. SpawnManager / SceneBuilder
        /// use a small margin so a dig mound never spawns tight against a sprout collider
        /// (the isometric diagonal neighbours pack surprisingly close together).</summary>
        public bool ContainsWorldExpanded(Vector3 world, int cellMargin)
        {
            if (_map == null)
            {
                return false;
            }

            Vector3Int c = _map.WorldToCell(world);
            return c.x >= _minX - cellMargin && c.x <= _maxX + cellMargin &&
                   c.y >= _minY - cellMargin && c.y <= _maxY + cellMargin;
        }
    }
}
