using System;
using System.Collections;
using System.Collections.Generic;
using HauntedHouse.Commands;
using HauntedHouse.Enemies;
using HauntedHouse.Progression;
using HauntedHouse.World;
using UnityEngine;

namespace HauntedHouse.Stealth
{
    /// <summary>
    /// 플레이어의 방 이동·은신과 적 추격·포획을 연결하는 조정자다.
    /// 적의 실제 NavMesh 이동은 EnemyController에 위임하고, 이 클래스는 추격 규칙과 대기 시간을 소유한다.
    /// </summary>
    public sealed class EnemyChaseSystem : MonoBehaviour
    {
        [Header("추격 참조")]
        [SerializeField] private Player player;
        [SerializeField] private PlayerRoomTracker playerRoomTracker;
        [SerializeField] private StealthHideSystem stealthHideSystem;
        [SerializeField] private PlayerCaptureDeathSystem captureDeathSystem;
        [SerializeField] private GameSession gameSession;
        [SerializeField] private EnemyController[] enemies = Array.Empty<EnemyController>();

        [Header("은신 방 대기")]
        [SerializeField, Min(0f)] private float hiddenRoomWaitDuration = 3f;

        private readonly Dictionary<EnemyController, Coroutine> waitRoutines =
            new Dictionary<EnemyController, Coroutine>();
        private readonly Dictionary<EnemyController, Action<Room, Room>> roomChangedHandlers =
            new Dictionary<EnemyController, Action<Room, Room>>();
        private readonly HashSet<EnemyController> pendingRoomChases =
            new HashSet<EnemyController>();

        /// <summary>숨은 방에 도착한 적이 대기할 시간을 초 단위로 반환한다.</summary>
        public float HiddenRoomWaitDuration => hiddenRoomWaitDuration;

        /// <summary>기본 참조와 씬의 적 목록을 보완한다.</summary>
        private void Awake()
        {
            player ??= FindFirstObjectByType<Player>();
            playerRoomTracker ??= FindFirstObjectByType<PlayerRoomTracker>();
            stealthHideSystem ??= FindFirstObjectByType<StealthHideSystem>();
            captureDeathSystem ??= FindFirstObjectByType<PlayerCaptureDeathSystem>();
            gameSession ??= FindFirstObjectByType<GameSession>();

            if (enemies == null || enemies.Length == 0)
            {
                enemies = FindObjectsByType<EnemyController>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            }
        }

        /// <summary>플레이어 방·은신·적 접촉 이벤트를 구독한다.</summary>
        private void OnEnable()
        {
            if (gameSession != null)
            {
                gameSession.GameplayStarted -= HandleGameplayStarted;
                gameSession.GameplayStarted += HandleGameplayStarted;
            }

            if (playerRoomTracker != null)
            {
                playerRoomTracker.RoomEntered += HandlePlayerRoomEntered;
            }

            if (stealthHideSystem != null)
            {
                stealthHideSystem.HideEntered += HandleHideEntered;
                stealthHideSystem.HideExited += HandleHideExited;
            }

            SubscribeEnemies();
        }

        /// <summary>이벤트와 진행 중인 은신 방 대기 코루틴을 해제한다.</summary>
        private void OnDisable()
        {
            if (gameSession != null)
            {
                gameSession.GameplayStarted -= HandleGameplayStarted;
            }

            if (playerRoomTracker != null)
            {
                playerRoomTracker.RoomEntered -= HandlePlayerRoomEntered;
            }

            if (stealthHideSystem != null)
            {
                stealthHideSystem.HideEntered -= HandleHideEntered;
                stealthHideSystem.HideExited -= HandleHideExited;
            }

            UnsubscribeEnemies();
            StopAllWaitRoutines();
            pendingRoomChases.Clear();
        }

        /// <summary>적 Start 이후 현재 플레이어 방을 기준으로 첫 추격 상태를 계산한다.</summary>
        private void Start()
        {
            if (gameSession == null || gameSession.IsGameplaySimulationEnabled)
            {
                RefreshVisibleChaseTargets();
            }
        }

        /// <summary>지정한 방에 활성 적이 있는지 은신 시스템에 제공한다.</summary>
        /// <param name="room">검사할 방.</param>
        /// <returns>활성 적이 있으면 true.</returns>
        public bool HasActiveEnemyInRoom(Room room)
        {
            if (room == null || enemies == null)
            {
                return false;
            }

            foreach (EnemyController enemy in enemies)
            {
                if (enemy != null && enemy.isActiveAndEnabled &&
                    enemy.State != EnemyState.Inactive && enemy.CurrentRoom == room)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>모든 적의 추격을 중단하고 기존 순찰 경로로 복귀시킨다.</summary>
        public void StopAllChases()
        {
            StopAllWaitRoutines();

            if (enemies == null)
            {
                return;
            }

            foreach (EnemyController enemy in enemies)
            {
                enemy?.StopChaseAndResumeRoute();
            }
        }

        /// <summary>체크포인트 복원이 끝나면 현재 플레이어 방 기준으로 추격 상태를 계산한다.</summary>
        private void HandleGameplayStarted()
        {
            RefreshVisibleChaseTargets();
        }

        /// <summary>플레이어가 다른 방에 들어가면 보이는 플레이어를 추격하도록 목적지를 갱신한다.</summary>
        private void HandlePlayerRoomEntered(Room previousRoom, Room currentRoom)
        {
            if (gameSession != null && !gameSession.IsGameplaySimulationEnabled)
            {
                return;
            }

            if (stealthHideSystem != null && stealthHideSystem.IsHidden)
            {
                return;
            }

            RefreshVisibleChaseTargets(currentRoom);
        }

        /// <summary>
        /// 은신 성공 시 이미 추격 중인 적에게만 숨은 방을 새 목표로 전달한다.
        /// 적이 추격 중이 아니면 은신으로 인해 새 탐지를 만들지 않는다.
        /// </summary>
        private void HandleHideEntered(Room hidingRoom)
        {
            if (gameSession != null && !gameSession.IsGameplaySimulationEnabled)
            {
                return;
            }

            if (hidingRoom == null || enemies == null)
            {
                return;
            }

            foreach (EnemyController enemy in enemies)
            {
                if (enemy != null && enemy.IsChasing)
                {
                    enemy.SetChaseTargetRoom(hidingRoom);
                }
            }
        }

        /// <summary>은신 해제 후 같은 방에 있는 적의 추격을 재개한다.</summary>
        private void HandleHideExited(Room exitedRoom)
        {
            if (gameSession != null && !gameSession.IsGameplaySimulationEnabled)
            {
                return;
            }

            RefreshVisibleChaseTargets();
        }

        /// <summary>현재 플레이어 방과 적 상태를 비교해 추격 여부를 갱신한다.</summary>
        private void RefreshVisibleChaseTargets(Room playerRoom = null)
        {
            if (gameSession != null && !gameSession.IsGameplaySimulationEnabled)
            {
                return;
            }

            if (player == null || playerRoomTracker == null || enemies == null ||
                (stealthHideSystem != null && stealthHideSystem.IsHidden))
            {
                return;
            }

            playerRoom ??= playerRoomTracker.CurrentRoom;
            if (playerRoom == null)
            {
                return;
            }

            foreach (EnemyController enemy in enemies)
            {
                if (enemy == null || !enemy.isActiveAndEnabled || enemy.State == EnemyState.Inactive)
                {
                    continue;
                }

                if (enemy.CurrentRoom == playerRoom)
                {
                    enemy.BeginChase(player.transform, playerRoom);
                }
                else if (enemy.IsChasing)
                {
                    enemy.SetChaseTargetRoom(playerRoom);
                }
            }
        }

        /// <summary>적 접촉을 포획 시스템에 전달하고 은신 중 접촉은 무시한다.</summary>
        private void HandleEnemyPlayerContact(EnemyController enemy, Player contactedPlayer)
        {
            GameLog.Log(
                $"EnemyChaseSystem이 적 접촉 이벤트를 받았습니다. " +
                $"Enemy={enemy?.name ?? "null"}, Player={contactedPlayer?.name ?? "null"}, " +
                $"SessionPhase={gameSession?.Phase.ToString() ?? "null"}.",
                this);

            if (gameSession != null && !gameSession.IsGameplaySimulationEnabled)
            {
                GameLog.LogWarning("적 접촉이 GameplaySimulation 비활성 상태라 차단됐습니다.", this);
                return;
            }

            if (contactedPlayer == null)
            {
                GameLog.LogWarning("적 접촉 이벤트의 Player 참조가 비어 있어 포획을 진행하지 않습니다.", this);
                return;
            }

            if (contactedPlayer != player)
            {
                GameLog.LogWarning("현재 추적 대상 Player가 아닌 객체와의 접촉이라 포획을 진행하지 않습니다.", this);
                return;
            }

            if (stealthHideSystem != null && stealthHideSystem.IsHidden)
            {
                GameLog.Log("플레이어가 은신 중이라 적 접촉 포획을 무시했습니다.", this);
                return;
            }

            if (playerRoomTracker != null && enemy.CurrentRoom != playerRoomTracker.CurrentRoom)
            {
                GameLog.LogWarning(
                    $"적과 플레이어의 논리 방이 달라 포획을 진행하지 않습니다. " +
                    $"EnemyRoom={enemy.CurrentRoom?.RoomId ?? "null"}, " +
                    $"PlayerRoom={playerRoomTracker.CurrentRoom?.RoomId ?? "null"}.",
                    this);
                return;
            }

            if (captureDeathSystem == null)
            {
                GameLog.LogError("EnemyChaseSystem에 PlayerCaptureDeathSystem이 연결되지 않았습니다.", this);
                return;
            }

            bool captured = captureDeathSystem.TryCapture(enemy);
            GameLog.Log($"적 접촉 포획 요청 결과: {captured}.", this);
        }

        /// <summary>적이 숨은 방에 진입하면 정지 후 3초 뒤 순찰 복귀를 예약한다.</summary>
        private void HandleChaseRoomReached(EnemyController enemy)
        {
            if (gameSession != null && !gameSession.IsGameplaySimulationEnabled)
            {
                return;
            }

            if (enemy == null || player == null || playerRoomTracker == null ||
                enemy.CurrentRoom != playerRoomTracker.CurrentRoom)
            {
                return;
            }

            if (stealthHideSystem == null || !stealthHideSystem.IsHidden)
            {
                // 보이는 플레이어를 쫓던 적이 같은 방에 도착한 경우에도
                // DoorTraversal이 완전히 끝난 시점에서 플레이어 위치 추격으로 전환한다.
                enemy.BeginChase(player.transform, playerRoomTracker.CurrentRoom);
                return;
            }

            StopWaitRoutine(enemy);
            enemy.HoldChaseAtCurrentPosition();
            waitRoutines[enemy] = StartCoroutine(WaitThenResumePatrol(enemy));
        }

        /// <summary>설정된 대기 시간 후 적을 원래 순찰로 돌려보낸다.</summary>
        private IEnumerator WaitThenResumePatrol(EnemyController enemy)
        {
            if (hiddenRoomWaitDuration > 0f)
            {
                yield return new WaitForSeconds(hiddenRoomWaitDuration);
            }

            waitRoutines.Remove(enemy);
            if (enemy != null && enemy.IsChasing &&
                stealthHideSystem != null && stealthHideSystem.IsHidden)
            {
                enemy.StopChaseAndResumeRoute();
            }
        }

        /// <summary>적 이벤트를 중복 없이 연결한다.</summary>
        private void SubscribeEnemies()
        {
            if (enemies == null)
            {
                return;
            }

            foreach (EnemyController enemy in enemies)
            {
                if (enemy == null)
                {
                    continue;
                }

                enemy.PlayerContactDetected -= HandleEnemyPlayerContact;
                enemy.PlayerContactDetected += HandleEnemyPlayerContact;
                enemy.ChaseRoomReached -= HandleChaseRoomReached;
                enemy.ChaseRoomReached += HandleChaseRoomReached;

                // 적이 순찰 중 플레이어 방으로 들어오는 경우도 즉시 추격을 시작해야 한다.
                if (roomChangedHandlers.TryGetValue(enemy, out Action<Room, Room> oldHandler))
                {
                    enemy.RoomChanged -= oldHandler;
                }

                Action<Room, Room> roomChangedHandler =
                    (previousRoom, currentRoom) => HandleEnemyRoomChanged(enemy, previousRoom, currentRoom);
                roomChangedHandlers[enemy] = roomChangedHandler;
                enemy.RoomChanged += roomChangedHandler;
            }
        }

        /// <summary>적 이벤트 구독을 해제한다.</summary>
        private void UnsubscribeEnemies()
        {
            if (enemies == null)
            {
                return;
            }

            foreach (EnemyController enemy in enemies)
            {
                if (enemy == null)
                {
                    continue;
                }

                enemy.PlayerContactDetected -= HandleEnemyPlayerContact;
                enemy.ChaseRoomReached -= HandleChaseRoomReached;

                if (roomChangedHandlers.TryGetValue(enemy, out Action<Room, Room> roomChangedHandler))
                {
                    enemy.RoomChanged -= roomChangedHandler;
                    roomChangedHandlers.Remove(enemy);
                }
            }
        }

        /// <summary>
        /// 적이 이동 중 플레이어의 현재 방에 진입하면 플레이어 위치를 NavMesh 추격 목표로 지정한다.
        /// 플레이어가 은신 중이면 RoomChanged를 탐지 이벤트로 사용하지 않는다.
        /// </summary>
        /// <param name="enemy">방을 변경한 적.</param>
        /// <param name="previousRoom">이전 적 방.</param>
        /// <param name="currentRoom">새로운 적 방.</param>
        private void HandleEnemyRoomChanged(EnemyController enemy, Room previousRoom, Room currentRoom)
        {
            if (gameSession != null && !gameSession.IsGameplaySimulationEnabled)
            {
                return;
            }

            if (enemy == null || currentRoom == null || player == null || playerRoomTracker == null ||
                (stealthHideSystem != null && stealthHideSystem.IsHidden) ||
                currentRoom != playerRoomTracker.CurrentRoom ||
                !enemy.isActiveAndEnabled || enemy.State == EnemyState.Inactive || enemy.IsChasing)
            {
                return;
            }

            // RoomChanged는 Doorway Sensor 통과 시점에 먼저 발생할 수 있으므로
            // 실제 문 통과 코루틴이 끝난 다음에만 BeginChase를 호출한다.
            if (pendingRoomChases.Add(enemy))
            {
                StartCoroutine(BeginChaseAfterTraversal(enemy, currentRoom));
            }
        }

        /// <summary>적의 문 통과가 끝난 뒤에도 플레이어 방에 있으면 추격을 시작한다.</summary>
        /// <param name="enemy">방에 진입한 적.</param>
        /// <param name="enteredRoom">적이 진입한 방.</param>
        private IEnumerator BeginChaseAfterTraversal(EnemyController enemy, Room enteredRoom)
        {
            while (enemy != null && enemy.IsTraversing)
            {
                yield return null;
            }

            pendingRoomChases.Remove(enemy);
            if (gameSession != null && !gameSession.IsGameplaySimulationEnabled)
            {
                yield break;
            }

            if (enemy == null || player == null || playerRoomTracker == null ||
                (stealthHideSystem != null && stealthHideSystem.IsHidden) ||
                enemy.CurrentRoom != enteredRoom || playerRoomTracker.CurrentRoom != enteredRoom ||
                !enemy.isActiveAndEnabled || enemy.State == EnemyState.Inactive)
            {
                yield break;
            }

            enemy.BeginChase(player.transform, enteredRoom);
        }

        /// <summary>특정 적의 은신 방 대기 코루틴을 중단한다.</summary>
        private void StopWaitRoutine(EnemyController enemy)
        {
            if (enemy == null || !waitRoutines.TryGetValue(enemy, out Coroutine routine))
            {
                return;
            }

            if (routine != null)
            {
                StopCoroutine(routine);
            }

            waitRoutines.Remove(enemy);
        }

        /// <summary>모든 은신 방 대기 코루틴을 중단한다.</summary>
        private void StopAllWaitRoutines()
        {
            foreach (Coroutine routine in waitRoutines.Values)
            {
                if (routine != null)
                {
                    StopCoroutine(routine);
                }
            }

            waitRoutines.Clear();
        }

        /// <summary>컴포넌트를 처음 추가했을 때 기본 참조를 연결한다.</summary>
        private void Reset()
        {
            player = FindFirstObjectByType<Player>();
            playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
            stealthHideSystem = FindFirstObjectByType<StealthHideSystem>();
            captureDeathSystem = FindFirstObjectByType<PlayerCaptureDeathSystem>();
            gameSession = FindFirstObjectByType<GameSession>();
            enemies = FindObjectsByType<EnemyController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        }
    }
}
