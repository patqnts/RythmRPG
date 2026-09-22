using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Collects everything the result screen shows while a battle runs. CombatController feeds it judgements,
    /// ability uses, turns and guards; damage, healing and mana are read from the combatants' events.
    /// </summary>
    public sealed class CombatStatsTracker
    {
        private readonly CombatResultGrading grading;
        private readonly CombatReport report = new();
        private PlayerCombatant player;
        private EnemyCombatant enemy;
        private int combo;
        private int lastMana;
        private double score;
        private float startTime;

        public int Combo => combo;
        public CombatReport Report => report;

        public CombatStatsTracker(CombatResultGrading grading) => this.grading = grading != null ? grading : CombatResultGrading.LoadOrDefault();

        public void Begin(PlayerCombatant playerCombatant, EnemyCombatant enemyCombatant)
        {
            Stop();
            player = playerCombatant;
            enemy = enemyCombatant;
            startTime = Time.unscaledTime;
            if (player != null)
            {
                lastMana = player.CurrentMana;
                player.Damaged += HandlePlayerDamaged;
                player.Healed += HandlePlayerHealed;
                player.ManaChanged += HandleManaChanged;
            }
            if (enemy != null) enemy.Damaged += HandleEnemyDamaged;
        }

        public void Stop()
        {
            if (player != null)
            {
                player.Damaged -= HandlePlayerDamaged;
                player.Healed -= HandlePlayerHealed;
                player.ManaChanged -= HandleManaChanged;
            }
            if (enemy != null) enemy.Damaged -= HandleEnemyDamaged;
            player = null;
            enemy = null;
        }

        public void RecordJudgement(HitJudgement judgement, PatternRunMode mode)
        {
            int[] counts = mode == PatternRunMode.PlayerAbility ? report.AbilityJudgements : report.DefenseJudgements;
            counts[(int)judgement]++;
            if (grading.BreaksCombo(judgement)) combo = 0;
            else combo++;
            report.MaxCombo = Mathf.Max(report.MaxCombo, combo);
            score += grading.NotePoints(judgement, combo);
        }

        public void RecordAbility(AbilityRuntimeInstance ability)
        {
            string name = ability?.Definition != null ? ability.Definition.DisplayName : "Ability";
            if (string.IsNullOrEmpty(name)) name = "Ability";
            report.AbilityUses.TryGetValue(name, out int uses);
            report.AbilityUses[name] = uses + 1;
        }

        public void RecordTurn() => report.Turns++;
        public void RecordGuard() => report.Guards++;

        /// <summary>Totals, accuracy, score and grade. Call before a defeat restores the player's health.</summary>
        public CombatReport Finish(bool victory, string enemyName)
        {
            report.Victory = victory;
            report.EnemyName = enemyName ?? string.Empty;
            report.BattleSeconds = Time.unscaledTime - startTime;
            report.HealthLeft = player != null && player.MaxHealth > 0 ? (float)player.CurrentHealth / player.MaxHealth : 0f;

            var all = new int[4];
            for (int i = 0; i < 4; i++) all[i] = report.DefenseJudgements[i] + report.AbilityJudgements[i];
            report.Accuracy = grading.Accuracy(all);
            report.DefenseAccuracy = grading.Accuracy(report.DefenseJudgements);
            report.AbilityAccuracy = grading.Accuracy(report.AbilityJudgements);

            int total = report.TotalNotes;
            int breaks = all[(int)HitJudgement.Miss] + (grading.BadBreaksCombo ? all[(int)HitJudgement.Bad] : 0);
            report.FullCombo = total > 0 && breaks == 0;
            report.AllPerfect = total > 0 && all[(int)HitJudgement.Perfect] == total;

            report.Score = (long)System.Math.Round(score) + grading.EndBonus(victory, report.HealthLeft);
            report.GradeValue = grading.GradeValue(report.Accuracy, report.HealthLeft);
            report.Grade = grading.GradeFor(report.GradeValue, report.FullCombo, victory);
            return report;
        }

        private void HandlePlayerDamaged(int amount) => report.DamageTaken += Mathf.Max(0, amount);
        private void HandlePlayerHealed(int amount) => report.Healed += Mathf.Max(0, amount);
        private void HandleEnemyDamaged(int amount) => report.DamageDealt += Mathf.Max(0, amount);

        private void HandleManaChanged(int current, int maximum)
        {
            if (current < lastMana) report.ManaSpent += lastMana - current;
            lastMana = current;
        }
    }
}
