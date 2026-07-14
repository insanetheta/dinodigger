using UnityEngine;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// The fenced dino home on the island. Holds the meadow's cell rectangle
    /// (outer ring = the decorative fence, interior = where residents wander)
    /// and answers containment / random-point queries in world space. Built and
    /// wired by SceneBuilder; consumed by GameManager, DinoController and
    /// SpawnManager (mound exclusion). Everything is null-tolerant so a legacy
    /// scene without a meadow still runs (dinos simply have no home to go to).
    /// </summary>
    public class MeadowArea : MonoBehaviour
    {
        [SerializeField] private OverworldMap _map;

        // Outer cell rect: the full patch INCLUDING the fence ring.
        [SerializeField] private int _minX;
        [SerializeField] private int _minY;
        [SerializeField] private int _maxX;
        [SerializeField] private int _maxY;

        // World-space gate center on the south side (fence opening).
        [SerializeField] private Vector3 _gateWorld;

        public Vector3 GateWorld => _gateWorld;

        /// <summary>World-space home of the egg-shard nest: the north-east corner cell
        /// of the wanderable interior (one cell inside the fence ring). A unique,
        /// stable spot the shard fly-to animation targets and the ceremony hatch spawns
        /// at. Falls back to the transform position on a legacy scene with no map.</summary>
        public Vector3 NestWorld
        {
            get
            {
                if (_map == null)
                {
                    return transform.position;
                }

                var c = new Vector3Int(_maxX - 1, _maxY - 1, 0);
                return _map.CellCenter(c);
            }
        }

        /// <summary>World-space center of the meadow patch.</summary>
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

        public void Configure(OverworldMap map, int minX, int minY, int maxX, int maxY, Vector3 gateWorld)
        {
            _map = map;
            _minX = minX;
            _minY = minY;
            _maxX = maxX;
            _maxY = maxY;
            _gateWorld = gateWorld;
        }

        /// <summary>True when the world point lies inside the FULL patch (fence ring included).
        /// Used for mound exclusion so nothing spawns against the fence either.</summary>
        public bool ContainsOuter(Vector3 world)
        {
            return ContainsCell(world, 0);
        }

        /// <summary>True when the world point lies inside the wanderable interior
        /// (one cell inside the fence ring).</summary>
        public bool ContainsInterior(Vector3 world)
        {
            return ContainsCell(world, 1);
        }

        private bool ContainsCell(Vector3 world, int inset)
        {
            if (_map == null)
            {
                return false;
            }

            Vector3Int cell = _map.WorldToCell(world);
            return cell.x >= _minX + inset && cell.x <= _maxX - inset &&
                   cell.y >= _minY + inset && cell.y <= _maxY - inset;
        }

        /// <summary>A random walkable world point in the meadow interior. Falls back
        /// to the meadow center when nothing suitable is found.</summary>
        public Vector3 RandomInteriorPoint()
        {
            if (_map == null)
            {
                return transform.position;
            }

            for (int attempt = 0; attempt < 12; attempt++)
            {
                var cell = new Vector3Int(
                    Random.Range(_minX + 1, _maxX),      // max exclusive: interior only
                    Random.Range(_minY + 1, _maxY), 0);
                if (_map.IsWalkableCell(cell))
                {
                    return _map.CellCenter(cell);
                }
            }

            return Center;
        }
    }
}
