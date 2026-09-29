using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Look of one response row: a bobbing cursor and a darker label on the selected row, a dimmed label otherwise.
    /// Hovering with the mouse selects the row.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PixelChoiceButton : MonoBehaviour, IPointerEnterHandler
    {
        public Image cursor;
        public TextMeshProUGUI label;
        public Color normalColor = new(0.486f, 0.471f, 0.565f, 1f);   // #7C7890
        public Color selectedColor = new(0.173f, 0.220f, 0.408f, 1f); // #2C3868
        public Color disabledColor = new(0.690f, 0.667f, 0.620f, 1f); // #B0AA9E
        [Tooltip("Cursor nudges right by 1 game pixel this often (seconds). 0 = no bob.")]
        [Min(0f)] public float cursorBobInterval = 0.3f;

        private StandardUIResponseButton responseButton;
        private Vector2 cursorHome;
        private bool cursorHomeSet;

        private void Awake() => responseButton = GetComponent<StandardUIResponseButton>();

        private void LateUpdate()
        {
            EventSystem current = EventSystem.current;
            bool selected = current != null && current.currentSelectedGameObject == gameObject;
            bool disabled = responseButton != null && responseButton.response != null && !responseButton.response.enabled;

            if (label != null)
            {
                Color wanted = disabled ? disabledColor : selected ? selectedColor : normalColor;
                if (label.color != wanted) label.color = wanted;
            }

            if (cursor == null) return;
            if (!cursorHomeSet)
            {
                cursorHome = cursor.rectTransform.anchoredPosition;
                cursorHomeSet = true;
            }
            if (cursor.enabled != selected) cursor.enabled = selected;
            if (!selected) return;
            bool nudged = cursorBobInterval > 0f && Mathf.FloorToInt(Time.unscaledTime / cursorBobInterval) % 2 == 1;
            cursor.rectTransform.anchoredPosition = cursorHome + (nudged ? Vector2.right : Vector2.zero);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            EventSystem current = EventSystem.current;
            if (current != null && current.currentSelectedGameObject != gameObject) current.SetSelectedGameObject(gameObject);
        }
    }
}
