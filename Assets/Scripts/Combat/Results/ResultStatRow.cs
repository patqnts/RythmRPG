using TMPro;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// One label/value line on the result screen. The row itself sits in a layout group; only its Content child
    /// moves and fades, so animations never fight the layout.
    /// </summary>
    public sealed class ResultStatRow : MonoBehaviour
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private TMP_Text label;
        [SerializeField] private TMP_Text value;

        private CanvasGroup group;

        public RectTransform Content => content != null ? content : (RectTransform)transform;
        public TMP_Text Label => label;
        public TMP_Text Value => value;

        public CanvasGroup Group
        {
            get
            {
                if (group == null)
                {
                    group = Content.GetComponent<CanvasGroup>();
                    if (group == null) group = Content.gameObject.AddComponent<CanvasGroup>();
                    group.interactable = false;
                    group.blocksRaycasts = false;
                }
                return group;
            }
        }

        public void Assign(RectTransform contentRoot, TMP_Text labelText, TMP_Text valueText)
        {
            content = contentRoot;
            label = labelText;
            value = valueText;
        }

        public void Setup(string labelText, Color labelColor, Color valueColor, int fontSize)
        {
            if (label != null)
            {
                label.text = labelText;
                label.color = labelColor;
                label.fontSize = fontSize;
            }
            if (value != null)
            {
                value.color = valueColor;
                value.fontSize = fontSize;
            }
        }

        public void SetValue(string text)
        {
            if (value != null) value.text = text;
        }
    }
}
