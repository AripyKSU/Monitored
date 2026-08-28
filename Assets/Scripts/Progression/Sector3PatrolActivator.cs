using System;
using HauntedHouse.Enemies;
using UnityEngine;

namespace HauntedHouse.Progression
{
    /// <summary>
    /// Save3 이후 Sector3 살인마 Patrol을 시작하도록 진행 상태와 기존 Enemy AI를 연결한다.
    /// 실제 이동·문 통과·추격은 EnemyController에 위임하고, 이 컴포넌트는 활성화 시점만 소유한다.
    /// </summary>
    public sealed class Sector3PatrolActivator : MonoBehaviour
    {
        private const float DepartureDelaySeconds = 2f;

        [Header("진행 참조")]
        [SerializeField] private GameSession gameSession;

        [Header("순찰 참조")]
        [SerializeField] private EnemyController enemy;
        [SerializeField] private PatrolRoute patrolRoute;

        /// <summary>활성화 어댑터가 사용하는 세션 참조를 반환한다.</summary>
        public GameSession GameSession => gameSession;

        /// <summary>Sector3 Patrol 대상 적을 반환한다.</summary>
        public EnemyController Enemy => enemy;

        /// <summary>Sector3 전용 순찰 경로를 반환한다.</summary>
        public PatrolRoute PatrolRoute => patrolRoute;

        /// <summary>세션 이벤트에 구독해 복원 완료와 Story Stage 전환을 관찰한다.</summary>
        private void OnEnable()
        {
            gameSession ??= FindFirstObjectByType<GameSession>();
            if (gameSession == null)
            {
                return;
            }

            gameSession.GameplayStarted -= HandleGameplayStarted;
            gameSession.GameplayStarted += HandleGameplayStarted;
            gameSession.StoryStageChanged -= HandleStoryStageChanged;
            gameSession.StoryStageChanged += HandleStoryStageChanged;
        }

        /// <summary>세션 이벤트 구독을 해제해 씬 재로드와 Restart 중 중복 호출을 막는다.</summary>
        private void OnDisable()
        {
            if (gameSession == null)
            {
                return;
            }

            gameSession.GameplayStarted -= HandleGameplayStarted;
            gameSession.StoryStageChanged -= HandleStoryStageChanged;
        }

        /// <summary>체크포인트 복원이 끝나고 입력이 다시 시작될 때 Patrol을 적용한다.</summary>
        private void HandleGameplayStarted()
        {
            TryActivatePatrol();
        }

        /// <summary>정상 진행에서 Save3가 활성화되어 Stage가 바뀌는 순간 Patrol을 적용한다.</summary>
        /// <param name="stage">새로 설정된 Story Stage.</param>
        private void HandleStoryStageChanged(StoryStage stage)
        {
            if (stage == StoryStage.Sector3)
            {
                TryActivatePatrol();
            }
        }

        /// <summary>
        /// Sector3 조건이 충족된 경우 기존 EnemyController에 경로와 첫 스폰 지점을 적용한다.
        /// 복원 중에는 호출하지 않고 GameplayStarted에서 한 번 더 실행해 복원 순서를 보장한다.
        /// </summary>
        private void TryActivatePatrol()
        {
            if (gameSession == null || gameSession.Phase != GameSessionPhase.Playing ||
                gameSession.CurrentStoryStage != StoryStage.Sector3 ||
                !gameSession.HasStoryFlag(StoryProgressFlags.CenterHallSecondEntered) ||
                !gameSession.HasStoryFlag(StoryProgressFlags.Sector2HideEncounterCompleted))
            {
                return;
            }

            if (enemy == null || patrolRoute == null || patrolRoute.Count == 0)
            {
                GameLog.LogError("Sector3PatrolActivator의 Enemy 또는 PatrolRoute 참조가 누락됐습니다.", this);
                return;
            }

            PatrolPoint firstPoint = patrolRoute.GetPoint(0);
            if (firstPoint == null)
            {
                GameLog.LogError("Sector3PatrolActivator의 첫 PatrolPoint가 비어 있습니다.", this);
                return;
            }

            // Save3 정상 진입에서는 Sector2 이벤트가 적을 비활성화한 뒤 Stage가 전환된다.
            // 체크포인트 Restart에서는 이미 같은 상태가 복원되므로 SetRoute·SetActive를 다시 적용해도
            // 항상 DiningRoom 첫 지점으로 되돌아가는 동일한 기준을 유지한다.
            if (!enemy.SetRoute(patrolRoute, 0))
            {
                GameLog.LogError("Sector3PatrolActivator가 PatrolRoute를 적용하지 못했습니다.", enemy);
                return;
            }

            ConfigureDepartureDelay();
            enemy.SetActive(true, firstPoint);
            GameLog.Log(
                $"Sector3 살인마 Patrol을 '{firstPoint.Room?.RoomId ?? "null"}' 첫 지점에서 시작했습니다.",
                this);
        }

        /// <summary>목적지 경고가 표시된 뒤 Sector3 적이 2초 후 출발하도록 런타임 제어기를 구성한다.</summary>
        private void ConfigureDepartureDelay()
        {
            EnemyPatrolDepartureDelay delay = enemy.GetComponent<EnemyPatrolDepartureDelay>() ??
                                              enemy.gameObject.AddComponent<EnemyPatrolDepartureDelay>();
            delay.Configure(enemy, DepartureDelaySeconds);
        }

        /// <summary>컴포넌트를 처음 추가할 때 씬의 공용 진행·적 참조를 보완한다.</summary>
        private void Reset()
        {
            gameSession = FindFirstObjectByType<GameSession>();
            enemy = GetComponent<EnemyController>() ?? FindFirstObjectByType<EnemyController>(FindObjectsInactive.Include);
            patrolRoute = FindFirstObjectByType<PatrolRoute>(FindObjectsInactive.Include);
        }
    }
}
