using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    public enum HitJudgement { Miss, Bad, Good, Perfect }

    public readonly struct RhythmJudgementResult
    {
        public readonly string NoteId;
        public readonly int LaneId;
        public readonly HitJudgement Judgement;
        public readonly float Distance;
        public readonly Vector3 WorldPosition;
        public readonly NoteResolutionSource Source;

        public RhythmJudgementResult(string noteId, int laneId, HitJudgement judgement, float distance,
            Vector3 worldPosition, NoteResolutionSource source)
        {
            NoteId = noteId;
            LaneId = laneId;
            Judgement = judgement;
            Distance = distance;
            WorldPosition = worldPosition;
            Source = source;
        }
    }

    public readonly struct RhythmPerformanceResult
    {
        public readonly int ExpectedNoteCount;
        public readonly int ResolvedNoteCount;
        public readonly int MissCount;
        public readonly float AverageWeight;
        public readonly IReadOnlyList<RhythmJudgementResult> Judgements;

        public RhythmPerformanceResult(int expectedNoteCount, int resolvedNoteCount, int missCount,
            float averageWeight, IReadOnlyList<RhythmJudgementResult> judgements)
        {
            ExpectedNoteCount = expectedNoteCount;
            ResolvedNoteCount = resolvedNoteCount;
            MissCount = missCount;
            AverageWeight = averageWeight;
            Judgements = judgements;
        }
    }

    public static class RhythmPerformanceCalculator
    {
        public static RhythmPerformanceResult Calculate(int expectedNoteCount,
            IReadOnlyList<RhythmJudgementResult> results, AbilityOutcomeProfile profile)
        {
            int safeExpectedCount = Mathf.Max(0, expectedNoteCount);
            int resolvedCount = results?.Count ?? 0;
            int missCount = Mathf.Max(0, safeExpectedCount - resolvedCount);
            float total = 0f;
            if (results != null)
            {
                foreach (RhythmJudgementResult result in results)
                {
                    if (result.Judgement == HitJudgement.Miss) missCount++;
                    total += profile != null ? profile.GetWeight(result.Judgement) : DefaultWeight(result.Judgement);
                }
            }
            float average = safeExpectedCount == 0 ? 0f : total / safeExpectedCount;
            return new RhythmPerformanceResult(safeExpectedCount, resolvedCount, missCount, average, results);
        }

        public static int CalculatePower(int basePower, RhythmPerformanceResult performance) =>
            Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0, basePower) * performance.AverageWeight));

        private static float DefaultWeight(HitJudgement judgement) => judgement switch
        {
            HitJudgement.Bad => 0.5f,
            HitJudgement.Good => 0.8f,
            HitJudgement.Perfect => 1f,
            _ => 0f
        };
    }
}
