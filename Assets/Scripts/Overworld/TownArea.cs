using System.Collections.Generic;
using UnityEngine;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// Data-only description of the Dino Town district: its center, a curated,
    /// ordered list of building plots, and a rough footprint for containment
    /// queries. Holds NO construction logic — <see cref="TownController"/> reads
    /// this to decide where the next building breaks ground.
    ///
    /// Built and wired by SceneBuilder (the town-district ticket) via
    /// <see cref="Configure"/>; every accessor is null-tolerant so a partially-wired
    /// scene never throws, and integration tests can add + Configure one by hand when
    /// the district has not been placed yet. The <c>Center</c> / <c>PlotWorld(i)</c> /
    /// <c>PlotCount</c> / <c>ContainsWorld</c> surface is the stable contract other
    /// systems code against.
    /// </summary>
    public class TownArea : MonoBehaviour
    {
        [SerializeField] private OverworldMap _map;
        [SerializeField] private Vector3 _center;
        [SerializeField] private List<Vector3> _plotWorlds = new List<Vector3>();
        [SerializeField] private float _radius = 3f;

        /// <summary>World-space center of the district (fallback target when no plots).</summary>
        public Vector3 Center => _center;

        /// <summary>Number of building plots in curated build order.</summary>
        public int PlotCount => _plotWorlds != null ? _plotWorlds.Count : 0;

        /// <summary>The walkability map, exposed so callers can clamp stand-points to land.</summary>
        public OverworldMap Map => _map;

        /// <summary>World position of the plot at <paramref name="index"/> in build order
        /// (clamped). Falls back to the center when no plots are configured.</summary>
        public Vector3 PlotWorld(int index)
        {
            if (_plotWorlds == null || _plotWorlds.Count == 0)
            {
                return _center;
            }

            index = Mathf.Clamp(index, 0, _plotWorlds.Count - 1);
            return _plotWorlds[index];
        }

        /// <summary>True when a world point lies within the district footprint.</summary>
        public bool ContainsWorld(Vector3 world)
        {
            world.z = _center.z;
            return (world - _center).sqrMagnitude <= _radius * _radius;
        }

        /// <summary>Wire the district: center, ordered plot world-positions, footprint
        /// radius, and the map (for stand-point clamping). Copies the plot list so the
        /// caller may reuse its own.</summary>
        public void Configure(OverworldMap map, Vector3 center, IList<Vector3> plotWorlds, float radius)
        {
            _map = map;
            _center = center;
            _radius = Mathf.Max(0.5f, radius);
            _plotWorlds = new List<Vector3>();
            if (plotWorlds != null)
            {
                for (int i = 0; i < plotWorlds.Count; i++)
                {
                    _plotWorlds.Add(plotWorlds[i]);
                }
            }
        }

        /// <summary>A walkable stand-point BESIDE a plot for a commuting builder, so the
        /// crew rings the site instead of standing on the building. Clamped to walkable
        /// ground when a map is wired; otherwise the raw offset point.</summary>
        public Vector3 StandWorld(int plotIndex, int builderSlot)
        {
            Vector3 p = PlotWorld(plotIndex);
            float ang = builderSlot * (Mathf.PI * 2f / 3f) + 0.6f;
            Vector3 stand = p + new Vector3(Mathf.Cos(ang) * 0.9f, Mathf.Sin(ang) * 0.6f - 0.5f, 0f);
            stand.z = p.z;

            if (_map != null)
            {
                Vector3 w = _map.NearestWalkable(stand, out bool found);
                if (found)
                {
                    return w;
                }
            }

            return stand;
        }
    }
}
