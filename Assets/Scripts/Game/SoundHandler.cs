using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Unity.VisualScripting.Member;

public class SoundHandler : MonoBehaviour
{
    // Start is called before the first frame update
    public static SoundHandler Instance;
    public AudioClip encounterSound;
    public AudioClip slideSound;
    public AudioClip clickSound;
    public AudioClip combatSound;



    public AudioSource source;
    public AudioSource clickSource;
    public AudioSource combatSource;

    private void Start()
    {
        Instance = this;
    }
    public void PlayEncounterSound()
    {
        if (encounterSound != null)
        {
            source.clip = encounterSound;
            source.Play();
        }
    }

    public void PlayCombatSound()
    {
        if (combatSound != null)
        {
            combatSource.clip = combatSound;
            combatSource.Play();
        }
    }

    public void StopSound()
    {
        combatSource.Stop();
    }

    public void PlaySlideSound()
    {
        if (slideSound != null)
        {
            source.clip = slideSound;
            source.Play();
        }
    }

    public void PlayClick()
    {
        if (clickSound != null)
        {
            clickSource.clip = clickSound;
            clickSource.Play();
        }
    }
}
