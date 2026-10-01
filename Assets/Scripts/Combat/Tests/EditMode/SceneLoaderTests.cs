using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RythmRPG.Core;
using UnityEngine;

namespace RythmRPG.Combat.Tests
{
    public sealed class SceneLoaderTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<UnityEngine.Object> created = new();
        private sealed class Backend : IGameSceneBackend
        {
            public bool Available = true;
            public bool Throw;
            public string ActivePath => "Assets/Scenes/Menu.unity";
            public Operation Operation = new();
            public bool CanLoad(string path) => Available;
            public IGameSceneOperation Load(string path) => Throw ? throw new InvalidOperationException("Test loading failure") : Operation;
        }
        private sealed class Operation : IGameSceneOperation
        {
            public float Progress => .9f;
            public bool IsDone { get; private set; }
            public bool AllowActivation { set { if (value) IsDone = true; } }
        }
        [TearDown]
        public void Cleanup()
        {
            for (int i = created.Count - 1; i >= 0; i--) if (created[i] != null) UnityEngine.Object.DestroyImmediate(created[i]);
            created.Clear();
        }
        private GameSceneDefinition Definition(string id, string path)
        {
            var definition = ScriptableObject.CreateInstance<GameSceneDefinition>();
            created.Add(definition); definition.id = id;
            Set(definition, "scenePath", path);
            return definition;
        }
        private GameSceneLoader Loader(out Backend backend, out GameSceneCatalog catalog, out GameSceneDefinition destination)
        {
            destination = Definition("dungeon", "Assets/Scenes/Dungeon.unity");
            catalog = ScriptableObject.CreateInstance<GameSceneCatalog>(); created.Add(catalog);
            catalog.scenes.Add(destination); catalog.firstArea = destination;
            catalog.fadeInSeconds = catalog.fadeOutSeconds = catalog.minimumLoadingSeconds = 0;
            var host = new GameObject("Loader test"); host.SetActive(false); created.Add(host);
            var loader = host.AddComponent<GameSceneLoader>();
            backend = new Backend(); Set(loader, "catalog", catalog); Set(loader, "backend", backend);
            return loader;
        }
        [Test]
        public void Catalog_RequiresUniqueStableIdsAndScenePaths()
        {
            var loader = Loader(out _, out var catalog, out var dungeon);
            Assert.That(catalog.TryGet("dungeon", out var found), Is.True);
            Assert.That(found, Is.SameAs(dungeon));
            catalog.scenes.Add(Definition("dungeon", "Assets/Scenes/Grassland.unity"));
            Assert.That(catalog.TryGet("dungeon", out _), Is.False);
            Assert.That(catalog.Problem(dungeon), Does.Contain("unique ID"));
            catalog.scenes.RemoveAt(1); Set(dungeon, "scenePath", "");
            Assert.That(loader.Problem(new SceneLoadRequest(dungeon)), Does.Contain("scene asset"));
        }
        [Test]
        public void Loader_RejectsMissingScenesDuplicateLoadsAndTravelToCurrentScene()
        {
            var loader = Loader(out var backend, out _, out var dungeon);
            backend.Available = false;
            Assert.That(loader.Problem(new SceneLoadRequest(dungeon)), Does.Contain("not enabled"));
            backend.Available = true;
            Set(loader, "loading", true);
            Assert.That(loader.Problem(new SceneLoadRequest(dungeon)), Does.Contain("already loading"));
            Set(loader, "loading", false); Set(dungeon, "scenePath", backend.ActivePath);
            Assert.That(loader.Problem(new SceneLoadRequest(dungeon)), Does.Contain("Already in"));
            Assert.That(loader.Problem(new SceneLoadRequest(dungeon, intent: SceneLoadIntent.Reload)), Is.Null);
        }
        [Test]
        public void Loader_GuardRejectsBeforeInputOrScreenChanges()
        {
            var loader = Loader(out _, out _, out var dungeon);
            Func<SceneLoadRequest, string> guard = _ => "Battle in progress";
            GameSceneLoader.CanTransition += guard;
            try
            {
                Assert.That(loader.Request(new SceneLoadRequest(dungeon)), Is.False);
                Assert.That(loader.LastError, Is.EqualTo("Battle in progress"));
                Assert.That(GameInput.IsGameplayBlocked, Is.False);
                Assert.That(Get(loader, "view"), Is.Null);
            }
            finally { GameSceneLoader.CanTransition -= guard; }
        }
        [Test]
        public void DeferredSceneExit_QueuesWhileGuardedAndDeduplicatesTheSameRequest()
        {
            var loader = Loader(out _, out var catalog, out var dungeon);
            Func<SceneLoadRequest, string> guard = _ => "Conversation is closing";
            GameSceneLoader.CanTransition += guard;
            try
            {
                var request = new SceneLoadRequest(dungeon, "from-dialogue");
                Assert.That(loader.RequestWhenAvailable(request), Is.True);
                Assert.That(Get(loader, "pendingRequest"), Is.SameAs(request));
                Assert.That(loader.PendingReason, Is.EqualTo("Conversation is closing"));
                Assert.That(loader.LastError, Is.Null);

                Assert.That(loader.RequestWhenAvailable(new SceneLoadRequest(dungeon, "from-dialogue")), Is.True);
                Assert.That(Get(loader, "pendingRequest"), Is.SameAs(request));

                var grassland = Definition("grassland", "Assets/Scenes/Grassland.unity");
                catalog.scenes.Add(grassland);
                Assert.That(loader.RequestWhenAvailable(new SceneLoadRequest(grassland)), Is.False);
                Assert.That(loader.LastError, Does.Contain("already waiting"));
                Assert.That(Get(loader, "pendingRequest"), Is.SameAs(request));
            }
            finally { GameSceneLoader.CanTransition -= guard; }
        }
        [Test]
        public void Loader_BackendFailureClearsCoverAndInputLock()
        {
            var loader = Loader(out var backend, out var catalog, out var dungeon);
            backend.Throw = true;
            var view = SceneLoadingView.Create(loader.transform, catalog);
            Set(loader, "view", view); Set(loader, "loading", true); Set(loader, "locked", true);
            bool prior = GamePause.PauseAllowed;
            Set(loader, "previousPauseAllowed", prior); GamePause.PauseAllowed = false; GameInput.BlockGameplay(loader);
            var routine = (IEnumerator)typeof(GameSceneLoader).GetMethod("Run", Private).Invoke(loader, new object[] { new SceneLoadRequest(dungeon) });
            try
            {
                while (routine.MoveNext()) { }
                Assert.That(loader.LastError, Is.EqualTo("Test loading failure"));
                Assert.That(GameInput.IsGameplayBlocked, Is.False);
                Assert.That(GamePause.PauseAllowed, Is.EqualTo(prior));
                Assert.That(view.gameObject.activeSelf, Is.False);
            }
            finally { GameInput.UnblockGameplay(loader); GamePause.PauseAllowed = prior; }
        }
        [Test]
        public void PlayerResources_TransferWithoutDamageOrHealingEvents()
        {
            var go = new GameObject("Resource transfer test"); created.Add(go);
            var player = go.AddComponent<PlayerCombatant>();
            int effects = 0; player.Damaged += _ => effects++; player.Healed += _ => effects++;
            player.RestoreResources(400, 25);
            Assert.That(player.CurrentHealth, Is.EqualTo(400)); Assert.That(player.CurrentMana, Is.EqualTo(25));
            Assert.That(effects, Is.Zero);
            player.RestoreResources(int.MaxValue, -1);
            Assert.That(player.CurrentHealth, Is.EqualTo(player.MaxHealth)); Assert.That(player.CurrentMana, Is.Zero);
        }
        [Test]
        public void NewRun_UsesAuthoredLoadoutAndStartsFreshWithoutChangingCurrentRun()
        {
            var before = RunBuild.Current;
            var first = SceneRunSettings.CreateNewRun(false);
            var second = SceneRunSettings.CreateNewRun(false);
            Assert.That(first.LoadoutProblem(), Is.Null);
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.Depth, Is.Zero);
            Assert.That(first.BonusMaxMana, Is.Zero);
            Assert.That(RunBuild.Current, Is.SameAs(before));
        }
        [Test]
        public void Loader_ActivationAndReadyHappenUnderCoverThenInputUnlocks()
        {
            var loader = Loader(out var backend, out var catalog, out var dungeon);
            var view = SceneLoadingView.Create(loader.transform, catalog); view.Show("Dungeon");
            Set(loader, "view", view); Set(loader, "loading", true); Set(loader, "locked", true);
            bool prior = GamePause.PauseAllowed;
            Set(loader, "previousPauseAllowed", prior); GameInput.BlockGameplay(loader);
            var order = new List<string>();
            Action<SceneLoadRequest> prepare = _ => { Assert.That(view.Alpha, Is.EqualTo(1)); Assert.That(backend.Operation.IsDone, Is.False); order.Add("prepare"); };
            Action<SceneLoadRequest> ready = _ => { Assert.That(view.Alpha, Is.EqualTo(1)); Assert.That(GameInput.IsGameplayBlocked, Is.True); order.Add("ready"); };
            Action<SceneLoadRequest> complete = _ => { Assert.That(GameInput.IsGameplayBlocked, Is.False); order.Add("complete"); };
            GameSceneLoader.PreparingActivation += prepare; GameSceneLoader.DestinationReady += ready; GameSceneLoader.TransitionCompleted += complete;
            var existingEventSystem = UnityEngine.EventSystems.EventSystem.current;
            try
            {
                var routine = (IEnumerator)typeof(GameSceneLoader).GetMethod("Run", Private).Invoke(loader, new object[] { new SceneLoadRequest(dungeon) });
                int steps = 0; while (routine.MoveNext()) Assert.That(++steps, Is.LessThan(10));
                Assert.That(order, Is.EqualTo(new[] { "prepare", "ready", "complete" }));
                Assert.That(view.gameObject.activeSelf, Is.False);
                Assert.That(loader.Progress, Is.EqualTo(1));
            }
            finally
            {
                GameSceneLoader.PreparingActivation -= prepare; GameSceneLoader.DestinationReady -= ready; GameSceneLoader.TransitionCompleted -= complete;
                GameInput.UnblockGameplay(loader); GamePause.PauseAllowed = prior;
                if (existingEventSystem == null && UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.Object.DestroyImmediate(UnityEngine.EventSystems.EventSystem.current.gameObject);
            }
        }
        private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    }
}
