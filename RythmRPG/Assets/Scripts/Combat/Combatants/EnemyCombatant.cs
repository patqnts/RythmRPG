using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

namespace RythmRPG.Combat
{
    public sealed class EnemyCombatant : MonoBehaviour
    {
        [SerializeField] private EnemyDefinition definition;
        [FormerlySerializedAs("_maxHealth"), SerializeField, Min(1)] private int fallbackMaxHealth = 100;
        [FormerlySerializedAs("_currentHealth"), SerializeField] private int currentHealth = 100;
        [FormerlySerializedAs("_animator"), SerializeField] private Animator animator;
        [FormerlySerializedAs("_spriteRenderer"), SerializeField] private SpriteRenderer spriteRenderer;
        [FormerlySerializedAs("_noteGenerator"), SerializeField] private RhythmPatternRunner patternRunner;

        private bool defeatRaised;
        private int battleStartHealth;

        public EnemyDefinition Definition => definition;
        public int MaxHealth => definition != null ? definition.MaxHealth : Mathf.Max(1, fallbackMaxHealth);
        public int CurrentHealth => currentHealth;
        public bool IsDefeated => currentHealth <= 0;
        public Animator Animator => animator;
        public SpriteRenderer SpriteRenderer => spriteRenderer;
        public RhythmPatternRunner PatternRunner => patternRunner;
        public event Action<int, int> HealthChanged;
        public event Action<int> Damaged;
        public event Action Defeated;

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            if (patternRunner == null) patternRunner = GetComponent<RhythmPatternRunner>();
            currentHealth = Mathf.Clamp(currentHealth <= 0 ? MaxHealth : currentHealth, 0, MaxHealth);
        }

        public void CaptureBattleStart()
        {
            battleStartHealth = currentHealth;
            defeatRaised = IsDefeated;
        }

        public void RestoreBattleStart()
        {
            currentHealth = Mathf.Clamp(battleStartHealth, 1, MaxHealth);
            defeatRaised = false;
            gameObject.SetActive(true);
            if (spriteRenderer != null) spriteRenderer.enabled = true;
            HealthChanged?.Invoke(currentHealth, MaxHealth);
        }

        public int ApplyDamage(int amount)
        {
            int previous = currentHealth;
            currentHealth = Mathf.Clamp(currentHealth - Mathf.Max(0, amount), 0, MaxHealth);
            int applied = previous - currentHealth;
            if (applied > 0)
            {
                if (definition != null) PlayAnimation(definition.HitAnimationName);
                Damaged?.Invoke(applied);
                HealthChanged?.Invoke(currentHealth, MaxHealth);
            }
            if (IsDefeated && !defeatRaised)
            {
                defeatRaised = true;
                Defeated?.Invoke();
            }
            return applied;
        }

        public EnemyPhaseDefinition ResolvePhase()
        {
            IReadOnlyList<EnemyPhaseDefinition> phases = definition?.Phases;
            if (phases == null || phases.Count == 0) return null;
            float healthPercent = MaxHealth <= 0 ? 0f : (float)currentHealth / MaxHealth;
            return phases.Where(phase => phase != null && healthPercent <= phase.EnterAtHealthPercent)
                .OrderBy(phase => phase.EnterAtHealthPercent).FirstOrDefault()
                ?? phases.Where(phase => phase != null).OrderByDescending(phase => phase.EnterAtHealthPercent).FirstOrDefault();
        }

        public EnemyAttackSequenceDefinition SelectAttackSequence(float roll01)
        {
            IReadOnlyList<EnemyAttackSequenceDefinition> sequences = ResolvePhase()?.AttackSequences;
            if (sequences == null || sequences.Count == 0) return null;
            List<EnemyAttackSequenceDefinition> candidates = sequences.Where(sequence => sequence != null).ToList();
            if (candidates.Count == 0) return null;
            float choice = Mathf.Clamp01(roll01) * candidates.Sum(sequence => sequence.SelectionWeight);
            foreach (EnemyAttackSequenceDefinition candidate in candidates)
            {
                choice -= candidate.SelectionWeight;
                if (choice <= 0f) return candidate;
            }
            return candidates[^1];
        }

        public void PlayAnimation(string stateName)
        {
            if (animator == null || string.IsNullOrWhiteSpace(stateName)) return;
            int hash = Animator.StringToHash(stateName);
            if (animator.HasState(0, hash)) animator.Play(hash, 0, 0f);
        }

        public void HideAfterDefeat()
        {
            if (spriteRenderer != null) spriteRenderer.enabled = false;
        }
    }
}
