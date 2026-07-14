using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// Wraps the isometric tilemaps and answers walkability queries. A cell is
    /// walkable when it has a Ground tile and no Water or Obstacle tile.
    /// </summary>
    public class OverworldMap : MonoBehaviour
    {
        [SerializeField] private Grid _grid;
        [SerializeField] private Tilemap _ground;
        [SerializeField] private Tilemap _water;
        [SerializeField] private Tilemap _obstacles;

        private readonly List<Vector3Int> _walkableCache = new List<Vector3Int>();
        private bool _cacheBuilt;

        public Grid Grid => _grid;

        public void Configure(Grid grid, Tilemap ground, Tilemap water, Tilemap obstacles)
        {
            _grid = grid;
            _ground = ground;
            _water = water;
            _obstacles = obstacles;
            _cacheBuilt = false;
        }

        public Vector3Int WorldToCell(Vector3 world)
        {
            return _grid != null ? _grid.WorldToCell(world) : Vector3Int.zero;
        }

        public Vector3 CellCenter(Vector3Int cell)
        {
            return _grid != null ? _grid.GetCellCenterWorld(cell) : (Vector3)cell;
        }

        public bool IsWalkableCell(Vector3Int cell)
        {
            if (_ground == null || _ground.GetTile(cell) == null)
            {
                return false;
            }

            if (_water != null && _water.GetTile(cell) != null)
            {
                return false;
            }

            if (_obstacles != null && _obstacles.GetTile(cell) != null)
            {
                return false;
            }

            return true;
        }

        public bool IsWalkableWorld(Vector3 world)
        {
            return IsWalkableCell(WorldToCell(world));
        }

        /// <summary>
        /// True when this walkable cell is a narrow corridor: pinched by unwalkable
        /// cells on BOTH sides of one grid axis (e.g. a 1-cell bridge over a stream, or
        /// a slot between rocks). The follower tightens its waypoint-arrival threshold
        /// through these so it threads the center instead of cutting the corner early.
        /// </summary>
        public bool IsCorridorCell(Vector3Int cell)
        {
            if (!IsWalkableCell(cell))
            {
                return false;
            }

            bool xPinched = !IsWalkableCell(cell + new Vector3Int(1, 0, 0)) &&
                            !IsWalkableCell(cell + new Vector3Int(-1, 0, 0));
            bool yPinched = !IsWalkableCell(cell + new Vector3Int(0, 1, 0)) &&
                            !IsWalkableCell(cell + new Vector3Int(0, -1, 0));
            return xPinched || yPinched;
        }

        /// <summary>The obstacle tile at a cell (tree/rock), or null. Lets the tap
        /// router identify tapped TREES without the tilemaps needing colliders.</summary>
        public TileBase ObstacleAt(Vector3Int cell)
        {
            return _obstacles != null ? _obstacles.GetTile(cell) : null;
        }

        /// <summary>
        /// Nearest walkable cell, but if the ring search fails (a tap deep in a large
        /// pond, past the ring radius) fall back to the first walkable cell along the
        /// line from the tap toward <paramref name="towardFallback"/> (the backhoe).
        /// A tap deep in the water then still drives the backhoe to the NEAR SHORE
        /// instead of being a silent no-op / honk. The march always succeeds while the
        /// reference point is on land, since the segment must cross the shoreline.
        /// </summary>
        public Vector3 NearestWalkable(Vector3 world, Vector3 towardFallback, out bool found)
        {
            Vector3 near = NearestWalkable(world, out found);
            if (found)
            {
                return near;
            }

            Vector3 d = towardFallback - world;
            d.z = 0f;
            float len = d.magnitude;
            if (len < 1e-4f)
            {
                found = false;
                return world;
            }

            Vector3 dir = d / len;
            const float stepLen = 0.25f;
            int steps = Mathf.CeilToInt(len / stepLen);
            for (int i = 1; i <= steps; i++)
            {
                Vector3 p = world + dir * Mathf.Min(stepLen * i, len);
                if (IsWalkableWorld(p))
                {
                    found = true;
                    return CellCenter(WorldToCell(p));
                }
            }

            found = false;
            return world;
        }

        /// <summary>
        /// Nearest walkable cell center to a world point. If the point itself is
        /// walkable it is returned unchanged; otherwise a small ring search runs.
        /// </summary>
        public Vector3 NearestWalkable(Vector3 world, out bool found)
        {
            Vector3Int origin = WorldToCell(world);
            if (IsWalkableCell(origin))
            {
                found = true;
                return CellCenter(origin);
            }

            for (int r = 1; r <= 8; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r)
                        {
                            continue; // only the ring edge
                        }

                        var c = new Vector3Int(origin.x + dx, origin.y + dy, origin.z);
                        if (IsWalkableCell(c))
                        {
                            found = true;
                            return CellCenter(c);
                        }
                    }
                }
            }

            found = false;
            return world;
        }

        private void BuildCache()
        {
            _walkableCache.Clear();
            if (_ground == null)
            {
                _cacheBuilt = true;
                return;
            }

            BoundsInt b = _ground.cellBounds;
            foreach (Vector3Int c in b.allPositionsWithin)
            {
                if (IsWalkableCell(c))
                {
                    _walkableCache.Add(c);
                }
            }

            _cacheBuilt = true;
        }

        public bool TryRandomWalkableCell(out Vector3Int cell)
        {
            if (!_cacheBuilt)
            {
                BuildCache();
            }

            if (_walkableCache.Count == 0)
            {
                cell = Vector3Int.zero;
                return false;
            }

            cell = _walkableCache[Random.Range(0, _walkableCache.Count)];
            return true;
        }

        /// <summary>Force a rebuild after the map is (re)painted at runtime.</summary>
        public void InvalidateCache() => _cacheBuilt = false;

        // TEST HOOK: whether the cell has any ground tile at all. Open ocean has
        // none — lets tests classify "coast" from the painted map instead of
        // re-deriving the island ellipse (which drifts at the rim).
        internal bool TestHasGround(Vector3Int cell)
        {
            return _ground != null && _ground.GetTile(cell) != null;
        }

        // TEST HOOK: whether the cell carries a water tile (pond or stream).
        internal bool TestHasWater(Vector3Int cell)
        {
            return _water != null && _water.GetTile(cell) != null;
        }

        // ------------------------------------------------------------ pathfinding

        private static readonly Vector3Int[] PathNeighbors =
        {
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
        };

        /// <summary>
        /// BFS over walkable cells from <paramref name="fromWorld"/> to (the nearest
        /// walkable cell of) <paramref name="toWorld"/>. Fills <paramref name="waypoints"/>
        /// with world-space cell centers, excluding the start cell and including the goal.
        /// Returns false when no route exists. The island is 24x24 so BFS is trivially fast.
        /// </summary>
        public bool FindPath(Vector3 fromWorld, Vector3 toWorld, List<Vector3> waypoints)
        {
            waypoints.Clear();

            Vector3Int start = WorldToCell(fromWorld);
            // Clamp the tap to walkable ground; if it is deep past the ring search
            // (far out in the pond) fall back to the near shore along the line back to
            // the mover, so a far-water tap still yields a real destination.
            Vector3 clampedGoal = NearestWalkable(toWorld, fromWorld, out bool goalFound);
            if (!goalFound)
            {
                return false;
            }

            Vector3Int goal = WorldToCell(clampedGoal);
            if (start == goal)
            {
                waypoints.Add(CellCenter(goal));
                return true;
            }

            var cameFrom = new Dictionary<Vector3Int, Vector3Int> { [start] = start };
            var frontier = new Queue<Vector3Int>();
            frontier.Enqueue(start);

            bool reached = false;
            while (frontier.Count > 0)
            {
                Vector3Int cur = frontier.Dequeue();
                if (cur == goal)
                {
                    reached = true;
                    break;
                }

                for (int i = 0; i < PathNeighbors.Length; i++)
                {
                    Vector3Int next = cur + PathNeighbors[i];
                    if (!cameFrom.ContainsKey(next) && IsWalkableCell(next))
                    {
                        cameFrom[next] = cur;
                        frontier.Enqueue(next);
                    }
                }
            }

            if (!reached)
            {
                return false;
            }

            for (Vector3Int c = goal; c != start; c = cameFrom[c])
            {
                waypoints.Add(CellCenter(c));
            }

            waypoints.Reverse();

            // BFS gives a 4-neighbour grid staircase; on the isometric map those grid
            // steps are SCREEN diagonals, so following the raw cell centers makes the
            // mover zigzag. Collapse the route with a line-of-sight string-pull so it
            // drives long smooth diagonals straight toward where the player clicked.
            SmoothWaypoints(fromWorld, waypoints);
            return true;
        }

        /// <summary>
        /// True if a straight walkable line connects <paramref name="a"/> and
        /// <paramref name="b"/>. Samples the segment at ~0.25-unit steps so an
        /// isometric diagonal that clips a water/obstacle cell between two walkable
        /// cells is correctly rejected.
        /// </summary>
        public bool HasLineOfSight(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            d.z = 0f;
            float len = d.magnitude;
            if (len < 1e-4f)
            {
                return IsWalkableWorld(b);
            }

            Vector3 dir = d / len;
            const float stepLen = 0.25f;
            int steps = Mathf.CeilToInt(len / stepLen);
            for (int i = 1; i < steps; i++)
            {
                if (!IsWalkableWorld(a + dir * (stepLen * i)))
                {
                    return false;
                }
            }

            return IsWalkableWorld(b);
        }

        // Half-width of the swept corridor used by <see cref="HasCorridorLineOfSight"/>,
        // in world units. Wide enough that a straight shortcut which merely grazes a
        // water/obstacle corner is rejected; narrow enough that genuinely open ground
        // still passes. Across a 1-cell bridge the offset samples land in the flanking
        // water, so the corridor test fails there ON PURPOSE and the follower falls
        // back to the raw cell-center waypoints (hugging the bridge center).
        private const float CorridorHalfWidth = 0.3f;

        /// <summary>
        /// Like <see cref="HasLineOfSight"/>, but samples THREE parallel lines — the
        /// center and two offset by +/-<see cref="CorridorHalfWidth"/> perpendicular —
        /// and requires all walkable. This gives the straight-line shortcut a body so
        /// it can never clip an obstacle/water corner that the single center ray misses.
        /// </summary>
        public bool HasCorridorLineOfSight(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            d.z = 0f;
            float len = d.magnitude;
            if (len < 1e-4f)
            {
                return IsWalkableWorld(b);
            }

            Vector3 dir = d / len;
            Vector3 perp = new Vector3(-dir.y, dir.x, 0f) * CorridorHalfWidth;
            const float stepLen = 0.25f;
            int steps = Mathf.CeilToInt(len / stepLen);
            for (int i = 1; i < steps; i++)
            {
                Vector3 p = a + dir * (stepLen * i);
                if (!IsWalkableWorld(p) ||
                    !IsWalkableWorld(p + perp) ||
                    !IsWalkableWorld(p - perp))
                {
                    return false;
                }
            }

            return IsWalkableWorld(b);
        }

        /// <summary>
        /// Greedy line-of-sight waypoint reduction (string-pulling). From each kept
        /// point, skip ahead to the FARTHEST later waypoint still reachable by a
        /// straight walkable line. The BFS topology guarantees adjacent cell centers
        /// are always reachable, so this only ever removes redundant staircase points.
        /// </summary>
        private void SmoothWaypoints(Vector3 fromWorld, List<Vector3> waypoints)
        {
            if (waypoints.Count <= 1)
            {
                return;
            }

            // Measure LOS from where the mover actually is, not the first cell center.
            var pts = new List<Vector3>(waypoints.Count + 1);
            pts.Add(fromWorld);
            pts.AddRange(waypoints);

            var reduced = new List<Vector3>(waypoints.Count);
            int i = 0;
            while (i < pts.Count - 1)
            {
                int j = pts.Count - 1;
                while (j > i + 1 && !HasCorridorLineOfSight(pts[i], pts[j]))
                {
                    j--;
                }

                reduced.Add(pts[j]); // never re-adds the start (pts[0])
                i = j;
            }

            waypoints.Clear();
            waypoints.AddRange(reduced);
        }
    }
}
