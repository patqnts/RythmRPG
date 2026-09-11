using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public class ArrowNote : NoteObject
{
    private bool isSpecialMovement;
    private float specialMovementTimer;
    private float shuffleTimer;
    private float shuffleInterval = 0.5f;
    private int minIdentity = 1;
    private int maxIdentity = 5;
    private bool specialLaunchStarted;

    void Start()
    {
        keys = FindObjectsOfType<KeyButton>();
        stateHandler = FindObjectOfType<PlayerStateHandler>();
        CombatManager.instance.StopAttackEvent += DestroyObject;
        isMoving = false;
        isSpecialMovement = true;
        specialMovementTimer = 5f;
        shuffleTimer = 5f;
    }

    private void OnDestroy()
    {
        CombatManager.instance.StopAttackEvent -= DestroyObject;
    }

    public override void Update()
    {
        if (isSpecialMovement)
        {
            SpecialMovement();
        }
        else if (isMoving)
        {
            // Implement specific movement for ArrowNote here
            // For example, you may want to rotate the arrow or apply a different movement pattern

            base.Update(); // Call the base class Update method for common movement behavior
        }
    }

    private void SpecialMovement()
    {
        if (specialMovementTimer > 0)
        {
            // Freeze the going down movement for 5 seconds
            specialMovementTimer -= Time.deltaTime;
        }
        else
        {
            if (shuffleTimer > 0)
            {
                // Shuffle noteIdentity between 1-5 every shuffleInterval seconds
                shuffleTimer -= Time.deltaTime;
                if (shuffleTimer <= 0)
                {
                    SetNoteIdentity(Random.Range(minIdentity, maxIdentity + 1));
                    shuffleTimer = shuffleInterval;
                }
            }
            else
            {
                // Pause for 0.5 seconds
                if (shuffleTimer > -0.5f)
                {
                    shuffleTimer -= Time.deltaTime;
                }
                else
                {
                    StartSpecialLaunch();
                }
            }
        }
    }

    private void StartSpecialLaunch()
    {
        if (specialLaunchStarted)
        {
            return;
        }

        specialLaunchStarted = true;
        isMoving = false;
        keyCode = CombatManager.instance.GetKeyCodeFromNoteIdentity(GetNoteIdentity());

        StopMovementTweens();
        TweenLaneX(GetNoteIdentity());
        TweenYTo(transform.position.y - 5000f, 1000f);
        Destroy(gameObject, 5f);
    }
}
