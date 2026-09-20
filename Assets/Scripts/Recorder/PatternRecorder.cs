using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class NoteData
{
    public int noteIdentity;
    public float timeStamp;
    public float speed;
    public PlayerState state;
    public HitEffect hitEffect;
}

public class PatternRecorder : MonoBehaviour
{
    public KeyCode[] keyCodes;
    public List<NoteData> recordedPattern = new List<NoteData>();
    private float recordingStartTime;
    public string saveDirectoryPath = "Assets/Scripts/Recorder/Patterns";
    public bool isRecording;
    public GameObject recordingIcon;
    public float speed;

    // UI elements
    public GameObject savePatternPanel;
    public InputField patternNameInputField;
    public InputField speedInput;
    public Button savePatternButton;
    public Text promptText;
    public PatternManagerUI patternManagerUI;
    public PatternPlayer patternPlayer;

    void Start()
    {
        if (!Directory.Exists(saveDirectoryPath))
        {
            Directory.CreateDirectory(saveDirectoryPath);
        }

        // Setup UI
        savePatternPanel.SetActive(false);
        savePatternButton.onClick.AddListener(OnSavePatternButtonClicked);
    }

    void Update()
    {
        recordingIcon.SetActive(isRecording);

        if (Input.GetKeyDown(KeyCode.F1))
        {
            isRecording = !isRecording;
            if (isRecording)
            {
                if (!float.TryParse(speedInput.text, out speed))
                {
                    speed = 8f; // Default speed if parsing fails
                }
                recordedPattern.Clear();
                recordingStartTime = Time.time;
            }
        }
        else if (Input.GetKeyDown(KeyCode.Return))
        {
            if (isRecording)
            {
                isRecording = false;
                ShowSavePatternPanel();
            }
        }

        if (isRecording)
        {
            for (int i = 0; i < keyCodes.Length; i++)
            {
                if (Input.GetKeyDown(keyCodes[i]))
                {
                    RecordKeyPress(i + 1);
                }
            }
        }
    }

    void RecordKeyPress(int noteIdentity)
    {
        NoteData noteData = new NoteData
        {
            noteIdentity = noteIdentity,
            timeStamp = Time.time - recordingStartTime,
            speed = this.speed,
            state = PlayerState.Default,
            hitEffect = HitEffect.Default
        };
        recordedPattern.Add(noteData);
    }

    void ShowSavePatternPanel()
    {
        savePatternPanel.SetActive(true);
        patternNameInputField.text = "";
        promptText.text = "Enter Pattern Name:";
    }

    void OnSavePatternButtonClicked()
    {
        string patternName = patternNameInputField.text;
        if (!string.IsNullOrEmpty(patternName))
        {
            SavePattern(patternName);

            savePatternPanel.SetActive(false);
            recordedPattern.Clear();
        }
        else
        {
            promptText.text = "Pattern name cannot be empty.";
        }
    }

    public void SavePattern(string patternName)
    {
        string saveFilePath = Path.Combine(saveDirectoryPath, patternName + ".json");
        string json = JsonUtility.ToJson(new Serialization<NoteData>(recordedPattern), true);
        File.WriteAllText(saveFilePath, json);

        patternManagerUI.UpdatePatternDropdown();
    }

    public List<string> LoadPatterns()
    {
        List<string> patternNames = new List<string>();
        DirectoryInfo dir = new DirectoryInfo(saveDirectoryPath);
        FileInfo[] files = dir.GetFiles("*.json");
        foreach (FileInfo file in files)
        {
            patternNames.Add(Path.GetFileNameWithoutExtension(file.Name));
        }
        return patternNames;
    }

    public void LoadPattern(string patternName)
    {
        string loadFilePath = Path.Combine(saveDirectoryPath, patternName + ".json");
        if (File.Exists(loadFilePath))
        {
            string json = File.ReadAllText(loadFilePath);
            recordedPattern = JsonUtility.FromJson<Serialization<NoteData>>(json).ToList();
        }
    }

    public void DeletePattern(string patternName)
    {
        string deleteFilePath = Path.Combine(saveDirectoryPath, patternName + ".json");
        if (File.Exists(deleteFilePath))
        {
            File.Delete(deleteFilePath);
        }
    }

    public void RenamePattern(string oldPatternName, string newPatternName)
    {
        string oldFilePath = Path.Combine(saveDirectoryPath, oldPatternName + ".json");
        string newFilePath = Path.Combine(saveDirectoryPath, newPatternName + ".json");
        if (File.Exists(oldFilePath))
        {
            File.Move(oldFilePath, newFilePath);
        }
    }

    public List<string> GetPatternNames()
    {
        List<string> patternNames = new List<string>();
        DirectoryInfo dir = new DirectoryInfo(saveDirectoryPath);
        FileInfo[] files = dir.GetFiles("*.json");
        foreach (FileInfo file in files)
        {
            patternNames.Add(Path.GetFileNameWithoutExtension(file.Name));
        }
        return patternNames;
    }
}

[System.Serializable]
public class Serialization<T>
{
    public List<T> target;
    public Serialization(List<T> target)
    {
        this.target = target;
    }
    public List<T> ToList()
    {
        return target;
    }
}
