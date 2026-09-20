using UnityEngine;

public class WaterInteractor : MonoBehaviour
{
    [Header("Movement Ripple")]
    [SerializeField] private float distanceBetweenRipples = 0.18f;
    [SerializeField] private float minimumMovement = 0.01f;
    [SerializeField] private float movementStrength = 0.8f;

    [Header("Idle Ripple")]
    [SerializeField] private bool createIdleRipples = true;
    [SerializeField] private float idleInterval = 1.4f;
    [SerializeField] private float idleStrength = 0.20f;

    [Header("Water Detection")]
    [SerializeField] private bool onlyWhenInWater = true;

    private Vector3 previousPosition;
    private Vector3 lastRipplePosition;
    private float nextIdleRipple;
    private bool isInWater;

    private void Start()
    {
        previousPosition = transform.position;
        lastRipplePosition = transform.position;
    }

    private void Update()
    {
        Vector3 currentPosition = transform.position;

        if (onlyWhenInWater && !isInWater)
        {
            previousPosition = currentPosition;
            return;
        }

        float frameMovement = Vector3.Distance(currentPosition, previousPosition);

        if (frameMovement >= minimumMovement)
        {
            float distance = Vector3.Distance(currentPosition, lastRipplePosition);

            if (distance >= distanceBetweenRipples)
            {
                CreateRipple(movementStrength);
                lastRipplePosition = currentPosition;
            }

            nextIdleRipple = Time.time + idleInterval;
        }
        else if (createIdleRipples && Time.time >= nextIdleRipple)
        {
            CreateRipple(idleStrength);
            nextIdleRipple = Time.time + idleInterval;
        }

        previousPosition = currentPosition;
    }

    private void CreateRipple(float strength)
    {
        if (WaterRippleManager.Instance != null)
            WaterRippleManager.Instance.AddRipple(transform.position, strength);
    }

    public void EnterWater()
    {
        isInWater = true;
        lastRipplePosition = transform.position;
        nextIdleRipple = Time.time + idleInterval;
        CreateRipple(1.25f);
    }

    public void ExitWater()
    {
        if (isInWater)
            CreateRipple(0.8f);

        isInWater = false;
    }
}
