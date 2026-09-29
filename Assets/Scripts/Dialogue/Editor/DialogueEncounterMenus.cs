using System.Collections.Generic;
using PixelCrushers.DialogueSystem;
using RythmRPG.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RythmRPG.Dialogue.EditorTools
{
    /// <summary>
    /// Sets enemies up so their battles start through the Dialogue System (see <see cref="DialogueEncounter"/>).
    /// </summary>
    public static class DialogueEncounterMenus
    {
        private const string Root = "Tools/Rythm RPG/Dialogue/Encounters/";

        [MenuItem(Root + "Talk Before Battle (Selected Enemies)", priority = 240)]
        private static void TalkFirst() => Convert(DialogueEncounter.EncounterMode.TalkFirst);

        [MenuItem(Root + "Ambush When Touched (Selected Enemies)", priority = 241)]
        private static void Ambush() => Convert(DialogueEncounter.EncounterMode.Ambush);

        [MenuItem(Root + "Talk Before Battle (Selected Enemies)", true)]
        [MenuItem(Root + "Ambush When Touched (Selected Enemies)", true)]
        private static bool HasSelection() => Selection.activeGameObject != null;

        [MenuItem(Root + "Turn Off Touch-To-Fight On Player", priority = 260)]
        private static void TurnOffTouchMenu()
        {
            if (TurnOffTouchToFight() == 0)
                EditorUtility.DisplayDialog("Encounters", "No PlayerEnemyInteractor3D found in the open scenes.", "OK");
        }

        private static void Convert(DialogueEncounter.EncounterMode mode)
        {
            var enemies = new List<EnemyCombatant>();
            bool skippedAssets = false;
            foreach (GameObject selected in Selection.gameObjects)
            {
                if (EditorUtility.IsPersistent(selected))
                {
                    skippedAssets = true;
                    continue;
                }
                EnemyCombatant enemy = selected.GetComponentInParent<EnemyCombatant>(true);
                if (enemy == null) enemy = selected.GetComponentInChildren<EnemyCombatant>(true);
                if (enemy != null && !enemies.Contains(enemy)) enemies.Add(enemy);
            }
            if (enemies.Count == 0)
            {
                EditorUtility.DisplayDialog("Encounters", skippedAssets
                    ? "Select the enemy in the scene (or open its prefab), not the prefab asset in the Project window."
                    : "Select an enemy (an object with EnemyCombatant) in the scene.", "OK");
                return;
            }

            Undo.SetCurrentGroupName(mode == DialogueEncounter.EncounterMode.TalkFirst ? "Talk Before Battle" : "Ambush When Touched");
            int group = Undo.GetCurrentGroup();

            foreach (EnemyCombatant enemy in enemies) Setup(enemy, mode);
            TurnOffTouchToFight();

            Undo.CollapseUndoOperations(group);
        }

        private static void Setup(EnemyCombatant enemy, DialogueEncounter.EncounterMode mode)
        {
            GameObject go = enemy.gameObject;
            bool talk = mode == DialogueEncounter.EncounterMode.TalkFirst;

            EnsureTriggerCollider(go);

            DialogueSystemTrigger trigger = go.GetComponent<DialogueSystemTrigger>();
            if (trigger == null) trigger = Undo.AddComponent<PixelCrushers.DialogueSystem.Wrappers.DialogueSystemTrigger>(go);
            Undo.RecordObject(trigger, "Dialogue Encounter");
            trigger.trigger = DialogueSystemTriggerEvent.OnUse;
            if (trigger.conversationConversant == null) trigger.conversationConversant = go.transform;
            string moved = null;
            if (string.IsNullOrEmpty(trigger.conversation))
            {
                moved = MoveEncounterStartConversation(enemy);
                if (moved != null) trigger.conversation = moved;
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(trigger);

            DialogueEncounter encounter = go.GetComponent<DialogueEncounter>();
            if (encounter == null) encounter = Undo.AddComponent<DialogueEncounter>(go);
            Undo.RecordObject(encounter, "Dialogue Encounter");
            encounter.mode = mode;
            encounter.battleWhenTriggered = true;
            // An ambush with nothing to say gets the classic "!" moment; after a conversation it would be odd.
            encounter.showNotice = !talk && string.IsNullOrEmpty(trigger.conversation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(encounter);

            Usable usable = go.GetComponent<Usable>();
            if (talk)
            {
                if (usable == null) usable = Undo.AddComponent<PixelCrushers.DialogueSystem.Wrappers.Usable>(go);
                else if (!usable.enabled)
                {
                    Undo.RecordObject(usable, "Dialogue Encounter");
                    usable.enabled = true;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(usable);
                }
            }
            else if (usable != null && usable.enabled)
            {
                // Otherwise Interact would also start the conversation.
                Undo.RecordObject(usable, "Dialogue Encounter");
                usable.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(usable);
            }

            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);

            string conversation = string.IsNullOrEmpty(trigger.conversation)
                ? "no conversation yet (pick one in its Dialogue System Trigger)"
                : $"conversation '{trigger.conversation}'" + (moved != null ? " (moved from its Encounter Start combat dialogue)" : "");
            Debug.Log($"[Encounters] {go.name}: {(talk ? "talk before battle" : "ambush when touched")}, {conversation}. " +
                      "The battle starts when the conversation ends.", go);
        }

        private static void EnsureTriggerCollider(GameObject go)
        {
            foreach (Collider existing in go.GetComponents<Collider>())
                if (existing.isTrigger) return;
            var sphere = Undo.AddComponent<SphereCollider>(go);
            sphere.isTrigger = true;
            sphere.radius = 1.2f;
        }

        /// <summary>
        /// The pre-fight talk now plays in the world, before the battle: take this enemy's Encounter Start combat
        /// dialogue (its EnemyCombatDialogue, or a Combat Dialogue Set for its enemy type) so it doesn't play twice.
        /// </summary>
        private static string MoveEncounterStartConversation(EnemyCombatant enemy)
        {
            EnemyCombatDialogue own = enemy.GetComponentInParent<EnemyCombatDialogue>(true);
            if (own != null && TryTake(own.entries, own, out string fromComponent)) return fromComponent;

            EnemyDefinition definition = enemy.Definition;
            if (definition == null) return null;
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(CombatDialogueSet)))
            {
                var set = AssetDatabase.LoadAssetAtPath<CombatDialogueSet>(AssetDatabase.GUIDToAssetPath(guid));
                if (set == null || set.enemy != definition) continue;
                if (!TryTake(set.entries, set, out string fromSet)) continue;
                AssetDatabase.SaveAssetIfDirty(set);
                return fromSet;
            }
            return null;
        }

        private static bool TryTake(List<CombatDialogueEntry> entries, Object owner, out string conversation)
        {
            conversation = null;
            if (entries == null) return false;
            foreach (CombatDialogueEntry entry in entries)
            {
                if (entry == null || entry.moment != CombatStoryMoment.EncounterStart
                    || string.IsNullOrWhiteSpace(entry.conversation)) continue;
                Undo.RecordObject(owner, "Dialogue Encounter");
                entries.Remove(entry);
                EditorUtility.SetDirty(owner);
                conversation = entry.conversation;
                Debug.Log($"[Encounters] Removed the Encounter Start entry '{conversation}' from {owner.name}: " +
                          "it now plays before the battle, from the enemy's Dialogue System Trigger.", owner);
                return true;
            }
            return false;
        }

        /// <summary>Battles now start from the Dialogue System, not from walking into enemies.</summary>
        private static int TurnOffTouchToFight()
        {
            int changed = 0;
            foreach (PlayerEnemyInteractor3D interactor in
                     Object.FindObjectsByType<PlayerEnemyInteractor3D>(FindObjectsInactive.Include))
            {
                if (EditorUtility.IsPersistent(interactor)) continue;
                var so = new SerializedObject(interactor);
                SerializedProperty overlap = so.FindProperty("useOverlapCheck");
                SerializedProperty touch = so.FindProperty("beginBattleOnTriggerEnter");
                if (overlap != null) overlap.boolValue = false;
                if (touch != null) touch.boolValue = false;
                if (so.ApplyModifiedProperties())
                {
                    changed++;
                    Debug.Log($"[Encounters] {interactor.name}: turned off touch-to-fight (Use Overlap Check, Begin Battle " +
                              "On Trigger Enter). Battles now start from enemies' Dialogue Encounters.", interactor);
                }
                else changed++;
            }
            return changed;
        }
    }
}
