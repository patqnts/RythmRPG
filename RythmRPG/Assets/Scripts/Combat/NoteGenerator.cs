using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NoteGenerator : MonoBehaviour
{
    public GameObject noteObjectPrefab; // Drag your NoteObject prefab to this field in the Inspector
    public float generationSpeed = 1f; // Adjust the speed as needed
    public Transform notesParent;
    void Start()
    {
        // Start generating notes at the specified speed
        StartCoroutine(GenerateNotes());
    }

    IEnumerator GenerateNotes()
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
                noteScript.keyCode = noteScript.GetKeyCodeFromNoteIdentity(noteScript.noteIdentity);
              
            }

            noteScript.gameObject.SetActive(true);
        }
    }
}
