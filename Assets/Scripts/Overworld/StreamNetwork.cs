using System.Collections.Generic;
using UnityEngine;

namespace DinoDigger.Overworld
{
    /// <summary>
    /// Holds the meandering stream courses carved into the island by SceneBuilder.
    /// Each course is an ordered list of cells running from a source (a coast/pond
    /// head) to a mouth; the water/bridge painting lives in the tilemaps, this
    /// component just remembers the ROUTES so ducks can drift along them.
    ///
    /// Lives on the Grid alongside <see cref="OverworldMap"/>, wired by SceneBuilder
    /// (Configure at build time; the cell lists serialize into the scene). Fully
    /// null/empty-tolerant: with no streams the duck spawner simply never spawns.
    /// </summary>
    public class StreamNetwork : MonoBehaviour
    {
        // Unity can't serialize a List<List<>>, so each course is wrapped.
        [System.Serializable]
        private class Course
        {
            public List<Vector3Int> cells = new List<Vector3Int>();
        }

        [SerializeField] private Grid _grid;
        [SerializeField] private List<Course> _courses = new List<Course>();

        /// <summary>Number of stream courses.</summary>
        public int Count => _courses != null ? _courses.Count : 0;

        /// <summary>Cells of stream course <paramref name="index"/> (source -> mouth),
        /// or null if out of range.</summary>
        public IReadOnlyList<Vector3Int> CourseCells(int index)
        {
            if (_courses == null || index < 0 || index >= _courses.Count)
            {
                return null;
            }

            return _courses[index].cells;
        }

        public Vector3 CellCenter(Vector3Int cell)
        {
            return _grid != null ? _grid.GetCellCenterWorld(cell) : (Vector3)cell;
        }

        /// <summary>Build-time wiring: store the grid + a deep copy of the courses.</summary>
        public void Configure(Grid grid, List<List<Vector3Int>> courses)
        {
            _grid = grid;
            _courses = new List<Course>();
            if (courses == null)
            {
                return;
            }

            foreach (List<Vector3Int> c in courses)
            {
                if (c == null || c.Count == 0)
                {
                    continue;
                }

                _courses.Add(new Course { cells = new List<Vector3Int>(c) });
            }
        }

        /// <summary>Fill <paramref name="worldOut"/> with the world-space cell centers
        /// of course <paramref name="index"/>. Returns false when the course is empty
        /// or missing.</summary>
        public bool TryGetCourseWorld(int index, List<Vector3> worldOut)
        {
            worldOut.Clear();
            IReadOnlyList<Vector3Int> cells = CourseCells(index);
            if (cells == null || cells.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                worldOut.Add(CellCenter(cells[i]));
            }

            return true;
        }
    }
}
