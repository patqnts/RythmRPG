using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.UI.Title.Editor
{
    /// <summary>Shared inspector: target-specific fields right under the shader, then look, disintegrate, playback.</summary>
    public abstract class TitleLogoEditorBase : UnityEditor.Editor
    {
        protected abstract string[] TargetFields { get; }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var drawn = new HashSet<string> { "m_Script" };
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

            Draw("shader", drawn);
            foreach (string field in TargetFields) Draw(field, drawn);

            SerializedProperty it = serializedObject.GetIterator();
            for (bool enter = true; it.NextVisible(enter); enter = false)
            {
                if (drawn.Contains(it.name)) continue;
                EditorGUILayout.PropertyField(it, true);
            }
            serializedObject.ApplyModifiedProperties();

            var logo = (TitleLogoBase)target;
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Look Presets", EditorStyles.boldLabel);
            DrawPresetButtons();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Disintegrate", EditorStyles.boldLabel);
            if (Application.isPlaying)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Assemble")) logo.PlayAssemble();
                    if (GUILayout.Button("Disintegrate")) logo.PlayDisintegrate();
                    if (GUILayout.Button("Reset")) { logo.StopAnimation(); logo.Disintegrate = 0f; }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Scrub the Disintegrate slider above to preview. Assemble / Disintegrate buttons appear in Play Mode.", MessageType.None);
            }

            DrawNotes();
        }

        protected virtual void DrawPresetButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Ink + Gold Burst")) ApplyPreset(TitleLogoLook.InkGoldBurst());
                if (GUILayout.Button("Gold Gradient")) ApplyPreset(TitleLogoLook.GoldGradient());
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Crimson Burst")) ApplyPreset(TitleLogoLook.CrimsonBurst());
                if (GUILayout.Button("Pixel Gold")) ApplyPreset(TitleLogoLook.PixelGold());
            }
        }

        protected virtual void DrawNotes() { }

        protected void ApplyPreset(TitleLogoLook preset)
        {
            var logo = (TitleLogoBase)target;
            Undo.RecordObject(logo, "Apply Title Logo Preset");
            logo.ApplyLook(preset);
            EditorUtility.SetDirty(logo);
        }

        void Draw(string field, HashSet<string> drawn)
        {
            SerializedProperty p = serializedObject.FindProperty(field);
            if (p == null) return;
            EditorGUILayout.PropertyField(p, true);
            drawn.Add(field);
        }

        // ------------------------------------------------------------------ shared creation helpers

        protected static Canvas FindOrCreateCanvas(GameObject context)
        {
            if (context)
            {
                Canvas parentCanvas = context.GetComponentInParent<Canvas>();
                if (parentCanvas) return parentCanvas;
            }
            Canvas existing = Object.FindAnyObjectByType<Canvas>();
            if (existing) return existing;

            var go = new GameObject("Title Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1
                                             | AdditionalCanvasShaderChannels.Normal
                                             | AdditionalCanvasShaderChannels.Tangent;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            Undo.RegisterCreatedObjectUndo(go, "Create Title Canvas");
            return canvas;
        }
    }

    [CustomEditor(typeof(TitleLogoText))]
    public sealed class TitleLogoTextEditor : TitleLogoEditorBase
    {
        protected override string[] TargetFields => new[] { "fontAsset", "focusCharacter", "manualFocus", "focusOffset" };

        protected override void DrawNotes()
        {
            var logo = (TitleLogoText)target;
            if (logo.Text && logo.Text.font && logo.Text.font.atlasTexture && !logo.Text.font.atlasTexture.isReadable)
            {
                EditorGUILayout.HelpBox(
                    "This font's atlas is not readable, so flakes are also spawned over empty space (invisible, just extra quads). " +
                    "Dynamic font assets are readable and skip empty cells.", MessageType.None);
            }
        }

        [MenuItem("GameObject/Rythm RPG/Title Logo (TMP)", false, 10)]
        static void CreateTitleLogo(MenuCommand command)
        {
            Canvas canvas = FindOrCreateCanvas(command.context as GameObject);

            GameObject root = new GameObject("Title Logo", typeof(RectTransform));
            GameObjectUtility.SetParentAndAlign(root, canvas.gameObject);
            var rootRt = (RectTransform)root.transform;
            rootRt.anchorMin = rootRt.anchorMax = new Vector2(0.5f, 0.68f);
            rootRt.sizeDelta = new Vector2(1400f, 420f);

            TextMeshProUGUI title = CreateLabel(root.transform, "Title", "RHYTHM RPG", 190f, new Vector2(0f, 40f), new Vector2(1400f, 260f));
            var titleLogo = title.gameObject.AddComponent<TitleLogoText>();
            titleLogo.ApplyLook(TitleLogoLook.InkGoldBurst());
            titleLogo.SetFocusCharacter(2);

            TextMeshProUGUI subtitle = CreateLabel(root.transform, "Subtitle", "a turn-based rhythm tale", 56f, new Vector2(60f, -130f), new Vector2(1200f, 90f));
            var subtitleLogo = subtitle.gameObject.AddComponent<TitleLogoText>();
            subtitleLogo.ApplyLook(TitleLogoLook.GoldGradient());
            subtitleLogo.SetFocusCharacter(-1);

            Undo.RegisterCreatedObjectUndo(root, "Create Title Logo");
            Selection.activeGameObject = title.gameObject;
        }

        static TextMeshProUGUI CreateLabel(Transform parent, string name, string content, float size, Vector2 position, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = position;
            rt.sizeDelta = box;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = size;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.extraPadding = true;
            tmp.raycastTarget = false;
            tmp.color = Color.white;
            return tmp;
        }
    }

    [CustomEditor(typeof(TitleLogoImage))]
    public sealed class TitleLogoImageEditor : TitleLogoEditorBase
    {
        protected override string[] TargetFields => new[] { "focus", "sprite" };

        protected override void DrawPresetButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Sprite Gold Burst")) ApplyPreset(TitleLogoLook.SpriteDefault());
                if (GUILayout.Button("Pixel Sprite")) ApplyPreset(TitleLogoLook.PixelSprite());
            }
            base.DrawPresetButtons();
        }

        protected override void DrawNotes()
        {
            var logo = (TitleLogoImage)target;
            Texture tex = logo.Graphic ? logo.Graphic.mainTexture : null;
            if (tex is Texture2D t2 && !t2.isReadable && logo.Disintegration.flakes)
            {
                EditorGUILayout.HelpBox(
                    "Tip: enable Read/Write on this sprite's texture so flakes skip transparent areas. " +
                    "Without it everything still works, with some invisible extra flakes.", MessageType.None);
            }
            if (logo.Graphic is Image img && img.sprite && img.sprite.packed && logo.Sprite.outlineTexels > 0f)
            {
                EditorGUILayout.HelpBox(
                    "This sprite is packed in an atlas: give it enough atlas padding for the outline, or neighbouring sprites can bleed in.",
                    MessageType.None);
            }
        }

        [MenuItem("GameObject/Rythm RPG/Title Logo (Image)", false, 11)]
        static void CreateTitleLogoImage(MenuCommand command)
        {
            Canvas canvas = FindOrCreateCanvas(command.context as GameObject);
            var go = new GameObject("Title Logo Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var context = command.context as GameObject;
            GameObjectUtility.SetParentAndAlign(go, context && context.GetComponentInParent<Canvas>() ? context : canvas.gameObject);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(512f, 512f);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            var logo = go.AddComponent<TitleLogoImage>();
            logo.ApplyLook(TitleLogoLook.SpriteDefault());
            Undo.RegisterCreatedObjectUndo(go, "Create Title Logo Image");
            Selection.activeGameObject = go;
        }

        [MenuItem("CONTEXT/Image/Add Title Logo Effect")]
        static void AddToImage(MenuCommand command)
        {
            var image = (Image)command.context;
            if (image.GetComponent<TitleLogoImage>()) return;
            var logo = Undo.AddComponent<TitleLogoImage>(image.gameObject);
            logo.ApplyLook(TitleLogoLook.SpriteDefault());
        }
    }
}
