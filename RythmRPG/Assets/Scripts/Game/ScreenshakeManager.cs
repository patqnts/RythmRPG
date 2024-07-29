using UnityEngine;

public class ScreenshakeManager : MonoBehaviour
{
    public Transform cameraTransform;
    private Vector3 originalPosition;

    private float shakeDuration = 0f;
    private float shakeMagnitude = 0.7f;
    private float dampingSpeed = 1.0f;

    private void Start()
    {
        originalPosition = cameraTransform.localPosition;
    }

    private void Update()
    {
        if (shakeDuration > 0)
        {
            cameraTransform.localPosition = originalPosition + Random.insideUnitSphere * shakeMagnitude;

            shakeDuration -= Time.deltaTime * dampingSpeed;
        }
        else
        {
            shakeDuration = 0f;
            cameraTransform.localPosition = originalPosition;
        }
    }

    public void ShakeLight()
    {
        shakeDuration = 0.06f;
        shakeMagnitude = 0.06f;
    }

    public void ShakeMedium()
    {
        shakeDuration = 0.3f;
        shakeMagnitude = 0.5f;
    }

    public void ShakeHeavy()
    {
        shakeDuration = 0.5f;
        shakeMagnitude = 1.0f;
    }

    public void ShakeCustom(float duration, float magnitude)
    {
        shakeDuration = duration;
        shakeMagnitude = magnitude;
    }
}
