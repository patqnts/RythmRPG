using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RythmRPG.Combat
{
    /// <summary>Mouse support for a reward card or a replacement row: hover selects it, click confirms it.</summary>
    public sealed class RewardChoiceButton : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        public int Index { get; set; }
        public event Action<int> Hovered;
        public event Action<int> Clicked;

        public void OnPointerEnter(PointerEventData eventData) => Hovered?.Invoke(Index);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) Clicked?.Invoke(Index);
        }
    }
}
