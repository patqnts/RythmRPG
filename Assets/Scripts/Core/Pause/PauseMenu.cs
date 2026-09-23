using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RythmRPG.Core
{
    /// <summary>
    /// The pause screen, built at runtime by <see cref="GamePause"/> (uGUI + TextMeshPro): Resume / Settings / Quit,
    /// a Settings page with Controls (rebinding), Display (fullscreen / windowed, resolution) and Gameplay (resume
    /// countdown) tabs, and the "3, 2, 1" countdown shown while resuming. Colors live in <see cref="PauseTheme"/>.
    /// Works with mouse, keyboard and gamepad; Esc / Start / B go back.
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        private enum Tab { Controls, Display, Gameplay }

        private GamePause pause;
        private Canvas canvas;
        private Image dim;
        private RectTransform mainPage, settingsPage;
        private Button resumeButton, settingsButton, quitButton;
        private TMP_Text hintText;

        private readonly Button[] tabButtons = new Button[3];
        private readonly RectTransform[] tabPages = new RectTransform[3];
        private readonly RectTransform[] tabFooters = new RectTransform[3];
        private ScrollRect scroll;
        private TMP_Text controlsStatus;
        private readonly Vector3[] corners = new Vector3[4];
        private GameObject lastSelected;

        /// <summary>Space kept free above and below the Settings panel, in reference pixels (1920x1080).</summary>
        private const float ScreenMargin = 36f;
        private const string ControlsHelp = "Select a binding, then press the new key.  Esc cancels.";
        private readonly List<Selectable[]>[] tabNav = { new(), new(), new() };
        private Tab currentTab;

        private readonly List<(RebindRow row, Button keyboard, Button gamepad)> rebindButtons = new();
        private Button rebindingButton;
        private Coroutine rebindStart;

        private PauseSelector displayModeSelector, resolutionSelector, countdownSelector, focusSelector;
        private List<Vector2Int> resolutions = new();

        private RectTransform countdownRoot;
        private TMP_Text countdownNumber, countdownCaption;

        private void Awake()
        {
            pause = GetComponent<GamePause>();
            Build();
            GamePause.StateChanged += OnStateChanged;
            GameInput.BindingsChanged += RefreshControls;
            OnStateChanged(GamePause.State);
        }

        private void OnDestroy()
        {
            GamePause.StateChanged -= OnStateChanged;
            GameInput.BindingsChanged -= RefreshControls;
        }

        private void Update()
        {
            if (GamePause.State == GamePause.PauseState.Resuming) UpdateCountdown();
        }

        private void LateUpdate()
        {
            if (settingsPage != null && settingsPage.gameObject.activeInHierarchy) KeepSelectionVisible();
        }

        /// <summary>Esc / Start / B while paused: leave Settings, or resume from the main page.</summary>
        public void Back()
        {
            if (GameInput.IsRebinding) return;
            if (settingsPage.gameObject.activeSelf)
            {
                ShowMain(settingsButton);
                return;
            }
            pause.Resume();
        }

        // ---------- State ----------

        private void OnStateChanged(GamePause.PauseState state)
        {
            if (state != GamePause.PauseState.Paused) GameInput.CancelRebind();
            canvas.gameObject.SetActive(state != GamePause.PauseState.Running);
            switch (state)
            {
                case GamePause.PauseState.Paused:
                    dim.color = PauseTheme.Dim;
                    countdownRoot.gameObject.SetActive(false);
                    ShowMain(resumeButton);
                    break;
                case GamePause.PauseState.Resuming:
                    mainPage.gameObject.SetActive(false);
                    settingsPage.gameObject.SetActive(false);
                    bool counting = GamePause.ActiveResumeCountdown > 0;
                    dim.color = counting ? PauseTheme.CountdownDim : Color.clear;
                    countdownRoot.gameObject.SetActive(counting);
                    if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                    UpdateCountdown();
                    break;
            }
        }

        private void ShowMain(Selectable select)
        {
            mainPage.gameObject.SetActive(true);
            settingsPage.gameObject.SetActive(false);
            hintText.text = ResumeHint();
            PauseUi.Select(select);
        }

        private static string ResumeHint()
        {
            RebindRow pauseRow = GameInput.Rows.FirstOrDefault(row => row.Action == GameInput.Pause);
            return pauseRow == null ? string.Empty : $"{pauseRow.KeyboardLabel} / {pauseRow.GamepadLabel}   Resume";
        }

        private void ShowSettings()
        {
            mainPage.gameObject.SetActive(false);
            settingsPage.gameObject.SetActive(true);
            ShowTab(currentTab);
            PauseUi.Select(tabButtons[(int)currentTab]);
        }

        private void ShowTab(Tab tab)
        {
            GameInput.CancelRebind();
            currentTab = tab;
            for (int i = 0; i < tabPages.Length; i++)
            {
                tabPages[i].gameObject.SetActive(i == (int)tab);
                tabFooters[i].gameObject.SetActive(i == (int)tab);
                ColorBlock colors = tabButtons[i].colors;
                colors.normalColor = i == (int)tab ? PauseTheme.TabActive : PauseTheme.Button;
                tabButtons[i].colors = colors;
            }

            scroll.content = tabPages[(int)tab];
            tabPages[(int)tab].anchoredPosition = Vector2.zero; // back to the top
            scroll.velocity = Vector2.zero;
            if (controlsStatus != null) SetStatus(ControlsHelp, false);

            if (tab == Tab.Controls) RefreshControls();
            if (tab == Tab.Display) RefreshDisplay();
            if (tab == Tab.Gameplay) RefreshGameplay();
            WireNavigation();
        }

        private void UpdateCountdown()
        {
            float remaining = GamePause.ResumeCountdownRemaining;
            int whole = Mathf.CeilToInt(remaining);
            countdownNumber.text = whole > 0 ? whole.ToString() : string.Empty;
            float t = remaining - Mathf.Floor(remaining); // 1 -> 0 through each second
            if (t <= 0f && whole > 0) t = 1f;
            countdownNumber.rectTransform.localScale = Vector3.one * (1f + 0.45f * t * t * t);
            Color color = PauseTheme.Accent;
            color.a = Mathf.Clamp01(0.25f + (1f - t) * 3f) * Mathf.Clamp01(t * 6f + 0.35f);
            countdownNumber.color = color;
        }

        // ---------- Build ----------

        private void Build()
        {
            RectTransform canvasRect = PauseUi.Rect("Pause Canvas", transform);
            canvas = canvasRect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            CanvasScaler scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect.gameObject.AddComponent<GraphicRaycaster>();

            dim = PauseUi.AddImage(PauseUi.Stretch(PauseUi.Rect("Dim", canvasRect)), PauseTheme.Dim, true);

            BuildMainPage(canvasRect);
            BuildSettingsPage(canvasRect);
            BuildCountdown(canvasRect);
        }

        private static RectTransform Panel(Transform parent, string name, Vector2 size)
        {
            RectTransform panel = PauseUi.Centered(PauseUi.Rect(name, parent), size);
            PauseUi.AddImage(panel, PauseTheme.Panel, true);
            // Accent strip along the top edge.
            RectTransform edge = PauseUi.Rect("Edge", panel);
            edge.anchorMin = new Vector2(0f, 1f);
            edge.anchorMax = Vector2.one;
            edge.pivot = new Vector2(0.5f, 1f);
            edge.sizeDelta = new Vector2(0f, 6f);
            edge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            PauseUi.AddImage(edge, PauseTheme.PanelEdge);
            return panel;
        }

        private static TMP_Text Title(Transform parent, string text, float size)
        {
            TextMeshProUGUI title = PauseUi.Label(parent, text, size, PauseTheme.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            title.characterSpacing = 14f;
            PauseUi.Size(title, height: size * 1.35f);
            return title;
        }

        private void BuildMainPage(RectTransform root)
        {
            mainPage = Panel(root, "Main Page", new Vector2(640f, 560f));
            PauseUi.Vertical(mainPage, 16f, new RectOffset(64, 64, 52, 40));
            Title(mainPage, "PAUSED", 80f);
            PauseUi.Size(PauseUi.Rect("Spacer", mainPage), height: 12f);

            resumeButton = PauseUi.MakeButton(mainPage, "Resume", () => pause.Resume(), 72f, 34f);
            settingsButton = PauseUi.MakeButton(mainPage, "Settings", ShowSettings, 72f, 34f);
            quitButton = PauseUi.MakeButton(mainPage, "Quit Game", Quit, 72f, 34f);
            PauseUi.Nav(resumeButton, quitButton, settingsButton, null, null);
            PauseUi.Nav(settingsButton, resumeButton, quitButton, null, null);
            PauseUi.Nav(quitButton, settingsButton, resumeButton, null, null);

            PauseUi.Size(PauseUi.Rect("Spacer", mainPage), flexibleHeight: 1f);
            hintText = PauseUi.Label(mainPage, string.Empty, 22f, PauseTheme.MutedText);
            PauseUi.Size(hintText, height: 30f);
        }

        private void BuildSettingsPage(RectTransform root)
        {
            settingsPage = Panel(root, "Settings Page", Vector2.zero);
            // Full height of the screen minus a margin, so it always fits (16:9, 16:10, 4:3, ultrawide...).
            settingsPage.anchorMin = new Vector2(0.5f, 0f);
            settingsPage.anchorMax = new Vector2(0.5f, 1f);
            settingsPage.sizeDelta = new Vector2(1180f, -2f * ScreenMargin);
            PauseUi.Vertical(settingsPage, 12f, new RectOffset(56, 56, 30, 28));
            Title(settingsPage, "SETTINGS", 52f);

            RectTransform tabs = PauseUi.Rect("Tabs", settingsPage);
            PauseUi.Horizontal(tabs, 12f).childForceExpandWidth = true;
            PauseUi.Size(tabs, height: 56f);
            string[] names = { "Controls", "Display", "Gameplay" };
            for (int i = 0; i < names.Length; i++)
            {
                Tab tab = (Tab)i;
                tabButtons[i] = PauseUi.MakeButton(tabs, names[i], () => ShowTab(tab), 56f, 28f);
                PauseUi.Size(tabButtons[i], flexibleWidth: 1f);
            }

            // Scrollable content: when the rows do not fit, a scrollbar appears on the right and the list scrolls
            // (mouse wheel, dragging the bar, or automatically to follow the keyboard / gamepad selection).
            RectTransform scrollArea = PauseUi.Rect("Scroll Area", settingsPage);
            PauseUi.Size(scrollArea, flexibleHeight: 1f);
            PauseUi.AddImage(scrollArea, Color.clear, true); // catches the mouse wheel over empty space

            RectTransform viewport = PauseUi.Stretch(PauseUi.Rect("Viewport", scrollArea));
            PauseUi.AddImage(viewport, Color.clear, true);
            viewport.gameObject.AddComponent<RectMask2D>();

            scroll = scrollArea.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 45f;
            scroll.verticalScrollbar = PauseUi.VerticalScrollbar(scrollArea, 14f);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = 10f;

            RectTransform footer = PauseUi.Rect("Footer", settingsPage);
            PauseUi.Size(footer, height: 60f);

            BuildControlsTab(PagePanel(viewport, Tab.Controls), FooterPanel(footer, Tab.Controls));
            BuildDisplayTab(PagePanel(viewport, Tab.Display), FooterPanel(footer, Tab.Display));
            BuildGameplayTab(PagePanel(viewport, Tab.Gameplay), FooterPanel(footer, Tab.Gameplay));
            settingsPage.gameObject.SetActive(false);
        }

        private RectTransform PagePanel(RectTransform viewport, Tab tab)
        {
            // Top-anchored, as tall as its rows (ContentSizeFitter), so the ScrollRect can scroll it.
            RectTransform page = PauseUi.Rect(tab + " Page", viewport);
            page.anchorMin = new Vector2(0f, 1f);
            page.anchorMax = new Vector2(1f, 1f);
            page.pivot = new Vector2(0.5f, 1f);
            page.sizeDelta = Vector2.zero;
            page.anchoredPosition = Vector2.zero;
            PauseUi.Vertical(page, 6f, new RectOffset(0, 0, 4, 4));
            ContentSizeFitter fitter = page.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            tabPages[(int)tab] = page;
            return page;
        }

        private RectTransform FooterPanel(RectTransform footer, Tab tab)
        {
            RectTransform panel = PauseUi.Stretch(PauseUi.Rect(tab + " Footer", footer));
            PauseUi.Horizontal(panel, 16f, alignment: TextAnchor.MiddleRight);
            tabFooters[(int)tab] = panel;
            return panel;
        }

        /// <summary>Scrolls the settings content so the selected control is fully visible.</summary>
        private void KeepSelectionVisible()
        {
            if (scroll == null || scroll.content == null || EventSystem.current == null) return;
            // Only when the selection moves with keyboard / gamepad, so the mouse wheel is never fought.
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected == lastSelected) return;
            lastSelected = selected;
            if (selected == null || selected == PointerSelects.LastPointerSelection) return;
            if (!selected.transform.IsChildOf(scroll.content)) return;

            RectTransform viewport = scroll.viewport;
            ((RectTransform)selected.transform).GetWorldCorners(corners);
            float top = viewport.InverseTransformPoint(corners[1]).y;
            float bottom = viewport.InverseTransformPoint(corners[0]).y;
            Rect view = viewport.rect;
            const float pad = 10f;

            float shift = 0f;
            if (top > view.yMax - pad) shift = -(top - (view.yMax - pad));
            else if (bottom < view.yMin + pad) shift = (view.yMin + pad) - bottom;
            if (Mathf.Approximately(shift, 0f)) return;

            RectTransform content = scroll.content;
            float maxScroll = Mathf.Max(0f, content.rect.height - view.height);
            Vector2 position = content.anchoredPosition;
            position.y = Mathf.Clamp(position.y + shift, 0f, maxScroll);
            content.anchoredPosition = position;
        }

        // ----- Controls -----

        private void BuildControlsTab(RectTransform page, RectTransform footer)
        {
            RectTransform header = PauseUi.Rect("Header", page);
            PauseUi.Horizontal(header, 16f, new RectOffset(20, 0, 0, 0));
            PauseUi.Size(header, height: 36f);
            PauseUi.Size(PauseUi.Label(header, "ACTION", 22f, PauseTheme.MutedText, TextAlignmentOptions.MidlineLeft), flexibleWidth: 1f);
            PauseUi.Size(PauseUi.Label(header, "KEYBOARD", 22f, PauseTheme.MutedText), 280f);
            PauseUi.Size(PauseUi.Label(header, "GAMEPAD", 22f, PauseTheme.MutedText), 280f);

            List<Selectable[]> nav = tabNav[(int)Tab.Controls];
            int index = 0;
            foreach (RebindRow row in GameInput.Rows)
            {
                RectTransform line = PauseUi.Rect(row.Label + " Row", page);
                PauseUi.Horizontal(line, 16f, new RectOffset(20, 0, 0, 0));
                PauseUi.Size(line, height: 44f);
                if (index++ % 2 == 0) PauseUi.AddImage(line, PauseTheme.RowStripe);

                PauseUi.Size(PauseUi.Label(line, row.Label, 28f, PauseTheme.Text, TextAlignmentOptions.MidlineLeft), flexibleWidth: 1f);
                Button keyboard = RebindButton(line, row, false);
                Button gamepad = row.GamepadIndex >= 0 ? RebindButton(line, row, true) : null;
                if (gamepad == null)
                    PauseUi.Size(PauseUi.Label(line, row.FixedGamepadLabel ?? "-", 26f, PauseTheme.MutedText), 280f);
                rebindButtons.Add((row, keyboard, gamepad));
                nav.Add(gamepad != null ? new Selectable[] { keyboard, gamepad } : new Selectable[] { keyboard });
            }

            controlsStatus = PauseUi.Label(footer, ControlsHelp, 22f, PauseTheme.MutedText, TextAlignmentOptions.MidlineLeft);
            PauseUi.Size(controlsStatus, flexibleWidth: 1f);
            Button reset = PauseUi.MakeButton(footer, "Reset to Defaults", ResetBindings, 56f, 26f, 280f);
            Button back = PauseUi.MakeButton(footer, "Back", () => ShowMain(settingsButton), 56f, 26f, 200f);
            nav.Add(new Selectable[] { reset, back });
        }

        private Button RebindButton(RectTransform line, RebindRow row, bool gamepad)
        {
            Button button = null;
            button = PauseUi.MakeButton(line, "-", () => BeginRebind(row, gamepad, button), 40f, 24f, 280f);
            return button;
        }

        private void BeginRebind(RebindRow row, bool gamepad, Button button)
        {
            GameInput.CancelRebind();
            if (rebindStart != null) StopCoroutine(rebindStart);
            rebindStart = StartCoroutine(RebindNextFrame(row, gamepad, button));
        }

        private IEnumerator RebindNextFrame(RebindRow row, bool gamepad, Button button)
        {
            rebindingButton = button;
            PauseUi.ButtonLabel(button).text = gamepad ? "Press a button..." : "Press a key...";
            PauseUi.ButtonLabel(button).color = PauseTheme.Accent;
            yield return null; // let the click / submit that started this finish first
            rebindStart = null;
            if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = false;
            SetStatus($"Press the new {(gamepad ? "button" : "key")} for {row.Label}...  (Esc cancels)", true);
            GameInput.StartRebind(row, gamepad, bound =>
            {
                if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = true;
                rebindingButton = null;
                RefreshControls();
                PauseUi.Select(button);
                if (!bound)
                {
                    SetStatus("Cancelled. Nothing changed.", false);
                    return;
                }
                string key = gamepad ? row.GamepadLabel : row.KeyboardLabel;
                RebindRow swapped = GameInput.LastSwappedRow;
                string swapNote = swapped != null
                    ? $"  {swapped.Label} now uses {(gamepad ? swapped.GamepadLabel : swapped.KeyboardLabel)}."
                    : string.Empty;
                SetStatus($"{row.Label} is now {key}.{swapNote}", true);
            });
        }

        private void ResetBindings()
        {
            GameInput.ResetToDefaults();
            SetStatus("All controls reset to their defaults.", true);
        }

        private void RefreshControls()
        {
            foreach ((RebindRow row, Button keyboard, Button gamepad) in rebindButtons)
            {
                SetBindingLabel(keyboard, row.KeyboardLabel);
                if (gamepad != null) SetBindingLabel(gamepad, row.GamepadLabel);
            }
            if (mainPage != null && mainPage.gameObject.activeSelf)
                hintText.text = ResumeHint();
        }

        private void SetBindingLabel(Button button, string text)
        {
            if (button == null || button == rebindingButton) return;
            TMP_Text label = PauseUi.ButtonLabel(button);
            bool unbound = string.IsNullOrEmpty(text) || text == "-";
            label.text = unbound ? "Not set" : text;
            label.color = unbound ? PauseTheme.MutedText : PauseTheme.Accent; // assigned keys stand out in gold
        }

        private void SetStatus(string text, bool highlight)
        {
            if (controlsStatus == null) return;
            controlsStatus.text = text;
            controlsStatus.color = highlight ? PauseTheme.Accent : PauseTheme.MutedText;
        }

        // ----- Display -----

        private void BuildDisplayTab(RectTransform page, RectTransform footer)
        {
            List<Selectable[]> nav = tabNav[(int)Tab.Display];
            displayModeSelector = PauseSelector.Create(page, "Display Mode");
            displayModeSelector.SetOptions(new[]
            {
                GameSettings.Describe(DisplayMode.Fullscreen),
                GameSettings.Describe(DisplayMode.Borderless),
                GameSettings.Describe(DisplayMode.Windowed)
            }, 0);
            displayModeSelector.Changed += _ => UpdateResolutionAvailability();
            resolutionSelector = PauseSelector.Create(page, "Resolution");
            nav.Add(new Selectable[] { displayModeSelector });
            nav.Add(new Selectable[] { resolutionSelector });

            if (Application.isEditor)
            {
                TMP_Text note = PauseUi.Label(page, "Display changes only take effect in a build, not in the Editor's Game view.",
                    22f, PauseTheme.MutedText, TextAlignmentOptions.MidlineLeft);
                PauseUi.Size(note, height: 44f);
            }

            Button apply = PauseUi.MakeButton(footer, "Apply", ApplyDisplay, 56f, 26f, 200f);
            Button back = PauseUi.MakeButton(footer, "Back", () => ShowMain(settingsButton), 56f, 26f, 200f);
            nav.Add(new Selectable[] { apply, back });
        }

        private void RefreshDisplay()
        {
            displayModeSelector.SetOptions(new[]
            {
                GameSettings.Describe(DisplayMode.Fullscreen),
                GameSettings.Describe(DisplayMode.Borderless),
                GameSettings.Describe(DisplayMode.Windowed)
            }, (int)GameSettings.CurrentDisplayMode);

            resolutions = GameSettings.AvailableResolutions();
            Vector2Int current = GameSettings.CurrentResolution;
            int index = Mathf.Max(0, resolutions.IndexOf(current));
            resolutionSelector.SetOptions(resolutions.Select(size => $"{size.x} x {size.y}").ToArray(), index);
            UpdateResolutionAvailability();
        }

        private void UpdateResolutionAvailability()
        {
            bool borderless = (DisplayMode)displayModeSelector.Index == DisplayMode.Borderless;
            if (borderless)
            {
                Vector2Int native = GameSettings.NativeResolution;
                int nativeIndex = resolutions.IndexOf(native);
                if (nativeIndex >= 0)
                    resolutionSelector.SetOptions(resolutions.Select(size => $"{size.x} x {size.y}").ToArray(), nativeIndex);
            }
            resolutionSelector.SetInteractableState(!borderless);
        }

        private void ApplyDisplay()
        {
            var mode = (DisplayMode)displayModeSelector.Index;
            Vector2Int size = resolutionSelector.Index < resolutions.Count ? resolutions[resolutionSelector.Index]
                : GameSettings.CurrentResolution;
            GameSettings.ApplyDisplay(mode, size);
        }

        // ----- Gameplay -----

        private void BuildGameplayTab(RectTransform page, RectTransform footer)
        {
            List<Selectable[]> nav = tabNav[(int)Tab.Gameplay];
            countdownSelector = PauseSelector.Create(page, "Resume Countdown (Combat)");
            countdownSelector.Changed += index => GameSettings.ResumeCountdownSeconds = index;
            focusSelector = PauseSelector.Create(page, "Pause When Window Loses Focus");
            focusSelector.Changed += index => GameSettings.PauseOnFocusLoss = index == 1;
            nav.Add(new Selectable[] { countdownSelector });
            nav.Add(new Selectable[] { focusSelector });

            Button back = PauseUi.MakeButton(footer, "Back", () => ShowMain(settingsButton), 56f, 26f, 200f);
            nav.Add(new Selectable[] { back });
        }

        private void RefreshGameplay()
        {
            string[] countdownOptions = Enumerable.Range(0, GameSettings.MaxResumeCountdown + 1)
                .Select(seconds => seconds == 0 ? "Off" : $"{seconds} s").ToArray();
            countdownSelector.SetOptions(countdownOptions, GameSettings.ResumeCountdownSeconds);
            focusSelector.SetOptions(new[] { "Off", "On" }, GameSettings.PauseOnFocusLoss ? 1 : 0);
        }

        // ----- Countdown -----

        private void BuildCountdown(RectTransform root)
        {
            countdownRoot = PauseUi.Stretch(PauseUi.Rect("Countdown", root));
            countdownNumber = PauseUi.Label(countdownRoot, "3", 260f, PauseTheme.Accent, TextAlignmentOptions.Center, FontStyles.Bold);
            PauseUi.Centered(countdownNumber.rectTransform, new Vector2(600f, 340f));
            countdownNumber.outlineWidth = 0.18f;
            countdownNumber.outlineColor = new Color32(20, 16, 8, 255);
            countdownCaption = PauseUi.Label(countdownRoot, "GET READY", 34f, PauseTheme.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            countdownCaption.characterSpacing = 18f;
            PauseUi.Centered(countdownCaption.rectTransform, new Vector2(800f, 60f));
            countdownCaption.rectTransform.anchoredPosition = new Vector2(0f, -210f);
            countdownRoot.gameObject.SetActive(false);
        }

        // ---------- Navigation ----------

        private void WireNavigation()
        {
            List<Selectable[]> rows = tabNav[(int)currentTab];
            Selectable firstItem = rows.Count > 0 ? rows[0][0] : null;
            Selectable lastItem = rows.Count > 0 ? rows[rows.Count - 1][0] : null;
            for (int i = 0; i < tabButtons.Length; i++)
            {
                Selectable left = tabButtons[(i + tabButtons.Length - 1) % tabButtons.Length];
                Selectable right = tabButtons[(i + 1) % tabButtons.Length];
                PauseUi.Nav(tabButtons[i], lastItem, firstItem, left, right);
            }

            for (int r = 0; r < rows.Count; r++)
            {
                Selectable[] row = rows[r];
                for (int c = 0; c < row.Length; c++)
                {
                    Selectable up = r > 0 ? Pick(rows[r - 1], c, row.Length) : tabButtons[(int)currentTab];
                    Selectable down = r < rows.Count - 1 ? Pick(rows[r + 1], c, row.Length) : tabButtons[(int)currentTab];
                    Selectable left = c > 0 ? row[c - 1] : null;
                    Selectable right = c < row.Length - 1 ? row[c + 1] : null;
                    PauseUi.Nav(row[c], up, down, left, right);
                }
            }
        }

        /// <summary>Item in a neighbouring row that lines up with column <paramref name="column"/> of <paramref name="width"/>.</summary>
        private static Selectable Pick(Selectable[] row, int column, int width)
        {
            if (row.Length == width) return row[column];
            // Rows of different widths: the last column maps to the last item, everything else to the first.
            return column == width - 1 && width > 1 ? row[row.Length - 1] : row[0];
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
