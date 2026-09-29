using PixelCrushers;
using PixelCrushers.DialogueSystem;
using RythmRPG.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Eastward-style speech bubble for the Dialogue System: a pixel-art bubble that floats above whoever is speaking,
    /// sizes itself to the line (up to <see cref="maxTextWidth"/>, then wraps), shows the speaker's name, types the line
    /// in monogram and shows a bobbing arrow when the player can continue.
    /// <para>
    /// One panel serves every speaker (NPC and player): each line, the bubble moves to the line's speaker and pops.
    /// Everything is in game pixels inside a <see cref="PixelDialogueSpace"/>. Built by
    /// Tools > Rythm RPG > Dialogue > Build Pixel Bubble Dialogue UI.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(30500)]
    public class PixelBubbleSubtitlePanel : StandardUISubtitlePanel
    {
        [Header("Pixel Bubble")]
        public PixelBubbleFrame frame = new();
        [Tooltip("Speaker name (also assigned to Portrait Name so the Dialogue System fills it in).")]
        public TextMeshProUGUI nameLabel;
        [Tooltip("The line (also assigned to Subtitle Text; has the typewriter).")]
        public TextMeshProUGUI bodyLabel;
        [Tooltip("Shown at the bottom right when the line has finished typing and the player can continue.")]
        public RectTransform continueArrow;

        [Header("Layout (game pixels)")]
        [Tooltip("Lines wrap at this width.")]
        [Min(16)] public int maxTextWidth = 192;
        [Min(0)] public int minTextWidth = 24;
        [Min(0)] public int paddingLeft = 6;
        [Min(0)] public int paddingRight = 6;
        [Min(0)] public int paddingTop = 3;
        [Min(0)] public int paddingBottom = 4;
        [Tooltip("Gap between the name and the line.")]
        [Min(0)] public int nameGap = 1;
        public bool showSpeakerName = true;
        [Tooltip("Arrow distance from the body's right and bottom edges.")]
        public Vector2Int arrowMargin = new(5, 3);
        [Min(0.05f)] public float arrowBobInterval = 0.35f;
        [Tooltip("Height above the speaker's pivot when it has no renderers and no PixelSpeechAnchor.")]
        public float fallbackHeadHeight = 1.5f;
        [Tooltip("Auto: the bubble goes below the speaker (tail up) when the listener stands higher on screen, so it " +
                 "covers neither of them. Chosen once per line.")]
        public BubblePlacement placement = BubblePlacement.Auto;

        [Header("Continue input")]
        [Tooltip("Continue / skip typing with the keys below and the game's Interact action. Mouse clicks use the " +
                 "invisible Continue Button.")]
        public bool handleContinueInput = true;
        public bool useGameInteract = true;
        public KeyCode[] continueKeys = { KeyCode.Space, KeyCode.Return, KeyCode.Z };
        [Tooltip("Presses are ignored for this long after the bubble starts waiting (avoids skipping lines by accident).")]
        [Min(0f)] public float continueInputDelay = 0.12f;

        private PixelDialogueSpace space;
        private AbstractTypewriterEffect typewriter;
        private string laidOutText;
        private string laidOutName;
        private bool laidOutNameVisible;
        private float waitingSince = -1f;
        private BubbleSide side;
        private bool sideDirty = true;
        private Transform sideSpeaker;
        private readonly System.Collections.Generic.HashSet<KeyCode> unsupportedKeys = new();

        protected PixelDialogueSpace Space => space != null ? space : space = PixelDialogueSpace.For(this);

        protected override void OnEnable()
        {
            base.OnEnable();
            frame.Pop();
            frame.ForgetTarget();
            laidOutText = null;
            sideDirty = true;
        }

        public override void SetContent(Subtitle subtitle)
        {
            base.SetContent(subtitle);
            frame.Pop();
            laidOutText = null;
            sideDirty = true;
        }

        // The invisible full-screen continue button must never hold the UI selection, or the Event System's Submit
        // would press it on top of our own key handling (skip + continue in one press).
        public override void Select(bool allowStealFocus) { }

        public override void CheckFocus() { }

        protected override void Update()
        {
            base.Update();
            HandleContinueInput();
        }

        protected virtual void LateUpdate()
        {
            PixelDialogueSpace pixelSpace = Space;
            if (pixelSpace == null) return;

            ReleaseContinueButtonSelection();
            if (NeedsLayout()) Layout();
            UpdateContinueArrow();

            Transform speaker = ResolveSpeaker();
            if (speaker != sideSpeaker) sideDirty = true;
            if (sideDirty)
            {
                // Once per line, so the bubble doesn't flip while people walk.
                side = frame.ChooseSide(pixelSpace, placement, speaker, ResolveListener(speaker), fallbackHeadHeight);
                sideSpeaker = speaker;
                sideDirty = false;
            }
            frame.PlaceOn(pixelSpace, speaker, fallbackHeadHeight, side);
        }

        // ---------- Layout ----------

        private bool NameVisible =>
            showSpeakerName && nameLabel != null && nameLabel.gameObject.activeSelf && !string.IsNullOrEmpty(nameLabel.text);

        private bool NeedsLayout()
        {
            if (bodyLabel == null) return false;
            bool nameVisible = NameVisible;
            return laidOutText == null
                   || laidOutText != bodyLabel.text
                   || laidOutNameVisible != nameVisible
                   || (nameVisible && laidOutName != nameLabel.text);
        }

        /// <summary>Sizes the body to the current name and line, in whole game pixels.</summary>
        public void Layout()
        {
            if (bodyLabel == null) return;
            frame.FixSlicedPixelsPerUnit();
            PixelDialogueStyle.EnsurePointFiltering(bodyLabel.font);
            if (nameLabel != null) PixelDialogueStyle.EnsurePointFiltering(nameLabel.font);
            laidOutText = bodyLabel.text;
            laidOutNameVisible = NameVisible;
            laidOutName = laidOutNameVisible ? nameLabel.text : null;

            int lineBox = PixelDialogueStyle.LineBoxHeight(bodyLabel);
            Vector2 textSize = string.IsNullOrEmpty(bodyLabel.text)
                ? new Vector2(0f, lineBox)
                : bodyLabel.GetPreferredValues(maxTextWidth, 0f);
            int textWidth = Mathf.Min(PixelDialogueStyle.Ceil(textSize.x), maxTextWidth);
            int textHeight = Mathf.Max(lineBox, PixelDialogueStyle.Ceil(textSize.y));

            int nameWidth = 0, nameHeight = 0;
            if (nameLabel != null)
            {
                nameLabel.enabled = laidOutNameVisible;
                if (laidOutNameVisible)
                {
                    Vector2 nameSize = nameLabel.GetPreferredValues(10000f, 0f);
                    nameWidth = PixelDialogueStyle.Ceil(nameSize.x);
                    nameHeight = Mathf.Max(PixelDialogueStyle.LineBoxHeight(nameLabel), PixelDialogueStyle.Ceil(nameSize.y));
                }
            }

            int innerWidth = Mathf.Max(minTextWidth, Mathf.Max(textWidth, nameWidth));
            int top = paddingTop;
            if (laidOutNameVisible)
            {
                PixelDialogueStyle.SetTopLeft(nameLabel.rectTransform, paddingLeft, top, nameWidth + 2, nameHeight);
                top += nameHeight + nameGap;
            }

            // Wider than the measured lines but never wider than the wrap width, so the text wraps exactly as measured.
            int textRectWidth = Mathf.Clamp(textWidth + 2, 1, Mathf.Max(maxTextWidth, textWidth));
            PixelDialogueStyle.SetTopLeft(bodyLabel.rectTransform, paddingLeft, top, textRectWidth, textHeight);

            int bottom = paddingBottom;
            if (continueArrow != null && LastLineReachesArrow(innerWidth)) bottom += Mathf.RoundToInt(continueArrow.sizeDelta.y);

            frame.SetBodySize(new Vector2Int(paddingLeft + innerWidth + paddingRight, top + textHeight + bottom));
        }

        private bool LastLineReachesArrow(int innerWidth)
        {
            bodyLabel.ForceMeshUpdate();
            TMP_TextInfo info = bodyLabel.textInfo;
            if (info == null || info.lineCount == 0) return false;
            TMP_LineInfo line = info.lineInfo[info.lineCount - 1];
            float right = 0f;
            int last = Mathf.Min(line.lastCharacterIndex, info.characterCount - 1);
            for (int i = Mathf.Max(0, line.firstCharacterIndex); i <= last; i++)
            {
                TMP_CharacterInfo character = info.characterInfo[i];
                if (char.IsWhiteSpace(character.character)) continue;
                right = Mathf.Max(right, character.xAdvance);
            }
            int bodyWidth = paddingLeft + innerWidth + paddingRight;
            float arrowLeft = bodyWidth - arrowMargin.x - continueArrow.sizeDelta.x;
            return paddingLeft + right + 2f > arrowLeft;
        }

        // ---------- Continue ----------

        private bool IsWaitingForContinue =>
            continueButton != null && continueButton.gameObject.activeInHierarchy && continueButton.interactable;

        private bool IsTyping
        {
            get
            {
                if (typewriter == null) typewriter = GetTypewriter();
                return typewriter != null && typewriter.isPlaying;
            }
        }

        private void UpdateContinueArrow()
        {
            if (continueArrow == null) return;
            bool show = IsWaitingForContinue && !IsTyping;
            if (continueArrow.gameObject.activeSelf != show) continueArrow.gameObject.SetActive(show);
            if (!show) return;
            bool down = Mathf.FloorToInt(Time.unscaledTime / arrowBobInterval) % 2 == 1;
            continueArrow.anchorMin = continueArrow.anchorMax = continueArrow.pivot = new Vector2(1f, 0f);
            continueArrow.anchoredPosition = new Vector2(-arrowMargin.x, arrowMargin.y - (down ? 1 : 0));
        }

        private void HandleContinueInput()
        {
            if (!IsWaitingForContinue)
            {
                waitingSince = -1f;
                return;
            }
            if (waitingSince < 0f)
            {
                waitingSince = Time.unscaledTime; // never on the frame the button appeared
                return;
            }
            if (!handleContinueInput || GamePause.IsPaused) return;
            if (Time.unscaledTime - waitingSince < continueInputDelay) return;
            if (!ContinuePressed()) return;
            continueButton.onClick.Invoke(); // StandardUIContinueButtonFastForward: finish typing, else continue
        }

        protected virtual bool ContinuePressed()
        {
            if (useGameInteract && GameInput.InteractPressed) return true;
            if (continueKeys == null) return false;
            foreach (KeyCode key in continueKeys)
            {
                if (key == KeyCode.None || unsupportedKeys.Contains(key)) continue;
                try
                {
                    if (InputDeviceManager.IsKeyDown(key)) return true;
                }
                catch (System.Exception)
                {
                    // Some KeyCodes have no Input System equivalent (e.g. KeypadEnter); skip them from now on.
                    unsupportedKeys.Add(key);
                    Debug.LogWarning($"[Pixel Dialogue] Continue key {key} isn't supported by the active input system; ignoring it.", this);
                }
            }
            return false;
        }

        private void ReleaseContinueButtonSelection()
        {
            EventSystem current = EventSystem.current;
            if (current != null && continueButton != null && current.currentSelectedGameObject == continueButton.gameObject)
                current.SetSelectedGameObject(null);
        }

        // ---------- Speaker ----------

        protected virtual Transform ResolveSpeaker()
        {
            Subtitle subtitle = currentSubtitle;
            if (subtitle != null && subtitle.speakerInfo != null && subtitle.speakerInfo.transform != null)
                return subtitle.speakerInfo.transform;
            if (actorOverridingPanel != null) return actorOverridingPanel;
            return DialogueManager.currentConversant;
        }

        /// <summary>Who the line is said to: the subtitle's listener, else the other conversation participant.</summary>
        protected virtual Transform ResolveListener(Transform speaker)
        {
            Subtitle subtitle = currentSubtitle;
            if (subtitle != null && subtitle.listenerInfo != null && subtitle.listenerInfo.transform != null)
                return subtitle.listenerInfo.transform;
            Transform actor = DialogueManager.currentActor;
            Transform conversant = DialogueManager.currentConversant;
            return speaker == actor ? conversant : actor;
        }
    }
}
