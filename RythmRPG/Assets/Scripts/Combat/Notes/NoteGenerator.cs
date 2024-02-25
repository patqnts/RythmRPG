using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NoteGenerator : MonoBehaviour
{
  
    public GameObject noteObjectPrefab; // Drag your NoteObject prefab to this field in the Inspector
    public float generationSpeed = 1f; // Adjust the speed as needed
    public KeyCode[] keyCodesAsign;
    public bool normalAttackState;
    void Start()
    {
        StartCoroutine(GenerateNotes(500));
    }

    IEnumerator GenerateNotes(int power)
    {
        while (true)
        {
            yield return new WaitForSeconds(generationSpeed);

            // Instantiate NoteObject prefab with a random noteIdentity between 1 and 5
            GameObject newNote = Instantiate(noteObjectPrefab, transform.position, Quaternion.identity);
            NoteObject noteScript = newNote.GetComponent<NoteObject>();

            if (noteScript != null)
            {
                noteScript.noteIdentity = Random.Range(1, 6);
                noteScript.isSpecial = Random.Range(0, 2) == 0;
                noteScript.keyCode = GetKeyCodeFromNoteIdentity(noteScript.noteIdentity);
                noteScript.speed = power;
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
