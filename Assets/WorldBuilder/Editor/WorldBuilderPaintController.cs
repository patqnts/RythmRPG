using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.WorldBuilder.Editor
{
    /// <summary>
    /// Applies every paint-tool edit (Brush/Eraser/Eyedropper from Phase 1, plus Phase 2's
    /// Rectangle/Fill/Line/Select/Move/Stamp/Copy-Paste/Rotate/Flip and terrain-transition painting) to a
    /// <see cref="WorldBuilderWorld"/>, with Unity Undo integration. Every method here records one Undo
    /// step and triggers exactly one dirty-chunk rebuild, so a whole drag/click operation always undoes
    /// as a single step.
    /// </summary>
    public class WorldBuilderPaintController
    {
        private readonly HashSet<TileCoord> strokeTouched = new HashSet<TileCoord>();
        private bool strokeActive;

        public void BeginStroke()
        {
            strokeTouched.Clear();
            strokeActive = true;
        }

        public void EndStroke(WorldBuilderWorld world)
        {
            strokeActive = false;
            strokeTouched.Clear();
        }

        // ---------------------------------------------------------------
        // Brush / Eraser (continuous drag painting)
        // ---------------------------------------------------------------

        public void Apply(WorldBuilderWorld world, TileCoord centerCoord, TileDefinition selectedTile)
        {
            if (world == null) return;

            TileLayerData layer = world.ActiveLayer;
            if (layer.locked) return;

            int brushSize = WorldBuilderPrefs.BrushSize;
            int half = brushSize / 2;

            RecordUndo(world, "Paint Tiles");
            TerrainTransitionSet terrainSet = ResolveActiveTerrainSet(world);

            for (int dx = -half; dx < brushSize - half; dx++)
            {
                for (int dz = -half; dz < brushSize - half; dz++)
                {
                    TileCoord coord = new TileCoord(centerCoord.x + dx, centerCoord.z + dz, centerCoord.elevationLevel);
                    if (strokeActive && !strokeTouched.Add(coord)) continue;

                    switch (WorldBuilderPrefs.ActiveTool)
                    {
                        case PaintTool.Brush:
                            PlaceOne(world, layer, coord, selectedTile, terrainSet);
                            break;
                        case PaintTool.Eraser:
                            EraseOne(world, layer, coord, terrainSet);
                            break;
                    }
                }
            }

            FinishEdit(world);
        }

        // ---------------------------------------------------------------
        // Rectangle
        // ---------------------------------------------------------------

        public void ApplyRectangle(WorldBuilderWorld world, TileCoord a, TileCoord b, TileDefinition selectedTile)
        {
            if (world == null || selectedTile == null) return;

            TileLayerData layer = world.ActiveLayer;
            if (layer.locked) return;

            RecordUndo(world, "Rectangle Fill");
            TerrainTransitionSet terrainSet = ResolveActiveTerrainSet(world);

            foreach (TileCoord coord in TileGridUtility.Rectangle(a, b))
            {
                PlaceOne(world, layer, coord, selectedTile, terrainSet);
            }

            FinishEdit(world);
        }

        // ---------------------------------------------------------------
        // Line
        // ---------------------------------------------------------------

        public void ApplyLine(WorldBuilderWorld world, TileCoord a, TileCoord b, TileDefinition selectedTile)
        {
            if (world == null || selectedTile == null) return;

            TileLayerData layer = world.ActiveLayer;
            if (layer.locked) return;

            RecordUndo(world, "Line Paint");
            TerrainTransitionSet terrainSet = ResolveActiveTerrainSet(world);

            foreach (TileCoord coord in TileGridUtility.Line(a, b))
            {
                PlaceOne(world, layer, coord, selectedTile, terrainSet);
            }

            FinishEdit(world);
        }

        // ---------------------------------------------------------------
        // Fill (flood fill)
        // ---------------------------------------------------------------

        public bool ApplyFill(WorldBuilderWorld world, TileCoord seed, TileDefinition selectedTile)
        {
            if (world == null || selectedTile == null) return false;

            TileLayerData layer = world.ActiveLayer;
            if (layer.locked) return false;

            List<TileCoord> region = TileGridUtility.FloodFill(layer, seed, WorldBuilderPrefs.FillMaxTiles, out bool capped);
            if (region.Count == 0) return capped;

            RecordUndo(world, "Fill");
            TerrainTransitionSet terrainSet = ResolveActiveTerrainSet(world);

            foreach (TileCoord coord in region)
            {
                PlaceOne(world, layer, coord, selectedTile, terrainSet);
            }

            FinishEdit(world);
            return capped;
        }

        // ---------------------------------------------------------------
        // Stamp
        // ---------------------------------------------------------------

        public void ApplyStamp(WorldBuilderWorld world, TileCoord anchor, TileStamp stamp)
        {
            if (world == null || stamp == null) return;

            TileLayerData layer = world.ActiveLayer;
            if (layer.locked) return;

            RecordUndo(world, "Place Stamp");
            int rotation = WorldBuilderPrefs.PlacementRotationSteps;
            TileFlip flip = WorldBuilderPrefs.PlacementFlip;

            foreach (StampCell stampCell in stamp.cells)
            {
                (int dx, int dz) offset = TileTransformUtility.FlipOffset(stampCell.dx, stampCell.dz, flip);
                offset = TileTransformUtility.RotateOffset(offset.dx, offset.dz, rotation);

                TileCoord coord = new TileCoord(anchor.x + offset.dx, anchor.z + offset.dz, anchor.elevationLevel);

                TileCellData cell = stampCell.cell;
                cell.rotationSteps = TileTransformUtility.CombineRotation(cell.rotationSteps, rotation);
                cell.flip = TileTransformUtility.CombineFlip(cell.flip, flip);

                world.SetTile(layer, coord, cell);
            }

            FinishEdit(world);
        }

        // ---------------------------------------------------------------
        // Select / Move / Copy / Paste / Rotate / Flip
        // ---------------------------------------------------------------

        public TileDefinition Eyedrop(WorldBuilderWorld world, TileCoord coord)
        {
            if (world == null) return null;

            TileLayerData layer = world.ActiveLayer;
            if (!layer.TryGetTile(coord, out TileCellData cell) || cell.IsEmpty) return null;

            return world.palette != null ? world.palette.FindById(cell.tileId) : null;
        }

        public void CopySelection(WorldBuilderWorld world, WorldBuilderSelection selection)
        {
            if (world == null || !selection.HasSelection) return;
            WorldBuilderClipboard.Copy(world.ActiveLayer, selection.Min, selection.Max);
        }

        public void DeleteSelection(WorldBuilderWorld world, WorldBuilderSelection selection)
        {
            if (world == null || !selection.HasSelection) return;

            TileLayerData layer = world.ActiveLayer;
            if (layer.locked) return;

            RecordUndo(world, "Delete Selection");
            TerrainTransitionSet terrainSet = ResolveActiveTerrainSet(world);

            for (int x = selection.Min.x; x <= selection.Max.x; x++)
            {
                for (int z = selection.Min.z; z <= selection.Max.z; z++)
                {
                    EraseOne(world, layer, new TileCoord(x, z, selection.Min.elevationLevel), terrainSet);
                }
            }

            FinishEdit(world);
        }

        public void PasteClipboard(WorldBuilderWorld world, TileCoord anchor)
        {
            if (world == null || !WorldBuilderClipboard.HasContent) return;

            TileLayerData layer = world.ActiveLayer;
            if (layer.locked) return;

            RecordUndo(world, "Paste");
            int rotation = WorldBuilderPrefs.PlacementRotationSteps;
            TileFlip flip = WorldBuilderPrefs.PlacementFlip;

            foreach (WorldBuilderClipboard.ClipboardCell clip in WorldBuilderClipboard.Snapshot())
            {
                (int dx, int dz) offset = TileTransformUtility.FlipOffset(clip.dx, clip.dz, flip);
                offset = TileTransformUtility.RotateOffset(offset.dx, offset.dz, rotation);

                TileCoord coord = new TileCoord(anchor.x + offset.dx, anchor.z + offset.dz, anchor.elevationLevel);

                TileCellData cell = clip.cell;
                cell.rotationSteps = TileTransformUtility.CombineRotation(cell.rotationSteps, rotation);
                cell.flip = TileTransformUtility.CombineFlip(cell.flip, flip);

                world.SetTile(layer, coord, cell);
            }

            FinishEdit(world);
        }

        /// <summary>Moves the selection's contents by (offsetX, offsetZ) tiles: clears the source cells, writes them at the new location.</summary>
        public void MoveSelection(WorldBuilderWorld world, WorldBuilderSelection selection, int offsetX, int offsetZ)
        {
            if (world == null || !selection.HasSelection) return;
            if (offsetX == 0 && offsetZ == 0) return;

            TileLayerData layer = world.ActiveLayer;
            if (layer.locked) return;

            List<(TileCoord source, TileCellData cell)> moved = new List<(TileCoord, TileCellData)>();
            for (int x = selection.Min.x; x <= selection.Max.x; x++)
            {
                for (int z = selection.Min.z; z <= selection.Max.z; z++)
                {
                    TileCoord source = new TileCoord(x, z, selection.Min.elevationLevel);
                    if (layer.TryGetTile(source, out TileCellData cell) && !cell.IsEmpty)
                    {
                        moved.Add((source, cell));
                    }
                }
            }

            RecordUndo(world, "Move Selection");
            TerrainTransitionSet terrainSet = ResolveActiveTerrainSet(world);

            foreach ((TileCoord source, TileCellData cell) entry in moved)
            {
                EraseOne(world, layer, entry.source, terrainSet);
            }

            foreach ((TileCoord source, TileCellData cell) entry in moved)
            {
                TileCoord destination = new TileCoord(entry.source.x + offsetX, entry.source.z + offsetZ, entry.source.elevationLevel);
                world.SetTile(layer, destination, entry.cell);
            }

            // Re-resolve terrain neighbors around both the vacated and the new footprint.
            if (terrainSet != null)
            {
                foreach ((TileCoord source, TileCellData cell) entry in moved)
                {
                    TerrainTransitionResolver.ResolveCellAndNeighbors(layer, terrainSet, entry.source);
                    TileCoord destination = new TileCoord(entry.source.x + offsetX, entry.source.z + offsetZ, entry.source.elevationLevel);
                    TerrainTransitionResolver.ResolveCellAndNeighbors(layer, terrainSet, destination);
                }
            }

            selection.Set(
                new TileCoord(selection.Min.x + offsetX, selection.Min.z + offsetZ, selection.Min.elevationLevel),
                new TileCoord(selection.Max.x + offsetX, selection.Max.z + offsetZ, selection.Min.elevationLevel));

            FinishEdit(world);
        }

        /// <summary>
        /// Rotates the selection's contents 90 degrees per step (as a block: both cell positions and
        /// each cell's own sprite rotation). Anchored at the selection's original min corner; a
        /// non-square selection's own bounding box swaps width/height, same as rotating a physical piece
        /// of grid paper. Works entirely in integer tile coordinates -- no division, so it is correct for
        /// selections whose width and height have different parities (an earlier "rotate around a
        /// doubled center coordinate" approach was not).
        /// </summary>
        public void RotateSelection(WorldBuilderWorld world, WorldBuilderSelection selection, int steps)
        {
            if (world == null || !selection.HasSelection) return;

            TileLayerData layer = world.ActiveLayer;
            if (layer.locked) return;

            int width = selection.Width;
            int height = selection.Height;

            List<(int lx, int lz, TileCellData cell)> source = new List<(int, int, TileCellData)>();
            for (int x = selection.Min.x; x <= selection.Max.x; x++)
            {
                for (int z = selection.Min.z; z <= selection.Max.z; z++)
                {
                    TileCoord coord = new TileCoord(x, z, selection.Min.elevationLevel);
                    if (layer.TryGetTile(coord, out TileCellData cell) && !cell.IsEmpty)
                    {
                        source.Add((x - selection.Min.x, z - selection.Min.z, cell));
                    }
                }
            }

            // Rotate the rectangle's own corners (not just occupied cells) to get a deterministic new
            // bounding box even when cells near the edge are empty.
            (int rx, int rz) c0 = TileTransformUtility.RotateOffset(0, 0, steps);
            (int rx, int rz) c1 = TileTransformUtility.RotateOffset(width - 1, 0, steps);
            (int rx, int rz) c2 = TileTransformUtility.RotateOffset(0, height - 1, steps);
            (int rx, int rz) c3 = TileTransformUtility.RotateOffset(width - 1, height - 1, steps);
            int minRx = Mathf.Min(Mathf.Min(c0.rx, c1.rx), Mathf.Min(c2.rx, c3.rx));
            int minRz = Mathf.Min(Mathf.Min(c0.rz, c1.rz), Mathf.Min(c2.rz, c3.rz));
            int maxRx = Mathf.Max(Mathf.Max(c0.rx, c1.rx), Mathf.Max(c2.rx, c3.rx));
            int maxRz = Mathf.Max(Mathf.Max(c0.rz, c1.rz), Mathf.Max(c2.rz, c3.rz));

            RecordUndo(world, "Rotate Selection");
            TerrainTransitionSet terrainSet = ResolveActiveTerrainSet(world);

            for (int x = selection.Min.x; x <= selection.Max.x; x++)
            for (int z = selection.Min.z; z <= selection.Max.z; z++)
                EraseOne(world, layer, new TileCoord(x, z, selection.Min.elevationLevel), terrainSet);

            List<TileCoord> destinations = new List<TileCoord>(source.Count);
            foreach ((int lx, int lz, TileCellData cell) entry in source)
            {
                (int rx, int rz) rotated = TileTransformUtility.RotateOffset(entry.lx, entry.lz, steps);
                TileCoord destination = new TileCoord(
                    selection.Min.x + (rotated.rx - minRx),
                    selection.Min.z + (rotated.rz - minRz),
                    selection.Min.elevationLevel);

                TileCellData cell = entry.cell;
                cell.rotationSteps = TileTransformUtility.CombineRotation(cell.rotationSteps, steps);
                world.SetTile(layer, destination, cell);
                destinations.Add(destination);
            }

            selection.Set(
                selection.Min,
                new TileCoord(selection.Min.x + (maxRx - minRx), selection.Min.z + (maxRz - minRz), selection.Min.elevationLevel));

            if (terrainSet != null)
            {
                foreach (TileCoord destination in destinations)
                {
                    TerrainTransitionResolver.ResolveCellAndNeighbors(layer, terrainSet, destination);
                }
            }

            FinishEdit(world);
        }

        /// <summary>
        /// Mirrors the selection's contents in place. Computes every destination from an immutable
        /// snapshot and applies clears/writes in separate passes, since a flip pairs up cells (the cell
        /// at the mirror of another cell's destination) and writing them one at a time would let a later
        /// cell's clear step delete data an earlier cell just wrote to that same spot.
        /// </summary>
        public void FlipSelection(WorldBuilderWorld world, WorldBuilderSelection selection, bool horizontal)
        {
            if (world == null || !selection.HasSelection) return;

            TileLayerData layer = world.ActiveLayer;
            if (layer.locked) return;

            TileFlip flipMask = horizontal ? TileFlip.Horizontal : TileFlip.Vertical;

            List<TileCoord> sources = new List<TileCoord>();
            List<(TileCoord destination, TileCellData cell)> moves = new List<(TileCoord, TileCellData)>();

            for (int x = selection.Min.x; x <= selection.Max.x; x++)
            {
                for (int z = selection.Min.z; z <= selection.Max.z; z++)
                {
                    TileCoord coord = new TileCoord(x, z, selection.Min.elevationLevel);
                    if (!layer.TryGetTile(coord, out TileCellData cell) || cell.IsEmpty) continue;

                    int mirroredX = horizontal ? selection.Min.x + (selection.Max.x - x) : x;
                    int mirroredZ = !horizontal ? selection.Min.z + (selection.Max.z - z) : z;
                    TileCoord destination = new TileCoord(mirroredX, mirroredZ, selection.Min.elevationLevel);

                    cell.flip = TileTransformUtility.CombineFlip(cell.flip, flipMask);
                    sources.Add(coord);
                    moves.Add((destination, cell));
                }
            }

            RecordUndo(world, "Flip Selection");

            foreach (TileCoord coord in sources) layer.RemoveTile(coord);
            foreach ((TileCoord destination, TileCellData cell) entry in moves) layer.SetTile(entry.destination, entry.cell);

            foreach (TileCoord coord in sources) world.MarkChunkDirty(coord);
            foreach ((TileCoord destination, TileCellData cell) entry in moves) world.MarkChunkDirty(entry.destination);

            TerrainTransitionSet terrainSet = ResolveActiveTerrainSet(world);
            if (terrainSet != null)
            {
                foreach (TileCoord coord in sources) TerrainTransitionResolver.ResolveCellAndNeighbors(layer, terrainSet, coord);
                foreach ((TileCoord destination, TileCellData cell) entry in moves) TerrainTransitionResolver.ResolveCellAndNeighbors(layer, terrainSet, entry.destination);
            }

            FinishEdit(world);
        }

        /// <summary>Toggles manualOverride on every painted cell in the selection (locks/unlocks them from terrain auto-resolution).</summary>
        public void ToggleManualOverride(WorldBuilderWorld world, WorldBuilderSelection selection)
        {
            if (world == null || !selection.HasSelection) return;

            TileLayerData layer = world.ActiveLayer;
            RecordUndo(world, "Toggle Manual Override");

            for (int x = selection.Min.x; x <= selection.Max.x; x++)
            {
                for (int z = selection.Min.z; z <= selection.Max.z; z++)
                {
                    TileCoord coord = new TileCoord(x, z, selection.Min.elevationLevel);
                    if (layer.TryGetTile(coord, out TileCellData cell) && !cell.IsEmpty)
                    {
                        cell.manualOverride = !cell.manualOverride;
                        layer.SetTile(coord, cell);
                    }
                }
            }

            EditorUtility.SetDirty(world);
        }

        // ---------------------------------------------------------------
        // Shared helpers
        // ---------------------------------------------------------------

        private void PlaceOne(WorldBuilderWorld world, TileLayerData layer, TileCoord coord, TileDefinition selectedTile, TerrainTransitionSet terrainSet)
        {
            TileCellData cell = new TileCellData
            {
                tileId = selectedTile.TileId,
                rotationSteps = WorldBuilderPrefs.PlacementRotationSteps,
                flip = WorldBuilderPrefs.PlacementFlip,
                manualOverride = terrainSet == null,
                terrainSetId = terrainSet != null ? terrainSet.TerrainSetId : null
            };

            world.SetTile(layer, coord, cell);

            if (terrainSet != null)
            {
                TerrainTransitionResolver.ResolveCellAndNeighbors(layer, terrainSet, coord);
            }
        }

        private void EraseOne(WorldBuilderWorld world, TileLayerData layer, TileCoord coord, TerrainTransitionSet terrainSet)
        {
            bool wasTerrain = layer.TryGetTile(coord, out TileCellData existing) && existing.IsTerrainManaged;
            world.EraseTile(layer, coord);

            if (wasTerrain && terrainSet != null)
            {
                TerrainTransitionResolver.ResolveCellAndNeighbors(layer, terrainSet, coord);
            }
        }

        private static TerrainTransitionSet ResolveActiveTerrainSet(WorldBuilderWorld world)
        {
            if (!WorldBuilderPrefs.TerrainModeEnabled || world.palette == null) return null;

            string guid = WorldBuilderPrefs.SelectedTerrainSetGuid;
            if (string.IsNullOrEmpty(guid)) return null;

            foreach (TerrainTransitionSet set in world.palette.terrainSets)
            {
                if (set != null && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(set)) == guid)
                {
                    return set;
                }
            }

            return null;
        }

        private static void RecordUndo(WorldBuilderWorld world, string label) => Undo.RegisterCompleteObjectUndo(world, label);

        private static void FinishEdit(WorldBuilderWorld world)
        {
            world.RebuildDirtyChunks();
            EditorUtility.SetDirty(world);
        }
    }
}
