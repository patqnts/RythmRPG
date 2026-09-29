using System.Collections.Generic;
using System.Text;
using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Response menu in the same pixel bubble, floating above the character who answers (usually the player).
    /// One row per response with a cursor on the selected one (see <see cref="PixelChoiceButton"/>). Rows are laid
    /// out here in whole game pixels instead of with layout groups.
    /// </summary>
    [DefaultExecutionOrder(30500)]
    public class PixelChoiceMenuPanel : StandardUIMenuPanel
    {
        [Header("Pixel Bubble")]
        public PixelBubbleFrame frame = new();
        [Tooltip("Parent of the response buttons (the Button Template Holder).")]
        public RectTransform rows;

        [Header("Layout (game pixels)")]
        [Min(0)] public int paddingLeft = 6;
        [Min(0)] public int paddingRight = 6;
        [Min(0)] public int paddingTop = 3;
        [Min(0)] public int paddingBottom = 4;
        [Tooltip("Distance between rows (the font's line height).")]
        [Min(1)] public int rowPitch = 13;
        [Tooltip("Label indent, leaving room for the cursor.")]
        [Min(0)] public int labelIndent = 7;
        [Min(0)] public int minWidth = 24;
        [Tooltip("Height above the responder's pivot when it has no renderers and no PixelSpeechAnchor.")]
        public float fallbackHeadHeight = 1.5f;
        [Tooltip("Auto: the menu goes below the responder (tail up) when the person they answer stands higher on screen.")]
        public BubblePlacement placement = BubblePlacement.Auto;
        [Tooltip("Always keep one response selected, so keyboard and gamepad work without clicking first.")]
        public bool keepSelection = true;

        private readonly List<StandardUIResponseButton> visibleButtons = new();
        private readonly StringBuilder keyBuilder = new();
        private PixelDialogueSpace space;
        private Transform responder;
        private Transform addressee;
        private string layoutKey;
        private BubbleSide side;
        private bool sideDirty = true;

        protected PixelDialogueSpace Space => space != null ? space : space = PixelDialogueSpace.For(this);

        protected override void OnEnable()
        {
            base.OnEnable();
            frame.Pop();
            frame.ForgetTarget();
            layoutKey = null;
            sideDirty = true;
        }

        public override void ShowResponses(Subtitle subtitle, Response[] responses, Transform target)
        {
            // target is the object that receives OnClick (the dialogue UI), not a character, so find who answers.
            responder = FindResponder(responses);
            addressee = subtitle != null && subtitle.speakerInfo != null ? subtitle.speakerInfo.transform : null;
            if (addressee == null || addressee == responder)
                addressee = responder == DialogueManager.currentActor ? DialogueManager.currentConversant : DialogueManager.currentActor;
            base.ShowResponses(subtitle, responses, target);
            frame.Pop();
            layoutKey = null;
            sideDirty = true;
        }

        protected virtual void LateUpdate()
        {
            PixelDialogueSpace pixelSpace = Space;
            if (pixelSpace == null) return;

            CollectVisibleButtons();
            string key = BuildLayoutKey();
            if (key != layoutKey)
            {
                layoutKey = key;
                Layout();
            }
            if (keepSelection) KeepSelection();

            Transform who = responder != null ? responder : DialogueManager.currentActor;
            if (sideDirty)
            {
                side = frame.ChooseSide(pixelSpace, placement, who, addressee, fallbackHeadHeight);
                sideDirty = false;
            }
            frame.PlaceOn(pixelSpace, who, fallbackHeadHeight, side);
        }

        // ---------- Layout ----------

        private void CollectVisibleButtons()
        {
            visibleButtons.Clear();
            foreach (GameObject instance in instantiatedButtons)
            {
                if (instance == null || !instance.activeSelf) continue;
                if (instance.TryGetComponent(out StandardUIResponseButton button)) visibleButtons.Add(button);
            }
            if (buttons != null)
            {
                foreach (StandardUIResponseButton button in buttons)
                {
                    if (button != null && button.gameObject.activeSelf && !visibleButtons.Contains(button)) visibleButtons.Add(button);
                }
            }
        }

        private string BuildLayoutKey()
        {
            keyBuilder.Clear();
            foreach (StandardUIResponseButton button in visibleButtons)
            {
                keyBuilder.Append(button.GetHashCode()).Append(':').Append(button.text).Append('\n');
            }
            return keyBuilder.ToString();
        }

        /// <summary>Stacks the response rows and sizes the bubble around them, in whole game pixels.</summary>
        public void Layout()
        {
            frame.FixSlicedPixelsPerUnit();
            int widest = 0;
            int lineBox = 11;
            foreach (StandardUIResponseButton button in visibleButtons)
            {
                TextMeshProUGUI label = LabelOf(button);
                if (label == null) continue;
                PixelDialogueStyle.EnsurePointFiltering(label.font);
                lineBox = PixelDialogueStyle.LineBoxHeight(label);
                widest = Mathf.Max(widest, PixelDialogueStyle.Ceil(label.GetPreferredValues(10000f, 0f).x));
            }

            int innerWidth = Mathf.Max(minWidth, labelIndent + widest);
            int count = visibleButtons.Count;
            int contentHeight = count == 0 ? lineBox : lineBox + (count - 1) * rowPitch;

            if (rows != null) PixelDialogueStyle.SetTopLeft(rows, paddingLeft, paddingTop, innerWidth, contentHeight);
            for (int i = 0; i < count; i++)
            {
                StandardUIResponseButton button = visibleButtons[i];
                int rowHeight = i == count - 1 ? lineBox : rowPitch;
                PixelDialogueStyle.SetTopLeft((RectTransform)button.transform, 0, i * rowPitch, innerWidth, rowHeight);
                TextMeshProUGUI label = LabelOf(button);
                if (label != null)
                    PixelDialogueStyle.SetTopLeft(label.rectTransform, labelIndent, 0, innerWidth - labelIndent + 2, lineBox);
            }

            frame.SetBodySize(new Vector2Int(paddingLeft + innerWidth + paddingRight, paddingTop + contentHeight + paddingBottom));
        }

        private static TextMeshProUGUI LabelOf(StandardUIResponseButton button) =>
            button != null && button.label != null ? button.label.textMeshProUGUI : null;

        // ---------- Selection ----------

        private void KeepSelection()
        {
            EventSystem current = EventSystem.current;
            if (current == null || visibleButtons.Count == 0) return;
            GameObject selected = current.currentSelectedGameObject;
            foreach (StandardUIResponseButton button in visibleButtons)
            {
                if (button.gameObject == selected) return;
            }

            StandardUIResponseButton first = null;
            foreach (StandardUIResponseButton button in visibleButtons)
            {
                bool enabledResponse = button.response == null || button.response.enabled;
                if (enabledResponse) { first = button; break; }
            }
            if (first == null) first = visibleButtons[0];
            current.SetSelectedGameObject(first.gameObject);
        }

        // ---------- Responder ----------

        protected virtual Transform FindResponder(Response[] responses)
        {
            ConversationModel model = DialogueManager.conversationModel;
            if (responses != null && model != null)
            {
                foreach (Response response in responses)
                {
                    if (response == null || response.destinationEntry == null) continue;
                    var info = model.GetCharacterInfo(response.destinationEntry.ActorID);
                    if (info != null && info.transform != null) return info.transform;
                }
            }
            return DialogueManager.currentActor;
        }
    }
}
