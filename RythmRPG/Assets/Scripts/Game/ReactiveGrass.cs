using UnityEngine;

public class ReactiveGrass : MonoBehaviour
{
    [Header("Grass Visual")]
    [Tooltip("Assign the SpriteRenderer or Quad child. Do not assign the collider root.")]
    [SerializeField] private Transform visual;

    [Header("Interactor Detection")]
    [SerializeField] private LayerMask interactorLayers;
    [SerializeField, Min(0.1f)] private float detectionRadius = 1.2f;

    [Header("Bending")]
    [SerializeField, Range(0f, 60f)] private float maximumBendAngle = 25f;
    [SerializeField, Min(0f)] private float bendSpeed = 15f;
    [SerializeField, Min(0f)] private float returnSpeed = 7f;

    private readonly Collider[] results = new Collider[16];

    private Quaternion restingRotation;

    private void Awake()
    {
        if (visual == null)
            visual = transform;

        restingRotation = visual.localRotation;
    }

    private void Update()
    {
        Vector3 grassPosition = transform.position;

        int count = Physics.OverlapSphereNonAlloc(
            grassPosition,
            detectionRadius,
            results,
            interactorLayers,
            QueryTriggerInteraction.Collide
        );

        Collider nearestCollider = null;
        Vector3 nearestPoint = Vector3.zero;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider current = results[i];

            if (current == null)
                continue;

            Vector3 point = current.ClosestPoint(grassPosition);

            Vector3 difference = grassPosition - point;
            difference.y = 0f;

            float distance = difference.magnitude;

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestCollider = current;
                nearestPoint = point;
            }
        }

        Quaternion targetRotation = restingRotation;
        float currentSpeed = returnSpeed;

        if (nearestCollider != null)
        {
            Vector3 awayDirection = grassPosition - nearestPoint;
            awayDirection.y = 0f;

            // ClosestPoint can return the grass position when inside a collider.
            if (awayDirection.sqrMagnitude < 0.0001f)
            {
                awayDirection =
                    grassPosition - nearestCollider.bounds.center;

                awayDirection.y = 0f;
            }

            if (awayDirection.sqrMagnitude < 0.0001f)
                awayDirection = transform.right;

            awayDirection.Normalize();

            float influence = Mathf.Clamp01(
                1f - nearestDistance / detectionRadius
            );

            // Smooth the response near the edge of the radius.
            influence = influence * influence * (3f - 2f * influence);

            float bendAngle = maximumBendAngle * influence;

            Transform parent = visual.parent;

            Vector3 parentLocalDirection = parent != null
                ? parent.InverseTransformDirection(awayDirection)
                : awayDirection;

            // Convert the direction into the visual's resting space.
            Vector3 visualLocalDirection =
                Quaternion.Inverse(restingRotation) *
                parentLocalDirection;

            // Keep the bending inside the sprite/quad plane.
            visualLocalDirection.z = 0f;

            if (visualLocalDirection.sqrMagnitude < 0.0001f)
                visualLocalDirection = Vector3.right;

            visualLocalDirection.Normalize();

            Vector3 bendAxis = Vector3.Cross(
                Vector3.up,
                visualLocalDirection
            ).normalized;

            targetRotation =
                restingRotation *
                Quaternion.AngleAxis(bendAngle, bendAxis);

            currentSpeed = bendSpeed;
        }

        float smoothing = 1f - Mathf.Exp(
            -currentSpeed * Time.deltaTime
        );

        visual.localRotation = Quaternion.Slerp(
            visual.localRotation,
            targetRotation,
            smoothing
        );
    }

    private void OnDisable()
    {
        if (visual != null)
            visual.localRotation = restingRotation;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}