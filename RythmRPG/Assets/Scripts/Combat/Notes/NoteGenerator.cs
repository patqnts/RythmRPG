using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NoteGenerator : MonoBehaviour
{
  
    public GameObject noteObjectPrefab; // Drag your NoteObject prefab to this field in the Inspector
    public float generationSpeed = 1f; // Adjust the speed as needed
    public KeyCode[] keyCodesAsign;
    private bool isAttacking = false;

    void Start()
    {
        keyCodesAsign = FindObjectOfType<CombatManager>().keyCodes;      
    }

    private void OnEnable()
    {
        CombatManager.instance.AttackEvent += Attack;
        CombatManager.instance.StopAttackEvent += StopAttack;
    }
    private void OnDisable()
    {
        CombatManager.instance.AttackEvent -= Attack;
        CombatManager.instance.StopAttackEvent -= StopAttack;
    }
    public void Attack()
    {
        StartCoroutine(AttackCycle());
    }

    public void StopAttack()
    {
        StopAllCoroutines();
        isAttacking = false;
    }

    IEnumerator AttackCycle()
    {
        isAttacking = true;

        while (isAttacking)
        {
            yield return StartCoroutine(GenerateWaveNotesForDuration(5f));
            yield return new WaitForSeconds(2f);
            //yield return StartCoroutine(GenerateRandomNotesForDuration(10f));
            //yield return new WaitForSeconds(4f);
        }
    }

    IEnumerator GenerateWaveNotesForDuration(float duration)
    {
        Debug.Log("Wave");
        float startTime = Time.time;
        int[] wavePattern = { 1, 2, 3, 4, 5, 4, 3, 2, 1, 2, 3, 4, 5 };
        int index = 0;

        while (Time.time - startTime < duration)
        {
            yield return new WaitForSeconds(.1f);

            // Instantiate NoteObject prefab with the current noteIdentity from the wave pattern
            GameObject newNote = Instantiate(noteObjectPrefab, transform.position, Quaternion.identity);
            NoteObject noteScript = newNote.GetComponent<NoteObject>();

            if (noteScript != null)
            {
                noteScript.speed = 8f;
                int noteIdentity = wavePattern[index];
                Debug.Log("Wave pattern noteIdentity: " + noteIdentity);
                noteScript.SetNoteIdentity(noteIdentity);
                Debug.Log("Assigned wave noteIdentity: " + noteScript.GetNoteIdentity());
                //noteScript.keyCode = GetKeyCodeFromNoteIdentity(noteScript.noteIdentity);
            }

            newNote.SetActive(true);
            index = (index + 1) % wavePattern.Length;
        }
    }

    IEnumerator GenerateNormalNotesForDuration(float duration)
    {
        Debug.Log("Normal");
        float startTime = Time.time;

        while (Time.time - startTime < duration)
        {
            yield return new WaitForSeconds(generationSpeed);

            // Instantiate NoteObject prefab with a random noteIdentity between 1 and 5
            GameObject newNote = Instantiate(noteObjectPrefab, transform.position, Quaternion.identity);
            NoteObject noteScript = newNote.GetComponent<NoteObject>();

            if (noteScript != null)
            {
                int randomRange = Random.Range(1, 6);
                Debug.Log("Random range: " + randomRange);
                noteScript.SetNoteIdentity(randomRange);
                Debug.Log("Assigned noteIdentity: " + noteScript.GetNoteIdentity());
                //noteScript.keyCode = GetKeyCodeFromNoteIdentity(noteScript.noteIdentity);
            }

            noteScript.gameObject.SetActive(true);
        }
    }

    IEnumerator GenerateRandomNotesForDuration(float duration)
    {
        Debug.Log("Random");
        float startTime = Time.time;

        while (Time.time - startTime < duration)
        {
            yield return new WaitForSeconds(generationSpeed);

            // Instantiate NoteObject prefab with a random noteIdentity between 1 and 5
            GameObject newNote = Instantiate(noteObjectPrefab, transform.position, Quaternion.identity);
            NoteObject noteScript = newNote.GetComponent<NoteObject>();

            if (noteScript != null)
            {
                //noteScript.noteIdentity = Random.Range(1, 6);
                //noteScript.isSpecial = Random.Range(0, 2) == 0;//
                noteScript.moveset = Moveset.Arrow;
                //noteScript.keyCode = GetKeyCodeFromNoteIdentity(noteScript.noteIdentity);
            }

            noteScript.gameObject.SetActive(true);
        }
    }

    //public KeyCode GetKeyCodeFromNoteIdentity(int identity)
    //{
    //    // Assuming noteIdentity is in the range of 1 to 5
    //    switch (identity)
    //    {
    //        case 1:
    //            return keyCodesAsign[0];
    //        case 2:
    //            return keyCodesAsign[1];
    //        case 3:
    //            return keyCodesAsign[2];
    //        case 4:
    //            return keyCodesAsign[3];
    //        case 5:
    //            return keyCodesAsign[4];
    //        default:
    //            return KeyCode.None;
    //    }
    //}
}
