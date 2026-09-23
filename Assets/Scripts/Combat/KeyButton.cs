using System.Collections;
using System.Collections.Generic;
using RythmRPG.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class KeyButton : MonoBehaviour
{
    // Start is called before the first frame update
    [SerializeField] public int keyIdentity;
    public KeyType keyType;
    public bool isPressed;
    public Sprite[] sprites;
    public SpriteRenderer spriteRenderer;
    [SerializeField] private UnityEngine.UI.Image uiImage;
    [SerializeField] private TMP_Text uiLabel;
    [SerializeField] private Sprite uiUnpressedSprite;
    [SerializeField] private Sprite uiPressedSprite;
    private bool interactable;
    private Vector3 restingScale;
    private Color restingColor = Color.white;
    private Coroutine feedbackRoutine;
    private SpriteRenderer activeRing;
    private Color uiRestingColor = Color.white;
    private Color uiLabelRestingColor = Color.white;


    private void Start()
    {
        interactable = true;
        restingScale = transform.localScale;
        if (spriteRenderer != null)
        {
            restingColor = spriteRenderer.color;
        }
        if (uiImage != null) uiRestingColor = uiImage.color;
        if (uiLabel != null) uiLabelRestingColor = uiLabel.color;
    }
    private void Update()
    {
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (spriteRenderer != null && sprites != null && sprites.Length > 0)
        {
            int desiredIndex = isPressed || !interactable ? 0 : 1;
            spriteRenderer.sprite = sprites[Mathf.Min(desiredIndex, sprites.Length - 1)];
        }
        if (uiImage != null)
        {
            if (uiPressedSprite != null || uiUnpressedSprite != null)
            {
                uiImage.sprite = isPressed && uiPressedSprite != null ? uiPressedSprite : uiUnpressedSprite;
            }

            float brightness = isPressed ? 1.65f : interactable ? 1f : 0.55f;
            uiImage.color = new Color(
                Mathf.Clamp01(uiRestingColor.r * brightness),
                Mathf.Clamp01(uiRestingColor.g * brightness),
                Mathf.Clamp01(uiRestingColor.b * brightness), uiRestingColor.a);
        }
    }

    public void ConfigureUI(int laneId, UnityEngine.UI.Image image, TMP_Text label,
        Sprite unpressedSprite = null, Sprite pressedSprite = null)
    {
        keyIdentity = laneId;
        uiImage = image;
        uiLabel = label;
        uiUnpressedSprite = unpressedSprite != null ? unpressedSprite : image != null ? image.sprite : null;
        uiPressedSprite = pressedSprite;
        spriteRenderer = null;
        interactable = true;
        restingScale = transform.localScale;
        if (uiImage != null) uiRestingColor = uiImage.color;
        if (uiLabel != null) uiLabelRestingColor = uiLabel.color;
    }
    
    /// <summary>Shows the lane's current key on the button (called again after the player rebinds it).</summary>
    public void SetKeyLabel(string text)
    {
        if (uiLabel != null && !string.IsNullOrEmpty(text)) uiLabel.text = text;
    }

    public void SetInteractable(bool interactable)
    {
        this.interactable = interactable;
    }

    public bool GetInteractable() { return interactable; }

    public void SetPressed(bool pressed)
    {
        isPressed = pressed;
    }

    public void PlayJudgementFeedback(HitJudgement judgement, Color color)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (feedbackRoutine != null)
        {
            StopCoroutine(feedbackRoutine);
        }

        if (activeRing != null)
        {
            Destroy(activeRing.gameObject);
        }

        feedbackRoutine = StartCoroutine(AnimateFeedback(judgement, color));
    }

    private IEnumerator AnimateFeedback(HitJudgement judgement, Color color)
    {
        float intensity = judgement == HitJudgement.Perfect ? 1.25f :
            judgement == HitJudgement.Good ? 1.16f : 1.1f;
        float duration = judgement == HitJudgement.Miss ? 0.22f : 0.16f;
        activeRing = CreateFeedbackRing(color);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float pulse = Mathf.Sin(progress * Mathf.PI);
            transform.localScale = restingScale * Mathf.Lerp(1f, intensity, pulse);

            if (spriteRenderer != null)
            {
                spriteRenderer.color = Color.Lerp(restingColor, color, pulse * 0.85f);
            }
            if (uiImage != null) uiImage.color = Color.Lerp(uiRestingColor, color, pulse * 0.85f);
            if (uiLabel != null) uiLabel.color = Color.Lerp(uiLabelRestingColor, color, pulse * 0.55f);

            if (activeRing != null)
            {
                activeRing.transform.localScale = Vector3.one * Mathf.Lerp(1f, 1.75f, progress);
                Color ringColor = color;
                ringColor.a = 1f - progress;
                activeRing.color = ringColor;
            }

            yield return null;
        }

        transform.localScale = restingScale;
        if (spriteRenderer != null)
        {
            spriteRenderer.color = restingColor;
        }
        if (uiImage != null) uiImage.color = uiRestingColor;
        if (uiLabel != null) uiLabel.color = uiLabelRestingColor;

        if (activeRing != null)
        {
            Destroy(activeRing.gameObject);
            activeRing = null;
        }

        feedbackRoutine = null;
    }

    private SpriteRenderer CreateFeedbackRing(Color color)
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null)
        {
            return null;
        }

        GameObject ringObject = new GameObject("Hit Ripple");
        ringObject.transform.SetParent(transform, false);
        SpriteRenderer ring = ringObject.AddComponent<SpriteRenderer>();
        ring.sprite = spriteRenderer.sprite;
        ring.color = color;
        ring.sortingLayerID = spriteRenderer.sortingLayerID;
        ring.sortingOrder = spriteRenderer.sortingOrder + 1;
        return ring;
    }

    private void OnDisable()
    {
        if (restingScale != Vector3.zero)
        {
            transform.localScale = restingScale;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.color = restingColor;
        }
        if (uiImage != null) uiImage.color = uiRestingColor;
        if (uiLabel != null) uiLabel.color = uiLabelRestingColor;

        if (activeRing != null)
        {
            Destroy(activeRing.gameObject);
            activeRing = null;
        }
    }
}
