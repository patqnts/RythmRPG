#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Builds the template HUD bars as real scene objects and assigns them to the scene's CombatUIController, so they
    /// can be moved, restyled, or dragged into the Project window to become prefabs (then set them on the HUD Style).
    /// Lives in the runtime assembly behind UNITY_EDITOR because the combat code has no editor assembly.
    /// </summary>
    internal static class CombatHudMenu
    {
        [MenuItem("Tools/Rythm RPG/Combat/Create HUD Bars In Scene")]
        private static void CreateHudBars()
        {
            CombatUIController ui = Object.FindAnyObjectByType<CombatUIController>(FindObjectsInactive.Include);
            Canvas canvas = ui != null ? ui.GetComponentInParent<Canvas>(true) : null;
            if (canvas == null && Selection.activeGameObject != null) canvas = Selection.activeGameObject.GetComponentInParent<Canvas>(true);
            if (canvas == null)
            {
                var canvasObject = new GameObject("Combat HUD Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                Undo.RegisterCreatedObjectUndo(canvasObject, "Create Combat HUD");
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
            }

            CombatHudStyle style = CombatHudStyle.LoadOrDefault();
            var root = new GameObject("Combat HUD Bars", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(root, "Create Combat HUD");
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(canvas.rootCanvas.transform, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;

            ResourceBarView health = Build(rootRect, "Player Health", style.PlayerHealth, style.PlayerHealthLayout, style, 1000);
            ResourceBarView mana = Build(rootRect, "Player Mana", style.PlayerMana, style.PlayerManaLayout, style, 100);
            ResourceBarView enemy = Build(rootRect, "Enemy Health", style.EnemyHealth, style.EnemyHealthLayout, style, 500);
            enemy.Title = "Enemy";

            if (ui != null)
            {
                Undo.RecordObject(ui, "Assign Combat HUD Bars");
                ui.EditorAssignBars(health, mana, enemy);
                EditorUtility.SetDirty(ui);
            }
            else Debug.LogWarning("No CombatUIController in the open scenes: the bars were created but not assigned.");

            Selection.activeGameObject = root;
        }

        private static ResourceBarView Build(RectTransform parent, string name, ResourceBarStyle barStyle, HudBarLayout layout,
            CombatHudStyle style, int previewValue)
        {
            ResourceBarView bar = ResourceBarView.CreateTemplate(parent, name, barStyle, style);
            ResourceBarView.ApplyLayout((RectTransform)bar.transform, layout);
            bar.Set(previewValue, previewValue, false);
            return bar;
        }
    }
}
#endif
