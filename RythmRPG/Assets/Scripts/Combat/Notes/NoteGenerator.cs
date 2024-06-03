using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NoteGenerator : MonoBehaviour
{
  
    public GameObject noteObjectPrefab; // Drag your NoteObject prefab to this field in the Inspector
    public GameObject lasePrefab; 
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

    //IEnumerator AttackCycle()
    //{
    //    isAttacking = true;

    //    while (isAttacking)
    //    {

    //        yield return StartCoroutine(GenerateNormalNotesForDuration(10f));
    //        yield return new WaitForSeconds(.25f);
    //        yield return StartCoroutine(GenerateRandomLaser(10f));
    //        yield return new WaitForSeconds(.25f);
    //        yield return StartCoroutine(GenerateSimultaneousNotes(10f, 4, true));
    //        yield return new WaitForSeconds(.25f);
    //        yield return StartCoroutine(GenerateWaveNotesForDuration(10f));
    //        yield return new WaitForSeconds(.25f);
    //    }
    //}

    IEnumerator AttackCycle()
    {
        isAttacking = true;

        while (isAttacking)
        {
            List<IEnumerator> attackPatterns = new List<IEnumerator>
        {
            GenerateNormalNotesForDuration(10f),
            GenerateRandomLaser(10f),
            GenerateSimultaneousNotes(10f, 4, true),
            GenerateWaveNotesForDuration(10f)
        };

            // Shuffle the list
            for (int i = 0; i < attackPatterns.Count; i++)
            {
                int randomIndex = Random.Range(0, attackPatterns.Count);
                IEnumerator temp = attackPatterns[i];
                attackPatterns[i] = attackPatterns[randomIndex];
                attackPatterns[randomIndex] = temp;
            }

            // Execute the coroutines in shuffled order
            foreach (var attackPattern in attackPatterns)
            {
                yield return StartCoroutine(attackPattern);
                yield return new WaitForSeconds(0.25f);
            }
        }
    }


    IEnumerator GenerateRandomLaser(float duration)
    {
        float startTime = Time.time;
        int previousNoteIdentity = -1;

        while (Time.time - startTime < duration)
        {
            yield return new WaitForSeconds(generationSpeed);

            // Instantiate NoteObject prefab with a random noteIdentity between 1 and 5
            GameObject newNote = Instantiate(lasePrefab, transform.position, Quaternion.identity);
            LaserNote noteScript = newNote.GetComponent<LaserNote>();

            if (noteScript != null)
            {
                int newNoteIdentity;
                do
                {
                    newNoteIdentity = Random.Range(1, 6);
                } while (newNoteIdentity == previousNoteIdentity);

                noteScript.SetNoteIdentity(newNoteIdentity);
                previousNoteIdentity = newNoteIdentity;
            }

            noteScript.gameObject.SetActive(true);
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
                noteScript.SetNoteIdentity(noteIdentity);
                noteScript.hitEffect = HitEffect.Default;
            }

            newNote.SetActive(true);
            index = (index + 1) % wavePattern.Length;
        }
    }

    IEnumerator GenerateNormalNotesForDuration(float duration)
    {
        float startTime = Time.time;

        while (Time.time - startTime < duration)
        {
            yield return new WaitForSeconds(.15f);

            // Instantiate NoteObject prefab with a random noteIdentity between 1 and 5
            GameObject newNote = Instantiate(noteObjectPrefab, transform.position, Quaternion.identity);
            NoteObject noteScript = newNote.GetComponent<NoteObject>();

            if (noteScript != null)
            {
                int randomRange = Random.Range(1, 6);
                noteScript.SetNoteIdentity(randomRange);
                noteScript.speed = 10;
                noteScript.state = PlayerState.Default;
                noteScript.hitEffect = HitEffect.Default;
            }

            noteScript.gameObject.SetActive(true);
        }
    }

    IEnumerator GenerateSimultaneousNotes(float duration, int fixedProjectileCount, bool randomAmount)
    {
        float startTime = Time.time;

        while (Time.time - startTime < duration)
        {
            yield return new WaitForSeconds(generationSpeed);

            // Determine the number of projectiles to generate
            int projectileCount = randomAmount ? Random.Range(1, fixedProjectileCount + 1) : fixedProjectileCount;

            // Create a list to store used identities
            List<int> usedIdentities = new List<int>();

            for (int i = 0; i < projectileCount; i++)
            {
                int noteIdentity;
                // Generate a unique random noteIdentity
                do
                {
                    noteIdentity = Random.Range(1, 6);
                } while (usedIdentities.Contains(noteIdentity));

                // Add the generated noteIdentity to the list of used identities
                usedIdentities.Add(noteIdentity);

                // Instantiate NoteObject prefab at the unique random position
                GameObject newNote = Instantiate(noteObjectPrefab, transform.position, Quaternion.identity);
                NoteObject noteScript = newNote.GetComponent<NoteObject>();

                if (noteScript != null)
                {
                    noteScript.SetNoteIdentity(noteIdentity);
                    noteScript.speed = 10;
                    noteScript.hitEffect = HitEffect.Default;
                }

                noteScript.gameObject.SetActive(true);
            }
        }
    }

}
