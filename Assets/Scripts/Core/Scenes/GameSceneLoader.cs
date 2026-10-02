using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RythmRPG.Core
{
    public enum SceneLoadIntent { Travel, NewGame, ReturnToMenu, Reload }
    public sealed class SceneLoadRequest
    {
        public GameSceneDefinition Destination { get; }
        public string EntryPoint { get; }
        public SceneLoadIntent Intent { get; }
        public SceneLoadRequest(GameSceneDefinition destination, string entryPoint = "", SceneLoadIntent intent = SceneLoadIntent.Travel)
        { Destination = destination; EntryPoint = entryPoint ?? string.Empty; Intent = intent; }
    }

    /// <summary>Separates transition flow from Unity loading so future backends and tests can reuse it.</summary>
    public interface IGameSceneOperation
    {
        float Progress { get; }
        bool IsDone { get; }
        bool AllowActivation { set; }
    }
    public interface IGameSceneBackend
    {
        bool CanLoad(string path);
        string ActivePath { get; }
        IGameSceneOperation Load(string path);
    }

    [DefaultExecutionOrder(-1100)]
    public sealed class GameSceneLoader : MonoBehaviour
    {
        public static GameSceneLoader Instance { get; private set; }
        public static bool IsLoading => Instance != null && Instance.loading;
        public static event Action<SceneLoadRequest> TransitionStarted;
        public static event Action<SceneLoadRequest> PreparingActivation;
        public static event Action<SceneLoadRequest> DestinationReady;
        public static event Action<SceneLoadRequest> TransitionCompleted;
        public static event Action<SceneLoadRequest, string> TransitionFailed;
        /// <summary>Return a reason to prevent travel, or null to allow it. Register game-specific checks here.</summary>
        public static event Func<SceneLoadRequest, string> CanTransition;

        [SerializeField] private GameSceneCatalog catalog;
        private IGameSceneBackend backend = new UnityBackend();
        private SceneLoadingView view;
        private ScenePlayerSpawn character;
        private ScenePlayerSpawn suspendedCharacter;
        private bool suspendedCharacterWasActive;
        private ulong characterAreaHandle;
        private bool loading;
        private bool previousPauseAllowed;
        private bool locked;
        private SceneLoadRequest pendingRequest;
        public GameSceneCatalog Catalog => catalog != null ? catalog : catalog = Resources.Load<GameSceneCatalog>(GameSceneCatalog.ResourcePath);
        public ScenePlayerSpawn Character => character;
        public float Progress { get; private set; }
        public string LastError { get; private set; }
        /// <summary>Why a deferred scene exit is waiting. Empty when no exit is queued.</summary>
        public string PendingReason { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (Instance != null) SceneManager.sceneLoaded -= Instance.OnSceneLoaded;
            Instance = null;
            TransitionStarted = PreparingActivation = DestinationReady = TransitionCompleted = null;
            TransitionFailed = null;
            CanTransition = null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() => Ensure();
        public static GameSceneLoader Ensure()
        {
            if (Instance != null) return Instance;
            var host = new GameObject("[Scene Loader]");
            DontDestroyOnLoad(host);
            return host.AddComponent<GameSceneLoader>();
        }
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ConfigureActiveScene();
        }
        private void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            ClearPending();
            Unlock(false);
            Instance = null;
        }
        private void Update()
        {
            if (pendingRequest == null || loading) return;
            SceneLoadRequest request = pendingRequest;
            string problem = IntrinsicProblem(request);
            if (problem != null)
            {
                ClearPending();
                Reject(request, problem);
                return;
            }
            string blocker = GuardProblem(request);
            if (!string.IsNullOrEmpty(blocker))
            {
                PendingReason = blocker;
                return;
            }
            ClearPending();
            Begin(request);
        }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { if (!loading) ConfigureActiveScene(); }
        private void ConfigureActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            var current = Catalog != null ? Catalog.ForPath(scene.path) : null;
            GamePause.PauseAllowed = current == null || !current.isMenu;
            if (current != null && current.isMenu && Catalog.createMenuControls) SceneTitleMenu.Ensure(Catalog);
            if (Application.isPlaying && !loading && current != null && !current.isMenu && characterAreaHandle != scene.handle.GetRawData())
                PrepareCharacterInScene(scene, null);
        }
        public bool Load(string sceneId, string entryPoint = "")
        {
            if (Catalog == null || !Catalog.TryGet(sceneId, out var scene)) return Reject(null, "Unknown scene ID: " + sceneId);
            return Request(TravelRequest(scene, entryPoint));
        }
        public SceneLoadRequest TravelRequest(GameSceneDefinition destination, string entryPoint = "") =>
            new SceneLoadRequest(destination, entryPoint,
                destination != null && destination.startsNewRun ? SceneLoadIntent.NewGame : SceneLoadIntent.Travel);
        public bool StartGame() => Request(new SceneLoadRequest(Catalog != null ? Catalog.firstArea : null, intent: SceneLoadIntent.NewGame));
        public bool ReturnToMenu() => Request(new SceneLoadRequest(Catalog != null ? Catalog.menu : null, intent: SceneLoadIntent.ReturnToMenu));
        public bool ReloadCurrent()
        {
            var scene = Catalog != null ? Catalog.ForPath(backend.ActivePath) : null;
            return Request(new SceneLoadRequest(scene, intent: SceneLoadIntent.Reload));
        }
        public string Problem(SceneLoadRequest request)
        {
            if (loading) return "A scene is already loading.";
            string problem = IntrinsicProblem(request);
            return problem ?? GuardProblem(request);
        }
        private string IntrinsicProblem(SceneLoadRequest request)
        {
            if (Catalog == null) return "Create Resources/Scenes/GameSceneCatalog first.";
            string problem = Catalog.Problem(request?.Destination);
            if (problem != null) return problem;
            if (!backend.CanLoad(request.Destination.Path)) return "Scene is not enabled in the build: " + request.Destination.Label;
            if (request.Intent == SceneLoadIntent.Travel && request.Destination.Path == backend.ActivePath) return "Already in " + request.Destination.Label + ".";
            return null;
        }
        private static string GuardProblem(SceneLoadRequest request)
        {
            string problem = null;
            if (CanTransition != null)
                foreach (Func<SceneLoadRequest, string> guard in CanTransition.GetInvocationList())
                {
                    try { problem = guard(request); }
                    catch (Exception e) { problem = e.Message; }
                    if (!string.IsNullOrEmpty(problem)) return problem;
                }
            return null;
        }
        public bool Request(SceneLoadRequest request)
        {
            string problem = Problem(request);
            if (problem != null) return Reject(request, problem);
            return Begin(request);
        }
        /// <summary>
        /// Accepts a valid request now and waits while a gameplay guard is temporarily blocking it. Scene exits use
        /// this so a UnityEvent fired on the final dialogue entry travels after the conversation has actually closed.
        /// Invalid destinations and requests made during an active scene transition still fail immediately.
        /// </summary>
        public bool RequestWhenAvailable(SceneLoadRequest request)
        {
            if (loading) return Reject(request, "A scene is already loading.");
            string problem = IntrinsicProblem(request);
            if (problem != null) return Reject(request, problem);
            string blocker = GuardProblem(request);
            if (string.IsNullOrEmpty(blocker)) return Begin(request);
            if (pendingRequest != null)
            {
                bool duplicate = pendingRequest.Destination == request.Destination
                                 && pendingRequest.EntryPoint == request.EntryPoint
                                 && pendingRequest.Intent == request.Intent;
                if (duplicate) return true;
                return Reject(request, "Another scene exit is already waiting.");
            }
            pendingRequest = request;
            PendingReason = blocker;
            LastError = null;
            return true;
        }
        private bool Begin(SceneLoadRequest request)
        {
            ClearPending();
            loading = true;
            LastError = null;
            Progress = 0;
            previousPauseAllowed = GamePause.PauseAllowed;
            locked = true;
            GamePause.PauseAllowed = false;
            GameInput.BlockGameplay(this);
            GameInput.CancelRebind();
            GamePause.Instance?.ResumeForSceneChange();
            view ??= SceneLoadingView.Create(transform, Catalog);
            view.Show(request.Destination.Label);
            Notify(TransitionStarted, request);
            StartCoroutine(Run(request));
            return true;
        }
        private void ClearPending()
        {
            pendingRequest = null;
            PendingReason = null;
        }
        private bool Reject(SceneLoadRequest request, string reason)
        {
            LastError = reason;
            NotifyFailure(request, reason);
            return false;
        }
        // Pump the transition so a backend failure cannot leave input locked or the screen black.
        private IEnumerator Run(SceneLoadRequest request)
        {
            IEnumerator transition = Transition(request);
            bool success = false;
            string failure = null;
            try
            {
                while (true)
                {
                    bool next;
                    object current = null;
                    try { next = transition.MoveNext(); if (next) current = transition.Current; }
                    catch (Exception e) { failure = LastError = e.Message; break; }
                    if (!next) { success = true; break; }
                    yield return current;
                }
            }
            finally
            {
                (transition as IDisposable)?.Dispose();
                view?.Hide();
                if (!success) RestoreSuspendedCharacter();
                Unlock(success);
            }
            if (success)
            {
                LastError = null;
                ConfigureActiveScene();
                Notify(TransitionCompleted, request);
            }
            else if (failure != null) NotifyFailure(request, failure);
        }
        private IEnumerator Transition(SceneLoadRequest request)
        {
            foreach (var step in Fade(1f, Catalog.fadeOutSeconds)) yield return step;
            float started = Time.unscaledTime;
            IGameSceneOperation operation = backend.Load(request.Destination.Path);
            if (operation == null) throw new InvalidOperationException("Unity could not start loading the scene.");
            operation.AllowActivation = false;
            try
            {
                while (operation.Progress < .9f && !operation.IsDone)
                {
                    Progress = Mathf.Clamp01(operation.Progress / .9f);
                    view.SetProgress(Progress);
                    yield return null;
                }
                while (Time.unscaledTime - started < Catalog.minimumLoadingSeconds) yield return null;
                Progress = 1;
                view.SetProgress(1);
                if (Application.isPlaying) SuspendCharacterForReset(request);
                Notify(PreparingActivation, request);
                operation.AllowActivation = true;
                while (!operation.IsDone) yield return null;
                // Let the destination's Start methods finish before moving its player or revealing the scene.
                yield return null;
                if (Application.isPlaying) PrepareCharacterInScene(SceneManager.GetActiveScene(), request);
                GamePause.EnsureEventSystem();
                Notify(DestinationReady, request);
                if (request.Destination.isMenu && Catalog.createMenuControls) SceneTitleMenu.Ensure(Catalog);
                foreach (var step in Fade(0f, Catalog.fadeInSeconds)) yield return step;
            }
            finally { operation.AllowActivation = true; }
        }

        private void SuspendCharacterForReset(SceneLoadRequest request)
        {
            if (character == null || (request.Intent != SceneLoadIntent.NewGame && !request.Destination.isMenu)) return;
            suspendedCharacter = character;
            suspendedCharacterWasActive = character.gameObject.activeSelf;
            character.gameObject.SetActive(false);
        }

        private void RestoreSuspendedCharacter()
        {
            if (suspendedCharacter != null) suspendedCharacter.gameObject.SetActive(suspendedCharacterWasActive);
            suspendedCharacter = null;
        }

        /// <summary>Keep one prefab character for the run; scene copies supply spawn and camera references.</summary>
        private void PrepareCharacterInScene(Scene scene, SceneLoadRequest request)
        {
            if (request != null && request.Destination.isMenu)
            {
                if (character != null) Destroy(character.gameObject);
                character = null;
                suspendedCharacter = null;
                characterAreaHandle = 0;
                return;
            }

            GameObject template = FindSceneCharacter(scene);
            GameObject prior = character != null ? character.gameObject : null;
            bool fresh = request == null ? prior == null : request.Intent == SceneLoadIntent.NewGame || prior == null;
            GameObject selected = fresh ? null : prior;
            if (selected == null && template != null && template.GetComponent<ScenePlayerSpawn>() != null)
                selected = template;
            if (selected == null && Catalog != null && Catalog.characterPrefab != null)
                selected = Instantiate(Catalog.characterPrefab);
            if (selected == null && template != null) selected = template;
            if (selected == null)
            {
                Debug.LogWarning("[Scenes] Assign Character Prefab in the scene catalog or place a Scene Player Spawn in " + scene.name + ".");
                characterAreaHandle = scene.handle.GetRawData();
                return;
            }

            ScenePlayerSpawn selectedMarker = selected.GetComponent<ScenePlayerSpawn>();
            if (selectedMarker == null) selectedMarker = selected.AddComponent<ScenePlayerSpawn>();
            Vector3 startPosition = template != null ? template.transform.position
                : Catalog != null && Catalog.characterPrefab != null ? Catalog.characterPrefab.transform.position : selected.transform.position;
            Quaternion startRotation = template != null ? template.transform.rotation
                : Catalog != null && Catalog.characterPrefab != null ? Catalog.characterPrefab.transform.rotation : selected.transform.rotation;

            if (template != null && template != selected)
            {
                RebindSceneCameras(scene, template.transform, selectedMarker);
                template.SetActive(false);
                Destroy(template);
            }
            if (prior != null && prior != selected) Destroy(prior);

            character = selectedMarker;
            suspendedCharacter = null;
            selected.SetActive(true);
            ScenePlayerSpawn.Place(selected.transform, scene, request?.EntryPoint ?? string.Empty, startPosition, startRotation);
            if (selected.transform.parent == null) DontDestroyOnLoad(selected);
            else Debug.LogWarning("[Scenes] The character must be a scene root to persist across areas.", selected);
            characterAreaHandle = scene.handle.GetRawData();
        }

        private GameObject FindSceneCharacter(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponent<ScenePlayerSpawn>() != null) return root;
            // Older scenes may still contain an inactive, unprefabbed Character. Treat it as a placement guide.
            if (Catalog != null && Catalog.characterPrefab != null)
                foreach (var root in scene.GetRootGameObjects())
                    if (root.name == Catalog.characterPrefab.name && root.GetComponent<CharacterController>() != null)
                        return root;
            return null;
        }

        private static void RebindSceneCameras(Scene scene, Transform oldRoot, ScenePlayerSpawn replacement)
        {
            foreach (var camera in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include))
            {
                if (camera.gameObject.scene != scene) continue;
                if (camera.Follow != null && camera.Follow.IsChildOf(oldRoot))
                    camera.Follow = MatchChild(oldRoot, replacement.transform, camera.Follow, replacement.CameraTarget);
                if (camera.LookAt != null && camera.LookAt.IsChildOf(oldRoot))
                    camera.LookAt = MatchChild(oldRoot, replacement.transform, camera.LookAt, replacement.CameraTarget);
            }
        }

        private static Transform MatchChild(Transform oldRoot, Transform newRoot, Transform oldChild, Transform fallback)
        {
            if (oldChild == oldRoot) return newRoot;
            var names = new Stack<string>();
            for (Transform current = oldChild; current != null && current != oldRoot; current = current.parent)
                names.Push(current.name);
            Transform target = newRoot;
            while (names.Count > 0)
            {
                target = target != null ? target.Find(names.Pop()) : null;
                if (target == null) return fallback;
            }
            return target;
        }
        private IEnumerable Fade(float target, float seconds)
        {
            float start = view.Alpha;
            float elapsed = 0;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                view.Alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / seconds));
                yield return null;
            }
            view.Alpha = target;
        }
        private void Unlock(bool success)
        {
            loading = false;
            if (!locked) return;
            GameInput.UnblockGameplay(this);
            GamePause.PauseAllowed = previousPauseAllowed;
            locked = false;
        }
        private static void Notify(Action<SceneLoadRequest> listeners, SceneLoadRequest request)
        {
            if (listeners == null) return;
            foreach (Action<SceneLoadRequest> listener in listeners.GetInvocationList())
                try { listener(request); } catch (Exception e) { Debug.LogException(e); }
        }
        private static void NotifyFailure(SceneLoadRequest request, string reason)
        {
            if (TransitionFailed == null) return;
            foreach (Action<SceneLoadRequest, string> listener in TransitionFailed.GetInvocationList())
                try { listener(request, reason); } catch (Exception e) { Debug.LogException(e); }
        }
        private sealed class UnityBackend : IGameSceneBackend
        {
            public string ActivePath => SceneManager.GetActiveScene().path;
            public bool CanLoad(string path) => Application.CanStreamedLevelBeLoaded(path);
            public IGameSceneOperation Load(string path) => new UnityOperation(SceneManager.LoadSceneAsync(path, LoadSceneMode.Single));
        }
        private sealed class UnityOperation : IGameSceneOperation
        {
            private readonly AsyncOperation operation;
            public UnityOperation(AsyncOperation value) => operation = value ?? throw new InvalidOperationException("Unable to load scene.");
            public float Progress => operation.progress;
            public bool IsDone => operation.isDone;
            public bool AllowActivation { set => operation.allowSceneActivation = value; }
        }
    }
}
