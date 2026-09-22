using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// How the player character performs an attack: walk to the middle of the screen, run a list of steps
    /// (animation, thrown projectile, spawned effect, sound, shake, wait, impact...), then walk back. Steps are
    /// polymorphic (<see cref="AttackStep"/>): add new kinds by writing a new AttackStep subclass; it appears in the
    /// "Add step" menu automatically. A step can run in the background (Wait For Completion off), so steps combine.
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Character Attack Sequence", fileName = "CharacterAttackSequence")]
    public sealed class CharacterAttackSequence : ScriptableObject
    {
        [Header("Move To Stage")]
        [Tooltip("Walk to the attack spot before the steps run, and back afterwards.")]
        [SerializeField] private bool moveToStage = true;
        [Tooltip("Attack spot in the camera view (0-1, 0 = bottom). 0.5, 0.5 is the middle of the screen.")]
        [SerializeField] private Vector2 stageViewport = new(0.5f, 0.45f);
        [SerializeField, Min(0f)] private float moveInSeconds = 0.35f;
        [SerializeField, Min(0f)] private float moveBackSeconds = 0.35f;
        [Tooltip("Optional Animator bool set while walking (e.g. \"IsMoving\").")]
        [SerializeField] private string movingBoolParameter = string.Empty;
        [Tooltip("Optional animator state played on arrival at the stage, before the steps (e.g. \"Idle\").")]
        [SerializeField] private string arriveState = string.Empty;

        [Header("Steps")]
        [SerializeReference, SubclassSelector] private List<AttackStep> steps = new();

        public bool MoveToStage => moveToStage;
        public Vector2 StageViewport => stageViewport;
        public float MoveInSeconds => moveInSeconds;
        public float MoveBackSeconds => moveBackSeconds;
        public string MovingBoolParameter => movingBoolParameter;
        public string ArriveState => arriveState;
        public IReadOnlyList<AttackStep> Steps => steps;

        /// <summary>True when any step lands hits; otherwise the ability's effects land after the last step.</summary>
        public bool HasImpactStep => TotalHitWeight > 0f;

        /// <summary>Sum of all steps' hit weights: each hit gets weight / total of the ability's damage or heal.</summary>
        public float TotalHitWeight
        {
            get
            {
                float total = 0f;
                foreach (AttackStep step in steps)
                    if (step != null) total += step.HitWeight;
                return total;
            }
        }
    }

    /// <summary>Marks a [SerializeReference] field so the inspector shows a type picker for it (and list elements).</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SubclassSelectorAttribute : PropertyAttribute { }
}
