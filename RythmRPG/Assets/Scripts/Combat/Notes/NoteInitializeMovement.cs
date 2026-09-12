using System;
using System.Collections;
using PrimeTween;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class NoteInitializeMovement : MonoBehaviour
{
    [SerializeField] private NoteInitializeMovementType movementType = NoteInitializeMovementType.None;

    [Header("Slow Then Burst")]
    [SerializeField, Min(0f)] private float slowStartDuration = 0.55f;
    [SerializeField, Min(0f)] private float slowStartLaneProgress = 0.2f;
    [SerializeField, Min(0.01f)] private float burstSpeed = 18f;

    [Header("Missile Guided Slither")]
    [FormerlySerializedAs("missileSwimDuration")]
    [SerializeField, Min(0.01f)] private float missileSlitherDuration = 1.5f;
    [FormerlySerializedAs("missileAmplitude")]
    [SerializeField, Min(0f)] private float missileSlitherAmplitude = 5f;
    [FormerlySerializedAs("missileLoops")]
    [SerializeField, Range(1, 2)] private int missileCurveCount = 2;
    [FormerlySerializedAs("missileLaneSettleDuration")]
    [SerializeField, Min(0f)] private float missileLandingDuration = 1f;
    [SerializeField] private bool rotateWithMissileMotion = true;
    [SerializeField] private bool resetRotationWhenFinished = true;
    [SerializeField] private float missileRotationOffset = 90f;
    [SerializeField] private bool useMissileForwardCurve = false;
    [SerializeField] private AnimationCurve missileForwardCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Missile Randomness")]
    [SerializeField] private bool randomizeMissileTrajectory = false;
    [SerializeField, Range(0f, 1f)] private float missileAmplitudeRandomness = 0.35f;
    [SerializeField, Range(0f, 1f)] private float missileDurationRandomness = 0.2f;
    [FormerlySerializedAs("missileMinRandomLoops")]
    [SerializeField, Range(1, 2)] private int missileMinRandomCurveCount = 1;
    [FormerlySerializedAs("missileMaxRandomLoops")]
    [SerializeField, Range(1, 2)] private int missileMaxRandomCurveCount = 1;

    [Header("Missile Launch")]
    [SerializeField] private bool randomizeMissileLaunchDirection = true;
    [SerializeField, Min(0f)] private float missileLaunchDistance = 5f;
    [SerializeField, Min(0.01f)] private float missileLaunchDuration = 1f;
    [SerializeField, Range(0f, 80f)] private float missileLaunchSideAngle = 35f;

    [Header("Missile Gizmo")]
    [SerializeField] private bool drawMissileTrajectoryGizmo = true;
    [SerializeField] private bool drawMissileTrajectoryOnlyWhenSelected = true;
    [SerializeField, Range(6, 80)] private int missileGizmoSegments = 32;
    [SerializeField] private Transform missileGizmoPreviewLaneTarget;
    [SerializeField] private float missileGizmoFallbackLaneXOffset = 2f;
    [SerializeField] private Color missileLaunchGizmoColor = new Color(1f, 0.35f, 0.1f, 0.85f);
    [SerializeField] private Color missileSlitherGizmoColor = new Color(0.1f, 0.8f, 1f, 0.85f);
    [SerializeField] private Color missileLandingGizmoColor = new Color(0.25f, 1f, 0.25f, 0.85f);

    private Coroutine movementRoutine;
    private bool hasActiveMissileTrajectory;
    private Vector3 activeMissileStartPosition;
    private Vector3 activeMissileLaneSpawnPosition;
    private MissileTrajectory activeMissileTrajectory;

    public bool ShouldRun => movementType != NoteInitializeMovementType.None;
    public bool IsPlaying { get; private set; }

    public void SetMovementType(NoteInitializeMovementType type)
    {
        movementType = type;
    }

    public void Play(Note note, KeyButton targetKey, Action onComplete)
    {
        Stop();

        if (!ShouldRun || note == null || targetKey == null)
        {
            onComplete?.Invoke();
            return;
        }

        movementRoutine = StartCoroutine(PlayRoutine(targetKey, onComplete));
    }

    public void Stop()
    {
        if (movementRoutine != null)
        {
            StopCoroutine(movementRoutine);
            movementRoutine = null;
        }

        Tween.StopAll(transform);
        IsPlaying = false;
        hasActiveMissileTrajectory = false;
    }

    private IEnumerator PlayRoutine(KeyButton targetKey, Action onComplete)
    {
        IsPlaying = true;

        switch (movementType)
        {
            case NoteInitializeMovementType.SlowThenBurst:
                yield return SlowThenBurst(targetKey);
                break;
            case NoteInitializeMovementType.MissileSCurve:
                yield return MissileSCurve(targetKey);
                break;
        }

        movementRoutine = null;
        IsPlaying = false;
        hasActiveMissileTrajectory = false;
        onComplete?.Invoke();
    }

    private IEnumerator SlowThenBurst(KeyButton targetKey)
    {
        Vector3 start = transform.position;
        Vector3 laneSpawnPosition = GetLaneSpawnPosition(targetKey, start);
        Vector3 slowEnd = Vector3.Lerp(start, laneSpawnPosition, Mathf.Clamp01(slowStartLaneProgress));

        if (slowStartDuration > 0f && slowStartLaneProgress > 0f)
        {
            Tween.Position(transform, slowEnd, slowStartDuration, Ease.OutSine);
            yield return new WaitForSeconds(slowStartDuration);
        }

        float duration = Mathf.Max(0.01f, Vector3.Distance(transform.position, laneSpawnPosition) / Mathf.Max(0.01f, burstSpeed));
        Tween.Position(transform, laneSpawnPosition, duration, Ease.InQuad);
        yield return new WaitForSeconds(duration);
    }

    private IEnumerator MissileSCurve(KeyButton targetKey)
    {
        Vector3 start = transform.position;
        Quaternion startRotation = transform.rotation;
        Vector3 previousPosition = start;
        MissileTrajectory trajectory = CreateMissileTrajectory();
        Vector3 laneSpawnPosition = GetLaneSpawnPosition(targetKey, start);

        activeMissileStartPosition = start;
        activeMissileLaneSpawnPosition = laneSpawnPosition;
        activeMissileTrajectory = trajectory;
        hasActiveMissileTrajectory = true;

        Vector3 launchEndPosition = start + GetMissileLaunchOffset(trajectory.launchDirection);
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, missileLaunchDuration);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            Vector3 nextPosition = Vector3.Lerp(start, launchEndPosition, EaseOutSine(normalizedTime));

            ApplyMotion(nextPosition, previousPosition);
            previousPosition = nextPosition;

            yield return null;
        }

        transform.position = launchEndPosition;
        previousPosition = launchEndPosition;

        elapsed = 0f;
        duration = trajectory.duration + Mathf.Max(0f, missileLandingDuration);
        Vector3 slitherStartPosition = launchEndPosition;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            float forward = GetMissileForward(normalizedTime);

            Vector3 nextPosition = GetMissileSlitherPosition(slitherStartPosition, laneSpawnPosition, forward, trajectory);
            nextPosition = PreventUpwardTravel(nextPosition, previousPosition);

            ApplyMotion(nextPosition, previousPosition);
            previousPosition = nextPosition;

            yield return null;
        }

        transform.position = laneSpawnPosition;

        if (resetRotationWhenFinished)
        {
            transform.rotation = startRotation;
        }

        hasActiveMissileTrajectory = false;
    }

    private void ApplyMotion(Vector3 nextPosition, Vector3 previousPosition)
    {
        if (rotateWithMissileMotion)
        {
            Vector3 delta = nextPosition - previousPosition;
            if (delta.sqrMagnitude > 0.0001f)
            {
                float rotationZ = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg + missileRotationOffset;
                transform.rotation = Quaternion.Euler(0f, 0f, rotationZ);
            }
        }

        transform.position = nextPosition;
    }

    private Vector3 GetLaneSpawnPosition(KeyButton targetKey, Vector3 spawnPosition)
    {
        spawnPosition.x = targetKey.transform.position.x;
        return spawnPosition;
    }

    private float EaseOutSine(float time)
    {
        return Mathf.Sin(Mathf.Clamp01(time) * Mathf.PI * 0.5f);
    }

    private float GetMissileForward(float normalizedTime)
    {
        float clampedTime = Mathf.Clamp01(normalizedTime);
        return useMissileForwardCurve && missileForwardCurve != null
            ? Mathf.Clamp01(missileForwardCurve.Evaluate(clampedTime))
            : clampedTime;
    }

    private MissileTrajectory CreateMissileTrajectory()
    {
        MissileTrajectory trajectory = new MissileTrajectory
        {
            horizontalAmplitude = missileSlitherAmplitude,
            duration = Mathf.Max(0.01f, missileSlitherDuration),
            curveCount = Mathf.Clamp(missileCurveCount, 1, 2),
            horizontalDirection = 1f,
            launchDirection = randomizeMissileLaunchDirection
                ? UnityEngine.Random.Range(-1, 2)
                : 0
        };

        if (!randomizeMissileTrajectory)
        {
            return trajectory;
        }

        int minCurveCount = Mathf.Clamp(missileMinRandomCurveCount, 1, 2);
        int maxCurveCount = Mathf.Clamp(missileMaxRandomCurveCount, minCurveCount, 2);
        trajectory.curveCount = UnityEngine.Random.Range(minCurveCount, maxCurveCount + 1);
        trajectory.horizontalDirection = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        trajectory.horizontalAmplitude *= RandomRangeAroundOne(missileAmplitudeRandomness);
        trajectory.duration *= RandomRangeAroundOne(missileDurationRandomness);

        return trajectory;
    }

    private Vector3 GetMissileLaunchOffset(int launchDirection)
    {
        float sideDirection = Mathf.Clamp(launchDirection, -1, 1);
        Vector3 launchDirectionVector = Quaternion.Euler(0f, 0f, -sideDirection * missileLaunchSideAngle) * Vector3.up;
        return launchDirectionVector * missileLaunchDistance;
    }

    private Vector3 PreventUpwardTravel(Vector3 nextPosition, Vector3 previousPosition)
    {
        if (nextPosition.y > previousPosition.y)
        {
            nextPosition.y = previousPosition.y;
        }

        return nextPosition;
    }

    private Vector3 GetMissileSlitherPosition(Vector3 start, Vector3 laneSpawnPosition, float forward, MissileTrajectory trajectory)
    {
        float normalizedForward = Mathf.Clamp01(forward);
        float landingProgress = normalizedForward;
        Vector3 position = Vector3.Lerp(start, laneSpawnPosition, landingProgress);

        float curveWave = Mathf.Sin(normalizedForward * Mathf.PI * trajectory.curveCount);
        float curveEnvelope = Mathf.Sin(normalizedForward * Mathf.PI);

        float xOffset = curveWave * curveEnvelope * trajectory.horizontalAmplitude;
        position.x += xOffset * trajectory.horizontalDirection;

        position.y = Mathf.Lerp(start.y, laneSpawnPosition.y, landingProgress);

        return position;
    }

    private void OnDrawGizmos()
    {
        if (drawMissileTrajectoryOnlyWhenSelected)
        {
            return;
        }

        DrawMissileTrajectoryGizmo();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawMissileTrajectoryOnlyWhenSelected)
        {
            return;
        }

        DrawMissileTrajectoryGizmo();
    }

    private void DrawMissileTrajectoryGizmo()
    {
        if (!drawMissileTrajectoryGizmo || movementType != NoteInitializeMovementType.MissileSCurve)
        {
            return;
        }

        if (hasActiveMissileTrajectory)
        {
            DrawMissileTrajectory(
                activeMissileStartPosition,
                activeMissileLaneSpawnPosition,
                activeMissileTrajectory,
                missileLaunchGizmoColor,
                missileSlitherGizmoColor);
            return;
        }

        Vector3 start = transform.position;
        Vector3 laneSpawnPosition = GetGizmoLaneSpawnPosition(start);
        MissileTrajectory trajectory = CreatePreviewMissileTrajectory();

        if (randomizeMissileLaunchDirection)
        {
            for (int launchDirection = -1; launchDirection <= 1; launchDirection++)
            {
                trajectory.launchDirection = launchDirection;
                DrawMissileTrajectory(
                    start,
                    laneSpawnPosition,
                    trajectory,
                    missileLaunchGizmoColor,
                    missileSlitherGizmoColor);
            }
        }
        else
        {
            DrawMissileTrajectory(
                start,
                laneSpawnPosition,
                trajectory,
                missileLaunchGizmoColor,
                missileSlitherGizmoColor);
        }
    }

    private MissileTrajectory CreatePreviewMissileTrajectory()
    {
        return new MissileTrajectory
        {
            horizontalAmplitude = missileSlitherAmplitude,
            duration = Mathf.Max(0.01f, missileSlitherDuration),
            curveCount = randomizeMissileTrajectory
                ? Mathf.Clamp(missileMaxRandomCurveCount, 1, 2)
                : Mathf.Clamp(missileCurveCount, 1, 2),
            horizontalDirection = 1f,
            launchDirection = 0
        };
    }

    private Vector3 GetGizmoLaneSpawnPosition(Vector3 start)
    {
        if (missileGizmoPreviewLaneTarget != null)
        {
            return GetLaneSpawnPosition(missileGizmoPreviewLaneTarget, start);
        }

        Note note = GetComponent<Note>();
        KeyButton targetKey = note != null ? FindGizmoTargetKey(note.GetNoteIdentity()) : null;
        if (targetKey != null)
        {
            return GetLaneSpawnPosition(targetKey, start);
        }

        start.x += missileGizmoFallbackLaneXOffset;
        return start;
    }

    private KeyButton FindGizmoTargetKey(int keyIdentity)
    {
        if (keyIdentity <= 0)
        {
            return null;
        }

        KeyButton[] sceneKeys = FindObjectsOfType<KeyButton>();
        foreach (KeyButton keyButton in sceneKeys)
        {
            if (keyButton != null && keyButton.keyIdentity == keyIdentity)
            {
                return keyButton;
            }
        }

        return null;
    }

    private Vector3 GetLaneSpawnPosition(Transform laneTarget, Vector3 spawnPosition)
    {
        spawnPosition.x = laneTarget.position.x;
        return spawnPosition;
    }

    private void DrawMissileTrajectory(
        Vector3 start,
        Vector3 laneSpawnPosition,
        MissileTrajectory trajectory,
        Color launchColor,
        Color slitherColor)
    {
        int segments = Mathf.Max(2, missileGizmoSegments);
        Vector3 launchEndPosition = start + GetMissileLaunchOffset(trajectory.launchDirection);
        Vector3 previousPosition = start;

        Gizmos.color = launchColor;
        for (int i = 1; i <= segments; i++)
        {
            float normalizedTime = i / (float)segments;
            Vector3 nextPosition = Vector3.Lerp(start, launchEndPosition, EaseOutSine(normalizedTime));
            Gizmos.DrawLine(previousPosition, nextPosition);
            previousPosition = nextPosition;
        }

        Gizmos.color = slitherColor;
        previousPosition = launchEndPosition;
        for (int i = 1; i <= segments; i++)
        {
            float normalizedTime = i / (float)segments;
            float forward = GetMissileForward(normalizedTime);
            Vector3 nextPosition = GetMissileSlitherPosition(launchEndPosition, laneSpawnPosition, forward, trajectory);
            nextPosition = PreventUpwardTravel(nextPosition, previousPosition);

            Gizmos.DrawLine(previousPosition, nextPosition);
            previousPosition = nextPosition;
        }

        Gizmos.color = missileLandingGizmoColor;
        Gizmos.DrawWireSphere(start, 0.12f);
        Gizmos.DrawWireSphere(launchEndPosition, 0.16f);
        Gizmos.DrawWireSphere(laneSpawnPosition, 0.2f);
    }

    private float RandomRangeAroundOne(float randomness)
    {
        float clampedRandomness = Mathf.Clamp01(randomness);
        return UnityEngine.Random.Range(1f - clampedRandomness, 1f + clampedRandomness);
    }

    private struct MissileTrajectory
    {
        public float horizontalAmplitude;
        public float duration;
        public int curveCount;
        public float horizontalDirection;
        public int launchDirection;
    }
}
