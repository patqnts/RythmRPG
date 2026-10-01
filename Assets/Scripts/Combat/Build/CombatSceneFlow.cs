using RythmRPG.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RythmRPG.Combat
{
    /// <summary>Run data belongs to the run; scene-local controllers and players are recreated by Unity.</summary>
    public static class CombatSceneFlow
    {
        private static RunBuildState previousBuild;
        private static bool resetBuild;
        private static bool haveResources;
        private static int health, mana, maxHealth, maxMana;
        private static bool priorLoadoutAllowed;
        private static bool activeTransition;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { previousBuild = null; activeTransition = resetBuild = haveResources = false; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            GameSceneLoader.CanTransition -= Guard;
            GameSceneLoader.CanTransition += Guard;
            GameSceneLoader.TransitionStarted -= Started; GameSceneLoader.TransitionStarted += Started;
            GameSceneLoader.PreparingActivation -= Preparing; GameSceneLoader.PreparingActivation += Preparing;
            GameSceneLoader.DestinationReady -= Ready; GameSceneLoader.DestinationReady += Ready;
            GameSceneLoader.TransitionCompleted -= Completed; GameSceneLoader.TransitionCompleted += Completed;
            GameSceneLoader.TransitionFailed -= Failed; GameSceneLoader.TransitionFailed += Failed;
            SceneManager.sceneLoaded -= Loaded; SceneManager.sceneLoaded += Loaded;
            ApplyScenePolicy();
        }
        private static string Guard(SceneLoadRequest request)
        {
            if (request.Intent == SceneLoadIntent.ReturnToMenu) return null;
            foreach (var combat in Object.FindObjectsByType<CombatController>())
                if (combat.IsBattleActive) return "Finish the battle before leaving this area.";
            if (request.Intent == SceneLoadIntent.NewGame) return SceneRunSettings.CreateNewRun(false).LoadoutProblem();
            return null;
        }
        private static void Started(SceneLoadRequest request)
        {
            activeTransition = true;
            priorLoadoutAllowed = LoadoutPanel.Allowed;
            LoadoutPanel.Allowed = false;
            if (LoadoutPanel.IsOpen) LoadoutPanel.Instance.Close();
            if (request.Intent == SceneLoadIntent.ReturnToMenu)
                foreach (var combat in Object.FindObjectsByType<CombatController>()) combat.CancelBattle();
            previousBuild = RunBuild.Current;
            resetBuild = false;
            var player = Object.FindAnyObjectByType<PlayerCombatant>();
            haveResources = request.Intent == SceneLoadIntent.Travel && !request.Destination.isMenu && player != null;
            if (haveResources)
            { health = player.CurrentHealth; mana = player.CurrentMana; maxHealth = player.MaxHealth; maxMana = player.MaxMana; }
        }
        private static void Preparing(SceneLoadRequest request)
        {
            if (request.Intent != SceneLoadIntent.NewGame) return;
            resetBuild = true;
            RunBuild.Current = SceneRunSettings.CreateNewRun();
        }
        private static void Ready(SceneLoadRequest request)
        {
            if (!haveResources) return;
            foreach (var player in Object.FindObjectsByType<PlayerCombatant>())
                if (player.gameObject.scene == SceneManager.GetActiveScene())
                {
                    player.SetMaxHealthOverride(maxHealth);
                    player.SetMaxManaOverride(maxMana);
                    player.RestoreResources(health, mana);
                    break;
                }
        }
        private static void Completed(SceneLoadRequest request) { previousBuild = null; activeTransition = resetBuild = haveResources = false; ApplyScenePolicy(); }
        private static void Failed(SceneLoadRequest request, string reason)
        {
            // Rejected requests during another transition must not undo its state.
            if (GameSceneLoader.IsLoading || !activeTransition) return;
            if (resetBuild) RunBuild.Current = previousBuild;
            previousBuild = null; activeTransition = resetBuild = haveResources = false;
            LoadoutPanel.Allowed = priorLoadoutAllowed;
        }
        private static void Loaded(Scene scene, LoadSceneMode mode) { if (!GameSceneLoader.IsLoading) ApplyScenePolicy(); }
        private static void ApplyScenePolicy()
        {
            var catalog = Resources.Load<GameSceneCatalog>(GameSceneCatalog.ResourcePath);
            var active = catalog != null ? catalog.ForPath(SceneManager.GetActiveScene().path) : null;
            LoadoutPanel.Allowed = active == null || !active.isMenu;
        }
    }
}
