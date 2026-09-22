using System;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.WorldBuilder.Editor
{
    /// <summary>
    /// Scene view object placement (Phase 3): click to place the selected <see cref="PropDefinition"/> at
    /// the raycast hit point on the active elevation plane. Deliberately reuses the exact fixes
    /// WorldBuilderSceneTool needed earlier in this project rather than risking rediscovering the same
    /// bugs independently:
    /// - The ground-plane raycast and the Q/View-tool navigation guard (<see cref="GroundPlaneMath"/>,
    ///   the <c>Tools.viewToolActive || Tools.current == Tool.View</c> check) so panning/orbiting the
    ///   Scene view never places a prop.
    /// - Clearing the Hierarchy selection while enabled, exactly like WorldBuilderSceneTool.ToolEnabled
    ///   does, rather than relying on HandleUtility.nearestControl picking to out-prioritize Unity's
    ///   built-in Move/Rotate/Scale gizmo -- that was tried for painting first and did not work (the
    ///   gizmo's own click handling runs before SceneView.duringSceneGui delegates get the event at all),
    ///   which is why this project settled on selection-clearing instead.
    ///
    /// Known limitation: this tool does not check whether WorldBuilderSceneTool's ground-painting toggle
    /// is also enabled. If both are on at once, a single click both paints a tile and places a prop --
    /// see world-builder-phase3.md.
    /// </summary>
    public class WorldBuilderPropTool
    {
        private bool toolEnabled;
        private UnityEngine.Object[] previousSelection;

        public WorldBuilderWorld ActiveWorld { get; set; }
        public PropDefinition SelectedProp { get; set; }

        public bool ToolEnabled
        {
            get => toolEnabled;
            set
            {
                if (toolEnabled == value) return;
                toolEnabled = value;

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

        public void OnSceneGUI(SceneView sceneView)
        {
            if (!ToolEnabled || ActiveWorld == null) return;

            Event e = Event.current;

            // Same navigation guard as WorldBuilderSceneTool.OnSceneGUI -- see that file's comment for
            // the full rationale (Tools.current alone misses Space-drag panning).
            bool navigating = Tools.viewToolActive || Tools.current == Tool.View;

            if (!navigating)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            WorldBuilderSettings settings = ActiveWorld.settings;
            float planeY = ActiveWorld.activeElevationLevel * settings.elevationIncrement;

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hasHit = GroundPlaneMath.RayPlaneIntersect(ray, planeY, out Vector3 hitPoint);

            if (!navigating && hasHit)
            {
                if (SelectedProp != null)
                {
                    WorldBuilderGizmos.DrawPropPreview(SelectedProp, hitPoint, planeY);
                }

                if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
                {
                    if (SelectedProp != null)
                    {
                        ActiveWorld.CreateProp(SelectedProp, hitPoint);
                    }

                    e.Use();
                }
            }

            sceneView.Repaint();
        }
    }
}
