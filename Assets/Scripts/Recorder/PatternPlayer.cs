using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PatternPlayer : MonoBehaviour
{
    public GameObject noteObjectPrefab;
    public PatternRecorder patternRecorder;
    public Button PlayButton;
    public Button StopButton;
    public string patternName;
    public Dropdown patternDropdown;

    private Coroutine playPatternCoroutine;

    void Start()
    {
        PlayButton.onClick.AddListener(OnPlayButtonClicked);
        StopButton.onClick.AddListener(OnStopButtonClicked);
        PopulateDropdown();
    }

    void PopulateDropdown()
    {
        List<string> patternNames = patternRecorder.GetPatternNames();
        patternDropdown.ClearOptions();
        patternDropdown.AddOptions(patternNames);
        patternDropdown.onValueChanged.AddListener(delegate { OnDropdownValueChanged(); });
        OnDropdownValueChanged();
    }
    void OnDropdownValueChanged()
    {
        patternName = patternDropdown.options[patternDropdown.value].text;
    }

    void OnPlayButtonClicked()
    {
        OnStopButtonClicked();
        StopAllCoroutines();
        if (playPatternCoroutine == null)
        {
            playPatternCoroutine = StartCoroutine(PlayPattern());
        }
    }

    void OnStopButtonClicked()
    {
        if (playPatternCoroutine != null)
        {
            StopCoroutine(playPatternCoroutine);
            playPatternCoroutine = null;
        }
    }

    IEnumerator PlayPattern()
    {
        OnDropdownValueChanged();
        patternRecorder.LoadPattern(patternName);
        List<NoteData> pattern = patternRecorder.recordedPattern;

        float previousTime = 0f;

        foreach (NoteData noteData in pattern)
        {
            float waitTime = noteData.timeStamp - previousTime;
            yield return new WaitForSeconds(waitTime);
            InstantiateNote(noteData);
            previousTime = noteData.timeStamp;
        }
    }


    void InstantiateNote(NoteData noteData)
    {
        GameObject newNote = Instantiate(noteObjectPrefab, transform.position, Quaternion.identity);
        Note noteScript = newNote.GetComponent<Note>();

        if (noteScript != null)
        {
            noteScript.SetNoteIdentity(noteData.noteIdentity);
            noteScript.speed = noteData.speed;
            noteScript.state = noteData.state;
            noteScript.hitEffect = noteData.hitEffect;
        }

        newNote.SetActive(true);
    }
}

