using TMPro;
using RythmRPG.UI.Title;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RythmRPG.Core
{
    public sealed class SceneTitleMenu : MonoBehaviour
    {
        [Header("Scene controls")]
        [Tooltip("Edit this text component to change the start prompt's font, size, color and wording. Runtime does not override its styling.")]
        [SerializeField] private TMP_Text prompt;
        [Tooltip("Container shown instead of the start prompt when Escape is pressed.")]
        [SerializeField] private GameObject quitChoices;
        [SerializeField] private Button quitButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private TMP_Text message;

        [Header("Title reveal")]
        [Tooltip("The title effect that must finish assembling before the menu appears and accepts input.")]
        [SerializeField] private TitleLogoBase titleAssembler;
        [Tooltip("How long the start prompt takes to reveal after the title finishes assembling.")]
        [Min(0f)] [SerializeField] private float promptRevealSeconds = 0.35f;
        [Tooltip("Controls the prompt reveal. Add this to the prompt object so its fade remains editable in the scene.")]
        [SerializeField] private CanvasGroup promptCanvasGroup;

        [Header("Quit selection")]
        [Tooltip("Arrow shown beside QUIT when keyboard, controller, or pointer navigation selects it.")]
        [SerializeField] private TMP_Text quitArrow;
        [Tooltip("Arrow shown beside CANCEL when keyboard, controller, or pointer navigation selects it.")]
        [SerializeField] private TMP_Text cancelArrow;

        private int acceptInputAfterFrame;
        private int checkTitleAfterFrame;
        private bool introReady;
        private bool revealStarted;
        private float revealStartedAt;
        private Vector3 promptRestScale = Vector3.one;
        private bool initialized;
        public static SceneTitleMenu Ensure(GameSceneCatalog catalog)
        {
            var existing = FindAnyObjectByType<SceneTitleMenu>();
            if (existing != null)
            {
                existing.InitializeControls();
                return existing;
            }
            var root = new GameObject("Scene Menu Controls", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10; canvas.pixelPerfect = true;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var panel = PauseUi.Centered(PauseUi.Rect("Actions", root.transform), new Vector2(600, 150));
            panel.anchoredPosition = catalog.menuPosition;
            PauseUi.Vertical(panel, 16, new RectOffset(8, 8, 8, 8));
            var menu = root.AddComponent<SceneTitleMenu>();
            menu.prompt = PauseUi.Label(panel, "PRESS ANY KEY TO START", 34, Color.white);
            menu.prompt.gameObject.name = "Start prompt";
            PauseUi.Size(menu.prompt, height: 64);
            var actions = PauseUi.Rect("Quit or Cancel", panel);
            menu.quitChoices = actions.gameObject;
            PauseUi.Horizontal(actions, 16).childForceExpandWidth = true;
            PauseUi.Size(actions, height: 64);
            menu.quitButton = PauseUi.MakeButton(actions, "QUIT", null, 64, 34);
            menu.cancelButton = PauseUi.MakeButton(actions, "CANCEL", null, 64, 34);
            PauseUi.ButtonLabel(menu.quitButton).gameObject.name = "Quit Label";
            PauseUi.ButtonLabel(menu.cancelButton).gameObject.name = "Cancel Label";
            menu.quitArrow = CreateSelectionArrow(menu.quitButton, "Quit Selection Arrow");
            menu.cancelArrow = CreateSelectionArrow(menu.cancelButton, "Cancel Selection Arrow");
            foreach (var button in new[] { menu.quitButton, menu.cancelButton })
            {
                var colors = button.colors;
                colors.normalColor = new Color(.06f, .06f, .06f, .65f);
                colors.highlightedColor = colors.selectedColor = new Color(.18f, .18f, .18f, .85f);
                colors.pressedColor = new Color(.35f, .35f, .35f, .9f);
                button.colors = colors;
                var edge = PauseUi.Rect("White edge", button.transform);
                edge.anchorMin = Vector2.zero; edge.anchorMax = Vector2.right;
                edge.offsetMin = Vector2.zero; edge.offsetMax = new Vector2(0, 3);
                PauseUi.AddImage(edge, new Color(1, 1, 1, .65f), false);
            }
            PauseUi.Nav(menu.quitButton, menu.cancelButton, menu.cancelButton, menu.cancelButton, menu.cancelButton);
            PauseUi.Nav(menu.cancelButton, menu.quitButton, menu.quitButton, menu.quitButton, menu.quitButton);
            menu.message = PauseUi.Label(panel, "", 22, Color.white, TextAlignmentOptions.Center);
            PauseUi.Size(menu.message, height: 40);
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(true)) if (catalog.font != null) label.font = catalog.font;
            GamePause.EnsureEventSystem();
            foreach (var sceneRoot in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (sceneRoot == root) continue;
                foreach (var label in sceneRoot.GetComponentsInChildren<TMP_Text>())
                    if (label.text.Trim().ToUpperInvariant().Contains("PRESS ANY KEY")) label.gameObject.SetActive(false);
            }
            menu.InitializeControls();
            return menu;
        }

        private void InitializeControls()
        {
            if (initialized || prompt == null || quitChoices == null || quitButton == null || cancelButton == null) return;
            if (titleAssembler == null) titleAssembler = FindAnyObjectByType<TitleLogoBase>();
            if (promptCanvasGroup == null)
            {
                promptCanvasGroup = prompt.GetComponent<CanvasGroup>();
                if (promptCanvasGroup == null) promptCanvasGroup = prompt.gameObject.AddComponent<CanvasGroup>();
            }
            if (quitArrow == null) quitArrow = CreateSelectionArrow(quitButton, "Quit Selection Arrow");
            if (cancelArrow == null) cancelArrow = CreateSelectionArrow(cancelButton, "Cancel Selection Arrow");
            quitButton.onClick.AddListener(ConfirmQuit);
            cancelButton.onClick.AddListener(CancelQuit);
            initialized = true;
            BeginIntro();
        }

        private void CancelQuit() => ShowQuitChoices(false);

        private void Update()
        {
            if (!initialized) return;
            if (!introReady)
            {
                AdvanceIntro();
                return;
            }
            if (Time.frameCount <= acceptInputAfterFrame || GameSceneLoader.IsLoading ||
                GameInput.IsRebinding || Time.frameCount == GameInput.RebindEndedFrame) return;

            var keyboard = Keyboard.current;
            // Escape must win over anyKey, otherwise opening Quit would also start the game.
            bool cancel = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            if (quitChoices.activeSelf)
            {
                foreach (var pad in Gamepad.all) cancel |= pad.buttonEast.wasPressedThisFrame;
                if (cancel) ShowQuitChoices(false);
                return; // Navigation and Submit belong to the Quit / Cancel buttons here.
            }
            if (cancel)
            {
                ShowQuitChoices(true);
                return;
            }

            bool start = keyboard != null && keyboard.anyKey.wasPressedThisFrame;
            start |= Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            foreach (var pad in Gamepad.all) start |= AnyButtonPressed(pad);
            if (!start) return;
            acceptInputAfterFrame = int.MaxValue;
            if (message != null) message.text = "";
            GameSceneLoader.Ensure().StartGame();
        }

        private void LateUpdate()
        {
            if (initialized) UpdateSelectionArrows();
        }

        private void BeginIntro()
        {
            introReady = false;
            revealStarted = false;
            checkTitleAfterFrame = Time.frameCount + 1;
            acceptInputAfterFrame = int.MaxValue;
            promptRestScale = prompt.rectTransform.localScale;
            prompt.gameObject.SetActive(false);
            quitChoices.SetActive(false);
            promptCanvasGroup.alpha = 0f;
            promptCanvasGroup.interactable = false;
            promptCanvasGroup.blocksRaycasts = false;
            UpdateSelectionArrows();
        }

        private void AdvanceIntro()
        {
            if (!revealStarted)
            {
                // Wait through the first scene frames so every title component has received OnEnable and started its tween.
                if (Time.frameCount <= checkTitleAfterFrame) return;
                if (titleAssembler != null && (titleAssembler.IsPlaying || titleAssembler.Disintegrate > 0.001f)) return;

                revealStarted = true;
                revealStartedAt = Time.unscaledTime;
                prompt.gameObject.SetActive(true);
            }

            float duration = Mathf.Max(0f, promptRevealSeconds);
            float t = duration <= 0f ? 1f : Mathf.Clamp01((Time.unscaledTime - revealStartedAt) / duration);
            float eased = t * t * (3f - 2f * t);
            promptCanvasGroup.alpha = eased;
            prompt.rectTransform.localScale = Vector3.LerpUnclamped(promptRestScale * 0.92f, promptRestScale, eased);
            if (t < 1f) return;

            prompt.rectTransform.localScale = promptRestScale;
            promptCanvasGroup.alpha = 1f;
            introReady = true;
            acceptInputAfterFrame = Time.frameCount + 1;
        }

        private static bool AnyButtonPressed(Gamepad pad) =>
            pad.buttonSouth.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame ||
            pad.buttonEast.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame ||
            pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame ||
            pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame ||
            pad.leftTrigger.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame ||
            pad.leftStickButton.wasPressedThisFrame || pad.rightStickButton.wasPressedThisFrame ||
            pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame ||
            pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame;

        private void ShowQuitChoices(bool show)
        {
            prompt.gameObject.SetActive(!show && (introReady || revealStarted));
            quitChoices.SetActive(show);
            // Consume the input that opened/closed this view, including a Cancel Submit.
            acceptInputAfterFrame = Time.frameCount + 1;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(show ? cancelButton.gameObject : null);
            UpdateSelectionArrows();
        }

        private void UpdateSelectionArrows()
        {
            bool showing = quitChoices != null && quitChoices.activeInHierarchy;
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (quitArrow != null) quitArrow.gameObject.SetActive(showing && selected == quitButton.gameObject);
            if (cancelArrow != null) cancelArrow.gameObject.SetActive(showing && selected == cancelButton.gameObject);
        }

        private static TMP_Text CreateSelectionArrow(Button button, string objectName)
        {
            TMP_Text buttonLabel = PauseUi.ButtonLabel(button);
            TextMeshProUGUI arrow = PauseUi.Label(button.transform, ">", buttonLabel != null ? buttonLabel.fontSize : 34f,
                buttonLabel != null ? buttonLabel.color : Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            arrow.gameObject.name = objectName;
            arrow.overflowMode = TextOverflowModes.Overflow;
            if (buttonLabel != null)
            {
                arrow.font = buttonLabel.font;
                arrow.fontSharedMaterial = buttonLabel.fontSharedMaterial;
            }
            RectTransform rect = arrow.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(20f, 0f);
            rect.sizeDelta = new Vector2(40f, 64f);
            arrow.gameObject.SetActive(false);
            return arrow;
        }

        private void ConfirmQuit()
        {
            if (!GameSceneLoader.IsLoading && quitChoices.activeSelf && Time.frameCount > acceptInputAfterFrame) Quit();
        }

        private void OnEnable()
        {
            GameSceneLoader.TransitionFailed += Failed;
            if (Application.isPlaying) InitializeControls();
        }
        private void OnDisable()
        {
            GameSceneLoader.TransitionFailed -= Failed;
            if (quitButton != null) quitButton.onClick.RemoveListener(ConfirmQuit);
            if (cancelButton != null) cancelButton.onClick.RemoveListener(CancelQuit);
            initialized = false;
        }
        private void Failed(SceneLoadRequest request, string reason)
        {
            if (prompt != null) ShowQuitChoices(false);
            if (message != null) message.text = reason;
        }
        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
