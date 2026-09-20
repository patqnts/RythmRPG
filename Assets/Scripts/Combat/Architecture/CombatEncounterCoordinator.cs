using System.Collections;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using PrimeTween;
using UnityEngine;

namespace RythmRPG.Combat
{
    public readonly struct CombatEncounterContext
    {
        public readonly PlayerCombatant Player;
        public readonly EnemyCombatant Enemy;

        public CombatEncounterContext(PlayerCombatant player, EnemyCombatant enemy)
        {
            Player = player;
            Enemy = enemy;
        }
    }

    public sealed class CombatEncounterCoordinator : MonoBehaviour
    {
        [Header("Cinemachine")]
        [SerializeField] private CinemachineVirtualCamera virtualCamera;
        [SerializeField, Min(0f)] private float cameraDamping = 0.35f;

        [Header("Combat Composition")]
        [SerializeField] private Vector2 playerViewportPosition = new(0.5f, 0.2f);
        [SerializeField] private Vector2 enemyViewportPosition = new(0.5f, 0.7f);
        [SerializeField, Min(0.1f)] private float minimumPlayerEnemySeparation = 2.4f;
        [SerializeField] private bool preservePlayerHeight = true;

        [Header("Transition")]
        [SerializeField, Min(0f)] private float moveDuration = 0.35f;
        [SerializeField] private GameObject combatUIRoot;
        [SerializeField] private int combatSortingOrder = 52;

        private Vector3 playerWorldPosition;
        private int playerSortingOrder;
        private int enemySortingOrder;
        private Transform previousCameraFollow;
        private Transform previousCameraLookAt;
        private Transform combatCameraTarget;
        private Transform combatEnemy;
        private CinemachineTransposer transposer;

        private void Awake()
        {
            ResolveReferences();
            EnsureCinemachineFollowRig();
            if (combatUIRoot != null) combatUIRoot.SetActive(false);
        }

        private void LateUpdate()
        {
            if (combatCameraTarget == null || combatEnemy == null) return;
            combatCameraTarget.position = ResolveCombatCameraTarget(combatEnemy.position);
        }

        public IEnumerator Prepare(CombatEncounterContext context)
        {
            ResolveReferences();
            EnsureCinemachineFollowRig();
            playerWorldPosition = context.Player.transform.position;
            SpriteRenderer playerRenderer = context.Player.GetComponentInChildren<SpriteRenderer>();
            SpriteRenderer enemyRenderer = context.Enemy.SpriteRenderer;
            if (playerRenderer != null)
            {
                playerSortingOrder = playerRenderer.sortingOrder;
                playerRenderer.sortingOrder = combatSortingOrder;
            }
            if (enemyRenderer != null)
            {
                enemySortingOrder = enemyRenderer.sortingOrder;
                enemyRenderer.sortingOrder = combatSortingOrder;
            }
            if (combatUIRoot != null) combatUIRoot.SetActive(true);

            ConfigureCombatCamera(context.Enemy.transform);
            Vector3 playerTargetPosition = ResolvePlayerCombatPosition(context);
            CharacterController playerController = context.Player.GetComponent<CharacterController>();
            bool playerControllerWasEnabled = playerController != null && playerController.enabled;
            if (playerControllerWasEnabled) playerController.enabled = false;

            Tween playerMove = playerTargetPosition != context.Player.transform.position
                ? Tween.Position(context.Player.transform, playerTargetPosition, moveDuration, Ease.InOutSine)
                : default;
            if (playerMove.isAlive) yield return new WaitForSeconds(moveDuration);

            if (playerControllerWasEnabled && playerController != null) playerController.enabled = true;
        }

        public void Restore(CombatEncounterContext context, bool victory)
        {
            Tween.StopAll(context.Player.transform);
            Tween.StopAll(context.Enemy.transform);
            context.Player.transform.position = playerWorldPosition;
            SpriteRenderer playerRenderer = context.Player.GetComponentInChildren<SpriteRenderer>();
            if (playerRenderer != null) playerRenderer.sortingOrder = playerSortingOrder;
            if (context.Enemy.SpriteRenderer != null) context.Enemy.SpriteRenderer.sortingOrder = enemySortingOrder;
            RestoreExplorationCamera(context.Player.transform);
            if (combatUIRoot != null) combatUIRoot.SetActive(false);
            context.Player.SendMessage("EnableMovement", SendMessageOptions.DontRequireReceiver);
            context.Player.SendMessage("CloseBattleBG", SendMessageOptions.DontRequireReceiver);
            if (victory) context.Enemy.gameObject.SetActive(false);
        }

        private void EnsureCinemachineFollowRig()
        {
            if (virtualCamera == null) return;

            Transform follow = virtualCamera.Follow;
            Vector3 currentOffset = follow != null
                ? virtualCamera.transform.position - follow.position
                : new Vector3(0f, 7f, -9f);

            if (follow != null && virtualCamera.transform.IsChildOf(follow.root))
                virtualCamera.transform.SetParent(null, true);

            transposer = virtualCamera.GetCinemachineComponent<CinemachineTransposer>();
            if (transposer == null)
            {
                transposer = virtualCamera.AddCinemachineComponent<CinemachineTransposer>();
                if (transposer != null) transposer.m_FollowOffset = currentOffset;
            }
            if (transposer == null) return;

            transposer.m_BindingMode = BindingMode.WorldSpace;
            transposer.m_XDamping = cameraDamping;
            transposer.m_YDamping = cameraDamping;
            transposer.m_ZDamping = cameraDamping;
        }

        private Vector3 ResolvePlayerCombatPosition(CombatEncounterContext context)
        {
            float playerHeight = preservePlayerHeight
                ? context.Player.transform.position.y
                : context.Enemy.transform.position.y;
            if (virtualCamera == null)
            {
                Vector3 away = context.Player.transform.position - context.Enemy.transform.position;
                away.y = 0f;
                if (away.sqrMagnitude <= 0.0001f) away = Vector3.back;
                Vector3 fallback = context.Enemy.transform.position
                    + away.normalized * minimumPlayerEnemySeparation;
                fallback.y = playerHeight;
                return fallback;
            }
            Vector3 cameraPosition = ResolveCombatCameraPosition(context.Enemy.transform.position);
            if (!TryViewportToHorizontalPlane(playerViewportPosition, playerHeight, cameraPosition, out Vector3 target))
            {
                Vector3 screenDown = Vector3.ProjectOnPlane(-virtualCamera.transform.up, Vector3.up).normalized;
                target = context.Enemy.transform.position + screenDown * minimumPlayerEnemySeparation;
                target.y = playerHeight;
            }

            Vector3 planarOffset = target - context.Enemy.transform.position;
            planarOffset.y = 0f;
            if (planarOffset.sqrMagnitude < minimumPlayerEnemySeparation * minimumPlayerEnemySeparation)
            {
                Vector3 direction = planarOffset.sqrMagnitude > 0.0001f
                    ? planarOffset.normalized
                    : Vector3.ProjectOnPlane(-virtualCamera.transform.up, Vector3.up).normalized;
                target += direction * (minimumPlayerEnemySeparation - planarOffset.magnitude);
            }
            target.y = playerHeight;
            return target;
        }

        private void ConfigureCombatCamera(Transform enemy)
        {
            if (virtualCamera == null || enemy == null) return;
            previousCameraFollow = virtualCamera.Follow;
            previousCameraLookAt = virtualCamera.LookAt;
            combatEnemy = enemy;

            if (combatCameraTarget == null)
            {
                GameObject targetObject = new("Combat Camera Frame Target");
                combatCameraTarget = targetObject.transform;
            }

            combatCameraTarget.position = ResolveCombatCameraTarget(enemy.position);
            virtualCamera.Follow = combatCameraTarget;
            virtualCamera.LookAt = null;
        }

        private Vector3 ResolveCombatCameraTarget(Vector3 enemyPosition)
        {
            return ResolveCombatCameraPosition(enemyPosition) - ResolveFollowOffset();
        }

        private Vector3 ResolveCombatCameraPosition(Vector3 enemyPosition)
        {
            if (virtualCamera == null) return enemyPosition;
            Transform cameraTransform = virtualCamera.transform;
            float orthographicSize = ResolveOrthographicSize();
            float aspect = ResolveCameraAspect();
            Vector2 normalizedOffset = enemyViewportPosition - new Vector2(0.5f, 0.5f);
            Vector3 screenOffset = cameraTransform.right * (normalizedOffset.x * 2f * orthographicSize * aspect)
                + cameraTransform.up * (normalizedOffset.y * 2f * orthographicSize);
            float depth = Mathf.Max(1f, Vector3.Dot(-ResolveFollowOffset(), cameraTransform.forward));
            return enemyPosition - screenOffset - cameraTransform.forward * depth;
        }

        private bool TryViewportToHorizontalPlane(Vector2 viewportPosition, float height,
            Vector3 cameraPosition, out Vector3 worldPosition)
        {
            Transform cameraTransform = virtualCamera.transform;
            float orthographicSize = ResolveOrthographicSize();
            Vector2 normalizedOffset = viewportPosition - new Vector2(0.5f, 0.5f);
            Vector3 rayOrigin = cameraPosition
                + cameraTransform.right * (normalizedOffset.x * 2f * orthographicSize * ResolveCameraAspect())
                + cameraTransform.up * (normalizedOffset.y * 2f * orthographicSize);
            Ray ray = new(rayOrigin, cameraTransform.forward);
            Plane plane = new(Vector3.up, new Vector3(0f, height, 0f));
            if (plane.Raycast(ray, out float distance))
            {
                worldPosition = ray.GetPoint(distance);
                return true;
            }
            worldPosition = default;
            return false;
        }

        private Vector3 ResolveFollowOffset()
        {
            if (transposer != null) return transposer.m_FollowOffset;
            if (virtualCamera != null && virtualCamera.Follow != null)
                return virtualCamera.transform.position - virtualCamera.Follow.position;
            return new Vector3(0f, 7f, -9f);
        }

        private float ResolveCameraAspect()
        {
            Camera outputCamera = Camera.main;
            if (outputCamera != null && outputCamera.aspect > 0f) return outputCamera.aspect;
            return 16f / 9f;
        }

        private float ResolveOrthographicSize()
        {
            Camera outputCamera = Camera.main;
            if (outputCamera != null && outputCamera.orthographic)
                return Mathf.Max(0.01f, outputCamera.orthographicSize);
            return Mathf.Max(0.01f, virtualCamera.m_Lens.OrthographicSize);
        }

        private void RestoreExplorationCamera(Transform player)
        {
            combatEnemy = null;
            if (virtualCamera != null)
            {
                virtualCamera.Follow = previousCameraFollow != null ? previousCameraFollow : player;
                virtualCamera.LookAt = previousCameraLookAt;
            }

            if (combatCameraTarget != null)
            {
                Destroy(combatCameraTarget.gameObject);
                combatCameraTarget = null;
            }
        }

        private void ResolveReferences()
        {
            combatUIRoot ??= FindNamedTransform("CombatSystemUI")?.gameObject;
            virtualCamera ??= FindFirstObjectByType<CinemachineVirtualCamera>(FindObjectsInactive.Include);
        }

        private static Transform FindNamedTransform(string objectName)
        {
            Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (Transform candidate in transforms)
            {
                if (candidate.name == objectName) return candidate;
            }
            return null;
        }
    }
}
