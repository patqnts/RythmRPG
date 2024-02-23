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

    void Start()
    {
        isMoving = false;
        isSpecialMovement = true;
        specialMovementTimer = 5f;
        shuffleTimer = 5f;
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
                    noteIdentity = Random.Range(minIdentity, maxIdentity + 1);
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
                    // Move speed for 1000 after the pause
                    float targetX = keys.Where(x => x.keyIdentity == noteIdentity).FirstOrDefault().gameObject.transform.position.x;
                    float smoothSpeed = 10f; // Adjust the smoothSpeed as needed

                    // Smoothly interpolate between current position and target position
                    transform.position = new Vector2(Mathf.Lerp(transform.position.x, targetX, Time.deltaTime * smoothSpeed), transform.position.y);

                    transform.position -= new Vector3(0, 1000 * Time.deltaTime, 0f);

                    Destroy(gameObject, 5f);
                    isMoving = false;
                }
            }
        }
    }
}
