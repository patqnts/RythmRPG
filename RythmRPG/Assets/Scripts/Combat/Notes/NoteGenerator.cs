using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NoteGenerator : MonoBehaviour
{ 
    public GameObject noteObjectPrefab; // Drag your NoteObject prefab to this field in the Inspector
    public GameObject lasePrefab; 
    public GameObject holdLaserPrefab; 
    public GameObject longNoteObjectPrefab; 
    public GameObject pongNoteObjectPrefab;
    public float attackDuration;
    public float generationSpeed = 1f; // Adjust the speed as needed
    public KeyCode[] keyCodesAsign;

    private bool isAttacking = false;
    public bool enableNormalNotes = true;
    public bool enableRandomLaser = true;
    public bool enableSimultaneousNotes = true;
    public bool enableWaveNotes = true;
    public bool enableHoldNotes = true;
    public bool enablePongNote = true;
    public bool enableHoldLaserNote = true;

    public event Action AttackAnimate;
    public event Action IdleAnimate;

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
            List<IEnumerator> attackPatterns = new List<IEnumerator>();

            if (enableNormalNotes)
                attackPatterns.Add(GenerateNormalNotesForDuration(attackDuration));
            if (enableRandomLaser)
                attackPatterns.Add(GenerateRandomLaser(attackDuration));
            if (enableSimultaneousNotes)
                attackPatterns.Add(GenerateSimultaneousNotes(attackDuration, 4, true));
            if (enableWaveNotes)
                attackPatterns.Add(GenerateWaveNotesForDuration(attackDuration));
            if (enableHoldNotes)
                attackPatterns.Add(GenerateHoldNotesForDuration(attackDuration));
            if (enablePongNote)
                attackPatterns.Add(GeneratePongNote());
            if (enableHoldLaserNote)
                attackPatterns.Add(GenerateRandomHoldLaser(attackDuration));

            // Shuffle the list
            for (int i = 0; i < attackPatterns.Count; i++)
            {
                int randomIndex = UnityEngine.Random.Range(0, attackPatterns.Count);
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

    IEnumerator GeneratePongNote()
    {
        yield return new WaitForSeconds(1f);
        GameObject pong = Instantiate(pongNoteObjectPrefab, transform.position, Quaternion.identity);
        PongNote noteScript = pong.GetComponent<PongNote>();
        int randomRange = UnityEngine.Random.Range(1, 6);
        noteScript.SetNoteIdentity(randomRange);
        noteScript.SetSpeed(3);

        while(pong != null)
        {
            yield return null;
        }

        yield return new WaitForSeconds(0f);
    }
    IEnumerator GenerateRandomLaser(float duration)
    {
        float startTime = Time.time;
        int previousNoteIdentity = -1;

        while (Time.time - startTime < duration)
        {
            yield return new WaitForSeconds(generationSpeed);

            // Instantiate NoteObject prefab with a random noteIdentity between 1 and 5
            AttackAnimate?.Invoke();
            GameObject newNote = Instantiate(lasePrefab, transform.position, Quaternion.identity);
            LaserNote noteScript = newNote.GetComponent<LaserNote>();

            if (noteScript != null)
            {
                int newNoteIdentity;
                do
                {
                    newNoteIdentity = UnityEngine.Random.Range(1, 6);
                } while (newNoteIdentity == previousNoteIdentity);

                noteScript.SetNoteIdentity(newNoteIdentity);
                previousNoteIdentity = newNoteIdentity;
            }

            noteScript.gameObject.SetActive(true);
        }
    }

    IEnumerator GenerateRandomHoldLaser(float duration)
    {
        float startTime = Time.time;
        int previousNoteIdentity = -1;
        while (Time.time - startTime < duration)
        {
            yield return new WaitForSeconds(generationSpeed);

            // Instantiate NoteObject prefab with a random noteIdentity between 1 and 5
            AttackAnimate?.Invoke();
            GameObject newNote = Instantiate(holdLaserPrefab, transform.position, Quaternion.identity);
            HoldLaserNote noteScript = newNote.GetComponent<HoldLaserNote>();

            if (noteScript != null)
            {
                int newNoteIdentity;
                do
                {
                    newNoteIdentity = UnityEngine.Random.Range(1, 6);
                } while (newNoteIdentity == previousNoteIdentity);

                noteScript.SetNoteIdentity(newNoteIdentity);
                previousNoteIdentity = newNoteIdentity;

                noteScript.SetSpeed(4);
                noteScript.state = PlayerState.Default;
                noteScript.hitEffect = HitEffect.Default;
                noteScript.length = UnityEngine.Random.Range(5, 6);
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
            AttackAnimate?.Invoke();
            GameObject newNote = Instantiate(noteObjectPrefab, transform.position, Quaternion.identity);
            Note noteScript = newNote.GetComponent<Note>();

            if (noteScript != null)
            {
                noteScript.SetSpeed(8f);
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
            AttackAnimate?.Invoke();
            GameObject newNote = Instantiate(noteObjectPrefab, transform.position, Quaternion.identity);
            Note noteScript = newNote.GetComponent<Note>();

            if (noteScript != null)
            {
                int randomRange = UnityEngine.Random.Range(1, 6);
                noteScript.SetNoteIdentity(randomRange);
                noteScript.speed = 10;
                noteScript.state = PlayerState.Default;
                noteScript.hitEffect = HitEffect.Default;
            }

            noteScript.gameObject.SetActive(true);
        }
    }

    IEnumerator GenerateHoldNotesForDuration(float duration)
    {
        float startTime = Time.time;

        while (Time.time - startTime < duration)
        {
            yield return new WaitForSeconds(.5f);

            // Instantiate NoteObject prefab with a random noteIdentity between 1 and 5
            AttackAnimate?.Invoke();
            GameObject newNote = Instantiate(longNoteObjectPrefab, transform.position, Quaternion.identity);
            HoldNoteObject noteScript = newNote.GetComponent<HoldNoteObject>();

            if (noteScript != null)
            {
                int randomRange = UnityEngine.Random.Range(1, 6);
                noteScript.SetNoteIdentity(randomRange);
                noteScript.SetSpeed(8);
                noteScript.state = PlayerState.Default;
                noteScript.hitEffect = HitEffect.Default;
                noteScript.length = UnityEngine.Random.Range(1, 3);
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
            int projectileCount = randomAmount ? UnityEngine.Random.Range(1, fixedProjectileCount + 1) : fixedProjectileCount;

            // Create a list to store used identities
            List<int> usedIdentities = new List<int>();

            for (int i = 0; i < projectileCount; i++)
            {
                int noteIdentity;
                // Generate a unique random noteIdentity
                do
                {
                    noteIdentity = UnityEngine.Random.Range(1, 6);
                } while (usedIdentities.Contains(noteIdentity));

                // Add the generated noteIdentity to the list of used identities
                usedIdentities.Add(noteIdentity);

                // Instantiate NoteObject prefab at the unique random position
                AttackAnimate?.Invoke();
                GameObject newNote = Instantiate(noteObjectPrefab, transform.position, Quaternion.identity);
                Note noteScript = newNote.GetComponent<Note>();

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
