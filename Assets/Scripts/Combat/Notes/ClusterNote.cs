using PrimeTween;
using RythmRPG.Combat;
using UnityEngine;

public class ClusterNote : Note
{
    // Start is called before the first frame update
    public int force;
    void Start()
    {
        keys = FindObjectsByType<KeyButton>(FindObjectsSortMode.None);
        if (UsesHorizontalGameplay)
        {
            StartFalling();
            return;
        }
        if (!TryStartInitializeMovement(StartMovementArc))
        {
            StartMovementArc();
        }
    }
    private void Update()
    {
    }

    private void StartMovementArc()
    {

        float initialVelocity = Mathf.Max(1f, force);
        float riseDuration = Mathf.Clamp(initialVelocity / 18f, 0.15f, 0.8f);
        float riseHeight = Mathf.Clamp(initialVelocity * 0.12f, 0.5f, 3f);

        Tween.PositionY(transform, transform.position.y + riseHeight, riseDuration, Ease.OutSine)
            .OnComplete(this, note => note.StartFalling());
    }

    private void StartFalling()
    {
        TweenLaneFall(GetNoteIdentity(), -3f, Mathf.Max(1f, force));
    }

    private void OnDestroy()
    {
    }
    protected override void OnHit(KeyButton keyButton, RhythmJudgementResult result)
    {
        StopMovementTweens();
        base.OnHit(keyButton, result);
    }
}
