using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class HitStop : MonoBehaviour
{
    bool waiting = false;
    public Volume volume; // Assign this in the Inspector
    private ChromaticAberration chromaticAberration;

    void Start()
    {
        // Retrieve the ChromaticAberration component from the Volume profile
        if (volume.profile.TryGet(out chromaticAberration))
        {
            // Set initial intensity if needed
            chromaticAberration.intensity.value = 0.2f;
        }
        else
        {
            Debug.LogError("Chromatic Aberration not found in the Volume profile.");
        }
    }

    public void Stop(float duration, float timeScale, GameObject collider)
    {
        if (waiting)
            return;

        Time.timeScale = timeScale;
        StartCoroutine(Wait(duration,collider));
    }

    public void Stop(GameObject collider,float duration)
    {
        Stop(duration, 0.0f, collider);
    }

    IEnumerator Wait(float duration, GameObject collider)
    {
        waiting = true;
        collider.GetComponent<Collider2D>().enabled = false;
        if (chromaticAberration != null)
        {
            chromaticAberration.intensity.value = 1f;
        }
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1.0f;
        waiting = false;
        collider.GetComponent<Collider2D>().enabled = true;
        if (chromaticAberration != null)
        {
            chromaticAberration.intensity.value = 0.2f;
        }
    }
}
