using RythmRPG.Combat;
using RythmRPG.Dialogue;
using UnityEngine;

// Dialogue System sequencer command (found by class name):
//   StartBattle()              fight the enemy in this conversation (speaker or listener) once the conversation ends
//   StartBattle(WHO)           a named subject (GameObject name, speaker, listener, tag=X)
//   StartBattle(WHO, true)     ... with the "!" notice
namespace PixelCrushers.DialogueSystem.SequencerCommands
{
    public class SequencerCommandStartBattle : SequencerCommand
    {
        private void Awake()
        {
            string subject = GetParameter(0);
            EnemyCombatant enemy = string.IsNullOrEmpty(subject)
                ? DialogueBattleStarter.EnemyAmong(speaker, listener)
                : DialogueBattleStarter.FindEnemy(GetSubject(0, listener));
            string notice = GetParameter(1);
            bool? showNotice = string.IsNullOrEmpty(notice) ? null : notice.Trim().ToLowerInvariant() == "true";
            DialogueBattleStarter.Request(enemy, showNotice);
            Stop();
        }
    }
}
