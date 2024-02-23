using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NoteGenerator : MonoBehaviour
{
    public GameObject Opponent;
    public GameObject noteObjectPrefab; // Drag your NoteObject prefab to this field in the Inspector
    public float generationSpeed = 1f; // Adjust the speed as needed
    public Transform notesParent;
    public KeyCode[] keyCodesAsign;
    void Start()
    {
        // Start generating notes at the specified speed
        StartCoroutine(GenerateNotes(500));
    }

    IEnumerator GenerateNotes(int power)
    {
        while (true)
        {
            yield return new WaitForSeconds(generationSpeed);

            // Instantiate NoteObject prefab with a random noteIdentity between 1 and 5
            GameObject newNote = Instantiate(noteObjectPrefab, notesParent);
            NoteObject noteScript = newNote.GetComponent<NoteObject>();

            if (noteScript != null)
            {
                // Set a random noteIdentity between 1 and 5
                noteScript.noteIdentity = Random.Range(1, 6);
                noteScript.keyCode = GetKeyCodeFromNoteIdentity(noteScript.noteIdentity);
                noteScript.speed = power;
                Opponent.transform.position = new Vector2(newNote.gameObject.transform.position.x, Opponent.transform.position.y);
            }

            noteScript.gameObject.SetActive(true);
        }
    }

    public KeyCode GetKeyCodeFromNoteIdentity(int identity)
    {
        // Assuming noteIdentity is in the range of 1 to 5
        switch (identity)
        {
            case 1:
                return keyCodesAsign[0];
            case 2:
                return keyCodesAsign[1];
            case 3:
                return keyCodesAsign[2];
            case 4:
                return keyCodesAsign[3];
            case 5:
                return keyCodesAsign[4];
            default:
                return KeyCode.None;
        }
    }
}
