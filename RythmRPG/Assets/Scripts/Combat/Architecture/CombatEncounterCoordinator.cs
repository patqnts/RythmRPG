using System.Collections;
using System.Linq;
using Cinemachine;
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
        [SerializeField] private Transform playerCombatPosition;
        [SerializeField] private Transform enemyCombatPosition;
        [SerializeField] private CinemachineVirtualCamera virtualCamera;
        [SerializeField] private GameObject combatUIRoot;
        [SerializeField] private int combatSortingOrder = 52;
        [SerializeField, Min(0f)] private float moveDuration = 0.35f;

        private Vector3 playerWorldPosition;
        private Vector3 enemyWorldPosition;
        private int playerSortingOrder;
        private int enemySortingOrder;

        private void Awake() => ResolveReferences();

        public IEnumerator Prepare(CombatEncounterContext context)
        {
            ResolveReferences();
            playerWorldPosition = context.Player.transform.position;
            enemyWorldPosition = context.Enemy.transform.position;
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
            if (virtualCamera != null) virtualCamera.Follow = null;
            if (combatUIRoot != null) combatUIRoot.SetActive(true);

            Tween playerMove = playerCombatPosition != null
                ? Tween.Position(context.Player.transform, playerCombatPosition.position, moveDuration, Ease.InOutSine)
                : default;
            Tween enemyMove = enemyCombatPosition != null
                ? Tween.Position(context.Enemy.transform, enemyCombatPosition.position, moveDuration, Ease.InOutSine)
                : default;
            if (playerMove.isAlive || enemyMove.isAlive) yield return new WaitForSeconds(moveDuration);
        }

        public void Restore(CombatEncounterContext context, bool victory)
        {
            Tween.StopAll(context.Player.transform);
            Tween.StopAll(context.Enemy.transform);
            context.Player.transform.position = playerWorldPosition;
            context.Enemy.transform.position = enemyWorldPosition;
            SpriteRenderer playerRenderer = context.Player.GetComponentInChildren<SpriteRenderer>();
            if (playerRenderer != null) playerRenderer.sortingOrder = playerSortingOrder;
            if (context.Enemy.SpriteRenderer != null) context.Enemy.SpriteRenderer.sortingOrder = enemySortingOrder;
            if (virtualCamera != null) virtualCamera.Follow = context.Player.transform;
            if (combatUIRoot != null) combatUIRoot.SetActive(false);
            context.Player.SendMessage("EnableMovement", SendMessageOptions.DontRequireReceiver);
            context.Player.SendMessage("CloseBattleBG", SendMessageOptions.DontRequireReceiver);
            if (victory) context.Enemy.gameObject.SetActive(false);
        }

        private void ResolveReferences()
        {
            Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            playerCombatPosition ??= allTransforms.FirstOrDefault(item => item.name == "Player Position");
            enemyCombatPosition ??= allTransforms.FirstOrDefault(item => item.name == "Enemy Position");
            combatUIRoot ??= allTransforms.FirstOrDefault(item => item.name == "CombatSystemUI")?.gameObject;
            virtualCamera ??= FindFirstObjectByType<CinemachineVirtualCamera>(FindObjectsInactive.Include);
        }
    }
}
