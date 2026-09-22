#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    /// <summary>
    /// One-click conversion of every legacy uGUI <see cref="Text"/> in the project to TextMeshPro.
    ///
    /// Tools > Rythm RPG > Convert All Text To TextMeshPro:
    /// 1. every prefab and every scene under Assets/ is opened; each Text becomes a TextMeshProUGUI on the same
    ///    GameObject (text, size, style, color, alignment, wrapping, best-fit, raycast kept). An Outline / Shadow
    ///    effect on it becomes a shared TMP outline material.
    /// 2. every serialized reference that pointed at the old Text (script fields, Button target graphics...) is
    ///    re-pointed at the new TextMeshProUGUI, so scripts whose fields are now TMP_Text keep their wiring.
    /// 3. each legacy Font becomes a dynamic TMP font asset once (Assets/TextMesh Pro/Generated) and the combat
    ///    style assets get it in their TextMeshPro font slots.
    /// Scenes are saved. Run it with the project under version control so the diff can be reviewed.
    /// Lives in the runtime assembly behind UNITY_EDITOR, like the other combat editor menus.
    /// </summary>
    internal static class LegacyTextToTmpMigration
    {
        private const string MenuPath = "Tools/Rythm RPG/Convert All Text To TextMeshPro";

        private sealed class Report
        {
            public int Texts;
            public int References;
            public int Scenes;
            public int Prefabs;
            public readonly List<string> Warnings = new();
        }

        [MenuItem(MenuPath)]
        private static void ConvertProject()
        {
            if (!EditorUtility.DisplayDialog("Convert All Text To TextMeshPro",
                    "Every legacy UI Text in every scene and prefab under Assets/ will be replaced by TextMeshPro, and " +
                    "the scenes will be saved.\n\nMake sure your work is committed first.", "Convert", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
            var report = new Report();
            try
            {
                ConvertStyleFonts(report);
                ConvertPrefabs(report);
                ConvertScenes(report);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
                AssetDatabase.SaveAssets();
            }

            var summary = new StringBuilder();
            summary.AppendLine($"Converted {report.Texts} Text component(s) in {report.Scenes} scene(s) and {report.Prefabs} prefab(s).");
            summary.AppendLine($"Re-pointed {report.References} reference(s) to the new TextMeshPro components.");
            if (report.Warnings.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine($"{report.Warnings.Count} warning(s) - see the Console.");
                foreach (string warning in report.Warnings) Debug.LogWarning("[Text -> TMP] " + warning);
            }
            Debug.Log("[Text -> TMP] " + summary);
            EditorUtility.DisplayDialog("Convert All Text To TextMeshPro", summary.ToString(), "OK");
        }

        // ---------- style assets ----------

        private static void ConvertStyleFonts(Report report)
        {
            AssignGeneratedFont<CombatHudStyle>("fontAsset", "legacyFont", report);
            AssignGeneratedFont<CombatResultStyle>("fontAsset", "legacyFont", report);
            AssignGeneratedFont<CombatLanePresentationTheme>("ButtonFontAsset", "ButtonFont", report);
        }

        private static void AssignGeneratedFont<T>(string tmpField, string legacyField, Report report) where T : ScriptableObject
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { "Assets" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                var so = new SerializedObject(asset);
                SerializedProperty tmp = so.FindProperty(tmpField);
                SerializedProperty legacy = so.FindProperty(legacyField);
                if (tmp == null || legacy == null || tmp.objectReferenceValue != null) continue;
                if (!(legacy.objectReferenceValue is Font font) || CombatText.IsBuiltinFont(font)) continue;
                TMP_FontAsset generated = CombatText.EditorGetOrCreateFontAsset(font);
                if (generated == null)
                {
                    report.Warnings.Add($"{asset.name}: could not make a TMP font from '{font.name}' (enable Include Font Data on it).");
                    continue;
                }
                tmp.objectReferenceValue = generated;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
        }

        // ---------- prefabs ----------

        private static void ConvertPrefabs(Report report)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.StartsWith("Assets/TextMesh Pro/")) continue;
                EditorUtility.DisplayProgressBar("Text -> TextMeshPro", path, (float)i / Mathf.Max(1, guids.Length) * 0.5f);

                // Cheap check first: only open prefabs that actually contain a legacy Text.
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || asset.GetComponentsInChildren<Text>(true).Length == 0) continue;

                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int converted = ConvertTexts(new[] { contents }, path, report);
                    if (converted > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        report.Prefabs++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
        }

        // ---------- scenes ----------

        private static void ConvertScenes(Report report)
        {
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                EditorUtility.DisplayProgressBar("Text -> TextMeshPro", path, 0.5f + (float)i / Mathf.Max(1, guids.Length) * 0.5f);
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int converted = ConvertTexts(scene.GetRootGameObjects(), path, report);
                if (converted <= 0) continue;
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                report.Scenes++;
            }
        }

        // ---------- conversion ----------

        private readonly struct LegacyTextData
        {
            public readonly string Text;
            public readonly Font Font;
            public readonly int FontSize;
            public readonly FontStyle FontStyle;
            public readonly Color Color;
            public readonly TextAnchor Alignment;
            public readonly bool RaycastTarget;
            public readonly bool RichText;
            public readonly bool Maskable;
            public readonly HorizontalWrapMode HorizontalOverflow;
            public readonly VerticalWrapMode VerticalOverflow;
            public readonly bool BestFit;
            public readonly int BestFitMin;
            public readonly int BestFitMax;
            public readonly float LineSpacing;
            public readonly bool Enabled;

            public LegacyTextData(Text text)
            {
                Text = text.text;
                Font = text.font;
                FontSize = text.fontSize;
                FontStyle = text.fontStyle;
                Color = text.color;
                Alignment = text.alignment;
                RaycastTarget = text.raycastTarget;
                RichText = text.supportRichText;
                Maskable = text.maskable;
                HorizontalOverflow = text.horizontalOverflow;
                VerticalOverflow = text.verticalOverflow;
                BestFit = text.resizeTextForBestFit;
                BestFitMin = text.resizeTextMinSize;
                BestFitMax = text.resizeTextMaxSize;
                LineSpacing = text.lineSpacing;
                Enabled = text.enabled;
            }
        }

        /// <summary>Converts every Text under the roots and re-points references found under the same roots.</summary>
        private static int ConvertTexts(IReadOnlyList<GameObject> roots, string context, Report report)
        {
            var texts = new List<Text>();
            foreach (GameObject root in roots)
            {
                foreach (Text text in root.GetComponentsInChildren<Text>(true))
                {
                    // Texts owned by a nested prefab instance are converted in that prefab's own asset.
                    if (PrefabUtility.IsPartOfPrefabInstance(text))
                    {
                        report.Warnings.Add($"{context}: '{PathOf(text.transform)}' belongs to a prefab instance; " +
                            "it is converted when that prefab is processed (apply/revert overrides if it still shows Text).");
                        continue;
                    }
                    texts.Add(text);
                }
            }
            if (texts.Count == 0) return 0;

            // Who points at which Text (by list index, since destroyed objects can't be compared later).
            var index = new Dictionary<Object, int>();
            for (int i = 0; i < texts.Count; i++) index[texts[i]] = i;
            var references = new List<(Component owner, string path, int text)>();
            foreach (GameObject root in roots)
            {
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null || component is Text || component is Transform) continue;
                    var so = new SerializedObject(component);
                    SerializedProperty property = so.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        Object value = property.objectReferenceValue;
                        if (value is Text && index.TryGetValue(value, out int i))
                            references.Add((component, property.propertyPath, i));
                    }
                }
            }

            var converted = new TextMeshProUGUI[texts.Count];
            int count = 0;
            for (int i = 0; i < texts.Count; i++)
            {
                converted[i] = Convert(texts[i], context, report);
                if (converted[i] != null) count++;
            }

            foreach ((Component owner, string path, int i) in references)
            {
                TextMeshProUGUI replacement = converted[i];
                if (owner == null || replacement == null) continue;
                var so = new SerializedObject(owner);
                SerializedProperty property = so.FindProperty(path);
                if (property == null) continue;
                property.objectReferenceValue = replacement;
                so.ApplyModifiedPropertiesWithoutUndo();
                if (new SerializedObject(owner).FindProperty(path).objectReferenceValue == replacement) report.References++;
                else report.Warnings.Add($"{context}: {owner.GetType().Name}.{path} on '{PathOf(owner.transform)}' still expects a " +
                    "legacy Text; change that field to TMP_Text and assign it by hand.");
            }

            report.Texts += count;
            return count;
        }

        private static TextMeshProUGUI Convert(Text text, string context, Report report)
        {
            GameObject go = text.gameObject;
            var data = new LegacyTextData(text);

            Color outlineColor = Color.clear;
            float outlinePixels = 0f;
            foreach (Shadow effect in go.GetComponents<Shadow>())
            {
                // Outline derives from Shadow; both become a TMP outline (a hard offset shadow reads the same at this size).
                if (outlineColor.a <= 0f || effect is Outline)
                {
                    outlineColor = effect.effectColor;
                    outlinePixels = Mathf.Max(Mathf.Abs(effect.effectDistance.x), Mathf.Abs(effect.effectDistance.y));
                }
                Object.DestroyImmediate(effect, true);
            }

            Object.DestroyImmediate(text, true);
            if (go.GetComponent<Text>() != null)
            {
                report.Warnings.Add($"{context}: could not remove the Text on '{PathOf(go.transform)}' (another component requires it).");
                return null;
            }
            if (go.GetComponent<Graphic>() != null)
            {
                report.Warnings.Add($"{context}: '{PathOf(go.transform)}' has another Graphic; TextMeshPro was not added.");
                return null;
            }

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = CombatText.IsBuiltinFont(data.Font) ? CombatText.DefaultFont
                : CombatText.EditorGetOrCreateFontAsset(data.Font);
            if (font == null)
            {
                font = CombatText.DefaultFont;
                if (data.Font != null && !CombatText.IsBuiltinFont(data.Font))
                    report.Warnings.Add($"{context}: '{PathOf(go.transform)}' uses '{data.Font.name}', which could not become a TMP font " +
                        "(enable Include Font Data on it); TMP's default font was used.");
            }
            if (font != null) tmp.font = font;
            tmp.text = data.Text;
            tmp.fontSize = data.FontSize;
            tmp.fontStyle = CombatText.ToTmp(data.FontStyle);
            tmp.color = data.Color;
            tmp.alignment = CombatText.ToTmp(data.Alignment);
            tmp.raycastTarget = data.RaycastTarget;
            tmp.richText = data.RichText;
            tmp.maskable = data.Maskable;
            tmp.textWrappingMode = data.HorizontalOverflow == HorizontalWrapMode.Wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.overflowMode = data.VerticalOverflow == VerticalWrapMode.Overflow ? TextOverflowModes.Overflow : TextOverflowModes.Truncate;
            if (data.BestFit)
            {
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = data.BestFitMin;
                tmp.fontSizeMax = data.BestFitMax;
            }
            if (!Mathf.Approximately(data.LineSpacing, 1f)) tmp.lineSpacing = (data.LineSpacing - 1f) * 100f;
            if (outlineColor.a > 0f)
                CombatText.ApplyOutline(tmp, outlineColor, CombatText.OutlineWidthFromPixels(outlinePixels, data.FontSize));
            tmp.enabled = data.Enabled;
            EditorUtility.SetDirty(go);
            return tmp;
        }

        private static string PathOf(Transform transform)
        {
            var names = new List<string>();
            for (Transform t = transform; t != null; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
#endif
