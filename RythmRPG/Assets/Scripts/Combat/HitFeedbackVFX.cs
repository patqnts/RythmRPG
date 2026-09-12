using System.Collections;
using UnityEngine;

/// <summary>
/// Runtime-created rhythm feedback. It deliberately has no prefab dependencies so
/// every combat scene gets readable feedback as soon as CombatManager is present.
/// </summary>
public class HitFeedbackVFX : MonoBehaviour
{
    [SerializeField] private float labelLifetime = 0.65f;
    [SerializeField] private float labelRiseDistance = 0.8f;
    [SerializeField] private int labelSortingOrder = 200;

    private CombatManager combatManager;

    private void OnEnable()
    {
        combatManager = GetComponent<CombatManager>();
        if (combatManager != null)
        {
            combatManager.HitJudgedEvent += Play;
        }
    }

    private void OnDisable()
    {
        if (combatManager != null)
        {
            combatManager.HitJudgedEvent -= Play;
        }
    }

    private void Play(RhythmJudgementResult result)
    {
        Color color = GetJudgementColor(result.judgement);

        if (result.keyButton != null)
        {
            result.keyButton.PlayJudgementFeedback(result.judgement, color);
        }

        StartCoroutine(AnimateLabel(result, color));
    }

    private IEnumerator AnimateLabel(RhythmJudgementResult result, Color color)
    {
        GameObject labelObject = new GameObject($"{result.judgement} Feedback");
        labelObject.transform.position = result.worldPosition + Vector3.up * 0.65f;

        TextMesh shadow = CreateText(labelObject.transform, "Shadow", result, Color.black, labelSortingOrder - 1);
        shadow.transform.localPosition = new Vector3(0.035f, -0.035f, 0f);

        TextMesh label = CreateText(labelObject.transform, "Label", result, color, labelSortingOrder);
        Vector3 startPosition = labelObject.transform.position;
        Vector3 startScale = Vector3.one * 0.35f;
        labelObject.transform.localScale = startScale;

        float elapsed = 0f;
        while (elapsed < labelLifetime)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / labelLifetime);
            float pop = Mathf.Sin(Mathf.Min(progress / 0.35f, 1f) * Mathf.PI * 0.5f);
            float fade = 1f - Mathf.InverseLerp(0.55f, 1f, progress);

            labelObject.transform.position = startPosition + Vector3.up * (labelRiseDistance * progress);
            labelObject.transform.localScale = Vector3.LerpUnclamped(startScale, Vector3.one, pop);
            label.color = WithAlpha(color, fade);
            shadow.color = WithAlpha(Color.black, fade * 0.8f);
            yield return null;
        }

        Destroy(labelObject);
    }

    private TextMesh CreateText(
        Transform parent,
        string objectName,
        RhythmJudgementResult result,
        Color color,
        int sortingOrder)
    {
        GameObject textObject = new GameObject(objectName);
        textObject.transform.SetParent(parent, false);

        TextMesh textMesh = textObject.AddComponent<TextMesh>();
        textMesh.text = GetLabel(result);
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.fontSize = 36;
        textMesh.characterSize = 0.075f;
        textMesh.fontStyle = FontStyle.Bold;
        textMesh.color = color;

        MeshRenderer renderer = textObject.GetComponent<MeshRenderer>();
        renderer.sortingOrder = sortingOrder;
        return textMesh;
    }

    private static string GetLabel(RhythmJudgementResult result)
    {
        if (result.judgement == HitJudgement.Miss || result.combo < 2)
        {
            return result.judgement.ToString().ToUpperInvariant();
        }

        return $"{result.judgement.ToString().ToUpperInvariant()}"; //\n{result.combo} COMBO
    }

    public static Color GetJudgementColor(HitJudgement judgement)
    {
        switch (judgement)
        {
            case HitJudgement.Perfect:
                return new Color(1f, 0.84f, 0.2f);
            case HitJudgement.Good:
                return new Color(0.2f, 1f, 0.75f);
            case HitJudgement.Bad:
                return new Color(1f, 0.5f, 0.12f);
            default:
                return new Color(1f, 0.2f, 0.35f);
        }
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
