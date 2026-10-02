using TMPro;
using UnityEngine;

namespace RythmRPG.Combat
{
    public sealed class RhythmPhraseView : MonoBehaviour
    {
        private CombatBuildRuntime runtime;
        private TextMeshProUGUI label;
        public void Bind(CombatBuildRuntime buildRuntime)
        {
            if (runtime != null) runtime.Changed -= Refresh;
            runtime = buildRuntime;
            runtime.Changed += Refresh;
            if (label == null)
            {
                var root = new GameObject("Rhythm phrase", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
                root.transform.SetParent(transform, false);
                root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                root.GetComponent<Canvas>().sortingOrder = 15;
                var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                var text = new GameObject("Phrase progress", typeof(RectTransform), typeof(TextMeshProUGUI));
                text.transform.SetParent(root.transform, false);
                var rect = text.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
                rect.anchoredPosition = new Vector2(0, -112);
                rect.sizeDelta = new Vector2(520, 34);
                label = text.GetComponent<TextMeshProUGUI>();
                ArtifactInterfaceStyle artifactStyle = ArtifactInterfaceStyle.Load();
                label.font = artifactStyle.bodyFont ?? BuildHudStyle.LoadOrDefault().FontAsset;
                label.fontSize = artifactStyle.BodyFontSize(22);
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;
            }
            Refresh();
        }
        private void Refresh()
        {
            PhraseOutcome phrase = runtime?.CurrentPhrase;
            label.text = phrase == null ? "" : $"{phrase.Label}{(phrase.Final ? " • Closing" : "")}   {phrase.Played}/{phrase.Expected}   {Mathf.RoundToInt(phrase.Accuracy * 100)}%  /  70%";
        }
        private void OnDestroy() { if (runtime != null) runtime.Changed -= Refresh; }
    }
}
