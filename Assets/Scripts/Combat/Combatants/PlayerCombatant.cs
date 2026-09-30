using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace RythmRPG.Combat
{
    public sealed class PlayerCombatant : MonoBehaviour
    {
        [FormerlySerializedAs("PlayerMaxHealth"), SerializeField, Min(1)] private int maxHealth = 1000;
        [FormerlySerializedAs("PlayerCurrentHealth"), SerializeField] private int currentHealth = 1000;
        [SerializeField, Min(0)] private int maxMana = 100;
        [SerializeField] private int currentMana = 100;

        private bool defeatRaised;
        private Snapshot battleStartSnapshot;
        private int maxHealthOverride;
        private int maxManaOverride;

        /// <summary>Max health as authored on the component (before build passives).</summary>
        public int BaseMaxHealth => maxHealth;
        /// <summary>Effective max health (a build may raise or lower it; see <see cref="SetMaxHealthOverride"/>).</summary>
        public int MaxHealth => maxHealthOverride > 0 ? maxHealthOverride : maxHealth;
        public int CurrentHealth => currentHealth;
        /// <summary>Max mana as authored on the component (before growth rewards).</summary>
        public int BaseMaxMana => maxMana;
        /// <summary>Effective max mana (growth rewards raise it; see <see cref="SetMaxManaOverride"/>).</summary>
        public int MaxMana => maxManaOverride > 0 ? maxManaOverride : maxMana;
        public int CurrentMana => currentMana;
        public bool IsDefeated => currentHealth <= 0;
        public event Action<int, int> HealthChanged;
        public event Action<int, int> ManaChanged;
        public event Action Defeated;
        public event Action<int> Damaged;
        public event Action<int> Healed;

        private void Awake() => ClampStats();

        public void CaptureBattleStart()
        {
            battleStartSnapshot = new Snapshot(currentHealth, currentMana);
            defeatRaised = IsDefeated;
        }

        public void RestoreBattleStart()
        {
            currentHealth = Mathf.Clamp(battleStartSnapshot.Health, 0, MaxHealth);
            currentMana = Mathf.Clamp(battleStartSnapshot.Mana, 0, MaxMana);
            defeatRaised = false;
            HealthChanged?.Invoke(currentHealth, MaxHealth);
            ManaChanged?.Invoke(currentMana, MaxMana);
        }

        public int ApplyDamage(int amount)
        {
            int previous = currentHealth;
            currentHealth = Mathf.Clamp(currentHealth - Mathf.Max(0, amount), 0, MaxHealth);
            int applied = previous - currentHealth;
            if (applied > 0)
            {
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

        public int Heal(int amount)
        {
            int previous = currentHealth;
            currentHealth = Mathf.Clamp(currentHealth + Mathf.Max(0, amount), 0, MaxHealth);
            int applied = currentHealth - previous;
            if (applied > 0)
            {
                Healed?.Invoke(applied);
                HealthChanged?.Invoke(currentHealth, MaxHealth);
            }
            return applied;
        }

        /// <summary>
        /// Build max health (0 = back to the authored value). Current health keeps its missing amount when the max
        /// grows and is clamped when it shrinks; <paramref name="fill"/> restores to full.
        /// </summary>
        public void SetMaxHealthOverride(int value, bool fill = false)
        {
            int previousMax = MaxHealth;
            maxHealthOverride = Mathf.Max(0, value);
            int newMax = MaxHealth;
            if (fill) currentHealth = newMax;
            else if (newMax > previousMax && currentHealth > 0) currentHealth = Mathf.Min(newMax, currentHealth + (newMax - previousMax));
            currentHealth = Mathf.Clamp(currentHealth, 0, newMax);
            if (previousMax != newMax || fill) HealthChanged?.Invoke(currentHealth, newMax);
        }

        /// <summary>
        /// Run max mana (0 = back to the authored value). Growing the max adds the difference to current mana; shrinking
        /// clamps it.
        /// </summary>
        public void SetMaxManaOverride(int value)
        {
            int previousMax = MaxMana;
            maxManaOverride = Mathf.Max(0, value);
            int newMax = MaxMana;
            if (newMax > previousMax) currentMana += newMax - previousMax;
            currentMana = Mathf.Clamp(currentMana, 0, newMax);
            if (previousMax != newMax) ManaChanged?.Invoke(currentMana, newMax);
        }

        public bool SpendMana(int amount)
        {
            int cost = Mathf.Max(0, amount);
            if (currentMana < cost) return false;
            currentMana -= cost;
            ManaChanged?.Invoke(currentMana, MaxMana);
            return true;
        }

        /// <summary>Restores mana (clamped to the maximum). Returns the amount actually gained.</summary>
        public int GainMana(int amount)
        {
            int previous = currentMana;
            currentMana = Mathf.Clamp(currentMana + Mathf.Max(0, amount), 0, MaxMana);
            int gained = currentMana - previous;
            if (gained > 0) ManaChanged?.Invoke(currentMana, MaxMana);
            return gained;
        }

        public void ResetToMaximum()
        {
            currentHealth = MaxHealth;
            currentMana = MaxMana;
            defeatRaised = false;
            HealthChanged?.Invoke(currentHealth, MaxHealth);
            ManaChanged?.Invoke(currentMana, MaxMana);
        }

        private void ClampStats()
        {
            maxHealth = Mathf.Max(1, maxHealth);
            maxMana = Mathf.Max(0, maxMana);
            currentHealth = Mathf.Clamp(currentHealth, 0, MaxHealth);
            currentMana = Mathf.Clamp(currentMana, 0, MaxMana);
        }

        private readonly struct Snapshot
        {
            public readonly int Health;
            public readonly int Mana;
            public Snapshot(int health, int mana) { Health = health; Mana = mana; }
        }
    }
}
