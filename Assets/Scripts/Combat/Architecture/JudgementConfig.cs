using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Timing windows in milliseconds, plus/minus from the beat (the moment the note reaches the hit line):
    /// Perfect = 0 to Perfect, Good = up to Good, Bad = up to Bad, Miss = up to Miss. A press further away than the
    /// Miss window is ignored. A note that goes past the Bad window without a press is a Miss.
    /// The windows are time-based, so they feel the same at any note speed.
    /// Stationary (laser) notes keep their own per-note windows from the chart.
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Judgement Config", fileName = "JudgementConfig")]
    public sealed class JudgementConfig : ScriptableObject
    {
        public const float MaxWindowMs = 400f;

        [Tooltip("Perfect range ends here, in ms from the beat (typical 15-40).")]
        [SerializeField, Range(1f, MaxWindowMs)] private float perfectMs = 40f;
        [Tooltip("Good range ends here (typical 50-80).")]
        [SerializeField, Range(1f, MaxWindowMs)] private float goodMs = 80f;
        [Tooltip("Bad range ends here (typical 100-125).")]
        [SerializeField, Range(1f, MaxWindowMs)] private float badMs = 125f;
        [Tooltip("Presses between Bad and this count as a Miss (typical 125-180). Further away they are ignored.")]
        [SerializeField, Range(1f, MaxWindowMs)] private float missMs = 180f;
        [Tooltip("A press inside the Miss range uses up the note as a Miss (standard). Off = such presses are ignored.")]
        [SerializeField] private bool pressInMissRangeCountsAsMiss = true;

        /// <summary>Window ends in seconds.</summary>
        public float PerfectWindow => perfectMs * 0.001f;
        public float GoodWindow => goodMs * 0.001f;
        public float BadWindow => badMs * 0.001f;
        public float MissWindow => missMs * 0.001f;
        public bool PressInMissRangeCountsAsMiss => pressInMissRangeCountsAsMiss;

        /// <summary>The (from, to) range in milliseconds that gives this judgement.</summary>
        public Vector2 RangeMs(HitJudgement judgement) => judgement switch
        {
            HitJudgement.Perfect => new Vector2(0f, perfectMs),
            HitJudgement.Good => new Vector2(perfectMs, goodMs),
            HitJudgement.Bad => new Vector2(goodMs, badMs),
            _ => new Vector2(badMs, missMs)
        };

        /// <summary>Judgement for a timing error in seconds (sign ignored: early and late are treated alike).</summary>
        public HitJudgement Evaluate(float seconds)
        {
            float error = Mathf.Abs(seconds);
            if (error <= PerfectWindow) return HitJudgement.Perfect;
            if (error <= GoodWindow) return HitJudgement.Good;
            if (error <= BadWindow) return HitJudgement.Bad;
            return HitJudgement.Miss;
        }

        /// <summary>Sets the four range ends in ms, keeping them ordered.</summary>
        public void SetRangesMs(float perfect, float good, float bad, float miss)
        {
            perfectMs = perfect;
            goodMs = good;
            badMs = bad;
            missMs = miss;
            Sanitize();
        }

        private void OnValidate() => Sanitize();

        private void Sanitize()
        {
            perfectMs = Mathf.Clamp(perfectMs, 1f, MaxWindowMs);
            goodMs = Mathf.Clamp(goodMs, perfectMs, MaxWindowMs);
            badMs = Mathf.Clamp(badMs, goodMs, MaxWindowMs);
            missMs = Mathf.Clamp(missMs, badMs, MaxWindowMs);
        }
    }

#if UNITY_EDITOR
    /// <summary>Shows the windows as one colored range bar with a min-max slider per judgement (milliseconds).</summary>
    [UnityEditor.CustomEditor(typeof(JudgementConfig))]
    internal sealed class JudgementConfigEditor : UnityEditor.Editor
    {
        private static readonly Color PerfectColor = new(1f, 0.84f, 0.2f);
        private static readonly Color GoodColor = new(0.2f, 1f, 0.75f);
        private static readonly Color BadColor = new(1f, 0.5f, 0.12f);
        private static readonly Color MissColor = new(1f, 0.2f, 0.35f);
        private static readonly Color IgnoredColor = new(0.35f, 0.35f, 0.38f);

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            UnityEditor.SerializedProperty perfect = serializedObject.FindProperty("perfectMs");
            UnityEditor.SerializedProperty good = serializedObject.FindProperty("goodMs");
            UnityEditor.SerializedProperty bad = serializedObject.FindProperty("badMs");
            UnityEditor.SerializedProperty miss = serializedObject.FindProperty("missMs");
            UnityEditor.SerializedProperty missPress = serializedObject.FindProperty("pressInMissRangeCountsAsMiss");

            UnityEditor.EditorGUILayout.HelpBox(
                "Milliseconds, plus/minus from the beat. Each judgement is a range; dragging one end moves the " +
                "neighbouring range with it. Typical: Perfect 15-40, Good 50-80, Bad 100-125, Miss 125-180.",
                UnityEditor.MessageType.None);

            float scale = Mathf.Max(50f, miss.floatValue * 1.2f);
            DrawBar(perfect.floatValue, good.floatValue, bad.floatValue, miss.floatValue, scale);
            UnityEditor.EditorGUILayout.Space(4f);

            float p = perfect.floatValue, g = good.floatValue, b = bad.floatValue, m = miss.floatValue;
            float zero = 0f;
            RangeRow("Perfect", ref zero, ref p, false);
            RangeRow("Good", ref p, ref g, true);
            RangeRow("Bad", ref g, ref b, true);
            RangeRow("Miss", ref b, ref m, true);
            using (new UnityEditor.EditorGUI.DisabledScope(true))
                UnityEditor.EditorGUILayout.TextField("Ignored", $"presses beyond {m:0} ms");
            UnityEditor.EditorGUILayout.PropertyField(missPress, new GUIContent("Press In Miss Range = Miss"));

            p = Mathf.Clamp(p, 1f, JudgementConfig.MaxWindowMs);
            g = Mathf.Clamp(g, p, JudgementConfig.MaxWindowMs);
            b = Mathf.Clamp(b, g, JudgementConfig.MaxWindowMs);
            m = Mathf.Clamp(m, b, JudgementConfig.MaxWindowMs);
            perfect.floatValue = Mathf.Round(p);
            good.floatValue = Mathf.Round(g);
            bad.floatValue = Mathf.Round(b);
            miss.floatValue = Mathf.Round(m);
            serializedObject.ApplyModifiedProperties();
        }

        private static void RangeRow(string name, ref float from, ref float to, bool fromEditable)
        {
            Rect row = UnityEditor.EditorGUILayout.GetControlRect();
            Rect label = new(row.x, row.y, UnityEditor.EditorGUIUtility.labelWidth, row.height);
            UnityEditor.EditorGUI.LabelField(label, new GUIContent(name + " (ms)", name + " range in milliseconds"));
            const float fieldWidth = 48f;
            Rect fromField = new(label.xMax, row.y, fieldWidth, row.height);
            Rect slider = new(fromField.xMax + 4f, row.y, row.width - label.width - fieldWidth * 2f - 8f, row.height);
            Rect toField = new(slider.xMax + 4f, row.y, fieldWidth, row.height);

            using (new UnityEditor.EditorGUI.DisabledScope(!fromEditable))
                from = UnityEditor.EditorGUI.FloatField(fromField, from);
            float low = from, high = to;
            UnityEditor.EditorGUI.MinMaxSlider(slider, ref low, ref high, 0f, JudgementConfig.MaxWindowMs);
            if (fromEditable) from = low;
            to = UnityEditor.EditorGUI.FloatField(toField, high);
        }

        private static void DrawBar(float perfect, float good, float bad, float miss, float scale)
        {
            Rect rect = UnityEditor.EditorGUILayout.GetControlRect(false, 22f);
            float Width(float value) => rect.width * Mathf.Clamp01(value / scale);
            float x = rect.x;
            Segment(ref x, rect, Width(perfect), PerfectColor, $"P {perfect:0}");
            Segment(ref x, rect, Width(good) - Width(perfect), GoodColor, $"G {good:0}");
            Segment(ref x, rect, Width(bad) - Width(good), BadColor, $"B {bad:0}");
            Segment(ref x, rect, Width(miss) - Width(bad), MissColor, $"M {miss:0}");
            Segment(ref x, rect, rect.xMax - x, IgnoredColor, "-");
        }

        private static void Segment(ref float x, Rect bar, float width, Color color, string text)
        {
            if (width <= 0f) return;
            Rect segment = new(x, bar.y, width, bar.height);
            UnityEditor.EditorGUI.DrawRect(segment, color);
            if (width > 34f)
            {
                var style = new GUIStyle(UnityEditor.EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.black }
                };
                GUI.Label(segment, text, style);
            }
            x += width;
        }
    }
#endif
}
