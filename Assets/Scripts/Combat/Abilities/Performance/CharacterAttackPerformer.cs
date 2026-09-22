using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Plays a <see cref="CharacterAttackSequence"/>: moves the player to the stage spot, runs the steps (some in the
    /// background), waits for them, then moves the player back to where it stood.
    /// </summary>
    public sealed class CharacterAttackPerformer : MonoBehaviour
    {
        [SerializeField] private CombatEncounterCoordinator encounterCoordinator;
        [SerializeField] private CombatLanePresentation3D lanePresentation;
        [Tooltip("Safety net: a sequence never blocks the turn for longer than this.")]
        [SerializeField, Min(1f)] private float maxSequenceSeconds = 15f;

        private Transform effectRoot;

        /// <param name="hit">Called for every hit the steps land, with its weight. The caller divides the ability's
        /// effects by weight and delivers anything left over after the sequence.</param>
        public IEnumerator Perform(CharacterAttackSequence sequence, PlayerCombatant player, EnemyCombatant enemy,
            Color accent, Action<float> hit)
        {
            ResolveReferences();
            if (sequence == null || player == null) yield break;

            Transform caster = player.transform;
            Animator animator = player.GetComponentInChildren<Animator>();
            AttackAnimationEventRelay relay = null;
            if (animator != null)
            {
                relay = animator.GetComponent<AttackAnimationEventRelay>();
                if (relay == null) relay = animator.gameObject.AddComponent<AttackAnimationEventRelay>();
            }
            Camera view = lanePresentation != null && lanePresentation.RenderCamera != null ? lanePresentation.RenderCamera : Camera.main;
            Vector3 home = caster.position;
            CharacterController controller = player.GetComponent<CharacterController>();
            bool controllerWasEnabled = controller != null && controller.enabled;
            if (controllerWasEnabled) controller.enabled = false;
            if (encounterCoordinator != null) encounterCoordinator.PlayerPlacementSuspended = true;

            try
            {
                if (sequence.MoveToStage && TryStagePosition(view, sequence.StageViewport, home.y, out Vector3 stage))
                    yield return Move(caster, animator, sequence.MovingBoolParameter, home, stage, sequence.MoveInSeconds);
                if (!string.IsNullOrWhiteSpace(sequence.ArriveState) && animator != null
                    && animator.HasState(0, Animator.StringToHash(sequence.ArriveState)))
                    animator.Play(sequence.ArriveState, 0, 0f);

                var context = new AttackStepContext
                {
                    Host = this,
                    Caster = caster,
                    Target = enemy != null ? enemy.transform : null,
                    CasterAnimator = animator,
                    Camera = view,
                    CasterHome = home,
                    EffectParent = EffectRoot,
                    Hit = hit,
                    AnimationEvents = relay,
                    Accent = accent
                };

                var background = new List<Coroutine>();
                var running = new HashSet<int>();
                float deadline = Time.time + maxSequenceSeconds;
                int index = 0;
                foreach (AttackStep step in sequence.Steps)
                {
                    if (step == null) continue;
                    int id = index++;
                    running.Add(id);
                    IEnumerator routine = RunStep(step, context, () => running.Remove(id));
                    if (step.WaitForCompletion)
                    {
                        Coroutine c = StartCoroutine(routine);
                        while (running.Contains(id) && Time.time < deadline) yield return null;
                    }
                    else
                    {
                        background.Add(StartCoroutine(routine));
                    }
                }

                // Let background steps (projectiles in flight, effects) finish before walking back.
                while (running.Count > 0 && Time.time < deadline) yield return null;

                if (sequence.MoveToStage && (caster.position - home).sqrMagnitude > 0.0001f)
                    yield return Move(caster, animator, sequence.MovingBoolParameter, caster.position, home, sequence.MoveBackSeconds);
            }
            finally
            {
                caster.position = home;
                SetBool(animator, sequence.MovingBoolParameter, false);
                if (controllerWasEnabled && controller != null) controller.enabled = true;
                if (encounterCoordinator != null) encounterCoordinator.PlayerPlacementSuspended = false;
            }
        }

        private static IEnumerator RunStep(AttackStep step, AttackStepContext context, Action done)
        {
            if (step.Delay > 0f) yield return new WaitForSeconds(step.Delay);
            IEnumerator inner = null;
            try
            {
                inner = step.Run(context);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            while (inner != null)
            {
                bool moved;
                try
                {
                    moved = inner.MoveNext();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    break;
                }

                if (!moved) break;
                yield return inner.Current;
            }

            done();
        }

        private static IEnumerator Move(Transform caster, Animator animator, string movingBool, Vector3 from, Vector3 to, float seconds)
        {
            SetBool(animator, movingBool, true);
            if (seconds > 0f)
            {
                float elapsed = 0f;
                while (elapsed < seconds)
                {
                    elapsed += Time.deltaTime;
                    caster.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / seconds)));
                    yield return null;
                }
            }

            caster.position = to;
            SetBool(animator, movingBool, false);
        }

        // The point on the character's ground plane that the camera shows at the given viewport position.
        private static bool TryStagePosition(Camera view, Vector2 viewport, float height, out Vector3 position)
        {
            position = default;
            if (view == null) return false;
            Ray ray = view.ViewportPointToRay(new Vector3(Mathf.Clamp01(viewport.x), Mathf.Clamp01(viewport.y), 0f));
            Plane plane = new(Vector3.up, new Vector3(0f, height, 0f));
            if (!plane.Raycast(ray, out float distance)) return false;
            position = ray.GetPoint(distance);
            position.y = height;
            return true;
        }

        private static void SetBool(Animator animator, string parameter, bool value)
        {
            if (animator == null || string.IsNullOrWhiteSpace(parameter)) return;
            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (p.type != AnimatorControllerParameterType.Bool || p.name != parameter) continue;
                animator.SetBool(parameter, value);
                return;
            }
        }

        private Transform EffectRoot
        {
            get
            {
                if (effectRoot == null)
                {
                    effectRoot = new GameObject("Attack Effects").transform;
                    effectRoot.SetParent(transform, false);
                }

                return effectRoot;
            }
        }

        private void ResolveReferences()
        {
            if (encounterCoordinator == null) encounterCoordinator = GetComponent<CombatEncounterCoordinator>();
            if (lanePresentation == null) lanePresentation = GetComponent<CombatLanePresentation3D>();
        }
    }
}
