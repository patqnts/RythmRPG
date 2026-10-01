using TMPro;
using UnityEngine;

namespace RythmRPG.Core
{
    public sealed class SceneLoadingView : MonoBehaviour
    {
        private CanvasGroup group;
        private TMP_Text status;
        private UnityEngine.UI.Image progress;
        public float Alpha { get => group.alpha; set => group.alpha = value; }
        public void Show(string destination) { gameObject.SetActive(true); Alpha = 0; status.text = destination; SetProgress(0); }
        public void SetProgress(float value) => progress.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
        public void Hide() => gameObject.SetActive(false);
        public static SceneLoadingView Create(Transform parent, GameSceneCatalog catalog)
        {
            var root = new GameObject("Loading Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            root.transform.SetParent(parent, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            canvas.pixelPerfect = true;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            var rect = (RectTransform)root.transform;
            PauseUi.AddImage(PauseUi.Stretch(PauseUi.Rect("Cover", rect)), catalog.background, true);
            var panel = PauseUi.Centered(PauseUi.Rect("Status", rect), new Vector2(520, 120));
            var text = PauseUi.Label(panel, "", 36, catalog.textColor, TextAlignmentOptions.Center);
            if (catalog.font != null) text.font = catalog.font;
            var textRect = text.rectTransform;
            textRect.anchorMin = new Vector2(0, .4f); textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            var rail = PauseUi.Centered(PauseUi.Rect("Progress", panel), new Vector2(400, 4));
            rail.anchoredPosition = new Vector2(0, -35);
            PauseUi.AddImage(rail, new Color(1, 1, 1, .16f), false);
            var fill = PauseUi.AddImage(PauseUi.Stretch(PauseUi.Rect("Fill", rail)), catalog.textColor, false);
            var view = root.AddComponent<SceneLoadingView>();
            view.group = root.GetComponent<CanvasGroup>(); view.status = text; view.progress = fill;
            view.Hide();
            return view;
        }
    }
}
