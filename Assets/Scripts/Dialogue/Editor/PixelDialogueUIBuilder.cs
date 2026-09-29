using System.IO;
using PixelCrushers;
using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace RythmRPG.Dialogue.EditorTools
{
    /// <summary>
    /// Builds the Eastward-style bubble dialogue UI for the Pixel Crushers Dialogue System:
    /// <list type="bullet">
    /// <item>sets the bubble sprites in Assets/Art/UI/Dialogue to pixel-art import settings (point, uncompressed, 9-slice),</item>
    /// <item>creates "monogram Pixel" — a TMP font asset of monogram rasterised at its native 16 px (crisp, no SDF blur),</item>
    /// <item>writes the prefab Assets/Prefab/UI/Dialogue/Pixel Bubble Dialogue UI.prefab.</item>
    /// </list>
    /// Re-running it rebuilds the prefab in place (same asset, references kept).
    /// </summary>
    public static class PixelDialogueUIBuilder
    {
        public const string ArtFolder = "Assets/Art/UI/Dialogue";
        public const string PrefabFolder = "Assets/Prefab/UI/Dialogue";
        public const string PrefabPath = PrefabFolder + "/Pixel Bubble Dialogue UI.prefab";
        public const string SourceFontPath = "Assets/Fonts/Monogram/monogram.ttf";
        public const string PixelFontPath = "Assets/Fonts/Monogram/monogram Pixel.asset";
        public const string FallbackFontPath = "Assets/Fonts/Monogram/monogram SDF.asset";

        // monogram is drawn on a 16 px em (1 font pixel = 64 units). Its real ascender is 10.66 px, which would put the
        // first baseline between screen pixels, so the pixel font asset uses whole-pixel metrics instead.
        private const int FontSize = 16;
        private const int Ascent = 9;
        private const int Descent = 2;
        private const int LineHeight = 13;
        private const string Prepopulate =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

        private static readonly Color NameColor = Hex("8A8494");
        private static readonly Color TextColor = Hex("2C3868");
        private static readonly Color ShadowColor = new(0f, 0f, 0f, 0.45f);
        private static readonly Color Transparent = new(0f, 0f, 0f, 0f);

        [MenuItem("Tools/Rythm RPG/Dialogue/Build Pixel Bubble Dialogue UI", priority = 200)]
        public static void BuildMenu()
        {
            GameObject prefab = Build();
            if (prefab == null) return;
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            Debug.Log($"[Pixel Dialogue] Built {PrefabPath}. Next: Tools > Rythm RPG > Dialogue > Use Pixel Bubble UI " +
                      "On Dialogue Manager (or drag the prefab into the Dialogue Manager's Display Settings > Dialogue UI).", prefab);
        }

        [MenuItem("Tools/Rythm RPG/Dialogue/Use Pixel Bubble UI On Dialogue Manager", priority = 201)]
        public static void AssignMenu()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) prefab = Build();
            if (prefab == null) return;

            DialogueSystemController[] managers =
                Object.FindObjectsByType<DialogueSystemController>(FindObjectsInactive.Include);
            if (managers.Length == 0)
            {
                EditorUtility.DisplayDialog("Pixel Bubble Dialogue UI",
                    "No Dialogue Manager in the open scene(s). Add one (Tools > Pixel Crushers > Dialogue System > " +
                    "Wizards, or drag the Dialogue Manager prefab in), then run this again.", "OK");
                return;
            }

            UsePixelArrowOnSelectors();
            foreach (DialogueSystemController manager in managers)
            {
                Undo.RecordObject(manager, "Use Pixel Bubble Dialogue UI");
                manager.displaySettings.dialogueUI = prefab;
                // Bubbles wait for the player (continue arrow). Typing is skipped by the first press.
                manager.displaySettings.subtitleSettings.continueButton =
                    DisplaySettings.SubtitleSettings.ContinueButtonMode.Always;
                EditorUtility.SetDirty(manager);
                PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
                EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
                Debug.Log($"[Pixel Dialogue] {manager.name}: Dialogue UI = {prefab.name}, Continue Button = Always. " +
                          "Save the scene to keep it.", manager);
            }
        }

        [MenuItem("Tools/Rythm RPG/Dialogue/Use Pixel Arrow On Proximity Selectors", priority = 202)]
        public static void UsePixelArrowOnSelectors()
        {
            // The pixel arrow (PixelUsableIndicator in the dialogue UI) replaces the selectors' own
            // "(spacebar to interact)" text: turn off the OnGUI text and the Standard UI selector elements.
            int count = 0;
            foreach (ProximitySelector selector in Object.FindObjectsByType<ProximitySelector>(FindObjectsInactive.Include))
            {
                Undo.RecordObject(selector, "Use Pixel Arrow");
                selector.useDefaultGUI = false;
                selector.useButton = PixelDialogueInputBridge.InteractButton; // game Interact: Enter / gamepad South
                MarkChanged(selector);
                DisableStandardSelectorUI(selector.gameObject);
                count++;
            }
            foreach (Selector selector in Object.FindObjectsByType<Selector>(FindObjectsInactive.Include))
            {
                Undo.RecordObject(selector, "Use Pixel Arrow");
                selector.useDefaultGUI = false;
                selector.useButton = PixelDialogueInputBridge.InteractButton; // game Interact: Enter / gamepad South
                MarkChanged(selector);
                DisableStandardSelectorUI(selector.gameObject);
                count++;
            }
            Debug.Log($"[Pixel Dialogue] Pixel interact arrow now used by {count} selector(s). Save the scene to keep it.");
        }

        private static void DisableStandardSelectorUI(GameObject owner)
        {
            foreach (SelectorUseStandardUIElements elements in owner.GetComponents<SelectorUseStandardUIElements>())
            {
                if (!elements.enabled) continue;
                Undo.RecordObject(elements, "Use Pixel Arrow");
                elements.enabled = false;
                MarkChanged(elements);
            }
        }

        private static void MarkChanged(Component component)
        {
            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }

        // ---------------------------------------------------------------- build

        public static GameObject Build()
        {
            Sprite bodySprite = ConfigureSprite("Bubble_Body.png", new Vector4(3f, 3f, 3f, 3f));
            Sprite tailSprite = ConfigureSprite("Bubble_Tail.png", Vector4.zero);
            Sprite arrowSprite = ConfigureSprite("Bubble_Arrow.png", Vector4.zero);
            Sprite cursorSprite = ConfigureSprite("Choice_Cursor.png", Vector4.zero);
            Sprite tailUpSprite = ConfigureSprite("Bubble_Tail_Up.png", Vector4.zero);
            Sprite usableSprite = ConfigureSprite("Usable_Arrow.png", Vector4.zero);
            if (bodySprite == null || tailSprite == null || tailUpSprite == null)
            {
                EditorUtility.DisplayDialog("Pixel Bubble Dialogue UI",
                    $"The bubble sprites are missing from {ArtFolder}.", "OK");
                return null;
            }

            TMP_FontAsset font = GetOrCreatePixelFont();
            if (font == null)
            {
                EditorUtility.DisplayDialog("Pixel Bubble Dialogue UI",
                    $"Could not load or create a monogram TMP font ({SourceFontPath}).", "OK");
                return null;
            }

            EnsureFolder(PrefabFolder);
            var sprites = new Sprites
            {
                Body = bodySprite, TailDown = tailSprite, TailUp = tailUpSprite, Arrow = arrowSprite,
                Cursor = cursorSprite, Usable = usableSprite,
            };
            GameObject root = BuildHierarchy(font, sprites);
            KeepPlacementTuning(root);
            try
            {
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
                if (!success) Debug.LogError($"[Pixel Dialogue] Could not save {PrefabPath}.");
                return success ? prefab : null;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private struct Sprites
        {
            public Sprite Body, TailDown, TailUp, Arrow, Cursor, Usable;
        }

        /// <summary>
        /// Rebuilding must not undo placement tweaks made on the prefab (head/feet gaps, bubble placement, arrow gap),
        /// so they are copied over from the existing prefab.
        /// </summary>
        private static void KeepPlacementTuning(GameObject root)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing == null) return;

            var oldSubtitle = existing.GetComponentInChildren<PixelBubbleSubtitlePanel>(true);
            var newSubtitle = root.GetComponentInChildren<PixelBubbleSubtitlePanel>(true);
            if (oldSubtitle != null && newSubtitle != null)
            {
                CopyFrameTuning(oldSubtitle.frame, newSubtitle.frame);
                newSubtitle.placement = oldSubtitle.placement;
            }

            var oldMenu = existing.GetComponentInChildren<PixelChoiceMenuPanel>(true);
            var newMenu = root.GetComponentInChildren<PixelChoiceMenuPanel>(true);
            if (oldMenu != null && newMenu != null)
            {
                CopyFrameTuning(oldMenu.frame, newMenu.frame);
                newMenu.placement = oldMenu.placement;
            }

            var oldArrow = existing.GetComponentInChildren<PixelUsableIndicator>(true);
            var newArrow = root.GetComponentInChildren<PixelUsableIndicator>(true);
            if (oldArrow != null && newArrow != null) newArrow.headGap = oldArrow.headGap;
        }

        private static void CopyFrameTuning(PixelBubbleFrame from, PixelBubbleFrame to)
        {
            if (from == null || to == null) return;
            to.headGap = from.headGap;
            to.feetGap = from.feetGap;
            to.belowThreshold = from.belowThreshold;
            to.tailInset = from.tailInset;
            to.screenMargin = from.screenMargin;
        }

        private static GameObject BuildHierarchy(TMP_FontAsset font, Sprites sprites)
        {
            // Root: its own overlay canvas, no Canvas Scaler (the Pixel Space does the scaling). If the Dialogue Manager
            // parents it under its own canvas this just becomes a nested canvas, which also works.
            var root = new GameObject("Pixel Bubble Dialogue UI", typeof(RectTransform));
            root.layer = UILayer;
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            canvas.pixelPerfect = false;
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            root.AddComponent<GraphicRaycaster>();
            var dialogueUI = root.AddComponent<StandardDialogueUI>();
            dialogueUI.addEventSystemIfNeeded = true;
            root.AddComponent<PixelDialoguePlayerLock>();
            root.AddComponent<PixelDialogueInputBridge>();

            RectTransform pixelSpace = NewRect("Pixel Space", root.transform);
            pixelSpace.gameObject.AddComponent<PixelDialogueSpace>();

            RectTransform main = NewRect("Main Panel", pixelSpace);
            Stretch(main);
            var mainPanel = main.gameObject.AddComponent<UIPanel>();
            // Focus is handled by the menu panel; the main panel must not select the invisible continue button.
            mainPanel.focusCheckFrequency = 0f;
            mainPanel.refreshSelectablesFrequency = 0f;
            main.gameObject.SetActive(false);

            BuildUsableIndicator(pixelSpace, sprites.Usable);
            PixelBubbleSubtitlePanel subtitle = BuildSubtitlePanel(main, dialogueUI, font, sprites);
            PixelChoiceMenuPanel menu = BuildMenuPanel(main, font, sprites);
            (UIPanel alertPanel, TextMeshProUGUI alertText) = BuildAlertPanel(pixelSpace, font, sprites.Body);

            if (dialogueUI.conversationUIElements == null) dialogueUI.conversationUIElements = new StandardUIDialogueControls();
            if (dialogueUI.alertUIElements == null) dialogueUI.alertUIElements = new StandardUIAlertControls();
            StandardUIDialogueControls controls = dialogueUI.conversationUIElements;
            controls.mainPanel = mainPanel;
            controls.subtitlePanels = new StandardUISubtitlePanel[] { subtitle };
            controls.defaultNPCSubtitlePanel = subtitle;
            controls.defaultPCSubtitlePanel = subtitle;
            controls.allowDialogueActorCustomPanels = true;
            controls.menuPanels = new StandardUIMenuPanel[] { menu };
            controls.defaultMenuPanel = menu;

            dialogueUI.alertUIElements.panel = alertPanel;
            dialogueUI.alertUIElements.alertText = new UITextField(alertText);
            dialogueUI.alertUIElements.queueAlerts = true;
            return root;
        }

        private static PixelBubbleSubtitlePanel BuildSubtitlePanel(Transform parent, StandardDialogueUI dialogueUI,
            TMP_FontAsset font, Sprites sprites)
        {
            Sprite arrowSprite = sprites.Arrow;
            RectTransform rect = NewRect("Bubble Subtitle Panel", parent);

            // Invisible full-screen button: a click anywhere continues (keys are handled by the panel itself).
            RectTransform continueRect = NewRect("Continue Button", rect);
            continueRect.anchorMin = continueRect.anchorMax = new Vector2(0.5f, 0.5f);
            continueRect.sizeDelta = new Vector2(8192f, 8192f);
            Image hitArea = AddImage(continueRect, null, Transparent, Image.Type.Simple, true);
            var continueButton = continueRect.gameObject.AddComponent<Button>();
            continueButton.transition = Selectable.Transition.None;
            continueButton.navigation = new Navigation { mode = Navigation.Mode.None };
            continueButton.targetGraphic = hitArea;
            var fastForward = continueRect.gameObject.AddComponent<StandardUIContinueButtonFastForward>();
            fastForward.dialogueUI = dialogueUI;
            continueRect.gameObject.SetActive(false);

            PixelBubbleFrame frame = BuildFrame(rect, sprites);

            TextMeshProUGUI nameLabel = NewLabel("Name", frame.body, font, NameColor);
            TextMeshProUGUI bodyLabel = NewLabel("Text", frame.body, font, TextColor);
            var typewriter = bodyLabel.gameObject.AddComponent<TextMeshProTypewriterEffect>();
            typewriter.charactersPerSecond = 40f;
            typewriter.fullPauseCharacters = ".!?";
            typewriter.fullPauseDuration = 0.25f;
            typewriter.quarterPauseCharacters = ",;:";
            typewriter.quarterPauseDuration = 0.1f;
            typewriter.silentCharacters = " ";
            fastForward.typewriterEffect = typewriter;

            RectTransform arrowRect = NewRect("Continue Arrow", frame.body);
            AddImage(arrowRect, arrowSprite, Color.white, Image.Type.Simple, false);
            arrowRect.anchorMin = arrowRect.anchorMax = arrowRect.pivot = new Vector2(1f, 0f);
            arrowRect.sizeDelta = SpriteSize(arrowSprite);
            arrowRect.anchoredPosition = new Vector2(-5f, 3f);
            arrowRect.gameObject.SetActive(false);

            var panel = rect.gameObject.AddComponent<PixelBubbleSubtitlePanel>();
            panel.frame = frame;
            panel.nameLabel = nameLabel;
            panel.bodyLabel = bodyLabel;
            panel.continueArrow = arrowRect;

            panel.panel = rect;
            panel.portraitImage = null;
            panel.portraitName = new UITextField(nameLabel);
            panel.subtitleText = new UITextField(bodyLabel);
            panel.continueButton = continueButton;
            panel.addSpeakerName = false;
            panel.accumulateText = false;
            panel.visibility = UIVisibility.UntilSuperceded;
            panel.onlyShowNPCPortraits = false;
            panel.clearTextOnClose = true;
            panel.clearTextOnConversationStart = true;
            panel.focusCheckFrequency = 0f;
            panel.refreshSelectablesFrequency = 0f;
            panel.deactivateOnHidden = true;

            rect.gameObject.SetActive(false);
            return panel;
        }

        private static PixelChoiceMenuPanel BuildMenuPanel(Transform parent, TMP_FontAsset font, Sprites sprites)
        {
            Sprite cursorSprite = sprites.Cursor;
            RectTransform rect = NewRect("Choice Menu Panel", parent);
            rect.gameObject.AddComponent<CanvasGroup>();
            PixelBubbleFrame frame = BuildFrame(rect, sprites);

            RectTransform rows = NewRect("Rows", frame.body);
            Image rowsGraphic = AddImage(rows, null, Transparent, Image.Type.Simple, false);

            // Template row: [cursor] label
            RectTransform template = NewRect("Response Button Template", rows);
            Image hitArea = AddImage(template, null, Transparent, Image.Type.Simple, true);
            var button = template.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hitArea;

            RectTransform cursorRect = NewRect("Cursor", template);
            Image cursorImage = AddImage(cursorRect, cursorSprite, Color.white, Image.Type.Simple, false);
            cursorRect.anchorMin = cursorRect.anchorMax = cursorRect.pivot = new Vector2(0f, 1f);
            cursorRect.sizeDelta = SpriteSize(cursorSprite);
            cursorRect.anchoredPosition = new Vector2(0f, -(Ascent - 7)); // top of the cursor = cap height of the label
            cursorImage.enabled = false;

            TextMeshProUGUI label = NewLabel("Label", template, font, TextColor);
            label.textWrappingMode = TextWrappingModes.NoWrap;

            var responseButton = template.gameObject.AddComponent<StandardUIResponseButton>();
            responseButton.button = button;
            responseButton.label = new UITextField(label);
            responseButton.setLabelColor = false;
            responseButton.defaultColor = TextColor;

            var look = template.gameObject.AddComponent<PixelChoiceButton>();
            look.cursor = cursorImage;
            look.label = label;
            template.gameObject.SetActive(false);

            var panel = rect.gameObject.AddComponent<PixelChoiceMenuPanel>();
            panel.frame = frame;
            panel.rows = rows;
            panel.panel = frame.body.GetComponent<Image>();
            panel.pcImage = null;
            panel.buttons = new StandardUIResponseButton[0];
            panel.buttonTemplate = responseButton;
            panel.buttonTemplateHolder = rowsGraphic;
            panel.buttonAlignment = ResponseButtonAlignment.ToFirst;
            panel.explicitNavigationForTemplateButtons = true;
            panel.loopExplicitNavigation = true;
            panel.blockInputDuration = 0.2f; // the press that finished the last line must not pick a response
            panel.showSelectionWhileInputBlocked = true;
            panel.deactivateOnHidden = true;

            rect.gameObject.SetActive(false);
            return panel;
        }

        private static (UIPanel, TextMeshProUGUI) BuildAlertPanel(Transform parent, TMP_FontAsset font, Sprite bodySprite)
        {
            RectTransform rect = NewRect("Alert Panel", parent);
            RectTransform shadow = NewRect("Shadow", rect);
            AddImage(shadow, bodySprite, ShadowColor, Image.Type.Sliced, false);
            RectTransform body = NewRect("Body", rect);
            AddImage(body, bodySprite, Color.white, Image.Type.Sliced, false);
            TextMeshProUGUI text = NewLabel("Text", body, font, TextColor);

            var box = rect.gameObject.AddComponent<PixelAlertBox>();
            box.body = body;
            box.bodyShadow = shadow;
            box.label = text;

            var panel = rect.gameObject.AddComponent<UIPanel>();
            panel.deactivateOnHidden = true;
            rect.gameObject.SetActive(false);
            return (panel, text);
        }

        /// <summary>Shadow, tail shadow, body and tail, in that draw order.</summary>
        private static void BuildUsableIndicator(Transform parent, Sprite usableSprite)
        {
            RectTransform rect = NewRect("Usable Indicator", parent);
            RectTransform shadow = NewRect("Shadow", rect);
            AddImage(shadow, usableSprite, ShadowColor, Image.Type.Simple, false);
            RectTransform arrow = NewRect("Arrow", rect);
            AddImage(arrow, usableSprite, Color.white, Image.Type.Simple, false);
            var indicator = rect.gameObject.AddComponent<PixelUsableIndicator>();
            indicator.arrow = arrow;
            indicator.arrowShadow = shadow;
            shadow.gameObject.SetActive(false);
            arrow.gameObject.SetActive(false);
        }

        private static PixelBubbleFrame BuildFrame(RectTransform root, Sprites sprites)
        {
            Sprite bodySprite = sprites.Body;
            Sprite tailSprite = sprites.TailDown;
            RectTransform bodyShadow = NewRect("Shadow", root);
            AddImage(bodyShadow, bodySprite, ShadowColor, Image.Type.Sliced, false);
            RectTransform tailShadow = NewRect("Tail Shadow", root);
            AddImage(tailShadow, tailSprite, ShadowColor, Image.Type.Simple, false);
            RectTransform body = NewRect("Body", root);
            AddImage(body, bodySprite, Color.white, Image.Type.Sliced, false);
            RectTransform tail = NewRect("Tail", root);
            AddImage(tail, tailSprite, Color.white, Image.Type.Simple, false);

            Vector2 tailSize = SpriteSize(tailSprite);
            return new PixelBubbleFrame
            {
                root = root,
                body = body,
                bodyShadow = bodyShadow,
                tail = tail,
                tailShadow = tailShadow,
                tailSize = new Vector2Int(Mathf.RoundToInt(tailSize.x), Mathf.RoundToInt(tailSize.y)),
                tailDownSprite = sprites.TailDown,
                tailUpSprite = sprites.TailUp,
            };
        }

        // ---------------------------------------------------------------- font

        private static TMP_FontAsset GetOrCreatePixelFont()
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PixelFontPath);
            if (fontAsset == null)
            {
                var source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
                if (source != null)
                {
                    fontAsset = TMP_FontAsset.CreateFontAsset(source, FontSize, 2, GlyphRenderMode.RASTER_HINTED,
                        256, 256, AtlasPopulationMode.Dynamic, false);
                }
                if (fontAsset == null)
                {
                    Debug.LogWarning($"[Pixel Dialogue] Could not create {PixelFontPath}; using {FallbackFontPath}.");
                    return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackFontPath);
                }

                fontAsset.name = Path.GetFileNameWithoutExtension(PixelFontPath);
                AssetDatabase.CreateAsset(fontAsset, PixelFontPath);
                Texture2D atlas = fontAsset.atlasTexture;
                if (atlas != null)
                {
                    atlas.name = fontAsset.name + " Atlas";
                    AssetDatabase.AddObjectToAsset(atlas, fontAsset);
                }
                Material material = fontAsset.material;
                if (material != null)
                {
                    material.name = fontAsset.name + " Material";
                    AssetDatabase.AddObjectToAsset(material, fontAsset);
                }
            }

            var face = fontAsset.faceInfo;
            face.ascentLine = Ascent;
            face.descentLine = -Descent;
            face.lineHeight = LineHeight;
            fontAsset.faceInfo = face;

            fontAsset.TryAddCharacters(Prepopulate, out string _, false);
            PixelDialogueStyle.EnsurePointFiltering(fontAsset);
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            return fontAsset;
        }

        // ---------------------------------------------------------------- sprites

        private static Sprite ConfigureSprite(string fileName, Vector4 border)
        {
            string path = $"{ArtFolder}/{fileName}";
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
            {
                Debug.LogError($"[Pixel Dialogue] Missing sprite {path}.");
                return null;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spriteBorder = border;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ---------------------------------------------------------------- helpers

        private static int UILayer => LayerMask.NameToLayer("UI") >= 0 ? LayerMask.NameToLayer("UI") : 5;

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = UILayer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Image AddImage(RectTransform rect, Sprite sprite, Color color, Image.Type type, bool raycast)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.type = type;
            image.raycastTarget = raycast;
            image.fillCenter = true;
            image.pixelsPerUnitMultiplier = 1f;
            if (sprite != null && type == Image.Type.Simple) rect.sizeDelta = SpriteSize(sprite);
            return image;
        }

        private static TextMeshProUGUI NewLabel(string name, Transform parent, TMP_FontAsset font, Color color)
        {
            RectTransform rect = NewRect(name, parent);
            PixelDialogueStyle.SetTopLeft(rect, 0, 0, 100, LineHeight);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSharedMaterial = font.material;
            label.fontSize = FontSize;
            label.enableAutoSizing = false;
            label.color = color;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            label.richText = true;
            label.raycastTarget = false;
            label.margin = Vector4.zero;
            label.extraPadding = false;
            label.text = string.Empty;
            return label;
        }

        private static Vector2 SpriteSize(Sprite sprite) =>
            sprite == null ? Vector2.zero : new Vector2(sprite.rect.width, sprite.rect.height);

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out Color color) ? color : Color.magenta;
    }
}
