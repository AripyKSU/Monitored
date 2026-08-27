using HauntedHouse.Enemies;
using UnityEngine;

namespace HauntedHouse.Progression
{
    /// <summary>
    /// Save4 이후 Sector4 살인마 Patrol 경로를 기존 EnemyController에 적용하는 진행 어댑터다.
    /// 실제 이동과 문 통과는 기존 Enemy AI에 위임하고, Sector4 진입·체크포인트 복원 시점만 연결한다.
    /// </summary>
    public sealed class Sector4PatrolActivator : MonoBehaviour
    {
        [Header("진행 참조")]
        [SerializeField] private GameSession gameSession;

        [Header("순찰 참조")]
        [SerializeField] private EnemyController enemy;
        [SerializeField] private PatrolRoute patrolRoute;

        /// <summary>Sector4 Patrol 활성화에 사용하는 GameSession을 반환한다.</summary>
        public GameSession GameSession => gameSession;

        /// <summary>Sector4 Patrol 대상 EnemyController를 반환한다.</summary>
        public EnemyController Enemy => enemy;

        /// <summary>Sector4 전용 PatrolRoute를 반환한다.</summary>
        public PatrolRoute PatrolRoute => patrolRoute;

        /// <summary>
        /// 세션 이벤트를 구독해 정상 Sector4 진입과 체크포인트 복원을 모두 관찰한다.
        /// 복원 중에는 GameSession이 GameplayStarted를 발행할 때까지 실제 활성화를 미룬다.
        /// </summary>
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

        /// <summary>씬 재로드나 컴포넌트 비활성화 때 세션 이벤트 구독을 해제한다.</summary>
        private void OnDisable()
        {
            if (gameSession == null)
            {
                return;
            }

            gameSession.GameplayStarted -= HandleGameplayStarted;
            gameSession.StoryStageChanged -= HandleStoryStageChanged;
        }

        /// <summary>체크포인트 복원이 끝나고 게임플레이가 재개될 때 Sector4 Patrol을 적용한다.</summary>
        private void HandleGameplayStarted()
        {
            TryActivatePatrol();
        }

        /// <summary>정상 진행에서 StoryStage가 Sector4로 바뀌는 순간 Patrol을 적용한다.</summary>
        /// <param name="stage">새로 바뀐 StoryStage.</param>
        private void HandleStoryStageChanged(StoryStage stage)
        {
            if (stage == StoryStage.Sector4)
            {
                TryActivatePatrol();
            }
        }

        /// <summary>
        /// Sector4 진행 조건이 충족된 경우 기존 Enemy에 Route와 MorgueRoom2 시작점을 적용한다.
        /// 복원 직후에도 같은 기준으로 다시 적용해 항상 첫 지점에서 Patrol을 시작하게 한다.
        /// </summary>
        private void TryActivatePatrol()
        {
            // StoryStage가 Sector4여도 BasementEntered가 없으면 정상적인 계단 진입 전이므로
            // Patrol을 앞당겨 시작하지 않는다. Restore 중에는 GameplayStarted에서 재시도한다.
            if (gameSession == null || gameSession.Phase != GameSessionPhase.Playing ||
                gameSession.CurrentStoryStage != StoryStage.Sector4 ||
                !gameSession.HasStoryFlag(StoryProgressFlags.BasementEntered))
            {
                return;
            }

            if (enemy == null || patrolRoute == null || patrolRoute.Count == 0)
            {
                GameLog.LogError("Sector4PatrolActivator의 Enemy 또는 PatrolRoute 참조가 누락됐습니다.", this);
                return;
            }

            PatrolPoint firstPoint = patrolRoute.GetPoint(0);
            if (firstPoint == null)
            {
                GameLog.LogError("Sector4PatrolActivator의 첫 PatrolPoint가 비어 있습니다.", this);
                return;
            }

            // 기존 SetRoute와 SetActive를 사용해 RoomGraph 경로 검증, 문 통과, 상태 전환을
            // EnemyController 한 곳에서 처리한다. 별도의 Sector4 이동 로직은 만들지 않는다.
            if (!enemy.SetRoute(patrolRoute, 0))
            {
                GameLog.LogError("Sector4PatrolActivator가 PatrolRoute를 적용하지 못했습니다.", enemy);
                return;
            }

            enemy.SetActive(true, firstPoint);
            GameLog.Log(
                $"Sector4 살인마 Patrol을 '{firstPoint.Room?.RoomId ?? "null"}' 첫 지점에서 시작했습니다.",
                this);
        }

        /// <summary>컴포넌트 추가 시 씬의 공용 진행·적 참조를 자동 보완한다.</summary>
        private void Reset()
        {
            gameSession = FindFirstObjectByType<GameSession>();
            enemy = GetComponent<EnemyController>() ??
                    FindFirstObjectByType<EnemyController>(FindObjectsInactive.Include);
            patrolRoute = FindFirstObjectByType<PatrolRoute>(FindObjectsInactive.Include);
        }
    }
}
