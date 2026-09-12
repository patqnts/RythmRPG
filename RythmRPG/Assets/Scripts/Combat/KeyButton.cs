using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class KeyButton : MonoBehaviour
{
    // Start is called before the first frame update
    [SerializeField] public int keyIdentity;
    public KeyType keyType;
    public bool isPressed;
    public Sprite[] sprites;
    public SpriteRenderer spriteRenderer;
    private bool interactable;
    private Vector3 restingScale;
    private Color restingColor = Color.white;
    private Coroutine feedbackRoutine;
    private SpriteRenderer activeRing;


    private void Start()
    {
        interactable = true;
        restingScale = transform.localScale;
        if (spriteRenderer != null)
        {
            restingColor = spriteRenderer.color;
        }
    }
    private void Update()
    {
        UpdateInput();
        UpdateVisual();
    }

    private void UpdateInput()
    {
        if (CombatManager.instance == null)
        {
            return;
        }

        KeyCode keyCode = CombatManager.instance.GetKeyCodeFromNoteIdentity(keyIdentity);
        if (keyCode == KeyCode.None)
        {
            isPressed = false;
            return;
        }

        isPressed = Input.GetKey(keyCode);

        if (Input.GetKeyDown(keyCode))
        {
            CombatManager.instance.HandleKeyPressed(this);
        }

        if (Input.GetKeyUp(keyCode))
        {
            CombatManager.instance.HandleKeyReleased(this);
        }
    }

    private void UpdateVisual()
    {
        // Set the sprite based on the isPressed value    
        if (isPressed || !interactable)
        {
            spriteRenderer.sprite = sprites[0]; // Assuming 1 is the index for the pressed state in your sprites array
        }
        else
        {
            spriteRenderer.sprite = sprites[1]; // Assuming 0 is the index for the not pressed state in your sprites array
        }
    }
    
    public void SetInteractable(bool interactable)
    {
        this.interactable = interactable;
    }

    public bool GetInteractable() { return interactable; }

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

        if (activeRing != null)
        {
            Destroy(activeRing.gameObject);
            activeRing = null;
        }
    }
}
