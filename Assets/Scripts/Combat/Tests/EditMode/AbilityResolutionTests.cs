using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace RythmRPG.Combat.Tests
{
    public sealed class AbilityResolutionTests
    {
        [Serializable]
        private sealed class CountingAmount : AbilityEffect
        {
            public int Total = 100;
            public readonly List<int> Applied = new();
            public override bool IsAmount => true;
            public override int TotalAmount(AbilityEffectContext context) => Total;
            public override void ApplyAmount(AbilityEffectContext context, int amount) => Applied.Add(amount);
        }

        [Serializable]
        private sealed class CountingOnce : AbilityEffect
        {
            public int Calls;
            public override void ApplyOnce(AbilityEffectContext context) => Calls++;
        }

        [Test]
        public void AmountIsSplitByWeight_AndSumsExactly()
        {
            var damage = new CountingAmount();
            var resolution = new AbilityResolution(new AbilityEffectContext(), new AbilityEffect[] { damage }, 3f);
            resolution.Hit(1f);
            resolution.Hit(1f);
            resolution.Hit(1f);
            resolution.Finish();
            Assert.That(damage.Applied, Is.EqualTo(new[] { 33, 34, 33 }));
        }

        [Test]
        public void UnevenWeights_GiveUnevenShares()
        {
            var damage = new CountingAmount();
            var resolution = new AbilityResolution(new AbilityEffectContext(), new AbilityEffect[] { damage }, 4f);
            resolution.Hit(1f);
            resolution.Hit(1f);
            resolution.Hit(2f);
            Assert.That(damage.Applied, Is.EqualTo(new[] { 25, 25, 50 }));
        }

        [Test]
        public void Finish_DeliversWhatIsLeft_WhenHitsWereCutShort()
        {
            var damage = new CountingAmount();
            var resolution = new AbilityResolution(new AbilityEffectContext(), new AbilityEffect[] { damage }, 5f);
            resolution.Hit(1f);
            resolution.Finish();
            Assert.That(damage.Applied, Is.EqualTo(new[] { 20, 80 }));
        }

        [Test]
        public void NoHits_EverythingLandsOnFinish_OnceEffectsOnce()
        {
            var damage = new CountingAmount();
            var once = new CountingOnce();
            var resolution = new AbilityResolution(new AbilityEffectContext(), new AbilityEffect[] { damage, once }, 0f);
            resolution.Finish();
            resolution.Finish();
            Assert.That(damage.Applied, Is.EqualTo(new[] { 100 }));
            Assert.That(once.Calls, Is.EqualTo(1));
        }

        [Test]
        public void LaneWard_BlocksBadAndMissOnlyInItsLanes_AndExpires()
        {
            var ward = new LaneWardModifier(new HashSet<int> { 2 }, 1);
            RhythmJudgementResult Result(int lane, HitJudgement j) => new("n", lane, j, 0f, Vector3.zero, NoteResolutionSource.PlayerInput);
            Assert.IsTrue(ward.BlocksDamage(Result(2, HitJudgement.Miss)));
            Assert.IsTrue(ward.BlocksDamage(Result(2, HitJudgement.Bad)));
            Assert.IsFalse(ward.BlocksDamage(Result(2, HitJudgement.Good)));
            Assert.IsFalse(ward.BlocksDamage(Result(3, HitJudgement.Miss)));
            ward.OnEnemyTurnEnded();
            Assert.IsTrue(ward.IsExpired);
            Assert.IsFalse(ward.BlocksDamage(Result(2, HitJudgement.Miss)));
        }

        [Test]
        public void ResourceRules_Defaults_WeighDamageAndMana()
        {
            CombatResourceRules rules = ScriptableObject.CreateInstance<CombatResourceRules>();
            Assert.That(rules.DefenseDamage(10, HitJudgement.Miss), Is.EqualTo(10));
            Assert.That(rules.DefenseDamage(10, HitJudgement.Bad), Is.EqualTo(5));
            Assert.That(rules.DefenseDamage(10, HitJudgement.Good), Is.EqualTo(0));
            Assert.That(rules.ManaGain(HitJudgement.Perfect, PatternRunMode.EnemyDefense), Is.EqualTo(1f));
            Assert.That(rules.ManaGain(HitJudgement.Perfect, PatternRunMode.PlayerAbility), Is.EqualTo(0.5f));
            Assert.That(rules.ManaGain(HitJudgement.Miss, PatternRunMode.EnemyDefense), Is.EqualTo(0f));
            UnityEngine.Object.DestroyImmediate(rules);
        }
    }
}
