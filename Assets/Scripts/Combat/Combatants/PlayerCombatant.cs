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

        public int MaxHealth => maxHealth;
        public int CurrentHealth => currentHealth;
        public int MaxMana => maxMana;
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
            currentHealth = Mathf.Clamp(battleStartSnapshot.Health, 0, maxHealth);
            currentMana = Mathf.Clamp(battleStartSnapshot.Mana, 0, maxMana);
            defeatRaised = false;
            HealthChanged?.Invoke(currentHealth, maxHealth);
            ManaChanged?.Invoke(currentMana, maxMana);
        }

        public int ApplyDamage(int amount)
        {
            int previous = currentHealth;
            currentHealth = Mathf.Clamp(currentHealth - Mathf.Max(0, amount), 0, maxHealth);
            int applied = previous - currentHealth;
            if (applied > 0)
            {
                Damaged?.Invoke(applied);
                HealthChanged?.Invoke(currentHealth, maxHealth);
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
            currentHealth = Mathf.Clamp(currentHealth + Mathf.Max(0, amount), 0, maxHealth);
            int applied = currentHealth - previous;
            if (applied > 0)
            {
                Healed?.Invoke(applied);
                HealthChanged?.Invoke(currentHealth, maxHealth);
            }
            return applied;
        }

        public bool SpendMana(int amount)
        {
            int cost = Mathf.Max(0, amount);
            if (currentMana < cost) return false;
            currentMana -= cost;
            ManaChanged?.Invoke(currentMana, maxMana);
            return true;
        }

        /// <summary>Restores mana (clamped to the maximum). Returns the amount actually gained.</summary>
        public int GainMana(int amount)
        {
            int previous = currentMana;
            currentMana = Mathf.Clamp(currentMana + Mathf.Max(0, amount), 0, maxMana);
            int gained = currentMana - previous;
            if (gained > 0) ManaChanged?.Invoke(currentMana, maxMana);
            return gained;
        }

        public void ResetToMaximum()
        {
            currentHealth = maxHealth;
            currentMana = maxMana;
            defeatRaised = false;
            HealthChanged?.Invoke(currentHealth, maxHealth);
            ManaChanged?.Invoke(currentMana, maxMana);
        }

        private void ClampStats()
        {
            maxHealth = Mathf.Max(1, maxHealth);
            maxMana = Mathf.Max(0, maxMana);
            currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
            currentMana = Mathf.Clamp(currentMana, 0, maxMana);
        }

        private readonly struct Snapshot
        {
            public readonly int Health;
            public readonly int Mana;
            public Snapshot(int health, int mana) { Health = health; Mana = mana; }
        }
    }
}
