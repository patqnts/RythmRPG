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

    public string RuntimeNoteId => runtimeNoteId;
    public bool IsResolved => resolved;
    public RhythmNoteData Data { get; private set; }
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
    }

    public virtual Vector3 GetJudgementWorldPosition() => transform.position;

    public virtual float GetTimingError(KeyButton keyButton)
    {
        return keyButton == null ? float.MaxValue : Mathf.Abs(GetJudgementWorldPosition().y - keyButton.transform.position.y);
    }

    public virtual HitJudgement AdjustJudgement(HitJudgement judgement, float timingError) => judgement;

    public virtual bool CanReceiveHit(KeyButton keyButton)
    {
        return isActiveAndEnabled && !resolved && !hitAccepted && !initializeMovementPlaying && IsWithinPressWindow(keyButton)
            && keyButton != null && keyButton.GetInteractable() && keyButton.keyIdentity == noteIdentity;
    }

    protected virtual bool IsWithinPressWindow(KeyButton keyButton) => canBePressed;

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
        Vector3 position = keyButton != null ? keyButton.transform.position : GetJudgementWorldPosition();
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
        KeyButton targetKey = GetIdentityButton();
        if (targetKey == null) return false;
        initializeMovementStarted = true;
        initializeMovementPlaying = true;
        StopMovementTweens();
        initializeMovement.Play(this, targetKey, () =>
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
        KeyButton key = GetKeyButton(keyIdentity);
        targetX = key != null ? key.transform.position.x : transform.position.x;
        return key != null;
    }

    protected bool TryGetMissTargetY(int keyIdentity, float offset, out float targetY)
    {
        KeyButton key = GetKeyButton(keyIdentity);
        targetY = key != null ? key.transform.position.y + offset : transform.position.y;
        return key != null;
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
        if (!TryGetMissTargetY(keyIdentity, targetYOffset, out float targetY)) return;
        TweenLaneX(keyIdentity, laneDuration);
        TweenYTo(targetY, moveSpeed);
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
