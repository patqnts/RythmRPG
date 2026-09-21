using System;
using System.Linq;
using PrimeTween;
using RythmRPG.Combat;
using RythmRPG.Rhythm;
using UnityEngine;

public enum Moveset { Default, Special, Arrow }

public class Note : MonoBehaviour
{
    [SerializeField] private int noteIdentity;
    [SerializeField] public float speed = 8f;
    [SerializeField] public int damage = 1;
    [SerializeField] public KeyButton[] keys;
    [SerializeField] public Animator animator;

    public bool isMoving;
    public bool canBePressed;
    public KeyCode keyCode;
    public Moveset moveset;
    public PlayerState state;
    public HitEffect hitEffect;

    protected KeyButton activeKeyButton;
    protected bool hitAccepted;
    private bool resolved;
    private RhythmJudgementResult pressJudgement;
    private NoteInitializeMovement initializeMovement;
    private bool initializeMovementStarted;
    private bool initializeMovementPlaying;
    private RhythmPatternRunner runner;
    private string runtimeNoteId;
    private float closestTimingError = float.MaxValue;
    private RhythmLaneTarget laneTarget;

    public string RuntimeNoteId => runtimeNoteId;
    public bool IsResolved => resolved;
    public RhythmNoteData Data { get; private set; }
    protected bool UsesHorizontalGameplay => runner != null && runner.HorizontalGameplay;
    public virtual bool ShouldAutoMissByPosition => true;
    public virtual bool ShouldResolveMissOnPlayerInput => false;

    public virtual void Initialize(RhythmNoteSpawnContext context)
    {
        runner = context.Runner;
        Data = context.NoteData;
        runtimeNoteId = string.IsNullOrEmpty(context.NoteId) ? Guid.NewGuid().ToString("N") : context.NoteId;
        noteIdentity = context.LaneId;
        speed = Mathf.Max(0.01f, context.Speed);
        damage = Mathf.Max(0, context.Damage);
        keys = context.Keys;
        activeKeyButton = null;
        hitAccepted = false;
        resolved = false;
        canBePressed = false;
        closestTimingError = float.MaxValue;
    }

    public virtual Vector3 GetJudgementWorldPosition() => transform.position;

    public virtual float GetTimingError(KeyButton keyButton)
    {
        RhythmLaneTarget target = GetLaneTarget();
        return target != null
            ? target.GetTimingDistance(GetJudgementWorldPosition())
            : keyButton != null ? Vector3.Distance(GetJudgementWorldPosition(), keyButton.transform.position) : float.MaxValue;
    }

    public virtual HitJudgement AdjustJudgement(HitJudgement judgement, float timingError) => judgement;

    public virtual bool HasPassedMissWindow(KeyButton keyButton, float badWindow)
    {
        RhythmLaneTarget target = GetLaneTarget();
        if (target == null) return false;
        float timingError = GetTimingError(keyButton);
        closestTimingError = Mathf.Min(closestTimingError, timingError);
        return closestTimingError <= badWindow
            && target.GetSignedProgressPastLine(GetJudgementWorldPosition()) > badWindow;
    }

    public virtual bool CanReceiveHit(KeyButton keyButton)
    {
        return isActiveAndEnabled && !resolved && !hitAccepted && !initializeMovementPlaying && IsWithinPressWindow(keyButton)
            && keyButton != null && keyButton.GetInteractable() && keyButton.keyIdentity == noteIdentity;
    }

    protected virtual bool IsWithinPressWindow(KeyButton keyButton) =>
        GetTimingError(keyButton) <= (runner != null ? runner.BadWindow : 0.9f);

    public bool TryHitFromKey(KeyButton keyButton, RhythmJudgementResult judgement)
    {
        if (!CanReceiveHit(keyButton)) return false;
        hitAccepted = true;
        activeKeyButton = keyButton;
        pressJudgement = judgement;
        OnHit(keyButton, judgement);
        return true;
    }

    public virtual bool IsUsingKey(KeyButton keyButton) => keyButton != null && activeKeyButton == keyButton;
    public virtual void OnKeyReleased(KeyButton keyButton) { }
    protected virtual bool ResolveImmediatelyOnPress => true;

    protected virtual void OnHit(KeyButton keyButton, RhythmJudgementResult result)
    {
        if (ResolveImmediatelyOnPress) Resolve(result);
    }

    protected RhythmJudgementResult GetPressJudgement() => pressJudgement;

    protected void Resolve(RhythmJudgementResult result)
    {
        if (resolved) return;
        resolved = true;
        canBePressed = false;
        runner?.ResolveNote(this, result);
        DestroyObject();
    }

    protected virtual void ResolveMiss(KeyButton keyButton, NoteResolutionSource source = NoteResolutionSource.Timeout)
    {
        if (resolved) return;
        RhythmLaneTarget target = GetLaneTarget();
        Vector3 position = target != null ? target.transform.position
            : keyButton != null ? keyButton.transform.position : GetJudgementWorldPosition();
        Resolve(new RhythmJudgementResult(runtimeNoteId, noteIdentity, HitJudgement.Miss,
            runner != null ? runner.BadWindow : 0.9f, position, source));
    }

    public virtual void ForceMiss(KeyButton keyButton, NoteResolutionSource source = NoteResolutionSource.Timeout) => ResolveMiss(keyButton, source);

    public void ForceResolve(RhythmJudgementResult result) => Resolve(result);
    public virtual bool ShouldDamagePlayerOnResolve(RhythmJudgementResult result) => result.Judgement == HitJudgement.Miss;

    protected KeyButton GetIdentityButton()
    {
        EnsureKeys();
        return keys.FirstOrDefault(key => key != null && key.keyIdentity == noteIdentity);
    }

    protected KeyButton GetKeyButton(int keyIdentity)
    {
        EnsureKeys();
        return keys.FirstOrDefault(key => key != null && key.keyIdentity == keyIdentity);
    }

    protected RhythmLaneTarget GetLaneTarget()
    {
        if (laneTarget != null && laneTarget.LaneId == noteIdentity) return laneTarget;
        laneTarget = FindObjectsByType<RhythmLaneTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(target => target != null && target.LaneId == noteIdentity);
        return laneTarget;
    }

    private void EnsureKeys()
    {
        if (keys == null || keys.Length == 0)
            keys = FindObjectsByType<KeyButton>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    protected void StopMovementTweens() => Tween.StopAll(transform);
    protected bool ShouldWaitForInitializeMovement() => TryStartInitializeMovement();

    protected bool TryStartInitializeMovement(Action onComplete = null)
    {
        if (initializeMovementPlaying) return true;
        if (initializeMovementStarted) return false;
        initializeMovement = GetComponent<NoteInitializeMovement>();
        if (initializeMovement == null || !initializeMovement.ShouldRun)
        {
            initializeMovementStarted = true;
            return false;
        }
        RhythmLaneTarget target = GetLaneTarget();
        if (target == null) return false;
        initializeMovementStarted = true;
        initializeMovementPlaying = true;
        StopMovementTweens();
        initializeMovement.Play(this, target.transform, () =>
        {
            initializeMovementPlaying = false;
            onComplete?.Invoke();
            OnInitializeMovementComplete();
        });
        return true;
    }

    protected virtual void OnInitializeMovementComplete() { }

    protected bool TryGetLaneX(int keyIdentity, out float targetX)
    {
        RhythmLaneTarget target = GetLaneTarget();
        targetX = target != null ? target.transform.position.x : transform.position.x;
        return target != null;
    }

    protected bool TryGetMissTargetY(int keyIdentity, float offset, out float targetY)
    {
        RhythmLaneTarget target = GetLaneTarget();
        targetY = target != null ? target.transform.position.y + offset : transform.position.y;
        return target != null;
    }

    protected bool TryGetMissTargetPosition(int keyIdentity, float missDistancePastKey, out Vector3 targetPosition)
    {
        RhythmLaneTarget target = GetLaneTarget();
        if (target == null)
        {
            targetPosition = transform.position;
            return false;
        }

        Vector3 toKey = target.transform.position - transform.position;
        Vector3 travelDirection = toKey.sqrMagnitude > 0.0001f ? toKey.normalized : -transform.up;
        targetPosition = target.transform.position + travelDirection * Mathf.Abs(missDistancePastKey);
        return true;
    }

    protected bool TryGetLaneTravelPositions(int keyIdentity, float missDistancePastKey,
        out Transform movementSpace, out Vector3 startLocal, out Vector3 keyLocal, out Vector3 targetLocal)
    {
        RhythmLaneTarget target = GetLaneTarget();
        movementSpace = transform.parent;
        startLocal = ToMovementLocal(transform.position, movementSpace);
        keyLocal = target != null ? ToMovementLocal(target.transform.position, movementSpace) : startLocal;
        targetLocal = startLocal;
        if (target == null) return false;

        Vector3 laneStartLocal = startLocal;
        laneStartLocal.x = keyLocal.x;
        Vector3 travelDirection = keyLocal - laneStartLocal;
        if (travelDirection.sqrMagnitude <= 0.0001f) travelDirection = keyLocal - startLocal;
        if (travelDirection.sqrMagnitude <= 0.0001f) travelDirection = Vector3.down;

        targetLocal = keyLocal + travelDirection.normalized * Mathf.Abs(missDistancePastKey);
        return true;
    }

    protected void TweenLaneX(int keyIdentity, float duration = 0.25f)
    {
        if (TryGetLaneX(keyIdentity, out float x))
            Tween.PositionX(transform, x, Mathf.Max(0.01f, duration), Ease.OutSine);
    }

    protected void TweenYTo(float targetY, float moveSpeed)
    {
        float duration = Mathf.Max(0.01f, Mathf.Abs(transform.position.y - targetY) / Mathf.Max(0.01f, moveSpeed));
        Tween.PositionY(transform, targetY, duration, Ease.Linear);
    }

    protected void TweenLaneFall(int keyIdentity, float targetYOffset, float moveSpeed, float laneDuration = 0.25f)
    {
        if (!TryGetLaneTravelPositions(keyIdentity, Mathf.Abs(targetYOffset), out Transform movementSpace,
                out Vector3 startLocal, out Vector3 keyLocal, out Vector3 targetLocal)) return;
        float duration = Mathf.Max(0.01f,
            Vector3.Distance(transform.position, FromMovementLocal(targetLocal, movementSpace)) / Mathf.Max(0.01f, moveSpeed));
        TweenLaneTravel(movementSpace, startLocal, keyLocal, targetLocal, duration, laneDuration);
    }

    protected void TweenLaneTravel(Transform movementSpace, Vector3 startLocal, Vector3 keyLocal, Vector3 targetLocal,
        float duration, float laneDuration = 0.25f)
    {
        float safeDuration = Mathf.Max(0.01f, duration);
        float safeLaneDuration = Mathf.Max(0.01f, laneDuration);
        Tween.Custom(transform, 0f, 1f, safeDuration, (target, progress) =>
        {
            Vector3 localPosition = Vector3.LerpUnclamped(startLocal, targetLocal, progress);
            float laneProgress = Mathf.Sin(Mathf.Clamp01(progress * safeDuration / safeLaneDuration) * Mathf.PI * 0.5f);
            localPosition.x = Mathf.LerpUnclamped(startLocal.x, keyLocal.x, laneProgress);
            target.position = FromMovementLocal(localPosition, movementSpace);
        }, Ease.Linear);
    }

    protected static Vector3 ToMovementLocal(Vector3 worldPosition, Transform movementSpace)
    {
        return movementSpace != null ? movementSpace.InverseTransformPoint(worldPosition) : worldPosition;
    }

    protected static Vector3 FromMovementLocal(Vector3 localPosition, Transform movementSpace)
    {
        return movementSpace != null ? movementSpace.TransformPoint(localPosition) : localPosition;
    }

    protected void ReportMiss(KeyButton keyButton) => ResolveMiss(keyButton);
    protected void CompleteHeldHit() => Resolve(GetPressJudgement());

    public void SetNoteIdentity(int value) => noteIdentity = value;
    public int GetNoteIdentity() => noteIdentity;
    public void SetSpeed(float value) => speed = value;
    public float GetSpeed() => speed;

    public virtual void DestroyObject()
    {
        canBePressed = false;
        isMoving = false;
        initializeMovementPlaying = false;
        initializeMovement?.Stop();
        if (animator != null) animator.SetTrigger("Hit");
        StopMovementTweens();
        Destroy(gameObject, 0.25f);
    }

    public void ClearWithoutResult()
    {
        if (resolved) return;
        resolved = true;
        canBePressed = false;
        DestroyObject();
    }
}

namespace RythmRPG.Combat
{
    public readonly struct RhythmNoteSpawnContext
    {
        public readonly RhythmPatternRunner Runner;
        public readonly RhythmNoteData NoteData;
        public readonly string NoteId;
        public readonly int LaneId;
        public readonly float Speed;
        public readonly int Damage;
        public readonly KeyButton[] Keys;

        public RhythmNoteSpawnContext(RhythmPatternRunner runner, RhythmNoteData noteData, string noteId,
            int laneId, float speed, int damage, KeyButton[] keys)
        {
            Runner = runner;
            NoteData = noteData;
            NoteId = noteId;
            LaneId = laneId;
            Speed = speed;
            Damage = damage;
            Keys = keys;
        }
    }
}
