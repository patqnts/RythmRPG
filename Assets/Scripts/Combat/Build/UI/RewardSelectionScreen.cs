using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RythmRPG.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    public enum RewardSelectionResult { Claimed, Skipped, Closed }

    /// <summary>
    /// Post-victory reward selection: the saved offer's options as cards (new ability / ability upgrade / passive) with
    /// their previews. Pick with arrows + Enter, number keys, gamepad or mouse. A new ability with every slot full opens
    /// a replacement step that lists each slot's consequences (the last 0 MP action cannot be replaced) or keeps it in
    /// reserve. Claims go through <see cref="RewardDirector.Claim"/>, so an offer is claimed exactly once.
    ///
    /// Visuals come from <see cref="RewardSelectionStyle"/>. The parts are ordinary uGUI objects; a hand-made prefab
    /// works when they are assigned. <see cref="CreateTemplate"/> builds the default layout.
    /// </summary>
    public sealed class RewardSelectionScreen : MonoBehaviour
    {
        private enum Mode { Hidden, Cards, Replace, Claiming, Closing }

        private const int ReserveRow = RunBuildState.SlotCount;

        [Tooltip("Empty = Resources/Combat/UI/RewardSelectionStyle (or built-in defaults).")]
        [SerializeField] private RewardSelectionStyle style;

        [Header("Parts")]
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Graphic dim;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text subtitle;
        [SerializeField] private TMP_Text prompt;
        [SerializeField] private TMP_Text message;
        [SerializeField] private RectTransform cardContainer;
        [Tooltip("Inactive card copied for every option.")]
        [SerializeField] private RewardCardView cardTemplate;
        [Header("Selected reward")]
        [SerializeField] private TMP_Text selectionTitle;
        [SerializeField] private TMP_Text selectionBody;
        [SerializeField] private ScrollRect selectionScroll;
        [SerializeField] private RectTransform selectionPanel;
        [SerializeField] private Button attuneButton;
        [Header("Replacement step")]
        [SerializeField] private CanvasGroup replaceGroup;
        [SerializeField] private TMP_Text replaceTitle;
        [SerializeField] private TMP_Text replaceSubtitle;
        [SerializeField] private RectTransform replaceRows;
        [Tooltip("Inactive row (Image + RewardChoiceButton, first TMP = label, second TMP = details) copied per slot.")]
        [SerializeField] private RectTransform replaceRowTemplate;
        [SerializeField] private AudioSource audioSource;

        private sealed class CardState
        {
            public RewardCardView View;
            public float AppearAt;
            public float Selection;
            public Vector2 Rest;
        }

        private sealed class RowState
        {
            public RectTransform Rect;
            public Image Background;
            public TMP_Text Label;
            public TMP_Text Detail;
            public bool Allowed;
        }

        private readonly List<CardState> cards = new();
        private readonly List<RowState> rows = new();
        private readonly List<GameObject> spawned = new();
        private Mode mode = Mode.Hidden;
        private RunBuildState build;
        private RewardOfferData offer;
        private BuildContentRegistry registry;
        private int selected;
        private int selectedRow;
        private float openedAt;
        private float modeAt;
        private float messageUntil;
        private int claimedCard = -1;
        private bool done;
        private ArtifactInterfaceView artifact;
        private int infoSelected = -1;

        public bool IsOpen => mode != Mode.Hidden;
        public RewardSelectionResult Result { get; private set; }

        private RewardSelectionStyle Style
        {
            get
            {
                if (style == null) style = RewardSelectionStyle.LoadOrDefault();
                return style;
            }
        }

        private void Awake()
        {
            if (mode == Mode.Hidden) HideImmediate();
        }

        /// <summary>Opens the screen for an offer and waits until the player claims or skips.</summary>
        public IEnumerator Show(RunBuildState runBuild, RewardOfferData rewardOffer, BuildContentRegistry contentRegistry)
        {
            if (runBuild == null || rewardOffer == null || rewardOffer.claimed || rewardOffer.options.Count == 0) yield break;
            Open(runBuild, rewardOffer, contentRegistry ?? BuildContentRegistry.Instance);
            if (!isActiveAndEnabled)
            {
                Debug.LogWarning("RewardSelectionScreen could not open because a parent object is inactive.", this);
                mode = Mode.Hidden;
                yield break;
            }
            while (!done) yield return null;
        }

        public void Open(RunBuildState runBuild, RewardOfferData rewardOffer, BuildContentRegistry contentRegistry)
        {
            mode = Mode.Cards; // before activating: Awake hides a screen that is not open
            build = runBuild;
            offer = rewardOffer;
            registry = contentRegistry;
            done = false;
            claimedCard = -1;
            selected = 0;
            Result = RewardSelectionResult.Closed;
            gameObject.SetActive(true);
            GamePause.EnsureEventSystem(); // mouse hover / click
            RewardSelectionStyle s = Style;
            openedAt = modeAt = GamePause.UnpausedRealtime;
            messageUntil = 0f;
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = true;
                group.interactable = true;
            }
            if (dim != null) dim.color = s.DimColor;
            if (title != null)
            {
                title.text = s.Title;
                title.color = s.TitleColor;
            }
            if (subtitle != null)
                subtitle.text = string.IsNullOrEmpty(s.SubtitleFormat) ? string.Empty : string.Format(s.SubtitleFormat, build.DisplayName);
            if (prompt != null) prompt.text = s.CardPrompt;
            if (message != null) message.text = string.Empty;
            SetReplaceVisible(false);
            BuildCards();
            artifact = ArtifactInterfaceView.Ensure(gameObject);
            artifact.SetVisibility(0f);
            artifact.RefreshEffects();
            Play(s.OpenSound);
        }

        // ---------- building ----------

        private void BuildCards()
        {
            ClearSpawned();
            cards.Clear();
            if (cardTemplate == null || cardContainer == null) return;
            RewardSelectionStyle s = Style;
            for (int i = 0; i < offer.options.Count; i++)
            {
                RewardOptionData option = offer.options[i];
                RewardPreview preview = RewardDirector.Preview(build, option, registry);
                RewardCardView view = Instantiate(cardTemplate, cardContainer, false);
                view.gameObject.SetActive(true);
                view.name = "Card " + (i + 1);
                spawned.Add(view.gameObject);
                view.Setup(s, option, preview, IconFor(option), GlyphFor(option), i + 1);
                RewardChoiceButton button = view.Root.GetComponent<RewardChoiceButton>();
                if (button != null)
                {
                    button.Index = i;
                    button.Hovered += HoverCard;
                    button.Clicked += ClickCard;
                }
                cards.Add(new CardState
                {
                    View = view,
                    AppearAt = openedAt + s.CardsDelay + i * s.CardStagger,
                    Rest = view.Root.anchoredPosition
                });
            }
            FitCards();
            UpdateSelectedInfo(true);
            ApplyCards(0f, true);
        }

        // Keep custom horizontal templates supported; the generated vertical grid fits against its height.
        private void FitCards()
        {
            if (cardContainer == null) return;
            RewardSelectionStyle s = Style;
            if (cardContainer.TryGetComponent<GridLayoutGroup>(out var grid))
            {
                float neededHeight = cards.Count * grid.cellSize.y + Mathf.Max(0, cards.Count - 1) * grid.spacing.y;
                float gridScale = Mathf.Min(1f, cardContainer.rect.height / Mathf.Max(1f, neededHeight),
                    cardContainer.rect.width / Mathf.Max(1f, grid.cellSize.x));
                cardContainer.localScale = new Vector3(gridScale, gridScale, 1f);
                return;
            }
            float needed = cards.Count * s.CardSize.x + Mathf.Max(0, cards.Count - 1) * s.CardSpacing;
            float available = cardContainer.rect.width > 1f ? cardContainer.rect.width : 1800f;
            float screenWidth = ((RectTransform)transform).rect.width;
            if (screenWidth > 1f) available = Mathf.Min(available, screenWidth - 96f);
            float scale = needed > available ? available / needed : 1f;
            cardContainer.localScale = new Vector3(scale, scale, 1f);
        }

        private Sprite IconFor(RewardOptionData option) => option.kind switch
        {
            RewardKind.NewAbility => registry.Ability(option.contentId)?.Icon,
            RewardKind.AbilityUpgrade => build.FindInstance(option.targetInstanceId)?.Definition?.Icon,
            RewardKind.Growth => Style.GrowthIcon(option.contentId),
            _ => registry.Passive(option.contentId) is PassiveDefinition passive && passive != null ? passive.Icon : null
        };

        private string GlyphFor(RewardOptionData option)
        {
            if (option.kind == RewardKind.Growth)
                return option.contentId == GrowthRewards.Health ? "HP" : option.contentId == GrowthRewards.Mana ? "MP" : "+";
            if (option.kind != RewardKind.Passive) return "+";
            PassiveDefinition passive = registry.Passive(option.contentId);
            string name = passive != null ? passive.DisplayName : option.contentId;
            return string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
        }

        private void BuildReplaceRows(RewardPreview preview)
        {
            foreach (RowState row in rows)
                if (row.Rect != null) Release(row.Rect.gameObject);
            rows.Clear();
            if (replaceRowTemplate == null || replaceRows == null) return;
            RewardSelectionStyle s = Style;
            for (int index = 0; index <= ReserveRow; index++)
            {
                RectTransform rect = Instantiate(replaceRowTemplate, replaceRows, false);
                rect.gameObject.SetActive(true);
                rect.name = index == ReserveRow ? "Reserve" : "Slot " + (index + 1);
                TMP_Text[] texts = rect.GetComponentsInChildren<TMP_Text>(true);
                var row = new RowState
                {
                    Rect = rect,
                    Background = rect.GetComponent<Image>(),
                    Label = texts.Length > 0 ? texts[0] : null,
                    Detail = texts.Length > 1 ? texts[1] : null
                };
                if (index == ReserveRow)
                {
                    row.Allowed = true;
                    if (row.Label != null) row.Label.text = $"[{index + 1}] {s.ReserveLabel}";
                    if (row.Detail != null) row.Detail.text = "Your loadout stays as it is. Equip it later from the build.";
                }
                else
                {
                    AbilityInstance current = build.GetSlot(index);
                    row.Allowed = preview.ReplaceableSlots.Contains(index);
                    string name = current != null ? RewardDirector.DescribeAbility(build.Quote(current)) : "(empty)";
                    if (row.Label != null) row.Label.text = RewardCardView.Plain($"[{index + 1}] {name}");
                    if (row.Detail != null)
                        row.Detail.text = preview.ReplacementConsequences.TryGetValue(index, out List<string> lines)
                            ? RewardCardView.Plain(string.Join(" ", lines)) : string.Empty;
                }
                RewardChoiceButton button = rect.GetComponent<RewardChoiceButton>();
                if (button != null)
                {
                    button.Index = index;
                    button.Hovered += HoverRow;
                    button.Clicked += ClickRow;
                }
                rows.Add(row);
            }
            selectedRow = FirstAllowedRow(0, +1);
            ApplyRows();
        }

        // ---------- input ----------

        private void Update()
        {
            if (mode == Mode.Hidden) return;
            RewardSelectionStyle s = Style;
            float now = GamePause.UnpausedRealtime;
            FitCards();
            if (group != null && mode != Mode.Closing)
                group.alpha = Mathf.Clamp01((now - openedAt) / Mathf.Max(0.01f, s.FadeInSeconds) * 3f);
            if (mode != Mode.Closing)
                artifact?.SetVisibility(Mathf.Clamp01((now - openedAt) / Mathf.Max(0.01f, s.FadeInSeconds)));

            ApplyCards(Time.unscaledDeltaTime, false);
            if (message != null && messageUntil > 0f && now > messageUntil)
            {
                message.text = string.Empty;
                messageUntil = 0f;
            }

            switch (mode)
            {
                case Mode.Claiming:
                    AnimateStamp(now - modeAt);
                    if (now - modeAt >= s.ClaimHoldSeconds) BeginClose(RewardSelectionResult.Claimed);
                    return;
                case Mode.Closing:
                    float t = Mathf.Clamp01((now - modeAt) / s.FadeOutSeconds);
                    artifact?.SetVisibility(1f - t);
                    if (group != null) group.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 1f, t));
                    if (t >= 1f) FinishClose();
                    return;
            }

            if (GamePause.IsPaused || now < openedAt + s.InputDelay) return;
            UnityEngine.InputSystem.Gamepad pad = UnityEngine.InputSystem.Gamepad.current;
            bool previous = CombatResultStyle.AnyKeyDown(s.PreviousKeys) || (pad != null && (pad.dpad.left.wasPressedThisFrame || pad.dpad.up.wasPressedThisFrame));
            bool next = CombatResultStyle.AnyKeyDown(s.NextKeys) || (pad != null && (pad.dpad.right.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame));
            bool confirm = CombatResultStyle.AnyKeyDown(s.ConfirmKeys) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            bool back = CombatResultStyle.AnyKeyDown(s.BackKeys) || (pad != null && pad.buttonEast.wasPressedThisFrame);
            int number = s.NumberKeys ? NumberPressed() : -1;

            if (mode == Mode.Cards)
            {
                if (number >= 0 && number < cards.Count) Select(number);
                if (previous) NavigateCards(-1);
                if (next) NavigateCards(1);
                if (confirm) ConfirmCard();
                else if (back && s.AllowSkip) BeginClose(RewardSelectionResult.Skipped);
            }
            else if (mode == Mode.Replace)
            {
                if (number >= 0 && number < rows.Count) SelectRow(number, true);
                if (previous) SelectRow(FirstAllowedRow(selectedRow - 1, -1), false);
                if (next) SelectRow(FirstAllowedRow(selectedRow + 1, +1), false);
                if (confirm) ConfirmRow();
                else if (back) SetReplaceVisible(false);
            }
        }

        private static int NumberPressed()
        {
            for (int i = 0; i < 9; i++)
                if (LegacyKeys.WasPressed(KeyCode.Alpha1 + i) || LegacyKeys.WasPressed(KeyCode.Keypad1 + i)) return i;
            return -1;
        }

        private bool AcceptsInput => GamePause.UnpausedRealtime >= openedAt + Style.InputDelay && !GamePause.IsPaused;

        private void NavigateCards(int direction)
        {
            if (cards.Count == 0) return;
            Select(((selected + direction) % cards.Count + cards.Count) % cards.Count);
        }

        private void HoverCard(int index)
        {
            if (mode == Mode.Cards && AcceptsInput) Select(index);
        }

        private void ClickCard(int index)
        {
            if (mode != Mode.Cards || !AcceptsInput) return;
            Select(index);
        }

        private void HoverRow(int index)
        {
            if (mode == Mode.Replace && AcceptsInput && index < rows.Count && rows[index].Allowed) SelectRow(index, false);
        }

        private void ClickRow(int index)
        {
            if (mode != Mode.Replace || !AcceptsInput) return;
            SelectRow(index, true);
            ConfirmRow();
        }

        private void Select(int index)
        {
            if (index < 0 || index >= cards.Count || index == selected) return;
            selected = index;
            UpdateSelectedInfo(true);
            Play(Style.MoveSound);
        }

        private void UpdateSelectedInfo(bool force = false)
        {
            if (!force && infoSelected == selected) return;
            infoSelected = selected;
            RewardPreview preview = selected >= 0 && selected < cards.Count ? cards[selected].View.Preview : null;
            if (selectionTitle != null) selectionTitle.text = RewardCardView.Plain(preview?.Title ?? string.Empty);
            if (selectionBody != null)
            {
                var lines = new List<string>();
                if (!string.IsNullOrEmpty(preview?.Summary)) lines.Add(preview.Summary);
                if (preview != null) lines.AddRange(preview.Details);
                selectionBody.text = RewardCardView.Plain(string.Join("\n\n", lines.Distinct()));
            }
            if (selectionScroll != null)
            {
                if (selectionBody != null)
                {
                    selectionBody.ForceMeshUpdate();
                    LayoutRebuilder.ForceRebuildLayoutImmediate(selectionBody.rectTransform);
                }
                Canvas.ForceUpdateCanvases();
                selectionScroll.StopMovement();
                selectionScroll.content.anchoredPosition = new Vector2(selectionScroll.content.anchoredPosition.x, 0f);
            }
        }

        private void SelectRow(int index, bool allowDisabled)
        {
            if (index < 0 || index >= rows.Count || index == selectedRow) return;
            if (!allowDisabled && !rows[index].Allowed) return;
            selectedRow = index;
            Play(Style.MoveSound);
            ApplyRows();
        }

        private int FirstAllowedRow(int start, int step)
        {
            if (rows.Count == 0) return 0;
            for (int i = 0; i < rows.Count; i++)
            {
                int index = ((start + step * i) % rows.Count + rows.Count) % rows.Count;
                if (rows[index].Allowed) return index;
            }
            return ReserveRow;
        }

        private void ConfirmCard()
        {
            if (selected < 0 || selected >= cards.Count) return;
            RewardPreview preview = cards[selected].View.Preview;
            if (preview != null && preview.NeedsReplacement)
            {
                Play(Style.ConfirmSound);
                BuildReplaceRows(preview);
                SetReplaceVisible(true);
                return;
            }
            Claim(-1);
        }

        private void ConfirmRow()
        {
            if (selectedRow < 0 || selectedRow >= rows.Count) return;
            if (!rows[selectedRow].Allowed)
            {
                ShowMessage("That slot holds your last 0 MP action.");
                Play(Style.DeniedSound);
                return;
            }
            Claim(selectedRow == ReserveRow ? -1 : selectedRow);
        }

        private void Claim(int replaceSlot)
        {
            RewardOptionData option = cards[selected].View.Option;
            ClaimStatus status = RewardDirector.Claim(build, offer, option.optionId, registry, replaceSlot);
            if (status != ClaimStatus.Claimed)
            {
                ShowMessage(status == ClaimStatus.AlreadyClaimed ? "This reward was already claimed." : "That reward can't be taken right now.");
                Play(Style.DeniedSound);
                if (status == ClaimStatus.AlreadyClaimed) BeginClose(RewardSelectionResult.Closed);
                return;
            }
            SetReplaceVisible(false);
            claimedCard = selected;
            mode = Mode.Claiming;
            modeAt = GamePause.UnpausedRealtime;
            if (prompt != null) prompt.text = string.Empty;
            Play(Style.ClaimSound);
        }

        // ---------- presentation ----------

        private void ApplyCards(float deltaTime, bool snap)
        {
            UpdateSelectedInfo();
            if (attuneButton != null) attuneButton.interactable = mode == Mode.Cards && AcceptsInput;
            RewardSelectionStyle s = Style;
            float now = GamePause.UnpausedRealtime;
            float blend = snap ? 1f : 1f - Mathf.Exp(-deltaTime / Mathf.Max(0.001f, s.SelectSmoothing));
            for (int i = 0; i < cards.Count; i++)
            {
                CardState card = cards[i];
                bool isTarget = mode == Mode.Claiming || mode == Mode.Closing ? i == claimedCard : i == selected && mode != Mode.Replace;
                card.Selection = Mathf.Lerp(card.Selection, isTarget ? 1f : 0f, blend);
                float appear = EaseOutCubic((now - card.AppearAt) / s.CardInSeconds);
                Vector2 offset = new(0f, -s.CardInDistance * (1f - appear) + s.SelectedLift * card.Selection);
                RectTransform rect = card.View.Root;
                ArtifactSlimeReaction.Ensure(rect).SetFocused(isTarget);
                rect.anchoredPosition = card.Rest + new Vector2(Mathf.Round(offset.x), Mathf.Round(offset.y));
                rect.localScale = Vector3.one * Mathf.Lerp(1f, s.SelectedScale, card.Selection);
                float focus = mode == Mode.Claiming || mode == Mode.Closing
                    ? (i == claimedCard ? 1f : 0.25f)
                    : Mathf.Lerp(s.UnselectedDim, 1f, card.Selection);
                if (card.View.Group != null) card.View.Group.alpha = appear * focus;
                card.View.SetHighlight(s, card.Selection);
            }
        }

        private void AnimateStamp(float elapsed)
        {
            if (claimedCard < 0 || claimedCard >= cards.Count) return;
            TMP_Text stamp = cards[claimedCard].View.Stamp;
            if (stamp == null) return;
            float t = Mathf.Clamp01(elapsed / 0.22f);
            stamp.rectTransform.localScale = Vector3.one * Mathf.Lerp(2.4f, 1f, EaseOutCubic(t));
            Color color = stamp.color;
            color.a = t;
            stamp.color = color;
        }

        private void ApplyRows()
        {
            RewardSelectionStyle s = Style;
            Color accent = selected >= 0 && selected < cards.Count ? s.KindColor(cards[selected].View.Option.kind) : s.TitleColor;
            for (int i = 0; i < rows.Count; i++)
            {
                RowState row = rows[i];
                bool isSelected = i == selectedRow;
                ArtifactSlimeReaction.Ensure(row.Rect).SetFocused(isSelected && row.Allowed);
                if (row.Background != null)
                    row.Background.color = isSelected ? new Color(accent.r, accent.g, accent.b, 0.28f) : new Color(1f, 1f, 1f, 0.05f);
                if (row.Label != null) row.Label.color = !row.Allowed ? s.DisabledColor : isSelected ? accent : s.TextColor;
                if (row.Detail != null) row.Detail.color = row.Allowed ? s.MutedColor : s.WarningColor;
            }
        }

        private void SetReplaceVisible(bool visible)
        {
            if (selectionPanel != null) selectionPanel.gameObject.SetActive(!visible);
            if (attuneButton != null) attuneButton.gameObject.SetActive(!visible);
            if (visible)
            {
                mode = Mode.Replace;
                modeAt = GamePause.UnpausedRealtime;
            }
            else if (mode == Mode.Replace) mode = Mode.Cards;
            RewardSelectionStyle s = Style;
            if (replaceGroup != null)
            {
                replaceGroup.alpha = visible ? 1f : 0f;
                replaceGroup.blocksRaycasts = visible;
                replaceGroup.interactable = visible;
            }
            if (replaceTitle != null) replaceTitle.text = s.ReplaceTitle;
            if (replaceSubtitle != null) replaceSubtitle.text = s.ReplaceSubtitle;
            if (prompt != null && mode != Mode.Claiming) prompt.text = visible ? s.ReplacePrompt : s.CardPrompt;
            artifact?.RefreshEffects();
        }

        private void ShowMessage(string text)
        {
            if (message == null) return;
            message.text = text;
            message.color = Style.WarningColor;
            messageUntil = GamePause.UnpausedRealtime + 2.5f;
        }

        private void BeginClose(RewardSelectionResult result)
        {
            if (mode == Mode.Closing) return;
            Result = result;
            mode = Mode.Closing;
            modeAt = GamePause.UnpausedRealtime;
            if (group != null) group.blocksRaycasts = false;
        }

        private void FinishClose()
        {
            ClearSpawned();
            foreach (RowState row in rows)
                if (row.Rect != null) Release(row.Rect.gameObject);
            rows.Clear();
            cards.Clear();
            HideImmediate();
            done = true;
        }

        private void HideImmediate()
        {
            mode = Mode.Hidden;
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }
            if (replaceGroup != null)
            {
                replaceGroup.alpha = 0f;
                replaceGroup.blocksRaycasts = false;
            }
            gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            // Switched off mid-show (scene change, battle cancelled): let a waiting Show() finish.
            if (mode != Mode.Hidden)
            {
                mode = Mode.Hidden;
                done = true;
            }
        }

        private void ClearSpawned()
        {
            foreach (GameObject item in spawned)
                if (item != null) Release(item);
            spawned.Clear();
        }

        private static void Release(GameObject item)
        {
            item.SetActive(false);
            if (Application.isPlaying) Destroy(item);
            else DestroyImmediate(item);
        }

        private void Play(AudioClip clip)
        {
            if (clip == null) return;
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
            audioSource.PlayOneShot(clip, Style.Volume);
        }

        private static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        // ---------- template ----------

        /// <summary>
        /// Builds the default screen on its own overlay canvas (above the battle result screen): dimmed backdrop, title,
        /// a row of cards, the replacement step panel and a prompt line.
        /// </summary>
        public static RewardSelectionScreen CreateTemplate(RewardSelectionStyle selectionStyle)
        {
            RewardSelectionStyle s = selectionStyle != null ? selectionStyle : RewardSelectionStyle.LoadOrDefault();
            var root = new GameObject("Reward Selection Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(AudioSource));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.pixelPerfect = true;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1010;
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            AudioSource audio = root.GetComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            TMP_FontAsset font = s.FontAsset;
            Color outline = s.OutlineColor;
            var rootRect = (RectTransform)root.transform;
            ArtifactInterfaceView.Ensure(root);

            Image dimImage = NewImage("Dim", rootRect, s.DimColor, true); // blocks clicks on the game UI behind
            Stretch(dimImage.rectTransform, 0f);

            TMP_Text titleText = NewText("Title", rootRect, font, s.TitleSize, s.TitleColor, outline, TextAlignmentOptions.Center);
            ArtifactInterfaceView.StyleHeading(titleText);
            Place(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -88f), new Vector2(1600f, 100f));
            TMP_Text subtitleText = NewText("Subtitle", rootRect, font, s.SubtitleSize, s.MutedColor, outline, TextAlignmentOptions.Center);
            Place(subtitleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -178f), new Vector2(1600f, 40f));
            subtitleText.gameObject.SetActive(false);

            // Choices form a vertical grid beside a single information panel.
            RectTransform cardsRect = NewRect("Cards", rootRect);
            Place(cardsRect, new Vector2(.5f, .5f), new Vector2(-380f, 12f), new Vector2(960f, 720f));
            GridLayoutGroup layout = cardsRect.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = s.CardSize;
            layout.spacing = new Vector2(0f, s.CardSpacing);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 1;
            layout.startAxis = GridLayoutGroup.Axis.Vertical;
            layout.childAlignment = TextAnchor.MiddleCenter;
            RewardCardView card = BuildCardTemplate(cardsRect, s, font, outline);
            card.gameObject.SetActive(false);

            Image info = NewImage("Selected Reward", rootRect, ArtifactInterfaceStyle.Load().glass, false);
            Place(info.rectTransform, new Vector2(.5f, .5f), new Vector2(470f, 12f), new Vector2(560f, 720f));
            ArtifactInterfaceView.Glass(info.rectTransform);
            ArtifactInterfaceView.Decorate("Info Frame", info.rectTransform, ArtifactGeometry.Shape.Frame, s.FrameColor);
            TMP_Text infoTitle = NewText("Title", info.transform, font, 44, s.TitleColor, Color.clear, TextAlignmentOptions.TopLeft);
            TopBand(infoTitle.rectTransform, 32f, 22f, 94f);
            Wrap(infoTitle, TextOverflowModes.Overflow);
            RectTransform infoScrollRoot = NewRect("Info Scroll", info.transform);
            Stretch(infoScrollRoot, 0f);
            infoScrollRoot.offsetMin = new Vector2(32f, 24f);
            infoScrollRoot.offsetMax = new Vector2(-32f, -138f);
            var infoScroll = infoScrollRoot.gameObject.AddComponent<ScrollRect>();
            RectTransform infoClip = NewRect("Viewport", infoScrollRoot);
            Stretch(infoClip, 0f);
            var infoHit = infoClip.gameObject.AddComponent<Image>();
            infoHit.color = Color.clear;
            infoClip.gameObject.AddComponent<RectMask2D>();
            TMP_Text infoBody = NewText("Body", infoClip, font, s.DetailSize, s.TextColor, Color.clear, TextAlignmentOptions.TopLeft);
            infoBody.rectTransform.anchorMin = new Vector2(0f, 1f);
            infoBody.rectTransform.anchorMax = Vector2.one;
            infoBody.rectTransform.pivot = new Vector2(.5f, 1f);
            infoBody.rectTransform.anchoredPosition = Vector2.zero;
            infoBody.rectTransform.sizeDelta = Vector2.zero;
            Wrap(infoBody, TextOverflowModes.Overflow);
            infoBody.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            infoScroll.viewport = infoClip;
            infoScroll.content = infoBody.rectTransform;
            infoScroll.horizontal = false;
            infoScroll.movementType = ScrollRect.MovementType.Clamped;
            infoScroll.scrollSensitivity = 34f;
            Image attune = NewImage("Attune", rootRect, new Color(.48f, .94f, 1f, .12f), true);
            Place(attune.rectTransform, new Vector2(.5f, 0f), new Vector2(470f, 122f), new Vector2(240f, 56f));
            ArtifactInterfaceView.Glass(attune.rectTransform);
            ArtifactInterfaceView.Decorate("Outline", attune.rectTransform, ArtifactGeometry.Shape.Frame, Color.white);
            var confirmButton = attune.gameObject.AddComponent<Button>();
            confirmButton.targetGraphic = attune;
            var attuneLabel = NewText("Label", attune.transform, font, 36, s.TitleColor, Color.clear, TextAlignmentOptions.Center);
            attuneLabel.text = "Attune";
            Stretch(attuneLabel.rectTransform, 0f);
            confirmButton.navigation = new Navigation { mode = Navigation.Mode.None };
            ArtifactSlimeReaction.Ensure(attune.rectTransform);

            TMP_Text messageText = NewText("Message", rootRect, font, s.SummarySize, s.WarningColor, outline, TextAlignmentOptions.Center);
            Place(messageText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 172f), new Vector2(1600f, 36f));
            TMP_Text promptText = NewText("Prompt", rootRect, font, s.PromptSize, s.MutedColor, outline, TextAlignmentOptions.Center);
            Place(promptText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1600f, 40f));

            // Replacement step.
            RectTransform replaceRect = NewRect("Replace Panel", rootRect);
            Place(replaceRect, new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(1180f, 680f));
            CanvasGroup replaceCanvasGroup = replaceRect.gameObject.AddComponent<CanvasGroup>();
            Image replaceShade = NewImage("Shade", replaceRect, new Color(0f, 0f, 0f, 0.55f), true);
            Stretch(replaceShade.rectTransform, -2000f); // dims the cards behind the panel
            Image replaceFrame = NewImage("Frame", replaceRect, Color.clear, true);
            Stretch(replaceFrame.rectTransform, 0f);
            Image replaceBackground = NewImage("Background", replaceRect, ArtifactInterfaceStyle.Load().glass, false);
            Stretch(replaceBackground.rectTransform, 4f);
            ArtifactInterfaceView.Glass(replaceRect);
            ArtifactInterfaceView.Decorate("Replacement Frame", replaceRect, ArtifactGeometry.Shape.Frame, s.KindColor(RewardKind.NewAbility));
            TMP_Text replaceTitleText = NewText("Title", replaceRect, font, s.CardTitleSize, s.TitleColor, outline, TextAlignmentOptions.Center);
            Place(replaceTitleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -55f), new Vector2(1100f, 60f));
            TMP_Text replaceSubtitleText = NewText("Subtitle", replaceRect, font, s.DetailSize, s.MutedColor, outline, TextAlignmentOptions.Center);
            Place(replaceSubtitleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -102f), new Vector2(1100f, 34f));
            RectTransform rowsRect = NewRect("Rows", replaceRect);
            rowsRect.anchorMin = new Vector2(0f, 0f);
            rowsRect.anchorMax = new Vector2(1f, 1f);
            rowsRect.offsetMin = new Vector2(40f, 36f);
            rowsRect.offsetMax = new Vector2(-40f, -130f);
            VerticalLayoutGroup vertical = rowsRect.gameObject.AddComponent<VerticalLayoutGroup>();
            vertical.spacing = 8f;
            vertical.childControlWidth = true;
            vertical.childControlHeight = true;
            vertical.childForceExpandWidth = true;
            vertical.childForceExpandHeight = false;
            RectTransform rowTemplate = BuildRowTemplate(rowsRect, s, font, outline);
            rowTemplate.gameObject.SetActive(false);
            // Above the cards, below the message and prompt lines.
            replaceRect.SetSiblingIndex(cardsRect.GetSiblingIndex() + 1);

            RewardSelectionScreen screen = root.AddComponent<RewardSelectionScreen>();
            screen.style = selectionStyle;
            screen.group = root.GetComponent<CanvasGroup>();
            screen.dim = dimImage;
            screen.title = titleText;
            screen.subtitle = subtitleText;
            screen.prompt = promptText;
            screen.message = messageText;
            screen.cardContainer = cardsRect;
            screen.cardTemplate = card;
            screen.replaceGroup = replaceCanvasGroup;
            screen.replaceTitle = replaceTitleText;
            screen.replaceSubtitle = replaceSubtitleText;
            screen.replaceRows = rowsRect;
            screen.replaceRowTemplate = rowTemplate;
            screen.audioSource = audio;
            screen.selectionTitle = infoTitle;
            screen.selectionBody = infoBody;
            screen.selectionScroll = infoScroll;
            screen.selectionPanel = info.rectTransform;
            screen.attuneButton = confirmButton;
            confirmButton.onClick.AddListener(() => { if (screen.mode == Mode.Cards && screen.AcceptsInput) screen.ConfirmCard(); });
            // Keep the dim backdrop separate; everything visible belongs to one masked projection.
            RectTransform projection = NewRect("Projection", rootRect);
            Stretch(projection, 0f);
            var projectionChildren = new List<Transform>();
            foreach (Transform child in rootRect)
                if (child != dimImage.transform && child != projection) projectionChildren.Add(child);
            foreach (Transform child in projectionChildren) child.SetParent(projection, false);
            screen.artifact = ArtifactInterfaceView.Ensure(root);
            screen.artifact.MaskSurface(projection, false);
            if (Application.isPlaying) screen.HideImmediate();
            return screen;
        }

        private static RewardCardView BuildCardTemplate(RectTransform parent, RewardSelectionStyle s, TMP_FontAsset font, Color outline)
        {
            RectTransform slot = NewRect("Card Template", parent);
            slot.sizeDelta = s.CardSize;
            LayoutElement element = slot.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = s.CardSize.x;
            element.preferredHeight = s.CardSize.y;

            RectTransform cardRoot = NewRect("Root", slot);
            Stretch(cardRoot, 0f);
            CanvasGroup cardGroup = cardRoot.gameObject.AddComponent<CanvasGroup>();
            cardRoot.gameObject.AddComponent<RewardChoiceButton>();
            Image frame = NewImage("Frame", cardRoot, Color.clear, true); // hover / click target
            Stretch(frame.rectTransform, 0f);
            Image background = NewImage("Background", cardRoot, s.CardColor, false);
            Stretch(background.rectTransform, 5f);
            ArtifactInterfaceView.Glass(cardRoot);
            ArtifactInterfaceView.Decorate("Prismatic Frame", cardRoot, ArtifactGeometry.Shape.Frame, s.FrameColor);

            float width = s.CardSize.x;
            Image strip = NewImage("Kind Strip", cardRoot, Color.white, false);
            TopBand(strip.rectTransform, 140f, 12f, 32f);
            TMP_Text kind = NewText("Kind", strip.rectTransform, font, s.KindSize, s.TextColor, Color.clear,
                TextAlignmentOptions.Left);
            kind.characterSpacing = 0f;
            Stretch(kind.rectTransform, 0f);
            var seal = ArtifactInterfaceView.Decorate("Ability Sigil", cardRoot, ArtifactGeometry.Shape.Sigil,
                new Color(0.48f, 0.94f, 1f, 0.4f));
            Place(seal.rectTransform, new Vector2(0f, .5f), new Vector2(74f, 0f), new Vector2(108f, 108f));

            Image icon = NewImage("Icon", cardRoot, Color.white, false);
            Place(icon.rectTransform, new Vector2(0f, .5f), new Vector2(74f, 0f), new Vector2(80f, 80f));
            TMP_Text glyph = NewText("Glyph", cardRoot, font, 40, Color.white, outline, TextAlignmentOptions.Center);
            Place(glyph.rectTransform, new Vector2(0f, .5f), new Vector2(74f, 0f), new Vector2(100f, 72f));

            TMP_Text cardTitle = NewText("Title", cardRoot, font, s.CardTitleSize, s.TextColor, outline, TextAlignmentOptions.Center);
            TopBand(cardTitle.rectTransform, 140f, 50f, 88f);
            cardTitle.alignment = TextAlignmentOptions.Left;
            Wrap(cardTitle, TextOverflowModes.Ellipsis);
            cardTitle.enableAutoSizing = true;
            cardTitle.fontSizeMin = Mathf.Max(12f, s.CardTitleSize * 0.6f);
            cardTitle.fontSizeMax = s.CardTitleSize;

            TMP_Text hint = NewText("Key Hint", cardRoot, font, s.PromptSize, s.MutedColor, outline, TextAlignmentOptions.Center);
            Place(hint.rectTransform, new Vector2(1f, 1f), new Vector2(-28f, -28f), new Vector2(48f, 36f));

            TMP_Text stamp = NewText("Stamp", cardRoot, font, s.StampSize, Color.white, outline, TextAlignmentOptions.Center);
            Place(stamp.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(width, 90f));
            stamp.rectTransform.localRotation = Quaternion.identity;

            RewardCardView view = slot.gameObject.AddComponent<RewardCardView>();
            view.Assign(cardRoot, cardGroup, frame, background, strip, kind, icon, glyph, cardTitle, null, null, hint, stamp);
            return view;
        }

        private static RectTransform BuildRowTemplate(RectTransform parent, RewardSelectionStyle s, TMP_FontAsset font, Color outline)
        {
            RectTransform row = NewRect("Row Template", parent);
            LayoutElement element = row.gameObject.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = 88f;
            Image background = row.gameObject.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.05f);
            background.raycastTarget = true;
            row.gameObject.AddComponent<RewardChoiceButton>();
            ArtifactInterfaceView.Glass(row);
            ArtifactInterfaceView.Decorate("Outline", row, ArtifactGeometry.Shape.Frame, Color.white);
            TMP_Text label = NewText("Label", row, font, s.SummarySize, s.TextColor, outline, TextAlignmentOptions.TopLeft);
            label.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.offsetMin = new Vector2(20f, 0f);
            label.rectTransform.offsetMax = new Vector2(-20f, -8f);
            label.overflowMode = TextOverflowModes.Ellipsis;
            TMP_Text detail = NewText("Detail", row, font, s.DetailSize, s.MutedColor, outline, TextAlignmentOptions.TopLeft);
            detail.rectTransform.anchorMin = new Vector2(0f, 0f);
            detail.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            detail.rectTransform.offsetMin = new Vector2(20f, 4f);
            detail.rectTransform.offsetMax = new Vector2(-20f, 0f);
            Wrap(detail, TextOverflowModes.Ellipsis);
            return row;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : go.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image NewImage(string name, Transform parent, Color color, bool raycast)
        {
            Image image = NewRect(name, parent).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static TMP_Text NewText(string name, Transform parent, TMP_FontAsset font, int size, Color color, Color outline,
            TextAlignmentOptions alignment)
        {
            TMP_FontAsset body = ArtifactInterfaceStyle.Load().bodyFont;
            return CombatText.CreateUGUI(name, parent, body != null ? body : font, size, color, alignment, Color.clear);
        }

        private static void Wrap(TMP_Text text, TextOverflowModes overflow)
        {
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = overflow;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        /// <summary>Full-width band pinned to the top: side inset, distance from the top, height.</summary>
        private static void TopBand(RectTransform rect, float sideInset, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(sideInset, -top - height);
            rect.offsetMax = new Vector2(-sideInset, -top);
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
