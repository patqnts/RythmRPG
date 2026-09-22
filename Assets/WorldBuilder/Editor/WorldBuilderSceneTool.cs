using System;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.WorldBuilder.Editor
{
    /// <summary>
    /// Scene view interaction: raycasts the mouse against the active elevation plane using a proper
    /// ray-plane intersection (never assumes screen coordinates equal XZ world coordinates), draws the
    /// grid/tool preview, and routes mouse and keyboard events to the paint controller. Every
    /// <see cref="PaintTool"/> is handled here: Brush/Eraser continuous-drag, Eyedropper single-click
    /// sample, Rectangle/Line/Select drag-to-define-then-commit-on-release, Fill/Stamp single-click
    /// commit, and Move click-inside-selection-then-drag. Hooked up to SceneView.duringSceneGui by
    /// <see cref="WorldBuilderWindow"/>.
    /// </summary>
    public class WorldBuilderSceneTool
    {
        private readonly WorldBuilderPaintController paintController = new WorldBuilderPaintController();
        private readonly WorldBuilderSelection selection = new WorldBuilderSelection();

        // Brush/Eraser continuous-drag state.
        private bool dragging;
        private TileCoord lastPaintedCoord;
        private bool hasLastPaintedCoord;

        // Rectangle/Line/Select drag-to-define state.
        private bool boxDragging;
        private TileCoord dragOrigin;

        // Move click-inside-then-drag state.
        private bool moveDragging;
        private TileCoord moveStartCoord;
        private int moveOffsetX;
        private int moveOffsetZ;

        private TileCoord lastHoverCoord;
        private bool toolEnabled;
        private UnityEngine.Object[] previousSelection;

        public WorldBuilderWorld ActiveWorld { get; set; }
        public TileDefinition SelectedTile { get; set; }

        /// <summary>
        /// Whether ground painting is active in the Scene view. Setting this also clears the current
        /// Hierarchy selection for as long as painting is on, and restores it afterward. This is
        /// necessary, not cosmetic: whenever a GameObject is selected and one of Unity's built-in
        /// transform tools (Move/Rotate/Scale) is active, Unity draws its own gizmo handles at that
        /// object, and those handles claim any click that lands near them before our fallback "default
        /// control" ever sees it -- so a click meant to paint next to a selected object would otherwise
        /// silently drag/rotate/scale it instead. Clearing the selection removes the gizmo without
        /// touching Tools.current at all, so whichever tool (Move included) you had active stays active
        /// and usable exactly as you left it the moment painting is turned back off.
        /// </summary>
        public bool ToolEnabled
        {
            get => toolEnabled;
            set
            {
                if (toolEnabled == value) return;
                toolEnabled = value;

                // Fully qualified: this class's own "Selection" property (the tile-selection rectangle)
                // would otherwise shadow UnityEditor.Selection here, since an instance member always
                // wins over a using-imported type for an unqualified name.
                if (toolEnabled)
                {
                    previousSelection = UnityEditor.Selection.objects;
                    UnityEditor.Selection.objects = Array.Empty<UnityEngine.Object>();
                }
                else if (previousSelection != null)
                {
                    UnityEditor.Selection.objects = previousSelection;
                    previousSelection = null;
                }
            }
        }

        public Action<TileDefinition> OnTileSampled { get; set; }

        /// <summary>Called with the capped flag whenever the Fill tool commits, so the window can surface a warning.</summary>
        public Action<bool> OnFillCapped { get; set; }

        public WorldBuilderSelection Selection => selection;
        public bool HasSelection => selection.HasSelection;
        public bool HasClipboardContent => WorldBuilderClipboard.HasContent;
        public TileCoord LastHoverCoord => lastHoverCoord;

        public void ClearSelection() => selection.Clear();

        /// <summary>Pastes the clipboard anchored at the most recent Scene-view hover position (see <see cref="LastHoverCoord"/>).</summary>
        public void PasteAtHover()
        {
            if (ActiveWorld == null) return;
            paintController.PasteClipboard(ActiveWorld, lastHoverCoord);
        }

        public void CopySelection()
        {
            if (ActiveWorld == null) return;
            paintController.CopySelection(ActiveWorld, selection);
        }

        public void CutSelection()
        {
            if (ActiveWorld == null) return;
            paintController.CopySelection(ActiveWorld, selection);
            paintController.DeleteSelection(ActiveWorld, selection);
        }

        public void DeleteSelection()
        {
            if (ActiveWorld == null) return;
            paintController.DeleteSelection(ActiveWorld, selection);
        }

        public void RotateSelection(int steps)
        {
            if (ActiveWorld == null) return;
            paintController.RotateSelection(ActiveWorld, selection, steps);
        }

        public void FlipSelection(bool horizontal)
        {
            if (ActiveWorld == null) return;
            paintController.FlipSelection(ActiveWorld, selection, horizontal);
        }

        public void ToggleManualOverrideOnSelection()
        {
            if (ActiveWorld == null) return;
            paintController.ToggleManualOverride(ActiveWorld, selection);
        }

        public TileStamp SaveSelectionAsStamp(string suggestedName)
        {
            if (ActiveWorld == null) return null;
            return WorldBuilderAssetFactory.CreateStampFromSelection(ActiveWorld.ActiveLayer, selection, suggestedName);
        }

        /// <summary>Resolves the currently-selected Stamp tool asset (by <see cref="WorldBuilderPrefs.SelectedStampGuid"/>), for the window to display.</summary>
        public TileStamp ResolveSelectedStamp() => ResolveSelectedStamp(ActiveWorld);

        public void OnSceneGUI(SceneView sceneView)
        {
            if (!ToolEnabled || ActiveWorld == null) return;

            Event e = Event.current;

            // Claim the control so default Scene view tools (move/rotate handles etc.) do not fight
            // for these mouse events while painting is active.
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            WorldBuilderSettings settings = ActiveWorld.settings;
            float planeY = ActiveWorld.activeElevationLevel * settings.elevationIncrement;
            float tileSize = Mathf.Max(0.01f, settings.tileWorldSize);

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hasHit = GroundPlaneMath.RayPlaneIntersect(ray, planeY, out Vector3 hitPoint);

            TileCoord coord = lastHoverCoord;
            if (hasHit)
            {
                coord = GroundPlaneMath.WorldToTileCoord(hitPoint, tileSize, ActiveWorld.activeElevationLevel);
                lastHoverCoord = coord;
            }

            if (WorldBuilderPrefs.ShowGrid)
            {
                WorldBuilderGizmos.DrawGrid(ActiveWorld, hasHit ? hitPoint : sceneView.pivot, 20);
            }

            if (WorldBuilderPrefs.ShowChunkBounds)
            {
                WorldBuilderGizmos.DrawChunkBounds(ActiveWorld, hasHit ? hitPoint : sceneView.pivot, 3);
            }

            PaintTool tool = WorldBuilderPrefs.ActiveTool;

            DrawSelectionOverlayIfAny(tileSize, planeY);
            HandleKeyboardShortcuts(e, tool, coord);

            if (hasHit)
            {
                DrawToolPreview(tool, coord, tileSize, planeY);
                HandleMouse(e, tool, coord);
            }
            else if (e.type == EventType.MouseUp && (dragging || boxDragging || moveDragging))
            {
                // Mouse released off the ground plane (extreme grazing angle) -- cancel cleanly rather
                // than leaving an operation half-committed.
                CancelDrag();
            }

            sceneView.Repaint();
        }

        // ---------------------------------------------------------------
        // Preview
        // ---------------------------------------------------------------

        private void DrawSelectionOverlayIfAny(float tileSize, float planeY)
        {
            if (!selection.HasSelection) return;

            TileCoord min = selection.Min;
            TileCoord max = selection.Max;
            if (moveDragging)
            {
                min = new TileCoord(min.x + moveOffsetX, min.z + moveOffsetZ, min.elevationLevel);
                max = new TileCoord(max.x + moveOffsetX, max.z + moveOffsetZ, max.elevationLevel);
            }

            WorldBuilderGizmos.DrawSelectionOverlay(min, max, tileSize, planeY);
        }

        private void DrawToolPreview(PaintTool tool, TileCoord coord, float tileSize, float planeY)
        {
            switch (tool)
            {
                case PaintTool.Brush:
                case PaintTool.Eraser:
                    WorldBuilderGizmos.DrawBrushPreview(coord, tileSize, WorldBuilderPrefs.BrushSize, planeY);
                    break;

                case PaintTool.Rectangle:
                case PaintTool.Select:
                    if (boxDragging) WorldBuilderGizmos.DrawRectPreview(dragOrigin, coord, tileSize, planeY);
                    else WorldBuilderGizmos.DrawBrushPreview(coord, tileSize, 1, planeY);
                    break;

                case PaintTool.Line:
                    if (boxDragging) WorldBuilderGizmos.DrawLinePreview(dragOrigin, coord, tileSize, planeY);
                    else WorldBuilderGizmos.DrawBrushPreview(coord, tileSize, 1, planeY);
                    break;

                case PaintTool.Fill:
                    WorldBuilderGizmos.DrawBrushPreview(coord, tileSize, 1, planeY);
                    break;

                case PaintTool.Stamp:
                    TileStamp stamp = ResolveSelectedStamp(ActiveWorld);
                    if (stamp != null)
                    {
                        WorldBuilderGizmos.DrawStampPreview(stamp, coord, WorldBuilderPrefs.PlacementRotationSteps, WorldBuilderPrefs.PlacementFlip, tileSize, planeY);
                    }
                    else
                    {
                        WorldBuilderGizmos.DrawBrushPreview(coord, tileSize, 1, planeY);
                    }

                    break;

                case PaintTool.Move:
                case PaintTool.Eyedropper:
                    // Move previews via the selection overlay above; Eyedropper has no paint footprint to preview.
                    break;
            }
        }

        // ---------------------------------------------------------------
        // Mouse routing
        // ---------------------------------------------------------------

        private void HandleMouse(Event e, PaintTool tool, TileCoord coord)
        {
            switch (tool)
            {
                case PaintTool.Brush:
                case PaintTool.Eraser:
                    HandleContinuousPaint(e, coord);
                    break;

                case PaintTool.Eyedropper:
                    HandleEyedropper(e, coord);
                    break;

                case PaintTool.Rectangle:
                    HandleBoxDrag(e, coord, (a, b) => paintController.ApplyRectangle(ActiveWorld, a, b, SelectedTile));
                    break;

                case PaintTool.Line:
                    HandleBoxDrag(e, coord, (a, b) => paintController.ApplyLine(ActiveWorld, a, b, SelectedTile));
                    break;

                case PaintTool.Select:
                    HandleBoxDrag(e, coord, (a, b) => selection.Set(a, b));
                    break;

                case PaintTool.Fill:
                    HandleSingleClick(e, () =>
                    {
                        bool capped = paintController.ApplyFill(ActiveWorld, coord, SelectedTile);
                        OnFillCapped?.Invoke(capped);
                    });
                    break;

                case PaintTool.Stamp:
                    HandleSingleClick(e, () =>
                    {
                        TileStamp stamp = ResolveSelectedStamp(ActiveWorld);
                        if (stamp != null) paintController.ApplyStamp(ActiveWorld, coord, stamp);
                    });
                    break;

                case PaintTool.Move:
                    HandleMoveDrag(e, coord);
                    break;
            }
        }

        private void HandleContinuousPaint(Event e, TileCoord coord)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                dragging = true;
                paintController.BeginStroke();
                paintController.Apply(ActiveWorld, coord, SelectedTile);
                lastPaintedCoord = coord;
                hasLastPaintedCoord = true;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && !e.alt && dragging)
            {
                if (!hasLastPaintedCoord || !coord.Equals(lastPaintedCoord))
                {
                    paintController.Apply(ActiveWorld, coord, SelectedTile);
                    lastPaintedCoord = coord;
                    hasLastPaintedCoord = true;
                }

                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && dragging)
            {
                dragging = false;
                hasLastPaintedCoord = false;
                paintController.EndStroke(ActiveWorld);
                e.Use();
            }
        }

        private void HandleEyedropper(Event e, TileCoord coord)
        {
            if (e.type != EventType.MouseDown || e.button != 0 || e.alt) return;

            TileDefinition sampled = paintController.Eyedrop(ActiveWorld, coord);
            if (sampled != null)
            {
                SelectedTile = sampled;
                WorldBuilderPrefs.SelectedTileId = sampled.TileId;
                OnTileSampled?.Invoke(sampled);
            }

            e.Use();
        }

        private void HandleBoxDrag(Event e, TileCoord coord, Action<TileCoord, TileCoord> onCommit)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                boxDragging = true;
                dragOrigin = coord;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && !e.alt && boxDragging)
            {
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && boxDragging)
            {
                boxDragging = false;
                onCommit(dragOrigin, coord);
                e.Use();
            }
        }

        private static void HandleSingleClick(Event e, Action action)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                action();
                e.Use();
            }
        }

        private void HandleMoveDrag(Event e, TileCoord coord)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                if (selection.HasSelection && selection.Contains(coord))
                {
                    moveDragging = true;
                    moveStartCoord = coord;
                    moveOffsetX = 0;
                    moveOffsetZ = 0;
                }

                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && !e.alt && moveDragging)
            {
                moveOffsetX = coord.x - moveStartCoord.x;
                moveOffsetZ = coord.z - moveStartCoord.z;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && moveDragging)
            {
                moveDragging = false;
                if (moveOffsetX != 0 || moveOffsetZ != 0)
                {
                    paintController.MoveSelection(ActiveWorld, selection, moveOffsetX, moveOffsetZ);
                }

                moveOffsetX = 0;
                moveOffsetZ = 0;
                e.Use();
            }
        }

        private void CancelDrag()
        {
            dragging = false;
            hasLastPaintedCoord = false;
            boxDragging = false;
            moveDragging = false;
            moveOffsetX = 0;
            moveOffsetZ = 0;
        }

        // ---------------------------------------------------------------
        // Keyboard shortcuts (Select tool only, and only with an active selection)
        // ---------------------------------------------------------------

        private void HandleKeyboardShortcuts(Event e, PaintTool tool, TileCoord coord)
        {
            if (tool != PaintTool.Select || !selection.HasSelection) return;
            if (e.type != EventType.KeyDown) return;

            bool modifier = e.control || e.command;

            if (modifier && e.keyCode == KeyCode.C)
            {
                paintController.CopySelection(ActiveWorld, selection);
                e.Use();
            }
            else if (modifier && e.keyCode == KeyCode.V)
            {
                paintController.PasteClipboard(ActiveWorld, coord);
                e.Use();
            }
            else if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                paintController.DeleteSelection(ActiveWorld, selection);
                e.Use();
            }
        }

        private static TileStamp ResolveSelectedStamp(WorldBuilderWorld world)
        {
            if (world == null || world.palette == null) return null;

            string guid = WorldBuilderPrefs.SelectedStampGuid;
            if (string.IsNullOrEmpty(guid)) return null;

            foreach (TileStamp stamp in world.palette.stamps)
            {
                if (stamp != null && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(stamp)) == guid)
                {
                    return stamp;
                }
            }

            return null;
        }
    }
}
