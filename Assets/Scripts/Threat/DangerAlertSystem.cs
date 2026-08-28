using System;
using HauntedHouse.Enemies;
using HauntedHouse.Progression;
using HauntedHouse.World;
using UnityEngine;

namespace HauntedHouse.Threat
{
    /// <summary>
    /// 플레이어와 모든 적의 방 상태를 관찰해 화면에 전달할 통합 위험경보를 계산한다.
    /// 실제 규칙 계산은 DangerAlertEvaluator에 위임하고, 이 클래스는 상태 연결과 변경 통지만 담당한다.
    /// </summary>
    public sealed class DangerAlertSystem : MonoBehaviour
    {
        [Header("경보 계산 대상")]
        [SerializeField] private PlayerRoomTracker playerRoomTracker;
        [SerializeField] private EnemyController[] enemies = Array.Empty<EnemyController>();
        [SerializeField] private GameSession gameSession;

        /// <summary>현재 통합된 위험경보 단계를 반환한다.</summary>
        public DangerAlertLevel CurrentLevel { get; private set; } = DangerAlertLevel.None;

        /// <summary>통합 경보 단계가 실제로 변경될 때 새 단계를 전달한다.</summary>
        public event Action<DangerAlertLevel> AlertChanged;

        /// <summary>
        /// 필수 참조를 보완하고 Inspector에 적 목록이 없을 때 씬의 적을 한 번 검색한다.
        /// 매 프레임 검색하지 않아 경보 계산 비용과 생명주기 의존성을 줄인다.
        /// </summary>
        private void Awake()
        {
            playerRoomTracker ??= FindFirstObjectByType<PlayerRoomTracker>();
            gameSession ??= FindFirstObjectByType<GameSession>();

            if (enemies == null || enemies.Length == 0)
            {
                enemies = FindObjectsByType<EnemyController>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            }

            if (playerRoomTracker == null)
            {
                GameLog.LogError("DangerAlertSystem에 PlayerRoomTracker가 연결되지 않았습니다.", this);
            }
        }

        /// <summary>플레이어와 적의 상태 변경 이벤트를 구독한다.</summary>
        private void OnEnable()
        {
            if (gameSession != null)
            {
                gameSession.PhaseChanged += HandleGameSessionPhaseChanged;
                gameSession.GameplayStarted += HandleGameplayStarted;
            }

            if (playerRoomTracker != null)
            {
                playerRoomTracker.RoomEntered += HandlePlayerRoomEntered;
            }

            if (enemies == null)
            {
                return;
            }

            foreach (EnemyController enemy in enemies)
            {
                SubscribeEnemy(enemy);
            }
        }

        /// <summary>컴포넌트가 비활성화될 때 모든 이벤트 구독을 해제한다.</summary>
        private void OnDisable()
        {
            if (gameSession != null)
            {
                gameSession.PhaseChanged -= HandleGameSessionPhaseChanged;
                gameSession.GameplayStarted -= HandleGameplayStarted;
            }

            if (playerRoomTracker != null)
            {
                playerRoomTracker.RoomEntered -= HandlePlayerRoomEntered;
            }

            if (enemies == null)
            {
                return;
            }

            foreach (EnemyController enemy in enemies)
            {
                UnsubscribeEnemy(enemy);
            }
        }

        /// <summary>
        /// 이벤트 구독이 끝난 뒤 초기 경보를 계산해 UI가 None 상태도 즉시 반영하도록 한다.
        /// </summary>
        private void Start()
        {
            if (IsGameplaySimulationEnabled())
            {
                Recalculate(true);
            }
        }

        /// <summary>복원이 끝나 플레이가 시작될 때 현재 적 상태를 기준으로 경보를 다시 계산한다.</summary>
        private void HandleGameplayStarted()
        {
            Recalculate(true);
        }

        /// <summary>복원 중에는 경보를 끄고 플레이 시작 시점에 다시 계산한다.</summary>
        private void HandleGameSessionPhaseChanged(GameSessionPhase phase)
        {
            if (phase == GameSessionPhase.Playing)
            {
                return;
            }

            SetLevel(DangerAlertLevel.None, true);
        }

        /// <summary>플레이어가 다른 방으로 확정 이동했을 때 전체 적의 경보를 다시 계산한다.</summary>
        private void HandlePlayerRoomEntered(Room previousRoom, Room currentRoom)
        {
            Recalculate(false);
        }

        /// <summary>적 상태가 바뀌었을 때 해당 상태를 반영해 전체 경보를 다시 계산한다.</summary>
        private void HandleEnemyStateChanged(EnemyState previousState, EnemyState currentState)
        {
            Recalculate(false);
        }

        /// <summary>적의 다음 목적지가 결정되거나 해제되었을 때 경보를 다시 계산한다.</summary>
        private void HandleEnemyDestinationChanged(Room destinationRoom)
        {
            Recalculate(false);
        }

        /// <summary>적이 문을 통과해 논리 방을 변경했을 때 경보를 다시 계산한다.</summary>
        private void HandleEnemyRoomChanged(Room previousRoom, Room currentRoom)
        {
            Recalculate(false);
        }

        /// <summary>
        /// 모든 활성 적이 만드는 경보 중 가장 높은 단계를 현재 경보로 확정한다.
        /// </summary>
        private void Recalculate(bool forceNotify)
        {
            if (!IsGameplaySimulationEnabled())
            {
                SetLevel(DangerAlertLevel.None, forceNotify);
                return;
            }

            Room playerRoom = playerRoomTracker != null ? playerRoomTracker.CurrentRoom : null;
            DangerAlertLevel calculatedLevel = DangerAlertLevel.None;

            if (enemies != null)
            {
                foreach (EnemyController enemy in enemies)
                {
                    if (enemy == null)
                    {
                        continue;
                    }

                    DangerAlertState state = new DangerAlertState(
                        playerRoom,
                        enemy.CurrentRoom,
                        enemy.NextDestinationRoom,
                        enemy.isActiveAndEnabled && enemy.State != EnemyState.Inactive,
                        enemy.IsChasing,
                        enemy.Mode == EnemyMode.ScriptedSequence);

                    DangerAlertLevel enemyLevel = DangerAlertEvaluator.Evaluate(state);
                    if (enemyLevel > calculatedLevel)
                    {
                        calculatedLevel = enemyLevel;
                    }

                    if (calculatedLevel == DangerAlertLevel.Emergency)
                    {
                        break;
                    }
                }
            }

            SetLevel(calculatedLevel, forceNotify);
        }

        /// <summary>경보 단계를 변경하고 필요한 경우 화면 구독자에게 알린다.</summary>
        private void SetLevel(DangerAlertLevel level, bool forceNotify)
        {
            if (!forceNotify && CurrentLevel == level)
            {
                return;
            }

            CurrentLevel = level;
            AlertChanged?.Invoke(CurrentLevel);
        }

        /// <summary>게임 세션이 플레이 중인지 확인해 복원 중 계산을 차단한다.</summary>
        private bool IsGameplaySimulationEnabled()
        {
            return gameSession == null || gameSession.IsGameplaySimulationEnabled;
        }

        /// <summary>한 적의 상태·목적지·방 변경 이벤트를 경보 시스템에 연결한다.</summary>
        private void SubscribeEnemy(EnemyController enemy)
        {
            if (enemy == null)
            {
                return;
            }

            enemy.StateChanged -= HandleEnemyStateChanged;
            enemy.StateChanged += HandleEnemyStateChanged;
            enemy.DestinationChanged -= HandleEnemyDestinationChanged;
            enemy.DestinationChanged += HandleEnemyDestinationChanged;
            enemy.RoomChanged -= HandleEnemyRoomChanged;
            enemy.RoomChanged += HandleEnemyRoomChanged;
        }

        /// <summary>한 적에 대한 경보 시스템 이벤트 구독을 해제한다.</summary>
        private void UnsubscribeEnemy(EnemyController enemy)
        {
            if (enemy == null)
            {
                return;
            }

            enemy.StateChanged -= HandleEnemyStateChanged;
            enemy.DestinationChanged -= HandleEnemyDestinationChanged;
            enemy.RoomChanged -= HandleEnemyRoomChanged;
        }

        /// <summary>컴포넌트를 처음 추가할 때 플레이어 방 추적기를 기본 연결한다.</summary>
        private void Reset()
        {
            playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
            gameSession = FindFirstObjectByType<GameSession>();
            enemies = FindObjectsByType<EnemyController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        }
    }
}
