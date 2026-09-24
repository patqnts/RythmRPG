using System;

namespace RythmRPG.LevelComposer.Simulation
{
    public enum Judgement
    {
        None,
        Perfect,
        Good,
        Bad,
        Miss
    }

    /// <summary>
    /// Moving-note timing windows in seconds (± from the beat), same meaning as the game's JudgementConfig:
    /// Perfect up to <see cref="Perfect"/>, Good up to <see cref="Good"/>, Bad up to <see cref="Bad"/>; a press between
    /// Bad and <see cref="Miss"/> uses the note up as a Miss; further away presses are ignored.
    /// Defaults match Resources/Combat/Judgement/JudgementConfig.asset (45 / 90 / 125 / 180 ms).
    /// </summary>
    public sealed class JudgementWindows
    {
        public double Perfect = 0.045d;
        public double Good = 0.090d;
        public double Bad = 0.125d;
        public double Miss = 0.180d;
        public bool PressInMissRangeCountsAsMiss = true;

        public Judgement Evaluate(double errorSeconds)
        {
            double e = Math.Abs(errorSeconds);
            if (e <= Perfect) return Judgement.Perfect;
            if (e <= Good) return Judgement.Good;
            if (e <= Bad) return Judgement.Bad;
            return Judgement.Miss;
        }

        public void Sanitize()
        {
            Perfect = Math.Max(0.001d, Perfect);
            Good = Math.Max(Perfect, Good);
            Bad = Math.Max(Good, Bad);
            Miss = Math.Max(Bad, Miss);
        }
    }

    /// <summary>Running results of a preview play-through.</summary>
    public sealed class SimStats
    {
        public int Perfect, Good, Bad, Miss;
        public int Combo, MaxCombo;
        public int DamageTaken;
        public int Presses;

        public int Judged { get { return Perfect + Good + Bad + Miss; } }

        /// <summary>0..1, weights Perfect 1, Good 0.75, Bad 0.4, Miss 0.</summary>
        public double Accuracy
        {
            get
            {
                int n = Judged;
                return n == 0 ? 1d : (Perfect + Good * 0.75d + Bad * 0.4d) / n;
            }
        }

        public void Add(Judgement j, int damage)
        {
            switch (j)
            {
                case Judgement.Perfect: Perfect++; break;
                case Judgement.Good: Good++; break;
                case Judgement.Bad: Bad++; break;
                case Judgement.Miss: Miss++; break;
                default: return;
            }

            if (j == Judgement.Miss) Combo = 0;
            else
            {
                Combo++;
                if (Combo > MaxCombo) MaxCombo = Combo;
            }

            DamageTaken += Math.Max(0, damage);
        }

        public void Reset()
        {
            Perfect = Good = Bad = Miss = Combo = MaxCombo = DamageTaken = Presses = 0;
        }
    }
}
