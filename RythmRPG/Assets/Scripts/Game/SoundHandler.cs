using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Unity.VisualScripting.Member;

public class SoundHandler : MonoBehaviour
{
    // Start is called before the first frame update
    public static SoundHandler Instance;
    public AudioClip encounterSound;
    public AudioSource source;

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
}
