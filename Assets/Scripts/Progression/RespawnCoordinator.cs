using System.Collections;
using System.Collections.Generic;
using HauntedHouse.Commands;
using HauntedHouse.Enemies;
using HauntedHouse.Inventory;
using HauntedHouse.Stealth;
using HauntedHouse.World;
using UnityEngine;

namespace HauntedHouse.Progression
{
    /// <summary>
    /// 하나의 CheckpointDefinition을 현재 씬의 플레이어·적·월드 상태에 순서대로 적용한다.
    /// 초기 시작과 Restart는 동일한 Restore 흐름을 사용하며 복원 중에는 GameSession이 모든 조작을 차단한다.
    /// </summary>
    public sealed class RespawnCoordinator : MonoBehaviour
    {
        [Header("게임 진행")]
        [SerializeField] private GameSession gameSession;

        [Header("플레이어")]
        [SerializeField] private Player player;
        [SerializeField] private PlayerRoomTracker playerRoomTracker;
        [SerializeField] private CameraDirector cameraDirector;
        [SerializeField] private PlayerInventory playerInventory;
        [SerializeField] private TargetCommandRunner targetCommandRunner;
        [SerializeField] private StealthHideSystem stealthHideSystem;
        [SerializeField] private PlayerCaptureDeathSystem captureDeathSystem;

        [Header("적")]
        [SerializeField] private EnemyChaseSystem enemyChaseSystem;
        [SerializeField] private EnemyController[] enemies = System.Array.Empty<EnemyController>();

        [Header("완료 상태")]
        [Tooltip("체크포인트에 등장할 수 있는 모든 ICompletedState 구현체를 연결한다. 비워두면 씬에서 한 번 검색한다.")]
        [SerializeField] private MonoBehaviour[] completedStateParticipants = System.Array.Empty<MonoBehaviour>();

        /// <summary>기본 참조를 보완하고 복원 가능한 상태 구성요소를 한 번 검색한다.</summary>
        private void Awake()
        {
            gameSession ??= FindFirstObjectByType<GameSession>();
            player ??= FindFirstObjectByType<Player>();
            playerRoomTracker ??= FindFirstObjectByType<PlayerRoomTracker>();
            cameraDirector ??= FindFirstObjectByType<CameraDirector>();
            playerInventory ??= FindFirstObjectByType<PlayerInventory>();
            targetCommandRunner ??= FindFirstObjectByType<TargetCommandRunner>();
            stealthHideSystem ??= FindFirstObjectByType<StealthHideSystem>();
            captureDeathSystem ??= FindFirstObjectByType<PlayerCaptureDeathSystem>();
            enemyChaseSystem ??= FindFirstObjectByType<EnemyChaseSystem>();

            if (enemies == null || enemies.Length == 0)
            {
                enemies = FindObjectsByType<EnemyController>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            }

            if (completedStateParticipants == null || completedStateParticipants.Length == 0)
            {
                completedStateParticipants = FindCompletedStateParticipants();
            }
        }

        /// <summary>
        /// 체크포인트 정의를 현재 월드에 적용한다.
        /// 각 단계 사이에 한 프레임을 양보해 비활성화·이동·카메라 전환 순서가 안정적으로 반영되게 한다.
        /// </summary>
        /// <param name="checkpoint">적용할 체크포인트 정의.</param>
        public IEnumerator Restore(CheckpointDefinition checkpoint)
        {
            if (checkpoint == null)
            {
                GameLog.LogError("RespawnCoordinator에 적용할 CheckpointDefinition이 없습니다.", this);
                yield break;
            }

            if (!checkpoint.TryValidateConfiguration(out string configurationError))
            {
                GameLog.LogError(configurationError, checkpoint);
                yield break;
            }

            // 현재 실행 중인 이동·상호작용·추격을 먼저 끊어 복원 이후에 오래된 코루틴이 개입하지 못하게 한다.
            targetCommandRunner?.AbortForRespawn();
            enemyChaseSystem?.StopAllChases();
            captureDeathSystem?.ResetAfterRetry();
            yield return null;

            // 체크포인트가 지정한 고유 아이템 목록으로 현재 인벤토리를 교체한다.
            if (playerInventory != null)
            {
                playerInventory.RestoreItems(checkpoint.InventoryItems);
            }

            // 사망 직전의 진행 flag를 제거하고 체크포인트 시점의 기준 flag만 다시 적용한다.
            gameSession?.StoryFlags.ReplaceWith(checkpoint.ProgressFlags);

            // 먼저 모든 복원 대상의 완료 상태를 해제한 뒤 체크포인트에 지정된 상태만 적용한다.
            RestoreCompletedStates(checkpoint);

            // 적은 현재 위치를 보존하지 않고 체크포인트 정의의 초기 경로·스폰 위치로 재구성한다.
            foreach (EnemyController enemy in enemies)
            {
                enemy?.SetActive(false);
            }

            if (checkpoint.EnemyStates != null)
            {
                foreach (CheckpointEnemyState enemyState in checkpoint.EnemyStates)
                {
                    enemyState.Enemy.RestoreCheckpointState(
                        enemyState.PatrolRoute,
                        enemyState.RouteStartIndex,
                        enemyState.Active);
                }
            }

            // 플레이어의 Transform과 논리 방을 직접 복원한다. RestoreRoom은 RoomEntered를 발행하지 않는다.
            if (player != null && !player.WarpTo(checkpoint.RespawnPoint))
            {
                GameLog.LogError(
                    $"체크포인트 '{checkpoint.CheckpointId}'의 플레이어 위치 복원에 실패했습니다.",
                    this);
            }

            if (playerRoomTracker != null && !playerRoomTracker.RestoreRoom(checkpoint.RespawnRoom))
            {
                GameLog.LogError(
                    $"체크포인트 '{checkpoint.CheckpointId}'의 논리 방 복원에 실패했습니다.",
                    this);
            }

            cameraDirector?.CutTo(checkpoint.RespawnRoom);
            yield return null;
        }

        /// <summary>모든 상태 참여자를 미완료로 초기화한 뒤 정의된 완료 상태를 적용한다.</summary>
        private void RestoreCompletedStates(CheckpointDefinition checkpoint)
        {
            if (completedStateParticipants != null)
            {
                foreach (MonoBehaviour component in completedStateParticipants)
                {
                    if (component is ICompletedState participant)
                    {
                        participant.RestoreCompletedState(false);
                    }
                }
            }

            if (checkpoint.CompletedStates == null)
            {
                return;
            }

            foreach (CheckpointCompletedState state in checkpoint.CompletedStates)
            {
                state.Participant?.RestoreCompletedState(state.Completed);
            }
        }

        /// <summary>컴포넌트를 처음 추가했을 때 기본 복원 참조를 연결한다.</summary>
        private void Reset()
        {
            gameSession = FindFirstObjectByType<GameSession>();
            player = FindFirstObjectByType<Player>();
            playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
            cameraDirector = FindFirstObjectByType<CameraDirector>();
            playerInventory = FindFirstObjectByType<PlayerInventory>();
            targetCommandRunner = FindFirstObjectByType<TargetCommandRunner>();
            stealthHideSystem = FindFirstObjectByType<StealthHideSystem>();
            captureDeathSystem = FindFirstObjectByType<PlayerCaptureDeathSystem>();
            enemyChaseSystem = FindFirstObjectByType<EnemyChaseSystem>();
            enemies = FindObjectsByType<EnemyController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            completedStateParticipants = FindCompletedStateParticipants();
        }

        /// <summary>현재 씬에서 ICompletedState를 구현한 MonoBehaviour만 찾아 반환한다.</summary>
        /// <returns>완료 상태 복원 대상의 고유 컴포넌트 배열.</returns>
        private static MonoBehaviour[] FindCompletedStateParticipants()
        {
            MonoBehaviour[] components = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            var participants = new List<MonoBehaviour>();

            // Unity 직렬화는 인터페이스 배열을 지원하지 않으므로 MonoBehaviour 배열로 보관하되,
            // 실제 완료 상태 계약을 구현한 컴포넌트만 필터링해 이후 복원 순회를 작게 유지한다.
            foreach (MonoBehaviour component in components)
            {
                if (component is ICompletedState)
                {
                    participants.Add(component);
                }
            }

            return participants.ToArray();
        }
    }
}
