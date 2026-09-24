using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

namespace RythmRPG.Combat
{
    /// <summary>
    /// TextMeshPro helpers shared by every piece of combat text (HUD, combo streak, lane buttons, result screen,
    /// floating numbers). Replaces the old UnityEngine.UI.Text / TextMesh + Outline setup:
    /// - fonts: an explicit TMP font asset wins; otherwise a legacy <see cref="Font"/> (the old style fields) is
    ///   turned into a dynamic TMP font asset once and cached; otherwise TMP's default font.
    /// - outlines: one shared material per (font, color, width) instead of a per-text material instance.
    /// In the editor, generated fonts and materials are saved under <see cref="GeneratedFolder"/> so scene objects
    /// built by the editor tools keep valid references after the scene is saved.
    /// </summary>
    public static class CombatText
    {
        public const string GeneratedFolder = "Assets/TextMesh Pro/Generated";
        /// <summary>Outline thickness (SDF units) used when a style only gives an outline color.</summary>
        public const float DefaultOutlineWidth = 0.22f;

        private static readonly Dictionary<Font, TMP_FontAsset> fontCache = new();
        private static readonly Dictionary<(TMP_FontAsset font, Color32 color, int width, bool onTop), Material> materialCache = new();

        public static TMP_FontAsset DefaultFont
        {
            get
            {
                TMP_Settings settings = TMP_Settings.instance;
                return settings != null ? TMP_Settings.defaultFontAsset : null;
            }
        }

        /// <summary>Explicit TMP font, else one generated from the legacy font, else TMP's default font.</summary>
        public static TMP_FontAsset ResolveFont(TMP_FontAsset asset, Font legacy = null)
        {
            if (asset != null) return asset;
            TMP_FontAsset fromLegacy = FromLegacyFont(legacy);
            return fromLegacy != null ? fromLegacy : DefaultFont;
        }

        /// <summary>Unity's built-in legacy fonts map to TMP's default font rather than a generated copy.</summary>
        public static bool IsBuiltinFont(Font font)
        {
            if (font == null) return true;
#if UNITY_EDITOR
            string path = AssetDatabase.GetAssetPath(font);
            if (string.IsNullOrEmpty(path) || path.StartsWith("Library/") || path.StartsWith("Resources/unity_builtin"))
                return true;
#endif
            return font.name == "LegacyRuntime" || font.name == "Arial";
        }

        /// <summary>A dynamic TMP font asset made from a legacy font (cached; saved as an asset in the editor).</summary>
        public static TMP_FontAsset FromLegacyFont(Font legacy)
        {
            if (legacy == null || IsBuiltinFont(legacy)) return null;
            if (fontCache.TryGetValue(legacy, out TMP_FontAsset cached) && cached != null) return cached;

            TMP_FontAsset created = null;
#if UNITY_EDITOR
            created = EditorGetOrCreateFontAsset(legacy);
#endif
            if (created == null)
            {
                created = TMP_FontAsset.CreateFontAsset(legacy, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                    AtlasPopulationMode.Dynamic, true);
                if (created != null) created.name = legacy.name + " SDF (runtime)";
            }
            if (created != null) fontCache[legacy] = created;
            return created;
        }

        /// <summary>A new TextMeshProUGUI child, configured like the old generated Text (no wrap, overflow, no raycast).</summary>
        public static TextMeshProUGUI CreateUGUI(string name, Transform parent, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions alignment, Color outline, float outlineWidth = DefaultOutlineWidth)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            Configure(text, font, size, color, alignment, outline, outlineWidth);
            return text;
        }

        public static void Configure(TMP_Text text, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions alignment, Color outline, float outlineWidth = DefaultOutlineWidth)
        {
            if (text == null) return;
            TMP_FontAsset resolved = font != null ? font : DefaultFont;
            if (resolved != null && text.font != resolved) text.font = resolved;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.richText = true;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            ApplyOutline(text, outline, outlineWidth);
        }

        /// <summary>Shared outline material for the text's font (alpha 0 = the font's plain material).</summary>
        public static void ApplyOutline(TMP_Text text, Color outline, float width = DefaultOutlineWidth, bool alwaysOnTop = false)
        {
            if (text == null || text.font == null) return;
            Material material = GetMaterial(text.font, outline, width, alwaysOnTop);
            if (material != null && text.fontSharedMaterial != material) text.fontSharedMaterial = material;
            text.extraPadding = outline.a > 0f;
        }

        /// <summary>
        /// A material for <paramref name="font"/> with an outline (and optionally ZTest Always, for world-space text
        /// that must never be hidden by geometry). Shared by every text that asks for the same look.
        /// </summary>
        public static Material GetMaterial(TMP_FontAsset font, Color outline, float width = DefaultOutlineWidth, bool alwaysOnTop = false)
        {
            if (font == null || font.material == null) return null;
            bool hasOutline = outline.a > 0f && width > 0f;
            if (!hasOutline && !alwaysOnTop) return font.material;

            Color32 color = hasOutline ? (Color32)outline : new Color32(0, 0, 0, 0);
            int widthKey = hasOutline ? Mathf.RoundToInt(width * 1000f) : 0;
            var key = (font, color, widthKey, alwaysOnTop);
            if (materialCache.TryGetValue(key, out Material cached) && cached != null) return cached;

            Material material = null;
#if UNITY_EDITOR
            material = EditorLoadOrCreateMaterial(font, color, widthKey, alwaysOnTop);
#endif
            if (material == null) material = BuildMaterial(font, color, widthKey, alwaysOnTop);
            materialCache[key] = material;
            return material;
        }

        private static Material BuildMaterial(TMP_FontAsset font, Color32 outline, int widthKey, bool alwaysOnTop)
        {
            var material = new Material(font.material) { name = MaterialName(font, outline, widthKey, alwaysOnTop) };
            if (outline.a > 0 && widthKey > 0)
            {
                material.EnableKeyword(ShaderUtilities.Keyword_Outline);
                material.SetColor(ShaderUtilities.ID_OutlineColor, outline);
                material.SetFloat(ShaderUtilities.ID_OutlineWidth, widthKey / 1000f);
            }
            if (alwaysOnTop) material.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
            return material;
        }

        private static string MaterialName(TMP_FontAsset font, Color32 outline, int widthKey, bool alwaysOnTop)
        {
            string name = font.name;
            if (outline.a > 0 && widthKey > 0) name += $" - Outline {ColorUtility.ToHtmlStringRGBA(outline)} {widthKey}";
            if (alwaysOnTop) name += " - On Top";
            return name;
        }

        public static TextAlignmentOptions ToTmp(TextAnchor anchor) => anchor switch
        {
            TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter => TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
            TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
            TextAnchor.MiddleRight => TextAlignmentOptions.Right,
            TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
            TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
            _ => TextAlignmentOptions.Center
        };

        public static FontStyles ToTmp(FontStyle style) => style switch
        {
            FontStyle.Bold => FontStyles.Bold,
            FontStyle.Italic => FontStyles.Italic,
            FontStyle.BoldAndItalic => FontStyles.Bold | FontStyles.Italic,
            _ => FontStyles.Normal
        };

        /// <summary>Legacy Outline effect distance (pixels) to a TMP outline width for a given font size.</summary>
        public static float OutlineWidthFromPixels(float pixels, float fontSize) =>
            Mathf.Clamp(Mathf.Abs(pixels) / Mathf.Max(1f, fontSize) * 3f, 0.08f, 0.35f);

#if UNITY_EDITOR
        public static TMP_FontAsset EditorGetOrCreateFontAsset(Font legacy)
        {
            if (legacy == null || IsBuiltinFont(legacy)) return null;
            string path = $"{GeneratedFolder}/{Sanitize(legacy.name)} SDF.asset";
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null) return existing;

            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(legacy, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, true);
            if (asset == null) return null; // "Include Font Data" is off on the source font; TMP already logged why.
            EnsureGeneratedFolder();
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            if (asset.atlasTextures != null)
            {
                foreach (Texture2D atlas in asset.atlasTextures)
                {
                    if (atlas == null || AssetDatabase.Contains(atlas)) continue;
                    atlas.name = asset.name + " Atlas";
                    AssetDatabase.AddObjectToAsset(atlas, asset);
                }
            }
            if (asset.material != null && !AssetDatabase.Contains(asset.material))
            {
                asset.material.name = asset.name + " Material";
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CombatText] Generated TextMeshPro font '{path}' from '{legacy.name}'.", asset);
            return asset;
        }

        private static Material EditorLoadOrCreateMaterial(TMP_FontAsset font, Color32 outline, int widthKey, bool alwaysOnTop)
        {
            // Fonts that aren't assets (runtime-generated) get runtime materials.
            if (!AssetDatabase.Contains(font)) return null;
            string path = $"{GeneratedFolder}/{Sanitize(MaterialName(font, outline, widthKey, alwaysOnTop))}.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            Material material = BuildMaterial(font, outline, widthKey, alwaysOnTop);
            EnsureGeneratedFolder();
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void EnsureGeneratedFolder()
        {
            if (AssetDatabase.IsValidFolder(GeneratedFolder)) return;
            AssetDatabase.CreateFolder("Assets/TextMesh Pro", "Generated");
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
#endif
    }
}
