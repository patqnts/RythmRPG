using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// A reusable, predefined arrangement of tiles, relative to an anchor cell at (0,0). Created from a
    /// Select-tool selection ("Save Selection as Stamp") and placed with the Stamp tool, which can also
    /// rotate/flip the whole arrangement using the same placement transform as every other paint tool.
    /// </summary>
    [Serializable]
    public struct StampCell
    {
        public int dx;
        public int dz;
        public TileCellData cell;
    }

    [CreateAssetMenu(menuName = "World Builder/Tile Stamp", fileName = "NewTileStamp")]
    public class TileStamp : ScriptableObject
    {
        [SerializeField] private string stampId;

        public string displayName = "Stamp";
        public int width = 1;
        public int height = 1;
        public List<StampCell> cells = new List<StampCell>();

        public string StampId
        {
            get
            {
                if (string.IsNullOrEmpty(stampId)) stampId = Guid.NewGuid().ToString("N");
                return stampId;
            }
        }

        private void Reset()
        {
            if (string.IsNullOrEmpty(stampId)) stampId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(displayName)) displayName = name;
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(stampId)) stampId = Guid.NewGuid().ToString("N");
        }
    }
}
