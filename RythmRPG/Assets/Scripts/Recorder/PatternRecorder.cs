using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

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
    public string saveFilePath = "Assets/Scripts/Recorder/pattern.json";
    public bool isRecording;

    void Start()
    {
        recordingStartTime = Time.time;
    }

    void Update()
    {
        if(Input.GetKeyDown(KeyCode.R))
        {
            isRecording = !isRecording;
        }
        else if(Input.GetKeyDown(KeyCode.T)) 
        {
            SavePattern();
            isRecording = !isRecording; 
        }

        if(isRecording)
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
            speed = 10,
            state = PlayerState.Default,
            hitEffect = HitEffect.Default
        };
        recordedPattern.Add(noteData);
    }

    public void SavePattern()
    {
        string json = JsonUtility.ToJson(new Serialization<NoteData>(recordedPattern), true);
        File.WriteAllText(saveFilePath, json);
    }

    public void LoadPattern()
    {
        if (File.Exists(saveFilePath))
        {
            string json = File.ReadAllText(saveFilePath);
            recordedPattern = JsonUtility.FromJson<Serialization<NoteData>>(json).ToList();
        }
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
