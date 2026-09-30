using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>An enemy note on the board, as the build runtime sees it.</summary>
    public readonly struct BoardNote
    {
        /// <summary>The note itself (opaque to the runtime).</summary>
        public readonly object Handle;
        public readonly string NoteId;
        /// <summary>Lane (key identity) the note travels in.</summary>
        public readonly int Lane;
        /// <summary>Seconds until the note reaches the hit line (negative once it passed).</summary>
        public readonly float SecondsUntilHit;
        public readonly int Damage;
        public readonly Vector3 Position;

        public BoardNote(object handle, string noteId, int lane, float secondsUntilHit, int damage, Vector3 position)
        {
            Handle = handle;
            NoteId = noteId;
            Lane = lane;
            SecondsUntilHit = secondsUntilHit;
            Damage = damage;
            Position = position;
        }
    }

    /// <summary>
    /// The rhythm board during an enemy turn, for effects that act on incoming notes (Storm Ward zaps, Stone Wall).
    /// Cleared notes resolve as <see cref="NoteResolutionSource.Modifier"/>: no damage, no combo, no counter charges.
    /// Implemented over the pattern runner in play; tests use a fake.
    /// </summary>
    public interface ICombatNoteBoard
    {
        /// <summary>True while an enemy-defense pattern runs.</summary>
        bool IsDefending { get; }
        /// <summary>Changes every time an enemy-defense pattern (attack step) starts.</summary>
        int PatternSerial { get; }
        /// <summary>
        /// Unresolved notes that can be cleared (not holds, mashes or rally shots), soonest first, reaching the hit line
        /// within <paramref name="withinSeconds"/>.
        /// </summary>
        IReadOnlyList<BoardNote> Upcoming(float withinSeconds);
        /// <summary>The lane with the most notes still to come in the running pattern (0 = none).</summary>
        int BusiestLane();
        /// <summary>Clears a note (no damage). False when it was already resolved.</summary>
        bool Clear(BoardNote note);
        /// <summary>Visual: a lightning arc between two points.</summary>
        void ShowArc(Vector3 from, Vector3 to, Color color);
    }
}
